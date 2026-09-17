using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct RegExpPrototypePopulateInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        FieldBuilder ObjectPrototypeField,
        EmittedSymbolRuntime Symbols,
        ConstructorBuilder TSFunctionCtorWithCache,
        FieldInfo UndefinedInstance
    );

    /// <summary>
    /// Populates <see cref="EmittedRegExpRuntime.Prototype"/> with the
    /// five well-known-symbol-keyed methods from ECMA-262 §22.2.5
    /// (@@match/@@matchAll/@@replace/@@search/@@split). The string-keyed dict
    /// gets a `constructor` slot pointing at typeof($RegExp); the symbol
    /// methods live in the per-object ConditionalWeakTable symbol-dict so
    /// `RegExp.prototype[Symbol.match]` resolves through the same path
    /// `regex[Symbol.match]` does.
    ///
    /// Without a supplied implementation the helpers do not exist, so the body
    /// degenerates to a no-op Ret — the field is still initialized to an
    /// empty dictionary by the cctor.
    /// </summary>
    private void DefineRegExpPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRegExpRuntime regExps)
    {
        if (regExps.Implementation is null)
        {
            regExps.PopulatePrototype = typeBuilder.DefineMethod(
                "_RegExpPrototypePopulate",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Void,
                Type.EmptyTypes);
            regExps.PopulatePrototype.GetILGenerator().Emit(OpCodes.Ret);
            regExps.MarkPrototypeBodyEmitted();
        }
        else
        {
            // The selected implementation requires the token reserved in phase 1.
            _ = regExps.PopulatePrototype;
        }
    }

    private void EmitRegExpPrototypePopulate(
        TypeBuilder typeBuilder,
        EmittedRegExpRuntime regExps,
        RegExpPrototypePopulateInputs inputs
    )
    {
        var regExp = regExps.RequireImplementation();
        var method = regExps.PopulatePrototype;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, regExps.Prototype);

        // ECMA-262 §22.2.6 RegExp.prototype.constructor === RegExp.
        // Plant in dict for fast-read + install a non-enumerable PDS descriptor
        // so Object.keys / for-in skip it per spec (§17 built-in attrs).
        var ctorDescLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
        EmitInstallConstructorDescriptor(il, new PrototypeDescriptorInputs(inputs.DescriptorStorage.DescriptorConstructor,
            inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
            inputs.DescriptorStorage.DefineProperty), regExps.Prototype, ctorDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, regExp.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Populate the symbol-keyed slots. The dispatch path
        // (RuntimeEmitter.Objects.Index.cs `symbolKeyLabel`) reads from
        // GetSymbolDict(obj), so we plant the entries in
        // GetSymbolDict(RegExpPrototypeField).
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Ldsfld, regExps.Prototype);
        il.Emit(OpCodes.Call, inputs.Symbols.GetStorage);
        il.Emit(OpCodes.Stloc, symbolDictLocal);

        var symbolSetItem = _types.GetMethod(_types.DictionaryObjectObject, "set_Item",
            _types.Object, _types.Object);

        void WireSymbol(FieldBuilder symbolField, MethodBuilder helper, string jsName, int jsLength)
        {
            // Store a real descriptor so gOPD/propertyIsEnumerable observe the
            // ECMA-262 §17 attributes. Symbol-index Get unwraps descriptor.Value.
            var fnLocal = il.DeclareLocal(_types.Object);
            var descLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
            il.Emit(OpCodes.Ldnull);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, inputs.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, fnLocal);

            il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descLocal);
            il.Emit(OpCodes.Ldloc, descLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);

            il.Emit(OpCodes.Ldloc, symbolDictLocal);
            il.Emit(OpCodes.Ldsfld, symbolField);
            il.Emit(OpCodes.Ldloc, descLocal);
            il.Emit(OpCodes.Callvirt, symbolSetItem);
        }

        // ECMA-262 §22.2.5 spec lengths:
        //   @@match=1, @@matchAll=1, @@replace=2, @@search=1, @@split=2.
        WireSymbol(inputs.Symbols.Match,    regExp.SymbolMatch,    "[Symbol.match]",    1);
        WireSymbol(inputs.Symbols.MatchAll, regExp.SymbolMatchAll, "[Symbol.matchAll]", 1);
        WireSymbol(inputs.Symbols.Replace,  regExp.SymbolReplace,  "[Symbol.replace]",  2);
        WireSymbol(inputs.Symbols.Search,   regExp.SymbolSearch,   "[Symbol.search]",   1);
        WireSymbol(inputs.Symbols.Split,    regExp.SymbolSplit,    "[Symbol.split]",    2);

        // ECMA-262 §22.2.5.{3-12} accessor descriptors. Each spec accessor
        // (source/flags/global/ignoreCase/multiline/sticky/unicode/dotAll/
        // hasIndices/unicodeSets) lives on RegExp.prototype as a real
        // accessor with a getter that throws TypeError on non-RegExp
        // `this`. test262's prototype/<flag>/this-val-non-obj.js and
        // this-val-regexp-prototype.js depend on these being real
        // descriptors retrievable via Object.getOwnPropertyDescriptor.
        var protoDescLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        void InstallAccessor(string jsName, MethodBuilder helper, int jsLength)
        {
            // var fn = new $TSFunction(null, helper.MethodInfo, jsName, jsLength);
            il.Emit(OpCodes.Ldnull);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Ldstr, "get " + jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, inputs.TSFunctionCtorWithCache);
            // descriptor = new $CompiledPropertyDescriptor { Getter = fn };
            var fnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, protoDescLocal);
            il.Emit(OpCodes.Ldloc, protoDescLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorGetter.GetSetMethod()!);
            // ECMA-262 §22.2.6 accessor descriptors are { enumerable:false,
            // configurable:true } — $CompiledPropertyDescriptor's ctor defaults
            // Enumerable=true so override it. prop-desc.js tests verify both.
            il.Emit(OpCodes.Ldloc, protoDescLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
            // PDSDefineProperty(RegExp.prototype, jsName, descriptor);
            il.Emit(OpCodes.Ldsfld, regExps.Prototype);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, protoDescLocal);
            il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        InstallAccessor("source",       regExp.PrototypeGetSource,      0);
        InstallAccessor("flags",        regExp.PrototypeGetFlags,       0);
        InstallAccessor("global",       regExp.PrototypeGetGlobal,      0);
        InstallAccessor("ignoreCase",   regExp.PrototypeGetIgnoreCase,  0);
        InstallAccessor("multiline",    regExp.PrototypeGetMultiline,   0);
        InstallAccessor("sticky",       regExp.PrototypeGetSticky,      0);
        InstallAccessor("unicode",      regExp.PrototypeGetUnicode,     0);
        InstallAccessor("dotAll",       regExp.PrototypeGetDotAll,      0);
        InstallAccessor("hasIndices",   regExp.PrototypeGetHasIndices,  0);
        InstallAccessor("unicodeSets",  regExp.PrototypeGetUnicodeSets, 0);

        // exec / test / toString as data properties. The helpers throw
        // TypeError on non-RegExp receivers; test262's prototype/exec/
        // S15.10.6.2_A2_*.js patterns set RegExp.prototype.exec onto a
        // plain object and verify the resulting call throws.
        var dataMethodFnLocal = il.DeclareLocal(_types.Object);
        var dataMethodDescLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        void InstallDataMethod(string jsName, MethodBuilder helper, int jsLength)
        {
            // Build fn = new $TSFunction(null, helper, jsName, jsLength) → fnLocal.
            il.Emit(OpCodes.Ldnull);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, inputs.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, dataMethodFnLocal);

            // ECMA-262 §17 built-in functions have no `prototype` property.
            // GetFunctionMethod would otherwise auto-create one on first read
            // via its MethodInfo-keyed prototype cache. Pre-install a PDS
            // data descriptor for "prototype" with value=undefined so the
            // PDS lookup (which runs BEFORE the auto-create branch) returns
            // undefined and prototype/exec/S15.10.6.2_A6.js's
            // `RegExp.prototype.exec.prototype === undefined` holds.
            il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, dataMethodDescLocal);
            il.Emit(OpCodes.Ldloc, dataMethodDescLocal);
            il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, dataMethodFnLocal);
            il.Emit(OpCodes.Ldstr, "prototype");
            il.Emit(OpCodes.Ldloc, dataMethodDescLocal);
            il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);

            // dict[jsName] = fn (covers the property-read fast path)
            il.Emit(OpCodes.Ldsfld, regExps.Prototype);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, dataMethodFnLocal);
            il.Emit(OpCodes.Callvirt, setItem);

            // Also install a PDS data descriptor on RegExp.prototype keyed by
            // jsName with { value: fn, writable: true, enumerable: false,
            // configurable: true } — ECMA-262 §17 built-in attrs. The dict
            // entry handles property reads (fast path); the PDS descriptor
            // gates Object.keys / for-in / propertyIsEnumerable / gOPD so
            // those report enumerable=false per spec.
            il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, dataMethodDescLocal);
            il.Emit(OpCodes.Ldloc, dataMethodDescLocal);
            il.Emit(OpCodes.Ldloc, dataMethodFnLocal);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, dataMethodDescLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
            // writable & configurable already true via ctor defaults.
            il.Emit(OpCodes.Ldsfld, regExps.Prototype);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, dataMethodDescLocal);
            il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        InstallDataMethod("exec",     regExp.PrototypeExec,     1);
        InstallDataMethod("test",     regExp.PrototypeTest,     1);
        InstallDataMethod("toString", regExp.PrototypeToString, 0);

        // RegExp.prototype's [[Prototype]] is %Object.prototype% per
        // ECMA-262 §22.2.6.
        il.Emit(OpCodes.Ldsfld, regExps.Prototype);
        il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypeField);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ret);
        regExps.MarkPrototypeBodyEmitted();
    }
}
