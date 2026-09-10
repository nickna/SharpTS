using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Emits a data result using explicit scratch/exit ownership. emitValue leaves one value;
    // this stage branches to endLabel with one descriptor object. Entry stack is empty.
    private void EmitBuiltinDataDescriptor(
        ILGenerator il, LocalBuilder resultDictLocal, Label endLabel,
        Action emitValue, bool writable, bool configurable)
    {
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        emitValue();
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", writable);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", configurable);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
    }

    // Proxy dispatch precedes symbol/string coercion. Returns from the emitted method on a match;
    // otherwise falls through empty. Owns its normalization locals and labels.
    private void EmitGetOwnDescriptorProxyReceiver(ILGenerator il, EmittedRuntime runtime)
    {
        // Proxy [[GetOwnProperty]] dispatch must run before the ordinary Symbol
        // dictionary fast path. Keeping arg1 object-valued preserves emitted
        // Symbols across the SharpTS.dll reflection boundary.
        var notProxyForDescriptorLabel = il.DefineLabel();
        var proxyForDescriptorLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il,
            () => il.Emit(OpCodes.Ldarg_0),
            proxyForDescriptorLabel,
            notProxyForDescriptorLabel);
        il.MarkLabel(proxyForDescriptorLabel);
        EmitProxyGetOwnPropertyDescriptorCompiledCall(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0),
            () => il.Emit(OpCodes.Ldarg_1));
        // The bridge uses SharpTS.dll's undefined singleton. Normalize it to
        // the generated assembly's singleton before returning to guest code.
        var proxyDescriptorResultLocal = il.DeclareLocal(_types.Object);
        var proxyDescriptorNotRuntimeUndefinedLabel = il.DefineLabel();
        var proxyDescriptorReturnLabel = il.DefineLabel();
        il.Emit(OpCodes.Stloc, proxyDescriptorResultLocal);
        il.Emit(OpCodes.Ldloc, proxyDescriptorResultLocal);
        il.Emit(OpCodes.Brfalse, proxyDescriptorNotRuntimeUndefinedLabel);
        il.Emit(OpCodes.Ldloc, proxyDescriptorResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType")!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "SharpTSUndefined");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, proxyDescriptorNotRuntimeUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, proxyDescriptorReturnLabel);
        il.MarkLabel(proxyDescriptorNotRuntimeUndefinedLabel);
        il.Emit(OpCodes.Ldloc, proxyDescriptorResultLocal);
        il.MarkLabel(proxyDescriptorReturnLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyForDescriptorLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    // A stored descriptor writes descriptorLocal and branches to hasDescriptorLabel empty.
    private void EmitGetOwnDescriptorGlobalReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder descriptorLocal,
        LocalBuilder resultDictLocal,
        Label hasDescriptorLabel,
        Label returnNullLabel,
        Label endLabel)
    {
        // The global object exposes its standard functions and constants as
        // own properties. Route descriptor values through the same global
        // lookup used by ordinary reads so cached function identity is
        // preserved (`desc.value === global.parseInt`).
        var notGlobalObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalObjectLabel);

        // User-defined global descriptors (including the Test262 $DONE hook)
        // take precedence over the synthesized intrinsic descriptors below.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brtrue, hasDescriptorLabel);

        // Configurable standard globals can be deleted. The intrinsic lookup
        // remains available internally, so hide it through the deletion ledger
        // before synthesizing the public own-property descriptor.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        void EmitGlobalDescriptorCheck(
            string name, bool writable, bool configurable)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, next);
            EmitBuiltinDataDescriptor(il, resultDictLocal, endLabel, () =>
            {
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, runtime.GlobalThisGetProperty);
            }, writable, configurable);
            il.MarkLabel(next);
        }
        EmitGlobalDescriptorCheck("parseInt", true, true);
        EmitGlobalDescriptorCheck("parseFloat", true, true);
        EmitGlobalDescriptorCheck("isNaN", true, true);
        EmitGlobalDescriptorCheck("isFinite", true, true);
        EmitGlobalDescriptorCheck("eval", true, true);
        EmitGlobalDescriptorCheck("NaN", false, false);
        EmitGlobalDescriptorCheck("Infinity", false, false);
        EmitGlobalDescriptorCheck("undefined", false, false);
        EmitGlobalDescriptorCheck("globalThis", true, true);
        foreach (var globalName in new[]
        {
            "Array", "Date", "RegExp", "Map", "Set", "WeakMap", "WeakSet",
            "Promise", "Function", "Object", "Number", "String", "Boolean",
            "Symbol", "Error", "TypeError", "RangeError", "ReferenceError",
            "SyntaxError", "URIError", "EvalError", "AggregateError", "Math", "JSON"
        })
        {
            EmitGlobalDescriptorCheck(globalName, true, true);
        }
        il.Emit(OpCodes.Br, returnNullLabel);
        il.MarkLabel(notGlobalObjectLabel);
    }

    // Reads live length before the ordinary stored-descriptor lookup. On a match branches
    // to the caller-owned endLabel with one object; otherwise falls through empty.
    private void EmitGetOwnDescriptorArrayLength(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // Array length is an intrinsic data property whose value lives on the
        // array, while a writable=false transition is recorded in the PDS.
        // Report the live value and the effective writable bit together;
        // reading descriptor.Value directly would become stale after a later
        // successful `arr.length = n` assignment.
        var notArrayLengthDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notArrayLengthDescriptorLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notArrayLengthDescriptorLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notArrayLengthDescriptorLabel);
    }

    // After PDS lookup, synthesizes intrinsic lastIndex. Entry/fallthrough are empty;
    // a match branches to the caller-owned endLabel with one result object.
    private void EmitGetOwnDescriptorRegExpReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // RegExp instances expose an intrinsic own lastIndex data property.
        // A user PDS descriptor won above; otherwise synthesize the live typed
        // value and the immutable enumerable/configurable attributes.
        if (_features.UsesRegExp)
        {
            var notRegExpLastIndexDescriptor = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpLastIndexDescriptor);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notRegExpLastIndexDescriptor);
            EmitBuiltinDataDescriptor(il, resultDictLocal, endLabel, () =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, "lastIndex");
                il.Emit(OpCodes.Call, runtime.GetProperty);
            }, writable: true, configurable: false);
            il.MarkLabel(notRegExpLastIndexDescriptor);
        }
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    private void EmitGetOwnDescriptorFunctionReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // ECMA-262 §17 — built-in functions expose `name` and `length` as
        // { writable: false, enumerable: false, configurable: true } own data
        // properties. Synthesize those descriptors for $TSFunction and the
        // promise resolve/reject callback wrappers. After
        // `delete fn.name`/`length`, IsBuiltinDeleted hides the synthetic
        // descriptor — descriptor lookup returns null, matching the post-
        // delete state expected by verifyProperty's isConfigurable check.
        var notTSFunctionForDescLabel = il.DefineLabel();
        var functionDescriptorCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionDescriptorCheckLabel);
        if (_features.UsesPromise)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.RequirePromise().ResolveCallbackType);
            il.Emit(OpCodes.Brtrue, functionDescriptorCheckLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Brtrue, functionDescriptorCheckLabel);
        if (_features.UsesPromise)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.RequirePromise().RejectCallbackType);
            il.Emit(OpCodes.Brfalse, notTSFunctionForDescLabel);
        }
        else
        {
            il.Emit(OpCodes.Br, notTSFunctionForDescLabel);
        }
        il.MarkLabel(functionDescriptorCheckLabel);

        // name / length only — anything else on a function returns null.
        var notFnNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFnNameLabel);
        // Hide if this instance had `name` deleted.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        // value = TSFunction.GetMember(fn, "name") — or just inline it via the
        // GetProperty path which handles function name lookup.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notFnNameLabel);

        var notFnLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFnLengthLabel);
        // Hide if this instance had `length` deleted.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notFnLengthLabel);

        // Constructor functions have an own non-configurable `prototype`
        // property. GetProperty supplies the stable cached prototype object
        // for user functions and $Undefined for non-constructable built-ins.
        var notFnPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFnPrototypeLabel);
        var functionPrototypeValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, functionPrototypeValueLocal);
        il.Emit(OpCodes.Ldloc, functionPrototypeValueLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldloc, functionPrototypeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, functionPrototypeValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notFnPrototypeLabel);

        // Other keys on a function: not own → null.
        il.Emit(OpCodes.Br, returnNullLabel);
        il.MarkLabel(notTSFunctionForDescLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    // Owns the Type receiver guard; constructor-specific stages retain dispatch order.
    private void EmitGetOwnDescriptorConstructorReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // System.Type — synthesize descriptor for built-in constructor's own
        // static properties. ECMA-262 §17 + §22.x: "prototype" is { value: X,
        // writable:false, enumerable:false, configurable:false }; static
        // constants (Number.MAX_VALUE etc.) likewise non-{writable,enumerable,
        // configurable}. Static methods are { writable:true, enumerable:false,
        // configurable:true }. verifyNotConfigurable / verifyProperty read
        // these descriptors via Object.getOwnPropertyDescriptor.
        var notTypeForDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeForDescLabel);
        EmitGetOwnDescriptorConstructorMetadata(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorRegExpConstructor(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorObjectConstructor(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorDateConstructor(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorArrayConstructor(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorNumberConstructor(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorConstructorFallback(
            il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);
        il.MarkLabel(notTypeForDescLabel);
    }

    // Called only for Type receivers. Handles prototype/name/length; entry/fallthrough
    // are empty, and matches branch to the caller-owned endLabel with one result object.
    private void EmitGetOwnDescriptorConstructorMetadata(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // "prototype" — non-configurable data descriptor.
        var typeIsPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsPrototypeLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsPrototypeLabel);
        // "name" — configurable data descriptor.
        var typeIsNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsNameLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsNameLabel);
        // "length" — configurable data descriptor.
        var typeIsLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsLengthLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsLengthLabel);
    }

    // Type receiver stage. Empty entry/fallthrough; matching escape lookup branches
    // to endLabel with one object and preserves the cached function identity.
    private void EmitGetOwnDescriptorRegExpConstructor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // RegExp.escape is emitted as $RegExp.Escape, whose CLR casing does
        // not match the JavaScript key. Build the same cached function wrapper
        // used by RegExpStaticEmitter so descriptor value identity is stable.
        if (_features.UsesRegExp)
        {
            var notRegExpEscapeDescriptor = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notRegExpEscapeDescriptor);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, "escape");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notRegExpEscapeDescriptor);
            EmitBuiltinDataDescriptor(il, resultDictLocal, endLabel, () =>
            {
                _types.EmitLoadMethodInfo(il, runtime.TSRegExpEscapeMethod);
                il.Emit(OpCodes.Ldstr, "escape");
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            }, writable: true, configurable: true);
            il.MarkLabel(notRegExpEscapeDescriptor);
        }
    }

    // Type receiver stage. Entry/fallthrough are empty; a matched static method
    // branches to the caller-owned endLabel with one result object. Owns dispatch labels.
    private void EmitGetOwnDescriptorObjectConstructor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // System.Object: explicit JS-spec static names list since static
        // dispatch is syntactic (compile-time) and runtime.GetProperty doesn't
        // resolve them via reflection. Synthesize spec-aligned method descriptors
        // for the known names. Mirrors HasOwnPropertyHelper's Object Type
        // names list.
        var objTypeIsObjectLabel = il.DefineLabel();
        var objTypeNotObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, objTypeNotObjectLabel);
        void EmitObjectMethodValueDescCheck(string n, MethodBuilder targetMethod, int specLength)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            // Build descriptor { value: $TSFunction.GetOrCreate(method, name, length), W:true, E:false, C:true }.
            // Test262 15.2.3.3-4-{23,24,25,etc.} verify `desc.value === Object.X`.
            // GetOrCreate caches by MethodInfo so this returns the SAME instance
            // as the static dispatch path that resolves Object.X directly.
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            _types.EmitLoadMethodInfo(il, targetMethod);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Ldc_I4, specLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        void EmitObjectMethodNameCheck(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            // Build descriptor { value: LookupBuiltInStaticMember(receiver, n),
            // W:true, E:false, C:true }. The lookup helper returns the SAME
            // $TSFunction wrapper that syntactic `Object.X` resolves to (via
            // TSFunctionGetOrCreate cache), so `desc.value === Object.X` holds.
            // Test262 15.2.3.3-4-{14,15,...} rely on this identity.
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldarg_0);  // receiver (typeof(Object))
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Call, runtime.LookupBuiltInStaticMember);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Use the value-aware variant only for methods whose MethodBuilder
        // is already defined when this gOPD method emits — others would
        // capture a null token (EmitObjectGetOwnPropertyDescriptor runs
        // at line 638 of RuntimeClass.cs setup; methods emitted later in
        // the dispatch chain — ObjectGetOwnPropertyDescriptors, Object
        // GetPrototypeOf, ObjectSetPrototypeOf, ObjectCreate, ObjectPrevent
        // Extensions, ObjectIsExtensible, ObjectGroupBy, GetOwnProperty
        // Symbols, ObjectDefineProperties — can't use this path here).
        EmitObjectMethodValueDescCheck("assign", runtime.ObjectAssign, 2);
        EmitObjectMethodNameCheck("create");
        EmitObjectMethodNameCheck("defineProperties");
        EmitObjectMethodValueDescCheck("defineProperty", runtime.ObjectDefineProperty, 3);
        EmitObjectMethodValueDescCheck("entries", runtime.GetEntries, 1);
        EmitObjectMethodValueDescCheck("freeze", runtime.ObjectFreeze, 1);
        EmitObjectMethodValueDescCheck("fromEntries", runtime.ObjectFromEntries, 1);
        EmitObjectMethodNameCheck("getOwnPropertyDescriptor");
        EmitObjectMethodNameCheck("getOwnPropertyDescriptors");
        EmitObjectMethodValueDescCheck("getOwnPropertyNames", runtime.GetOwnPropertyNames, 1);
        EmitObjectMethodNameCheck("getOwnPropertySymbols");
        EmitObjectMethodNameCheck("getPrototypeOf");
        EmitObjectMethodNameCheck("groupBy");
        EmitObjectMethodValueDescCheck("hasOwn", runtime.ObjectHasOwn, 2);
        EmitObjectMethodValueDescCheck("is", runtime.ObjectIs, 2);
        EmitObjectMethodNameCheck("isExtensible");
        EmitObjectMethodValueDescCheck("isFrozen", runtime.ObjectIsFrozen, 1);
        EmitObjectMethodValueDescCheck("isSealed", runtime.ObjectIsSealed, 1);
        EmitObjectMethodValueDescCheck("keys", runtime.GetKeys, 1);
        EmitObjectMethodNameCheck("preventExtensions");
        EmitObjectMethodValueDescCheck("seal", runtime.ObjectSeal, 1);
        EmitObjectMethodNameCheck("setPrototypeOf");
        EmitObjectMethodNameCheck("values");
        il.MarkLabel(objTypeNotObjectLabel);
    }

    // Type receiver stage. Empty entry/fallthrough; matches branch to endLabel
    // with an adapter-backed descriptor object, preserving function identity.
    private void EmitGetOwnDescriptorDateConstructor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // Date static methods need the same adapter-backed cached wrappers as
        // direct `Date.UTC`/`Date.parse`/`Date.now` reads. The generic Type
        // probe can see the CLR UTC method first and wrap a different method
        // identity, breaking descriptor.value identity.
        if (runtime.TSDateUTCStatic is not null)
        {
            var notDateTypeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSDateType);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notDateTypeLabel);
            void EmitDateStaticDescriptor(
                string name, MethodBuilder target, int length)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, propNameLocal);
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, _types.GetMethod(
                    _types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, next);
                EmitBuiltinDataDescriptor(il, resultDictLocal, endLabel, () =>
                {
                    _types.EmitLoadMethodInfo(il, target);
                    il.Emit(OpCodes.Ldstr, name);
                    il.Emit(OpCodes.Ldc_I4, length);
                    il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
                }, writable: true, configurable: true);
                il.MarkLabel(next);
            }
            EmitDateStaticDescriptor("UTC", runtime.TSDateUTCStatic, 7);
            if (runtime.TSDateParseStatic is not null)
                EmitDateStaticDescriptor(
                    "parse", runtime.TSDateParseStatic, 1);
            il.MarkLabel(notDateTypeLabel);
        }
    }

    // Type receiver stage. Empty entry/fallthrough; matched static methods branch
    // to the caller-owned endLabel with one result object.
    private void EmitGetOwnDescriptorArrayConstructor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // IList<object> → JS Array constructor. Same method-descriptor shape
        // as Object.
        var notArrayTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notArrayTypeLabel);
        void EmitArrayMethodNameCheck(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        EmitArrayMethodNameCheck("from");
        EmitArrayMethodNameCheck("fromAsync");
        EmitArrayMethodNameCheck("isArray");
        EmitArrayMethodNameCheck("of");
        il.MarkLabel(notArrayTypeLabel);
    }

    // Type receiver stage. Empty entry/fallthrough; matched methods/constants branch
    // to the caller-owned endLabel with one result object.
    private void EmitGetOwnDescriptorNumberConstructor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label endLabel)
    {
        // System.Double → JS Number constructor. Static constants have W:F,E:F,C:F;
        // static methods have W:T,E:F,C:T. Same dispatch shape as Object Type.
        var notDoubleTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notDoubleTypeLabel);
        void EmitNumberStaticCheck(string n, bool isMethod, double? constValue = null, MethodBuilder? methodTarget = null, int methodArity = 1)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            if (constValue.HasValue)
            {
                il.Emit(OpCodes.Ldc_R8, constValue.Value);
                il.Emit(OpCodes.Box, _types.Double);
            }
            else if (methodTarget != null)
            {
                // Emit TSFunction.GetOrCreate(methodInfo, name, length) so the
                // descriptor's .value === Number.X (same cached wrapper).
                _types.EmitLoadMethodInfo(il, methodTarget);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Ldc_I4, methodArity);
                il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, runtime.GetProperty);
            }
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", isMethod);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", isMethod);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Constants — embed the literal value so dynamic gOPD round-trips even
        // when runtime.GetProperty can't resolve the JS-static intercept path.
        EmitNumberStaticCheck("MAX_VALUE", false, double.MaxValue);
        EmitNumberStaticCheck("MIN_VALUE", false, double.Epsilon);
        EmitNumberStaticCheck("NaN", false, double.NaN);
        EmitNumberStaticCheck("POSITIVE_INFINITY", false, double.PositiveInfinity);
        EmitNumberStaticCheck("NEGATIVE_INFINITY", false, double.NegativeInfinity);
        EmitNumberStaticCheck("MAX_SAFE_INTEGER", false, 9007199254740991.0);
        EmitNumberStaticCheck("MIN_SAFE_INTEGER", false, -9007199254740991.0);
        EmitNumberStaticCheck("EPSILON", false, 2.220446049250313e-16);
        // Methods — TODO: emit TSFunction.GetOrCreate inline once EmitNumberMethods
        // runs BEFORE EmitObjectGetOwnPropertyDescriptor (currently runs after,
        // so runtime.NumberParseInt etc. are null at this emit site).
        EmitNumberStaticCheck("parseInt", true);
        EmitNumberStaticCheck("parseFloat", true);
        EmitNumberStaticCheck("isNaN", true);
        EmitNumberStaticCheck("isFinite", true);
        EmitNumberStaticCheck("isInteger", true);
        EmitNumberStaticCheck("isSafeInteger", true);
        il.MarkLabel(notDoubleTypeLabel);
    }

    // Final Type receiver stage. Empty entry; every path branches to a caller exit:
    // returnNullLabel with no value, or endLabel with one descriptor object.
    private void EmitGetOwnDescriptorConstructorFallback(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // Probe GetProperty: if it returns a non-undefined value, the property
        // is reachable through our static dispatch — synthesize a descriptor.
        // This catches JS-named constants (Number.MAX_VALUE → System.Double.
        // MaxValue) where reflection on the Type by JS name would miss.
        // Skip null returns since `GetProperty` returns null for unresolved.
        var typeProbeValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, typeProbeValueLocal);
        // Reject null and Undefined sentinel (property unknown).
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        // Has a value — synthesize descriptor. Per ECMA-262 §17, built-in
        // METHODS are { writable:true, enumerable:false, configurable:true };
        // built-in CONSTANTS (Number.MAX_VALUE etc.) are { writable:false,
        // enumerable:false, configurable:false }. Distinguish via $TSFunction
        // marker on the probed value.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        // Branch on $TSFunction (method) vs other (constant).
        var typeProbeIsFnLabel = il.DefineLabel();
        var typeProbeAfterAttrsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, typeProbeIsFnLabel);
        // Constant: W=false, E=false, C=false.
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Br, typeProbeAfterAttrsLabel);
        il.MarkLabel(typeProbeIsFnLabel);
        // Method: W=true, E=false, C=true.
        EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.MarkLabel(typeProbeAfterAttrsLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    // Array/list selection and canonical index/hole validation share only stage-owned locals.
    private void EmitGetOwnDescriptorIndexedReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // No descriptor - check if it's an array first
        var notListLabel = il.DefineLabel();
        var notTSArrayLabel = il.DefineLabel();
        var isListLabel = il.DefineLabel();
        var handleArrayLabel = il.DefineLabel();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var descriptorReceiverIsTSArray = il.DeclareLocal(_types.Boolean);

        // Check for List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Check for $Array (SharpTSArray)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);

        // It's $Array - get Elements list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, descriptorReceiverIsTSArray);
        il.Emit(OpCodes.Br, handleArrayLabel);

        il.MarkLabel(isListLabel);
        // listLocal already has the list

        il.MarkLabel(handleArrayLabel);
        // Handle array property - check if propName is "length" or numeric index

        // Check for "length" property
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notLengthLabel);

        // Return length descriptor: { value: length, writable: true, enumerable: false, configurable: false }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // value = list.Count
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = false
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = false
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notLengthLabel);
        // Check if it's a numeric index
        var notNumericIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notNumericIndexLabel);

        // Array and arguments carriers reach indexed storage only when the
        // property key uses its canonical decimal spelling.
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notNumericIndexLabel);

        // Check if index is in bounds
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnNullLabel);

        // $TSArray length can include holes. A deleted index is not an own
        // property even though it remains below Elements.Count; raw List
        // receivers retain their dense in-bounds semantics.
        var descriptorArrayIndexPresent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorReceiverIsTSArray);
        il.Emit(OpCodes.Brfalse, descriptorArrayIndexPresent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.MarkLabel(descriptorArrayIndexPresent);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnNullLabel);

        // Arguments/legacy List carriers use the shared hole sentinel for a
        // deleted index. In-range holes are absent own properties.
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        // Return element descriptor: { value: element, writable: true, enumerable: true, configurable: true }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // value = list[index]
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notNumericIndexLabel);
        // Not length or numeric index on array - return null
        il.Emit(OpCodes.Br, returnNullLabel);

        il.MarkLabel(notTSArrayLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    private void EmitGetOwnDescriptorMathReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // Math singleton dict — synthesize spec descriptors for its known
        // methods (W:T,E:F,C:T) and constants (W:F,E:F,C:F). The singleton
        // is otherwise empty; static dispatch handles Math.abs() etc., but
        // gOPD(Math, "abs") needs to report the spec descriptor. Skip the
        // synth if the (Math, name) pair was deleted (IsBuiltinDeleted).
        var notMathSingletonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, notMathSingletonLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        void EmitMathNameDesc(string n, bool isMethod, double? constValue = null,
            MethodBuilder? methodTarget = null, int methodArity = 1)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            if (constValue.HasValue)
            {
                il.Emit(OpCodes.Ldc_R8, constValue.Value);
                il.Emit(OpCodes.Box, _types.Double);
            }
            else if (methodTarget != null)
            {
                // $TSFunction.GetOrCreate(adapter MethodInfo, name, length)
                // returns the SAME instance as the static dispatch path
                // (MathStaticEmitter uses TSFunctionGetOrCreate too), so
                // `desc.value === Math.X` holds in user code.
                _types.EmitLoadMethodInfo(il, methodTarget);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Ldc_I4, methodArity);
                il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            }
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", isMethod);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", isMethod);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Methods (W:T, E:F, C:T) with identity-stable value (Math.X === Math.X).
        // Spec lengths per ECMA-262 §21.3.2 — same as MathStaticEmitter.
        (string n, MethodBuilder? m, int len)[] mathMethods =
        {
            ("abs",    runtime.MathAbsAdapter,    1),
            ("acos",   runtime.MathAcosAdapter,   1),
            ("acosh",  runtime.MathAcoshAdapter,  1),
            ("asin",   runtime.MathAsinAdapter,   1),
            ("asinh",  runtime.MathAsinhAdapter,  1),
            ("atan",   runtime.MathAtanAdapter,   1),
            ("atan2",  runtime.MathAtan2Adapter,  2),
            ("atanh",  runtime.MathAtanhAdapter,  1),
            ("cbrt",   runtime.MathCbrtAdapter,   1),
            ("ceil",   runtime.MathCeilAdapter,   1),
            ("clz32",  runtime.MathClz32Adapter,  1),
            ("cos",    runtime.MathCosAdapter,    1),
            ("cosh",   runtime.MathCoshAdapter,   1),
            ("exp",    runtime.MathExpAdapter,    1),
            ("expm1",  runtime.MathExpm1Adapter,  1),
            ("floor",  runtime.MathFloorAdapter,  1),
            ("fround", runtime.MathFroundAdapter, 1),
            ("f16round", runtime.MathF16RoundAdapter, 1),
            ("hypot",  runtime.MathHypotAdapter,  2),
            ("imul",   runtime.MathImulAdapter,   2),
            ("log",    runtime.MathLogAdapter,    1),
            ("log10",  runtime.MathLog10Adapter,  1),
            ("log1p",  runtime.MathLog1pAdapter,  1),
            ("log2",   runtime.MathLog2Adapter,   1),
            ("max",    runtime.MathMaxAdapter,    2),
            ("min",    runtime.MathMinAdapter,    2),
            ("pow",    runtime.MathPowAdapter,    2),
            // "random" → runtime.Random; EmitRandom now precedes gOPD emit
            // (see RuntimeEmitter.RuntimeClass.cs ~line 660), so we can wire
            // the actual MethodBuilder here for `desc.value === Math.random`.
            ("random", runtime.Random,             0),
            ("round",  runtime.MathRoundAdapter,  1),
            ("sign",   runtime.MathSignAdapter,   1),
            ("sin",    runtime.MathSinAdapter,    1),
            ("sinh",   runtime.MathSinhAdapter,   1),
            ("sqrt",   runtime.MathSqrtAdapter,   1),
            ("tan",    runtime.MathTanAdapter,    1),
            ("tanh",   runtime.MathTanhAdapter,   1),
            ("trunc",  runtime.MathTruncAdapter,  1),
            ("sumPrecise", runtime.MathSumPrecise, 1),
        };
        foreach (var (mn, mb, ml) in mathMethods)
            EmitMathNameDesc(mn, isMethod: true, methodTarget: mb, methodArity: ml);
        // Constants (W:F, E:F, C:F) with embedded literal values.
        EmitMathNameDesc("E", isMethod: false, constValue: System.Math.E);
        EmitMathNameDesc("LN10", isMethod: false, constValue: System.Math.Log(10));
        EmitMathNameDesc("LN2", isMethod: false, constValue: System.Math.Log(2));
        EmitMathNameDesc("LOG10E", isMethod: false, constValue: 1.0 / System.Math.Log(10));
        EmitMathNameDesc("LOG2E", isMethod: false, constValue: 1.0 / System.Math.Log(2));
        EmitMathNameDesc("PI", isMethod: false, constValue: System.Math.PI);
        EmitMathNameDesc("SQRT1_2", isMethod: false, constValue: System.Math.Sqrt(0.5));
        EmitMathNameDesc("SQRT2", isMethod: false, constValue: System.Math.Sqrt(2));
        il.MarkLabel(notMathSingletonLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    private void EmitGetOwnDescriptorJsonReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // JSON singleton — synth descriptors for parse/stringify/isRawJSON/rawJSON.
        // Same pattern as Math; the singleton dict is empty, static dispatch
        // handles JSON.X(); gOPD just needs to report spec attrs.
        var notJsonSingletonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Bne_Un, notJsonSingletonLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        void EmitJsonNameDesc(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "get_Item", _types.String));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        if (_features.UsesJSON)
        {
            EmitJsonNameDesc("parse");
            EmitJsonNameDesc("stringify");
            EmitJsonNameDesc("isRawJSON");
            EmitJsonNameDesc("rawJSON");
        }
        il.MarkLabel(notJsonSingletonLabel);
    }

    // Requires an empty entry stack. Unhandled receivers fall through empty; matches
    // branch to endLabel with one result object, or returnNullLabel with an empty stack.
    // The caller owns those exits and resultDictLocal; scratch locals/internal labels belong here.
    // Writes valueLocal as scratch while constructing the default descriptor.
    private void EmitGetOwnDescriptorDictionaryReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        LocalBuilder valueLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        // No descriptor - check if property exists on the object directly (Dictionary case)
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // Check if dictionary contains the key
        var dictContainsKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Property exists on dict - create default data descriptor.
        // ECMA-262 §10.1.5.1 OrdinaryGetOwnProperty + Object.freeze/seal:
        // frozen → writable=false + configurable=false; sealed → writable
        // preserves but configurable=false. We don't store per-property
        // descriptors when the user wrote `obj.foo = X` directly, so we
        // synthesize one — and reflect the object's frozen/sealed state
        // here so verifyProperty sees the spec-mandated immutability.
        var dictIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var dictIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, dictIsSealedLocal);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // Get the value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, valueLocal);

        // Set value property
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = !frozen (sealed preserves writable=true for the synth path).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !frozen
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true (freeze/seal preserve enumerability).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = !(frozen || sealed).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldloc, dictIsSealedLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !(frozen || sealed)
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        // Not a dictionary - check if it implements $IHasFields (class instances)
        il.MarkLabel(notDictLabel);
    }

    // Reads literal accessor tables after dictionary lookup. Empty entry/fallthrough;
    // a match branches to caller-owned endLabel with one descriptor object.
    // Uses caller-owned valueLocal as TryGetValue scratch; all other scratch locals are private.
    private void EmitGetOwnDescriptorLiteralAccessor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        LocalBuilder valueLocal,
        Label endLabel)
    {
        // Object-literal accessors live in $Object's getter/setter tables
        // rather than PDS. Surface them as ordinary own accessor descriptors
        // so Proxy [[Get]]/[[Set]] can preserve an explicit Receiver.
        var notTSObjectAccessorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectAccessorLabel);
        var literalGetterLocal = il.DeclareLocal(_types.Object);
        var literalSetterLocal = il.DeclareLocal(_types.Object);
        var literalGetterDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var literalSetterDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var checkLiteralSetterLabel = il.DefineLabel();
        var literalAccessorFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetGettersDict);
        il.Emit(OpCodes.Stloc, literalGetterDictLocal);
        il.Emit(OpCodes.Ldloc, literalGetterDictLocal);
        il.Emit(OpCodes.Brfalse, checkLiteralSetterLabel);
        il.Emit(OpCodes.Ldloc, literalGetterDictLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, literalGetterLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, literalAccessorFoundLabel);
        il.MarkLabel(checkLiteralSetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetSettersDict);
        il.Emit(OpCodes.Stloc, literalSetterDictLocal);
        il.Emit(OpCodes.Ldloc, literalSetterDictLocal);
        il.Emit(OpCodes.Brfalse, notTSObjectAccessorLabel);
        il.Emit(OpCodes.Ldloc, literalSetterDictLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, literalSetterLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notTSObjectAccessorLabel);

        il.MarkLabel(literalAccessorFoundLabel);
        // Fetch the other half when the getter-side lookup found the key.
        var literalOtherHalfDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, literalSetterLocal);
        il.Emit(OpCodes.Brtrue, literalOtherHalfDoneLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetSettersDict);
        il.Emit(OpCodes.Stloc, literalSetterDictLocal);
        il.Emit(OpCodes.Ldloc, literalSetterDictLocal);
        il.Emit(OpCodes.Brfalse, literalOtherHalfDoneLabel);
        il.Emit(OpCodes.Ldloc, literalSetterDictLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, literalSetterLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(literalOtherHalfDoneLabel);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        void EmitLiteralAccessorSlot(string name, LocalBuilder slot)
        {
            var haveSlotLabel = il.DefineLabel();
            var storeSlotLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Brtrue, haveSlotLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Br, storeSlotLabel);
            il.MarkLabel(haveSlotLabel);
            il.Emit(OpCodes.Ldloc, slot);
            il.MarkLabel(storeSlotLabel);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "set_Item"));
        }
        EmitLiteralAccessorSlot("get", literalGetterLocal);
        EmitLiteralAccessorSlot("set", literalSetterLocal);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notTSObjectAccessorLabel);
    }

    // Final ordinary receiver stage. Requires an empty stack; every path branches to
    // returnNullLabel empty or endLabel with one result. Owns field lookup locals/labels.
    private void EmitGetOwnDescriptorFieldsReceiver(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder resultDictLocal,
        LocalBuilder valueLocal,
        Label returnNullLabel,
        Label endLabel)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Get the fields dictionary from the class instance
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // Check if the fields dictionary contains the key
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Build data descriptor from the class field value. Same frozen/sealed
        // reflection as the dict path so Object.freeze on a class instance
        // surfaces writable=false / configurable=false.
        var hfIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var hfIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, hfIsSealedLocal);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // Get the value from the fields dictionary
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, valueLocal);

        // Set value
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = !frozen.
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true.
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = !(frozen || sealed).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldloc, hfIsSealedLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
    }
}
