using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitGetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.GetIndex = method;

        var il = method.GetILGenerator();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var typedArrayLabel = il.DefineLabel();
        var tsBufferLabel = il.DefineLabel();
        var kvpLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxyGetIndexCheck(il, runtime, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // globalThis/global sentinel (#271): `root[stringKey]` resolves through
        // GlobalThisGetProperty (the index is coerced to a property-key string),
        // mirroring the value-position GetProperty routing. Symbol keys are handled
        // above by the per-object symbol-dict path.
        var notGlobalThisIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalThisIdxLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GlobalThisGetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notGlobalThisIdxLabel);

        // $Buffer (check before TypedArray — the emitted IsTypedArray helper
        // excludes $Buffer, and GetTypedArrayElement would throw for it).
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, tsBufferLabel);
        }

        // TypedArray (check before List since TypedArray is more specific)
        // Skip when no typed-array kind was emitted — IsTypedArrayMethod always
        // returns false in that case anyway, but eliding the call is cleaner.
        if (_features.HasAnyTypedArray)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.IsTypedArrayMethod);
            il.Emit(OpCodes.Brtrue, typedArrayLabel);
        }

        // $Array (wrapper around List<object?>) - check before List
        var tsArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayLabel);

        // Descriptor-driven: check each array backing type
        var listGetLabels = new List<(ArrayElementsDescriptor desc, Label label)>();
        foreach (var desc in ArrayElements.All)
        {
            var label = il.DefineLabel();
            listGetLabels.Add((desc, label));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, desc.GetListType(_types));
            il.Emit(OpCodes.Brtrue, label);
        }

        // Native .NET Array (e.g., string[] from command line args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ArrayType);
        il.Emit(OpCodes.Brtrue, arrayLabel);

        // KeyValuePair<object, object> (Map entries when spread into array)
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, kvpType);
        il.Emit(OpCodes.Brtrue, kvpLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Dict with string key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $Object: route through $Runtime.GetProperty(obj, index.ToString())
        // so prototype-chain walks + getters fire. Test262 patterns like
        // `obj[0] = 11; arr.some.call(obj, …)` need numeric indexed reads
        // to land in the same store as `obj.length` (own _fields).
        var tsObjectIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectIdxLabel);

        // $TSFunction indexed read: route through GetProperty so PDS-stored
        // entries from `fun[i] = v` (set via the matching SetIndex branch)
        // round-trip. Mirrors the $Object handling above.
        var tsFunctionIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionIdxLabel);

        // System.Type indexed get: route through $Runtime.GetProperty so the
        // Type branch (LookupBuiltInStaticMember + per-type handlers) fires —
        // matches the syntactic `Object.assign` dispatch identity. Without
        // this, `Object["assign"]` falls through to GetFieldsProperty which
        // doesn't recognize built-in static-method names.
        var typeIdxGetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeIdxGetLabel);

        // Class instance — any non-Symbol key coerces to a property-key string
        // via Stringify (ECMA-262 §7.1.19). Earlier branches already split out
        // arrays / typed-arrays / dicts / $Object / $TSFunction; whatever is
        // left is a class instance whose fields are string-keyed.
        il.Emit(OpCodes.Br, classInstanceLabel);

        il.MarkLabel(typeIdxGetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        // Fallthrough: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: use GetSymbolDict(obj).TryGetValue(index, out value)
        il.MarkLabel(symbolKeyLabel);
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var symbolValueLocal = il.DeclareLocal(_types.Object);
        // var symbolDict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);
        // if (symbolDict.TryGetValue(index, out value)) return value;
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, symbolValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        var symbolFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, symbolFoundLabel);

        // #266: symbol-keyed class accessor (`get [Symbol.x]() {...}`). After an
        // own symbol data property misses, consult the per-class accessor registry
        // (walking the base chain). A found getter is a MethodInfo invoked with the
        // receiver — instance getters bind `this`, static getters ignore it.
        {
            var noSymGetterLabel = il.DefineLabel();
            var symGetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.FindSymbolGetter);
            il.Emit(OpCodes.Stloc, symGetterLocal);
            il.Emit(OpCodes.Ldloc, symGetterLocal);
            il.Emit(OpCodes.Brfalse, noSymGetterLabel);
            // return ((MethodBase)getter).Invoke(obj, Array.Empty<object>());
            il.Emit(OpCodes.Ldloc, symGetterLocal);
            il.Emit(OpCodes.Castclass, _types.MethodBase);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noSymGetterLabel);
        }

        // #647/#755: computed symbol-keyed method (`[Symbol.iterator]() {...}`). Reading the member
        // returns a receiver-bound callable — `new $TSFunction(obj, method)` — so a standalone
        // `obj[Symbol.iterator]()` keeps `this`. InvokeWithThis uses the bound `_target` for an
        // instance method (and null for a static one), mirroring the string-key method path
        // (GetFieldsProperty). for...of / spread / for-await pass the receiver themselves, so they
        // worked even when this returned the raw MethodInfo.
        {
            var noSymMethodLabel = il.DefineLabel();
            var symMethodLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.FindSymbolMethod);
            il.Emit(OpCodes.Stloc, symMethodLocal);
            il.Emit(OpCodes.Ldloc, symMethodLocal);
            il.Emit(OpCodes.Brfalse, noSymMethodLabel);
            il.Emit(OpCodes.Ldarg_0);                          // obj — receiver to bind
            il.Emit(OpCodes.Ldloc, symMethodLocal);            // found MethodInfo (typed object)
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);   // new $TSFunction(obj, method)
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noSymMethodLabel);
        }

        // Not found in user-set symbol dict — fall back to prototype-keyed
        // well-known-symbol dispatch. Currently only RegExp.prototype carries
        // symbol-keyed methods (@@match/@@matchAll/@@replace/@@search/@@split,
        // ECMA-262 §22.2.5). When UsesRegExp is gated off there can't be a
        // RegExp value at runtime, so skip the Isinst entirely.
        if (_features.UsesRegExp)
        {
            var notRegExpForSymbolLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpForSymbolLabel);

            // Ordinary inherited symbol lookup precedes the intrinsic fallback.
            // This observes replacements such as
            // `RegExp.prototype[Symbol.search] = custom` for every symbol, while
            // the populated intrinsic descriptors naturally preserve the
            // standard methods when no replacement exists.
            il.Emit(OpCodes.Call, runtime.RegExpPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, symbolValueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brtrue, symbolFoundLabel);
            EmitRegExpSymbolDispatch(il, runtime);
            il.MarkLabel(notRegExpForSymbolLabel);
        }

        // String primitives inherit @@iterator from String.prototype. Re-enter
        // GetIndex on the prototype dictionary so descriptor unwrapping follows
        // the same ordinary [[Get]] path as a direct prototype access.
        var notStringForSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringForSymbolLabel);
        il.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringForSymbolLabel);

        // Array-receiver symbol-key walk: when the per-object symbol dict
        // doesn't carry the key, walk up to Array.prototype's symbol dict.
        // Required for `arr[Symbol.iterator]` to resolve to Array.prototype.
        // values (ECMA-262 §23.1.3.34). Covers List<object> and $TSArray.
        void EmitProtoSymbolFallback(Type receiverType, FieldBuilder protoField, MethodBuilder populate)
        {
            var notThisRcvLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, receiverType);
            il.Emit(OpCodes.Brfalse, notThisRcvLabel);
            il.Emit(OpCodes.Call, populate);
            var protoSymDict = il.DeclareLocal(_types.DictionaryObjectObject);
            var protoSymVal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, protoField);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, protoSymDict);
            il.Emit(OpCodes.Ldloc, protoSymDict);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, protoSymVal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brfalse, notThisRcvLabel);
            il.Emit(OpCodes.Ldloc, protoSymVal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notThisRcvLabel);
        }
        EmitProtoSymbolFallback(_types.ListOfObject, runtime.ArrayPrototypeField, runtime.ArrayPrototypePopulateMethod);
        // TSArrayType is non-null here by contract: the dispatch above (Isinst TSArrayType)
        // already passed it to il.Emit, which throws on null. A redundant != null guard here
        // would only poison nullable flow analysis for the unconditional casts further down.
        EmitProtoSymbolFallback(runtime.TSArrayType, runtime.ArrayPrototypeField, runtime.ArrayPrototypePopulateMethod);
        // Functions inherit symbol-keyed properties from Function.prototype,
        // including Symbol.isConcatSpreadable. The own symbol dictionary was
        // already checked above; only a miss reaches this fallback.
        EmitProtoSymbolFallback(runtime.TSFunctionType, runtime.FunctionPrototypeField, runtime.FunctionPrototypePopulateMethod);
        EmitProtoSymbolFallback(runtime.BoundTSFunctionType, runtime.FunctionPrototypeField, runtime.FunctionPrototypePopulateMethod);

        // Ordinary symbol-keyed [[Get]] walks the receiver's explicit PDS
        // prototype chain just like string-keyed GetProperty. This is needed
        // for inherited well-known symbols on boxed primitive and RegExp
        // prototypes (notably Symbol.isConcatSpreadable), and for user-defined
        // symbol data/accessor properties on arbitrary prototypes.
        {
            var symbolProtoLocal = il.DeclareLocal(_types.Object);
            var symbolProtoDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
            var symbolProtoLoopLabel = il.DefineLabel();
            var symbolProtoDoneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
            il.Emit(OpCodes.Stloc, symbolProtoLocal);
            il.MarkLabel(symbolProtoLoopLabel);
            il.Emit(OpCodes.Ldloc, symbolProtoLocal);
            il.Emit(OpCodes.Brfalse, symbolProtoDoneLabel);
            il.Emit(OpCodes.Ldloc, symbolProtoLocal);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, symbolProtoDictLocal);
            il.Emit(OpCodes.Ldloc, symbolProtoDictLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, symbolValueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brtrue, symbolFoundLabel);
            il.Emit(OpCodes.Ldloc, symbolProtoLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
            il.Emit(OpCodes.Stloc, symbolProtoLocal);
            il.Emit(OpCodes.Br, symbolProtoLoopLabel);
            il.MarkLabel(symbolProtoDoneLabel);
        }

        // #265: symbol-keyed expando statics set on a base class constructor are
        // readable through subclasses (`Base[Symbol.x] = v` visible as `Sub[Symbol.x]`).
        // The per-object symbol dict is keyed by Type identity per-class, so walk the
        // constructor's .NET base-type chain (D.BaseType === C) until a dict carries
        // the key, mirroring the string-keyed walk in GetProperty's Type handler.
        {
            var notTypeForSymbolLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.Type);
            il.Emit(OpCodes.Brfalse, notTypeForSymbolLabel);
            var symWalkType = il.DeclareLocal(_types.Type);
            var symWalkDict = il.DeclareLocal(_types.DictionaryObjectObject);
            var symWalkVal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Stloc, symWalkType);
            var symWalkLoop = il.DefineLabel();
            il.MarkLabel(symWalkLoop);
            // symWalkType = symWalkType.BaseType;  (null terminates the chain)
            il.Emit(OpCodes.Ldloc, symWalkType);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, symWalkType);
            il.Emit(OpCodes.Ldloc, symWalkType);
            il.Emit(OpCodes.Brfalse, notTypeForSymbolLabel);
            // if (GetSymbolDict(symWalkType).TryGetValue(index, out val)) return val;
            il.Emit(OpCodes.Ldloc, symWalkType);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, symWalkDict);
            il.Emit(OpCodes.Ldloc, symWalkDict);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, symWalkVal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brfalse, symWalkLoop);
            il.Emit(OpCodes.Ldloc, symWalkVal);
            il.Emit(OpCodes.Stloc, symbolValueLocal);
            il.Emit(OpCodes.Br, symbolFoundLabel);
            il.MarkLabel(notTypeForSymbolLabel);
        }

        // Return undefined for missing symbol properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolFoundLabel);
        // Object.defineProperty and computed accessors store a full descriptor
        // in the symbol dictionary. Apply ordinary [[Get]] semantics here.
        var symbolDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var symbolRawValueLabel = il.DefineLabel();
        var symbolDataValueLabel = il.DefineLabel();
        var symbolUndefinedValueLabel = il.DefineLabel();
        var symbolDescriptorHasGetterLabel = il.DefineLabel();
        var symbolGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, symbolValueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Stloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Brfalse, symbolRawValueLabel);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, symbolGetterLocal);
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Brtrue, symbolDescriptorHasGetterLabel);
        // No getter plus a setter is an accessor whose read value is undefined.
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, symbolUndefinedValueLabel);
        il.Emit(OpCodes.Br, symbolDataValueLabel);

        il.MarkLabel(symbolDescriptorHasGetterLabel);
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, symbolUndefinedValueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(symbolUndefinedValueLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(symbolDataValueLabel);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(symbolRawValueLabel);
        il.Emit(OpCodes.Ldloc, symbolValueLocal);
        il.Emit(OpCodes.Ret);

        // TypedArray handler — skipped when typed arrays aren't emitted.
        if (_features.HasAnyTypedArray)
        {
            il.MarkLabel(typedArrayLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
            il.Emit(OpCodes.Ret);
        }

        // $Buffer handler: load byte from the underlying byte[] and return as boxed double.
        // Matches SharpTSBuffer.this[int] semantics: out-of-range returns NaN (boxed double),
        // in-range returns the byte as a double. Gated together with the dispatch arm.
        if (_features.UsesBuffer)
        {
            il.MarkLabel(tsBufferLabel);
            var bufDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
            var bufIndexLocal = il.DeclareLocal(_types.Int32);
            var bufInRangeLabel = il.DefineLabel();
            var bufOutOfRangeLabel = il.DefineLabel();
            // data = ((TSBuffer)obj).Data;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Call, runtime.TSBufferGetData);
            il.Emit(OpCodes.Stloc, bufDataLocal);
            // idx = Convert.ToInt32(index);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Stloc, bufIndexLocal);
            // if (idx < 0) goto outOfRange;
            il.Emit(OpCodes.Ldloc, bufIndexLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, bufOutOfRangeLabel);
            // if (idx >= data.Length) goto outOfRange;
            il.Emit(OpCodes.Ldloc, bufIndexLocal);
            il.Emit(OpCodes.Ldloc, bufDataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, bufOutOfRangeLabel);
            il.Emit(OpCodes.Br, bufInRangeLabel);
            // out-of-range: return NaN (boxed) — matches SharpTSBuffer.this[int] return.
            il.MarkLabel(bufOutOfRangeLabel);
            il.Emit(OpCodes.Ldc_R8, double.NaN);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            // in-range: return (double)data[idx] boxed
            il.MarkLabel(bufInRangeLabel);
            il.Emit(OpCodes.Ldloc, bufDataLocal);
            il.Emit(OpCodes.Ldloc, bufIndexLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }

        // Class instance handler: stringify the key (ECMA ToPropertyKey) and
        // route through GetFieldsProperty(obj, key). Single path handles
        // strings, numbers (-0 → "0", 1.5 → "1.5"), undefined, null, booleans.
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        // GetIndex is ordinary [[Get]] after ToPropertyKey.  Re-enter the
        // shared GetProperty pipeline so primitive receivers walk their
        // Boolean/Number prototypes and arbitrary host/class receivers still
        // reach GetFieldsProperty through GetProperty's final fallback.
        // The old direct GetFieldsProperty call exposed CLR methods instead
        // (false["toString"] returned "False", and numeric prototype methods
        // received an unbound/wrong receiver).
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        // $Object indexed get: route through $Runtime.GetProperty(obj, Stringify(index)).
        // Stringify handles ECMA ToPropertyKey for primitives — Callvirt-on-null
        // and "True"/"False"/.NET-locale-specific number forms are no longer
        // hazards.
        il.MarkLabel(tsObjectIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        // $TSFunction indexed get — same shape as $Object indexed get.
        il.MarkLabel(tsFunctionIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        // $Array handler: route through the long-indexed Get, which returns
        // $Undefined for OOB and unholes hole slots. Matches the descriptor-
        // driven List branches below that already OOB-return undefined —
        // without this, real packages (semver, minimatch, yaml) crash when
        // `arr[i]` runs past the end during parsing.
        il.MarkLabel(tsArrayLabel);

        // Object-valued property keys require ToPropertyKey with string hint
        // before array-index classification. Sending a Dictionary/$Object
        // directly to Convert.ToInt64 leaks InvalidCastException and skips
        // observable toString/valueOf calls (including abrupt completion).
        var tsArrayIndexIsPrimitiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var tsArrayCoerceObjectKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, tsArrayCoerceObjectKeyLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, tsArrayIndexIsPrimitiveLabel);
        il.MarkLabel(tsArrayCoerceObjectKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayIndexIsPrimitiveLabel);

        // Non-numeric string index → route as named-property get (ECMA-262
        // §23.1.5). Convert.ToInt64("foo") throws FormatException — pre-fix
        // the array would crash when verifyProperty did `arr["foo"]` on a
        // PDS-installed named property.
        var tsArrayStringIdxLabel = il.DefineLabel();
        var tsArrayProceedToInt64Label = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, tsArrayStringIdxLabel);
        il.Emit(OpCodes.Br, tsArrayProceedToInt64Label);
        il.MarkLabel(tsArrayStringIdxLabel);
        var tsArrayStrIdxParsed = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, tsArrayStrIdxParsed);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, tsArrayProceedToInt64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayProceedToInt64Label);

        // ECMA-262 6.1.7: array indexes are uint32 < 2^32-1. Indexes ≥ 2^32-1
        // (or negative) are NOT array indexes — they're regular named
        // properties. Route those via GetProperty(arr, idx.ToString()) so
        // PDS-stored values (from the symmetric SetIndex path) round-trip.
        var doArrayGetLabel = il.DefineLabel();
        var routeAsNamedGetLabel = il.DefineLabel();
        var convertArrayIndexLabel = il.DefineLabel();
        var tsArrayGetIdx = il.DeclareLocal(_types.Int64);
        var tsArrayStringKey = il.DeclareLocal(_types.String);
        var tsArrayParsedIndex = il.DeclareLocal(_types.UInt32);

        // String keys are array indices only when they are canonical uint32
        // strings other than 2^32-1. Convert.ToInt64 would otherwise collapse
        // "-0" to element 0 and throw for ordinary names such as "length".
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, tsArrayStringKey);
        il.Emit(OpCodes.Brfalse, convertArrayIndexLabel);
        il.Emit(OpCodes.Ldloc, tsArrayStringKey);
        il.Emit(OpCodes.Ldloca, tsArrayParsedIndex);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, routeAsNamedGetLabel);
        il.Emit(OpCodes.Ldloc, tsArrayParsedIndex);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, routeAsNamedGetLabel);
        il.Emit(OpCodes.Ldloc, tsArrayStringKey);
        il.Emit(OpCodes.Ldloca, tsArrayParsedIndex);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, routeAsNamedGetLabel);
        il.Emit(OpCodes.Ldloc, tsArrayParsedIndex);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Stloc, tsArrayGetIdx);
        il.Emit(OpCodes.Br, doArrayGetLabel);

        il.MarkLabel(convertArrayIndexLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.Object));
        il.Emit(OpCodes.Stloc, tsArrayGetIdx);

        il.Emit(OpCodes.Ldloc, tsArrayGetIdx);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, routeAsNamedGetLabel);
        il.Emit(OpCodes.Ldloc, tsArrayGetIdx);
        il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue - 1);
        il.Emit(OpCodes.Bgt, routeAsNamedGetLabel);
        il.Emit(OpCodes.Br, doArrayGetLabel);

        il.MarkLabel(routeAsNamedGetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doArrayGetLabel);

        // An index can carry an ACCESSOR descriptor: `Object.defineProperty(arr, "0",
        // {get})` (ECMA-262 §10.4.2.1 routes an array-index [[DefineOwnProperty]] through
        // OrdinaryDefineOwnProperty, which accepts accessor descriptors). Those live in the
        // PDS keyed by the index's string form, not in the element storage, so the raw
        // element read below would answer `undefined`. Consult the PDS first.
        var tsArrayNoIdxGetterLabel = il.DefineLabel();
        var tsArrayIdxGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        // Key off the ORIGINAL index argument, via the same ToJsString the named-property
        // route above uses, so `arr[0]` and `arr["0"]` land on one PDS key.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldloca, tsArrayIdxGetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
        il.Emit(OpCodes.Brfalse, tsArrayNoIdxGetterLabel);
        il.Emit(OpCodes.Ldarg_0);                    // receiver
        il.Emit(OpCodes.Ldloc, tsArrayIdxGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);      // empty args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayNoIdxGetterLabel);

        // Setter-only/data descriptors also shadow the backing element.
        var tsArrayIdxDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var tsArrayNoIdxDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsArrayIdxDescriptorLocal);
        il.Emit(OpCodes.Ldloc, tsArrayIdxDescriptorLocal);
        il.Emit(OpCodes.Brfalse, tsArrayNoIdxDescriptorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayNoIdxDescriptorLabel);

        var tsArrayOwnIndex = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, tsArrayGetIdx);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brtrue, tsArrayOwnIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayOwnIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, tsArrayGetIdx);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayGetLong);
        il.Emit(OpCodes.Ret);

        // Descriptor-driven: emit get handler for each backing type.
        // Bounds-checks to match JS array-indexing semantics — out-of-range reads
        // (including negative indices) yield `undefined` rather than an IndexOutOfRangeException.
        // Surfaced by real-package testing: minimatch's `set[set.length - 1]` on an
        // empty array blew up with ArgumentOutOfRangeException before this guard.
        foreach (var (desc, label) in listGetLabels)
        {
            var listType = desc.GetListType(_types);
            il.MarkLabel(label);

            var listLocal = il.DeclareLocal(listType);
            var idxLocal = il.DeclareLocal(_types.Int32);
            var inRangeLabel = il.DefineLabel();
            var oobLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, listType);
            il.Emit(OpCodes.Stloc, listLocal);

            // Non-numeric string index → route as named-property get (ECMA-262
            // §23.1.5 Array exotic objects accept arbitrary named properties).
            // Convert.ToInt32("foo") throws FormatException — pre-fix the array
            // would crash at runtime when verifyProperty did `arr[key]` for a
            // string-keyed prop stored via the symmetric SetIndex+PDS path.
            var listStringIndexLabel = il.DefineLabel();
            var listProceedWithToInt32Label = il.DefineLabel();
            var listNamedPropertyLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brtrue, listStringIndexLabel);
            il.Emit(OpCodes.Br, listProceedWithToInt32Label);
            il.MarkLabel(listStringIndexLabel);
            // If string parses as an integer index, fall through to normal path;
            // otherwise route to GetProperty(arr, name).
            var listStrIdxParsed = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Ldloca, listStrIdxParsed);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, listNamedPropertyLabel);
            // CanonicalNumericIndexString: spellings such as "-0" and "01"
            // are ordinary named properties even though Int32.TryParse accepts
            // them as integer values.
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Ldloca, listStrIdxParsed);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, listProceedWithToInt32Label);
            il.MarkLabel(listNamedPropertyLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listProceedWithToInt32Label);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Stloc, idxLocal);

            // Array literals use the object-list backing type. Indexed
            // accessors installed through Object.defineProperty live in PDS,
            // so consult them before the raw CLR list slot just as the
            // dedicated $Array arm does above.
            var noListIndexGetterLabel = il.DefineLabel();
            var listIndexGetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Ldloca, listIndexGetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
            il.Emit(OpCodes.Brfalse, noListIndexGetterLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listIndexGetterLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noListIndexGetterLabel);

            var listIndexDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var noListIndexDescriptorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listIndexDescriptorLocal);
            il.Emit(OpCodes.Ldloc, listIndexDescriptorLocal);
            il.Emit(OpCodes.Brfalse, noListIndexDescriptorLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noListIndexDescriptorLabel);

            // if (idx < 0) goto oob;
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, oobLabel);
            // if (idx < list.Count) goto inRange;
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count"));
            il.Emit(OpCodes.Blt, inRangeLabel);

            il.MarkLabel(oobLabel);
            // Absent own indices still perform ordinary prototype lookup.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(inRangeLabel);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
            desc.EmitBoxElement(il, _types);
            // Unhole: an in-range slot holding $ArrayHole.Instance reads as
            // `undefined` per ECMA-262 (holes are absent, not present-with-hole).
            // The $Array path already unholes via TSArrayGetLong; plain
            // List<object> receivers (e.g. the List returned by ArrayMap, or a
            // list mutated by `delete arr[i]`) reached here and leaked the raw
            // sentinel — so `[1,2,3,4,5].map(cb-that-deletes)[i]` compared
            // unequal to `undefined`. The isinst is a no-op for value-typed
            // backing lists, which never contain holes.
            var listElementLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, listElementLocal);
            var notHoleLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listElementLocal);
            il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
            il.Emit(OpCodes.Brfalse, notHoleLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notHoleLabel);
            il.Emit(OpCodes.Ldloc, listElementLocal);
            il.Emit(OpCodes.Ret);
        }

        // Native .NET Array handler (e.g., string[] from command line args)
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ArrayType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ArrayType, "GetValue", _types.Int32));
        il.Emit(OpCodes.Ret);

        // KeyValuePair<object, object> handler (Map entries spread into array)
        // Treats the pair as [key, value] tuple: index 0 = Key, index 1 = Value
        il.MarkLabel(kvpLabel);
        var kvpLocal = il.DeclareLocal(kvpType);
        // Unbox the KeyValuePair
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, kvpType);
        il.Emit(OpCodes.Stloc, kvpLocal);
        // Convert index to int
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        // Check if index is 0: return Key
        var kvpIndex1Label = il.DefineLabel();
        var kvpReturnNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, kvpIndex1Label); // If not 0, check for 1
        // Index is 0: return Key
        il.Emit(OpCodes.Pop); // Remove the index
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        // Check if index is 1: return Value
        il.MarkLabel(kvpIndex1Label);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, kvpReturnNullLabel); // If not 1, return null
        // Index is 1: return Value
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        // Index is neither 0 nor 1: return null
        il.MarkLabel(kvpReturnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        var charLocal = il.DeclareLocal(_types.Char);
        var strLocal = il.DeclareLocal(_types.String);
        var intIdxLocal = il.DeclareLocal(_types.Int32);
        var doubleIdxLocal = il.DeclareLocal(_types.Double);
        var strOobLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);
        // Coerce index via Double-or-string parsing — Convert.ToInt32 throws on
        // non-numeric strings ("foo"), but per JS spec `"hello"["foo"]` returns
        // undefined rather than throwing. Use TryParse with fallback.
        // First check if arg1 is double: unbox + Conv_I4. Otherwise TryParse string.
        var idxFromDoubleLabel = il.DefineLabel();
        var idxFromStringLabel = il.DefineLabel();
        var idxParseDoneLabel = il.DefineLabel();
        var strNamedPropertyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, idxFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, idxFromStringLabel);
        // Other property keys are ordinary named properties after
        // ToPropertyKey, not character-index misses.
        il.Emit(OpCodes.Br, strNamedPropertyLabel);

        il.MarkLabel(idxFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, doubleIdxLocal);
        // String exotic indices must be integral finite numeric property
        // keys. Conv_I4 alone maps NaN and fractions to plausible indices
        // (typically 0 / truncation), making s[NaN] or s[1.5] read a real
        // character instead of undefined.
        il.Emit(OpCodes.Ldloc, doubleIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, strOobLabel);
        il.Emit(OpCodes.Ldloc, doubleIdxLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, intIdxLocal);
        il.Emit(OpCodes.Ldloc, doubleIdxLocal);
        il.Emit(OpCodes.Ldloc, intIdxLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, strOobLabel);
        il.Emit(OpCodes.Br, idxParseDoneLabel);

        il.MarkLabel(idxFromStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, intIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        // A non-canonical/non-integer string is a named property (for example
        // "charAt"), so let String.prototype participate in the lookup.
        il.Emit(OpCodes.Brfalse, strNamedPropertyLabel);

        il.MarkLabel(idxParseDoneLabel);
        // Bounds check: if idx < 0 || idx >= length, return undefined (JS semantics)
        il.Emit(OpCodes.Ldloc, intIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, strOobLabel);
        il.Emit(OpCodes.Ldloc, intIdxLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "get_Length"));
        il.Emit(OpCodes.Bge, strOobLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, intIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strNamedPropertyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strOobLabel);
        // JS: str[n] for out-of-bounds n returns undefined (not null). Returning null would
        // make `case undefined:` switches fall through to default, breaking loops that
        // terminate on undefined char reads (e.g. yaml's lexer).
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string — fast-path avoids the Stringify call.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Anything else: route through ECMA ToPropertyKey (Stringify) — covers
        // numeric keys, undefined, null, booleans uniformly.
        il.Emit(OpCodes.Br, dictNumericKeyLabel);

        var valueLocal = il.DeclareLocal(_types.Object);
        var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // Helper: emit the PDS-first lookup. Accessor-only properties keep an
        // undefined placeholder in the dictionary to preserve creation order,
        // so probing backing storage first would bypass their getters.
        void EmitDictLookup(Action emitDict, Action emitKey)
        {
            var foundFieldsLabel = il.DefineLabel();
            var notFoundLabel = il.DefineLabel();

            // A descriptor, when present, is authoritative over its backing
            // value/placeholder and GetProperty applies [[Get]] semantics.
            il.Emit(OpCodes.Ldarg_0);
            emitKey();
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, pdsDescLocal);
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Brfalse, notFoundLabel);
            il.Emit(OpCodes.Ldarg_0);
            emitKey();
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notFoundLabel);
            // No descriptor: retain the direct own-data fast path.
            emitDict();
            emitKey();
            il.Emit(OpCodes.Ldloca, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
            il.Emit(OpCodes.Brtrue, foundFieldsLabel);

            // Ordinary inherited lookup / undefined miss.
            il.Emit(OpCodes.Ldarg_0);
            emitKey();
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(foundFieldsLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(dictStringKeyLabel);
        EmitDictLookup(
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _types.DictionaryStringObject); },
            () => { il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, _types.String); });

        il.MarkLabel(dictNumericKeyLabel);
        EmitDictLookup(
            () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _types.DictionaryStringObject); },
            () => { il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, runtime.ToJsString); });

        // Defunct labels — replaced by EmitDictLookup. Mark unreachable for IL
        // verification balance.
        var foundLabel = il.DefineLabel();
        il.MarkLabel(foundLabel);
        var foundNumLabel = il.DefineLabel();
        il.MarkLabel(foundNumLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.SetIndex = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var typedArraySetLabel = il.DefineLabel();
        var tsBufferSetLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxySetIndexCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), () => il.Emit(OpCodes.Ldarg_2), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // globalThis/global sentinel (#271): `root[stringKey] = v` stores into the
        // shared global-properties dictionary. Symbol keys fall through to the
        // per-object symbol-dict path above.
        var notGlobalThisIdxSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalThisIdxSetLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.GlobalThisSetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notGlobalThisIdxSetLabel);

        // $Buffer (check before TypedArray — IsTypedArray excludes $Buffer).
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, tsBufferSetLabel);
        }

        // TypedArray (check before List since TypedArray is more specific) —
        // gated alongside the handler body below.
        if (_features.HasAnyTypedArray)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.IsTypedArrayMethod);
            il.Emit(OpCodes.Brtrue, typedArraySetLabel);
        }

        // $Array (wrapper around List<object?>) - check before List
        var tsArraySetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArraySetLabel);

        // Descriptor-driven: check each array backing type
        var listSetLabels = new List<(ArrayElementsDescriptor desc, Label label)>();
        foreach (var desc in ArrayElements.All)
        {
            var label = il.DefineLabel();
            listSetLabels.Add((desc, label));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, desc.GetListType(_types));
            il.Emit(OpCodes.Brtrue, label);
        }

        // Dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // System.Type indexed set: route to SetProperty so PDS-backed storage
        // handles `Object["foo"] = X` patterns. Required for propertyHelper.js's
        // isWritable/isConfigurable round-trip via bracket-access set+read on
        // built-in constructors (verifyProperty Object.assign etc.).
        var typeIdxSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeIdxSetLabel);

        // $Object indexed set: route to $Runtime.SetProperty so the value lands
        // in the same _fields store as named property writes. Pre-fix, indexed
        // writes silently dropped on $Object instances (e.g. `new Foo()[0] = 11`).
        var tsObjectIdxSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectIdxSetLabel);

        // $TSFunction indexed set: route to $Runtime.SetProperty so PDS-backed
        // storage handles `fun[0] = 12` patterns (Test262's
        // `Array.prototype.X.call(fnLikeArray, ...)` which decorates functions
        // with indexed elements before iterating them). Reuses the $Object
        // path's index-to-string coercion via SetProperty under the hood.
        var tsFunctionIdxSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionIdxSetLabel);

        // Class instance / unknown receiver fallback: route to SetFieldsProperty
        // with index coerced to string. SetFieldsProperty's own scoped PDS-store
        // fallback handles ad-hoc indexed writes on Date/RegExp/Promise; other
        // unknown types fall through to silent-no-op via SetFieldsProperty's
        // SetMember-reflection-not-found branch.
        // Null index (e.g. unsupported Symbol like Symbol.matchAll) → silent
        // no-op rather than NRE on `null.ToString()`.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Br, classInstanceLabel);

        // Fallthrough: return (ignore)
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        // System.Type indexed set handler — coerce key via Stringify and route
        // to SetProperty (PDS-backed storage on Type receivers).
        il.MarkLabel(typeIdxSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);

        // $Object indexed set handler: SetProperty(obj, Stringify(index), value).
        // Stringify performs ECMA ToPropertyKey for primitives — null→"null",
        // undefined→"undefined", -0→"0", etc.
        il.MarkLabel(tsObjectIdxSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);

        // $TSFunction indexed set — coerce key via Stringify and route to SetProperty.
        il.MarkLabel(tsFunctionIdxSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: ECMA-262 §10.1.9 OrdinarySetWithOwnDescriptor —
        // honor non-extensibility for new symbol keys, mirror the string-key
        // path. If frozen/sealed/non-extensible (via CWT) AND the symbol key
        // isn't already present in the symbol dict, silently no-op (non-
        // strict).
        il.MarkLabel(symbolKeyLabel);
        {
            // #266: symbol-keyed class accessor setter (`set [Symbol.x](v) {...}`).
            // A registered setter takes the write (accessor semantics) instead of
            // storing a data property. Found setter is a MethodInfo invoked with the
            // receiver + value — instance setters bind `this`, static ignore it.
            var noSymSetterLabel = il.DefineLabel();
            var symSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.FindSymbolSetter);
            il.Emit(OpCodes.Stloc, symSetterLocal);
            il.Emit(OpCodes.Ldloc, symSetterLocal);
            il.Emit(OpCodes.Brfalse, noSymSetterLabel);
            // ((MethodBase)setter).Invoke(obj, new object[] { value }); return;
            il.Emit(OpCodes.Ldloc, symSetterLocal);
            il.Emit(OpCodes.Castclass, _types.MethodBase);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noSymSetterLabel);

            var symDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
            var symExistingValueLocal = il.DeclareLocal(_types.Object);
            var symExistingDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, symDictLocal);

            // Frozen objects reject writes to existing symbol properties too.
            // (Sealed/non-extensible objects may still update an existing
            // writable property, so only the frozen table is checked here.)
            var symFrozenStateLocal = il.DeclareLocal(_types.Object);
            var symReceiverNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, symFrozenStateLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.ConditionalWeakTable, "TryGetValue",
                _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, symReceiverNotFrozenLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symReceiverNotFrozenLabel);

            // Existing descriptor entries apply ordinary setter/writable
            // semantics while preserving their attributes.
            var symRawSetLabel = il.DefineLabel();
            var symCreateLabel = il.DefineLabel();
            var symCheckExtensibilityLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symDictLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, symExistingValueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            il.Emit(OpCodes.Brfalse, symCheckExtensibilityLabel);
            il.Emit(OpCodes.Ldloc, symExistingValueLocal);
            il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Stloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Brfalse, symRawSetLabel);

            var symNoSetterLabel = il.DefineLabel();
            var symSetterValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, symSetterValueLocal);
            il.Emit(OpCodes.Ldloc, symSetterValueLocal);
            il.Emit(OpCodes.Brfalse, symNoSetterLabel);
            il.Emit(OpCodes.Ldloc, symSetterValueLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, symNoSetterLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, symSetterValueLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(symNoSetterLabel);
            // Any accessor descriptor without a callable setter ignores a
            // non-strict write.
            var symDataDescriptorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, symDataDescriptorLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symDataDescriptorLabel);
            var symReturnWithoutSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, symReturnWithoutSetLabel);
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, symReturnWithoutSetLabel);
            il.Emit(OpCodes.Ldloc, symExistingDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symReturnWithoutSetLabel);
            il.Emit(OpCodes.Ret);

            // Check extensibility (frozen/sealed/preventExt). On hit, no-op.
            il.MarkLabel(symCheckExtensibilityLabel);
            var symExtTmp = il.DeclareLocal(_types.Object);
            var symNotNonExtLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, runtime.NonExtensibleObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, symExtTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, symNotNonExtLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symNotNonExtLabel);

            var symNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, symExtTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, symNotFrozenLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symNotFrozenLabel);

            var symNotSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, symExtTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, symNotSealedLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(symNotSealedLabel);

            // A newly-created symbol property is an ordinary data descriptor,
            // not an attribute-less raw side-table value. The descriptor ctor
            // supplies writable/enumerable/configurable = true.
            il.Emit(OpCodes.Br, symCreateLabel);
            il.MarkLabel(symRawSetLabel);
            il.Emit(OpCodes.Ldloc, symDictLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item"));
            il.Emit(OpCodes.Ret);

            il.MarkLabel(symCreateLabel);
            var newSymbolDescriptorLocal = il.DeclareLocal(
                runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, newSymbolDescriptorLocal);
            il.Emit(OpCodes.Ldloc, newSymbolDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt,
                runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, symDictLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, newSymbolDescriptorLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryObjectObject, "set_Item"));
            il.Emit(OpCodes.Ret);
        }

        // TypedArray handler — skipped when typed arrays aren't emitted.
        if (_features.HasAnyTypedArray)
        {
            il.MarkLabel(typedArraySetLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);
            il.Emit(OpCodes.Ret);
        }

        // $Buffer handler: data[idx] = (byte)(Convert.ToInt32(value) & 0xFF).
        // Matches SharpTSBuffer.this[int]= semantics: out-of-range is a no-op.
        // Gated together with the dispatch arm above.
        if (_features.UsesBuffer)
        {
            il.MarkLabel(tsBufferSetLabel);
            var bufSetDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
            var bufSetIndexLocal = il.DeclareLocal(_types.Int32);
            var bufSetDoneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Call, runtime.TSBufferGetData);
            il.Emit(OpCodes.Stloc, bufSetDataLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Stloc, bufSetIndexLocal);
            // if (idx < 0) goto done;
            il.Emit(OpCodes.Ldloc, bufSetIndexLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, bufSetDoneLabel);
            // if (idx >= data.Length) goto done;
            il.Emit(OpCodes.Ldloc, bufSetIndexLocal);
            il.Emit(OpCodes.Ldloc, bufSetDataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, bufSetDoneLabel);
            // data[idx] = (byte)(Convert.ToInt32(value) & 0xFF);
            il.Emit(OpCodes.Ldloc, bufSetDataLocal);
            il.Emit(OpCodes.Ldloc, bufSetIndexLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Ldc_I4, 0xFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Stelem_I1);
            il.MarkLabel(bufSetDoneLabel);
            il.Emit(OpCodes.Ret);
        }

        // Class instance / unknown handler: SetFieldsProperty(obj, Stringify(index), value).
        // Stringify covers ECMA ToPropertyKey for primitives so numeric / undefined /
        // bool indexes round-trip through PDS for built-ins (Date/RegExp/Promise) per
        // SetFieldsProperty's scoped PDS-store fallback.
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // $Array handler: unwrap to List<object?> elements, then set
        il.MarkLabel(tsArraySetLabel);
        // Route through the long-indexed Set, which handles:
        //   - auto-extend beyond current length (JS `arr[5] = x` on an empty
        //     array creates holes and extends — without this, real packages
        //     like semver crashed with ArgumentOutOfRangeException on the
        //     `regexp.src[index] = value` idiom)
        //   - sparse transition past SparseThreshold
        //   - internal _isFrozen check (silently no-ops on frozen arrays;
        //     strict-mode wraps via SetIndexStrict)
        // Legacy FrozenObjectsField check kept for pre-M2 paths that froze
        // the $Array via the global weak table instead of arr.Freeze().
        // Per ECMA-262 6.1.7: array indexes are uint32 < 2^32-1. Indexes ≥
        // 2^32-1 are NOT array indexes — they're regular named properties.
        // Route those via SetProperty(arr, idx.ToString(), value) so the
        // $Array PDS-data-store fallback picks them up. Without this, $Array.Set
        // throws RangeError for `a[4294967295] = X`.
        var tsArrayFrozenLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, tsArrayFrozenLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, nullLabel); // Frozen — silently return

        var tsArraySetKeyLocal = il.DeclareLocal(_types.String);
        var idxLong = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, tsArraySetKeyLocal);

        var routeAsNamedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsArraySetKeyLocal);
        il.Emit(OpCodes.Ldloca, idxLong);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Int64, "TryParse", _types.String, _types.Int64.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, routeAsNamedLabel);

        // Non-extensible check: spec ECMA-262 §10.4.2 Array exotic [[Set]]
        // delegates to OrdinarySet which rejects new-property additions on
        // non-extensible receivers. For arrays, "new" means index >= length.
        var tsArrayExtensibleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsExtensible);
        il.Emit(OpCodes.Brtrue, tsArrayExtensibleLabel);
        // Non-extensible: silently return if idx >= length (new index).
        il.Emit(OpCodes.Ldloc, idxLong);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge, nullLabel);
        il.MarkLabel(tsArrayExtensibleLabel);

        // If idx < 0 OR idx >= 2^32-1, route to SetProperty (named property).
        var doArraySetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, idxLong);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, routeAsNamedLabel);
        il.Emit(OpCodes.Ldloc, idxLong);
        il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue - 1);
        il.Emit(OpCodes.Bgt, routeAsNamedLabel);
        il.Emit(OpCodes.Br, doArraySetLabel);

        il.MarkLabel(routeAsNamedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArraySetKeyLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doArraySetLabel);

        // Indexed descriptors participate in [[Set]] before array storage.
        var tsArraySetDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var tsArraySetRawStorage = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArraySetKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsArraySetDescriptorLocal);
        il.Emit(OpCodes.Ldloc, tsArraySetDescriptorLocal);
        il.Emit(OpCodes.Brfalse, tsArraySetRawStorage);
        var tsArraySetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, tsArraySetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, tsArraySetterLocal);
        var tsArrayNoSetter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsArraySetterLocal);
        il.Emit(OpCodes.Brfalse, tsArrayNoSetter);
        il.Emit(OpCodes.Ldloc, tsArraySetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArraySetterLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayNoSetter);
        il.Emit(OpCodes.Ldloc, tsArraySetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, nullLabel); // getter-only accessor
        il.Emit(OpCodes.Ldloc, tsArraySetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.MarkLabel(tsArraySetRawStorage);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, idxLong);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLong);
        il.Emit(OpCodes.Ret);

        // Descriptor-driven: emit set handler for each backing type
        foreach (var (desc, label) in listSetLabels)
        {
            var listType = desc.GetListType(_types);
            il.MarkLabel(label);

            var listSetKeyLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Stloc, listSetKeyLocal);

            // Lists (including $Arguments) can carry ordinary named
            // properties in the descriptor store. Only canonical integer
            // keys belong in the indexed storage path; route every other key
            // through SetProperty so writable/accessor semantics are shared
            // with dot-property writes instead of Convert.ToInt32 throwing.
            var listSetNumericKeyLabel = il.DefineLabel();
            var listSetParsedIndexLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldloc, listSetKeyLocal);
            il.Emit(OpCodes.Ldloca, listSetParsedIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
            il.Emit(OpCodes.Brtrue, listSetNumericKeyLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listSetKeyLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetNumericKeyLabel);

            var listSetDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var listSetCanCreate = il.DefineLabel();
            var listSetRawStorage = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listSetKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listSetDescriptorLocal);
            il.Emit(OpCodes.Ldloc, listSetDescriptorLocal);
            il.Emit(OpCodes.Brfalse, listSetCanCreate);
            var listSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, listSetDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, listSetterLocal);
            var listNoSetter = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listSetterLocal);
            il.Emit(OpCodes.Brfalse, listNoSetter);
            il.Emit(OpCodes.Ldloc, listSetterLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, nullLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listSetterLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listNoSetter);
            il.Emit(OpCodes.Ldloc, listSetDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, nullLabel);
            il.Emit(OpCodes.Ldloc, listSetDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, nullLabel);
            il.Emit(OpCodes.Br, listSetRawStorage);

            il.MarkLabel(listSetCanCreate);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listSetKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
            il.Emit(OpCodes.Brfalse, nullLabel);
            il.MarkLabel(listSetRawStorage);

            if (desc.Kind == ArrayElementsKind.Object)
            {
                // Object list has frozen check before mutation
                var listFrozenCheckLocal = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, listFrozenCheckLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
                il.Emit(OpCodes.Brtrue, nullLabel); // Frozen - silently return
                // Use SetArrayElement for JS-spec auto-extend semantics (list[N] = v on
                // an array with length < N must zero-pad up to N). Matches the typed-list
                // branch below — direct set_Item throws ArgumentOutOfRangeException for
                // out-of-bounds writes, which real npm packages hit (e.g., semver re.js
                // `src[index] = value` where `index = R++` runs past the initial empty list).
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, listType);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, desc.GetSetArrayElementMethod(runtime));
            }
            else
            {
                // Typed list: cast, convert index, convert value to element type, use SetArrayElement helper
                var convertMethod = desc.Kind == ArrayElementsKind.Double
                    ? _types.GetMethod(_types.Convert, "ToDouble", _types.Object)
                    : _types.GetMethod(_types.Convert, "ToBoolean", _types.Object);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, listType);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, convertMethod);
                il.Emit(OpCodes.Call, desc.GetSetArrayElementMethod(runtime));
            }
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(dictLabel);
        // Math singleton: silently no-op writes to non-writable spec
        // constants (E/LN10/LN2/LOG10E/LOG2E/PI/SQRT1_2/SQRT2 per
        // ECMA-262 §21.3.1 — W:F,E:F,C:F). Without this guard the
        // bracket-write stores in the dict and subsequent reads return
        // the mutated value, breaking propertyHelper's isWritable check.
        var dictSkipMathConstLabel = il.DefineLabel();
        var dictNotMathLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, dictNotMathLabel);
        // Argument 1 must be a string key matching a constant name.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        var mathKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, mathKeyLocal);
        il.Emit(OpCodes.Ldloc, mathKeyLocal);
        il.Emit(OpCodes.Brfalse, dictNotMathLabel);
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        void SkipIfMathConst(string n)
        {
            il.Emit(OpCodes.Ldloc, mathKeyLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, dictSkipMathConstLabel);
        }
        SkipIfMathConst("E"); SkipIfMathConst("LN10"); SkipIfMathConst("LN2");
        SkipIfMathConst("LOG10E"); SkipIfMathConst("LOG2E"); SkipIfMathConst("PI");
        SkipIfMathConst("SQRT1_2"); SkipIfMathConst("SQRT2");
        il.Emit(OpCodes.Br, dictNotMathLabel);
        il.MarkLabel(dictSkipMathConstLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dictNotMathLabel);
        // Route through SetProperty so PDS setter accessors fire. Pre-fix,
        // this branch wrote directly to dict._fields, bypassing any
        // Object.defineProperty(obj, k, {set: ...}) accessor — `obj[1] = v`
        // landed in _fields without invoking the setter, and a subsequent
        // `obj[1]` read would shadow the PDS getter (return _fields' value
        // instead of firing the get accessor). SetProperty's PDSTryGetSetter
        // branch invokes the setter; if no PDS setter exists it falls
        // through to dict.set_Item — same as the previous direct write.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Anything else: ECMA ToPropertyKey via Stringify (covers numeric,
        // undefined, null, booleans uniformly).
        il.Emit(OpCodes.Br, dictNumericKeyLabel);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DeleteIndex(object obj, object key) -> bool
    /// Handles both symbol keys and string keys for delete operations.
    /// </summary>
    private void EmitDeleteIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitDeleteIndexCore(typeBuilder, runtime, strict: false);

    /// <summary>
    /// Emits DeleteIndex(object obj, object key) -> bool (non-strict) or
    /// DeleteIndexStrict(object obj, object key, bool strictMode) -> bool.
    /// Handles both symbol keys and string keys. On a frozen/sealed dictionary
    /// receiver the non-strict variant returns false; the strict variant throws
    /// a TypeError when strictMode is set, else returns false. All other
    /// branches ($TSFunction, $Array, symbol keys, System.Type, PDS
    /// configurability, Math/JSON singletons) are identical in both variants.
    /// </summary>
    private void EmitDeleteIndexCore(TypeBuilder typeBuilder, EmittedRuntime runtime, bool strict)
    {
        var method = typeBuilder.DefineMethod(
            strict ? "DeleteIndexStrict" : "DeleteIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            strict ? [_types.Object, _types.Object, _types.Boolean] : [_types.Object, _types.Object]
        );
        if (strict)
            runtime.DeleteIndexStrict = method;
        else
            runtime.DeleteIndex = method;

        var il = method.GetILGenerator();

        // Emits the failed-delete path for the dict receiver: strict mode
        // (arg 2 set) throws TypeError, otherwise returns false.
        void EmitDeleteIndexFail(string message)
        {
            if (strict)
            {
                var sloppyLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Brfalse, sloppyLabel);
                EmitThrowTypeError(il, runtime, message);
                il.MarkLabel(sloppyLabel);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null check on obj - return true (deleting from null is allowed in JS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

        // Check if index is a symbol first
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // Function-like runtime wrappers — `delete fn.name` / `delete
        // fn.length` records the
        // deletion in the per-instance set so HasOwnPropertyHelper /
        // GetFunctionMethod / ObjectGetOwnPropertyDescriptor stop reporting
        // the synthetic value. ECMA-262 §17 declares these as configurable;
        // pre-fix this fell through to trueLabel without recording, so
        // verifyProperty's isConfigurable (delete + re-check hasOwn) failed.
        var tsFunctionDeleteIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionDeleteIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Brtrue, tsFunctionDeleteIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Brtrue, tsFunctionDeleteIdxLabel);

        // $Array — `delete arr[i]` turns the slot into a hole via DeleteAt.
        // Must come BEFORE the trueLabel fallthrough so we actually delete;
        // the pre-M3 code just returned true without mutating.
        var tsArrayDeleteIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayDeleteIdxLabel);

        // $Arguments / legacy List<object> array carriers use ArrayHole for
        // deleted indexed properties, while retaining their stable backing
        // Count and (for arguments) separate visible length.
        var listDeleteIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listDeleteIdxLabel);

        // Dict<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // System.Type - route to DeleteProperty so Type-specific configurability
        // rules (non-configurable prototype/name/length + Number constants vs
        // configurable static methods) and the per-Type deletion tracker apply.
        // Required for bracket-delete on built-in constructors (propertyHelper's
        // isConfigurable round-trip via `delete obj[name]`).
        var typeDelIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeDelIdxLabel);

        // Remaining runtime-backed objects use the named-property delete path
        // after ToPropertyKey coercion. This is essential for PDS-only own
        // properties on Error/Date/RegExp/Promise instances; returning true
        // without deleting made configurable descriptors remain observable.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        if (strict)
        {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        }
        else
        {
            il.Emit(OpCodes.Call, runtime.DeleteProperty);
        }
        il.Emit(OpCodes.Ret);

        // Type delete handler — coerce key via Stringify and call DeleteProperty.
        il.MarkLabel(typeDelIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.DeleteProperty);
        il.Emit(OpCodes.Ret);

        // $TSFunction handler: honor frozen/sealed + PDS configurability before
        // recording the deletion. Mirrors DeleteProperty's $TSFunction path so
        // bracket-form delete on a sealed function (verifyProperty's
        // isConfigurable check) returns false instead of silently removing.
        il.MarkLabel(tsFunctionDeleteIdxLabel);
        {
            var tsFnIdxKeyStr = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Stloc, tsFnIdxKeyStr);

            var tsFnIdxTmp = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnIdxTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnIdxNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnIdxNotFrozenLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsFnIdxNotFrozenLabel);
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnIdxTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnIdxNotSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnIdxNotSealedLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsFnIdxNotSealedLabel);
            var tsFnIdxDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, tsFnIdxKeyStr);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsFnIdxDescLocal);
            var tsFnIdxNoPdsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnIdxDescLocal);
            il.Emit(OpCodes.Brfalse, tsFnIdxNoPdsLabel);
            il.Emit(OpCodes.Ldloc, tsFnIdxDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var tsFnIdxConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsFnIdxConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnIdxConfigurableLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, tsFnIdxKeyStr);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(tsFnIdxNoPdsLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, tsFnIdxKeyStr);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // $Array handler: convert index to long, call DeleteAt, return true.
        // DeleteAt silently no-ops for frozen arrays / OOB indices (JS-spec).
        // Non-numeric string keys route to DeleteProperty for PDS-stored named
        // properties — pre-fix Convert.ToInt64("foo") threw FormatException,
        // crashing propertyHelper.js's isConfigurable check on frozen arrays.
        il.MarkLabel(tsArrayDeleteIdxLabel);
        {
            var tsArrDelStrLabel = il.DefineLabel();
            var tsArrDelProceedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, tsArrDelProceedLabel);
            var tsArrDelStrParsed = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Ldloca, tsArrDelStrParsed);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
            il.Emit(OpCodes.Brtrue, tsArrDelProceedLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, runtime.DeleteProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelProceedLabel);
        }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.Object));
        var tsArrayDeleteIndexLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, tsArrayDeleteIndexLocal);
        var tsArrayDeleteKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, tsArrayDeleteKeyLocal);

        // An indexed descriptor governs configurability even though the array
        // element itself is backed by dense/sparse storage.
        var tsArrayDeleteDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var tsArrayDeleteStorage = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArrayDeleteKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsArrayDeleteDescriptorLocal);
        il.Emit(OpCodes.Ldloc, tsArrayDeleteDescriptorLocal);
        il.Emit(OpCodes.Brfalse, tsArrayDeleteStorage);
        il.Emit(OpCodes.Ldloc, tsArrayDeleteDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        var tsArrayDeleteConfigurable = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, tsArrayDeleteConfigurable);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayDeleteConfigurable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArrayDeleteKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(tsArrayDeleteStorage);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, tsArrayDeleteIndexLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayDeleteAt);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // List<object> handler: honor an indexed PDS descriptor, then replace
        // an in-range live slot with the shared hole sentinel.
        il.MarkLabel(listDeleteIdxLabel);
        {
            var listDeleteKeyLocal = il.DeclareLocal(_types.String);
            var listDeleteIndexLocal = il.DeclareLocal(_types.Int32);
            var listDeleteNotNumeric = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Stloc, listDeleteKeyLocal);

            // SetIntegrityLevel marks the List-backed receiver frozen/sealed
            // in PDS state. Deletion must fail even when an older descriptor
            // object still carries configurable=true.
            var listDeleteNotFrozen = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, listDeleteNotFrozen);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listDeleteNotFrozen);
            var listDeleteNotSealed = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsSealed);
            il.Emit(OpCodes.Brfalse, listDeleteNotSealed);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listDeleteNotSealed);

            il.Emit(OpCodes.Ldloc, listDeleteKeyLocal);
            il.Emit(OpCodes.Ldloca, listDeleteIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, listDeleteNotNumeric);

            var listDeleteDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var listDeleteStorage = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listDeleteKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listDeleteDescriptorLocal);
            il.Emit(OpCodes.Ldloc, listDeleteDescriptorLocal);
            il.Emit(OpCodes.Brfalse, listDeleteStorage);
            il.Emit(OpCodes.Ldloc, listDeleteDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var listDeleteConfigurable = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, listDeleteConfigurable);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listDeleteConfigurable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listDeleteKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(listDeleteStorage);
            var listDeleteDone = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listDeleteIndexLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, listDeleteDone);
            il.Emit(OpCodes.Ldloc, listDeleteIndexLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.ListOfObject);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Bge, listDeleteDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.ListOfObject);
            il.Emit(OpCodes.Ldloc, listDeleteIndexLocal);
            il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "set_Item", [_types.Int32, _types.Object]));
            il.MarkLabel(listDeleteDone);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(listDeleteNotNumeric);
            // Named properties on List-backed arguments live entirely in PDS.
            // Delete them here instead of delegating to DeleteProperty, whose
            // receiver table intentionally has no raw-List branch.
            var listNamedDeleteDescriptor = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listDeleteKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listNamedDeleteDescriptor);
            var listNamedDeleteDone = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listNamedDeleteDescriptor);
            il.Emit(OpCodes.Brfalse, listNamedDeleteDone);
            il.Emit(OpCodes.Ldloc, listNamedDeleteDescriptor);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var listNamedDeleteConfigurable = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, listNamedDeleteConfigurable);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listNamedDeleteConfigurable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, listDeleteKeyLocal);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(listNamedDeleteDone);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // Symbol key handler: honor frozen/sealed (same rationale as the
        // string-key dict path) before falling through to GetSymbolDict.Remove.
        // ECMA-262 §10.1.10 OrdinaryDelete: a non-configurable own property
        // refuses [[Delete]] — Object.seal/freeze mark every own descriptor
        // non-configurable, so symbol-keyed entries on a sealed/frozen object
        // must also reject delete. Pre-fix `delete obj[sym]` returned true
        // for sealed objects with symbol props.
        il.MarkLabel(symbolKeyLabel);
        var symDelObjLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symDelObjLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var symDelNotFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, symDelNotFrozenLabel);
        EmitDeleteIndexFail("Cannot delete a non-configurable symbol property");
        il.MarkLabel(symDelNotFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symDelObjLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var symDelNotSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, symDelNotSealedLabel);
        EmitDeleteIndexFail("Cannot delete a non-configurable symbol property");
        il.MarkLabel(symDelNotSealedLabel);
        // A symbol descriptor can itself be non-configurable even when the
        // containing object is not sealed or frozen.
        var symDeleteDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var symDeleteValueLocal = il.DeclareLocal(_types.Object);
        var symDeleteDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var symDeleteAllowedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDeleteDictLocal);
        il.Emit(OpCodes.Ldloc, symDeleteDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, symDeleteValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, symDeleteAllowedLabel);
        il.Emit(OpCodes.Ldloc, symDeleteValueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Stloc, symDeleteDescriptorLocal);
        il.Emit(OpCodes.Ldloc, symDeleteDescriptorLocal);
        il.Emit(OpCodes.Brfalse, symDeleteAllowedLabel);
        il.Emit(OpCodes.Ldloc, symDeleteDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, symDeleteAllowedLabel);
        EmitDeleteIndexFail("Cannot delete a non-configurable symbol property");
        il.MarkLabel(symDeleteAllowedLabel);
        il.Emit(OpCodes.Ldloc, symDeleteDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "Remove", _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if frozen
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);
        // Frozen - fail (strict throws / sloppy returns false)
        EmitDeleteIndexFail("Cannot delete property of a frozen object");

        // Check if sealed
        il.MarkLabel(notFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSealedLabel);
        // Sealed - fail (strict throws / sloppy returns false)
        EmitDeleteIndexFail("Cannot delete property of a sealed object");

        // Coerce key to string, then PDS-check before dict.Remove. Bracket-
        // access delete on RegExp.prototype["dotAll"] (or similar PDS-installed
        // accessor) needs the same configurability check + PDS+dict cleanup
        // as `delete obj.name` (DeleteProperty); without this the dict-only
        // Remove returns false and the PDS entry survives. ECMA-262 §10.1.10.
        il.MarkLabel(notSealedLabel);
        var didxKeyStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, didxKeyStrLocal);
        var didxAfterKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, didxAfterKeyLabel);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, didxKeyStrLocal);

        il.MarkLabel(didxAfterKeyLabel);
        // PDS lookup for configurability + PDS cleanup.
        var didxDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, didxKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, didxDescLocal);
        var didxNoPdsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, didxDescLocal);
        il.Emit(OpCodes.Brfalse, didxNoPdsLabel);
        il.Emit(OpCodes.Ldloc, didxDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        var didxConfigurableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, didxConfigurableLabel);
        // Non-configurable PDS descriptor — return false without removing.
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(didxConfigurableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, didxKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(didxNoPdsLabel);

        // Always also remove from the dict (data entry without PDS).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, didxKeyStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        // Math singleton: reject delete on non-configurable spec constants
        // (E/LN10/.../SQRT2 per ECMA-262 §21.3.1 — C:F).
        var didxNotMathConstLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, didxNotMathConstLabel);
        var didxFalseRetLabel = il.DefineLabel();
        void DidxRejectIfMathConst(string n)
        {
            il.Emit(OpCodes.Ldloc, didxKeyStrLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, didxFalseRetLabel);
        }
        DidxRejectIfMathConst("E"); DidxRejectIfMathConst("LN10"); DidxRejectIfMathConst("LN2");
        DidxRejectIfMathConst("LOG10E"); DidxRejectIfMathConst("LOG2E"); DidxRejectIfMathConst("PI");
        DidxRejectIfMathConst("SQRT1_2"); DidxRejectIfMathConst("SQRT2");
        il.MarkLabel(didxNotMathConstLabel);

        // Math/JSON singleton: also mark the deletion in the per-receiver
        // tracker so HasOwnPropertyHelper's synth-name check stops reporting
        // the property as own (the dicts are empty; the static names are
        // what makes them "own").
        var didxMarkDelLabel = il.DefineLabel();
        var didxAfterMarkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Beq, didxMarkDelLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Beq, didxMarkDelLabel);
        il.Emit(OpCodes.Br, didxAfterMarkLabel);
        il.MarkLabel(didxMarkDelLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, didxKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
        il.MarkLabel(didxAfterMarkLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(didxFalseRetLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Return true (default)
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDeleteIndexStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitDeleteIndexCore(typeBuilder, runtime, strict: true);

    /// <summary>
    /// Emits inline IL for the RegExp.prototype symbol-keyed dispatch path.
    /// Stack on entry: empty.
    /// Stack on exit: empty (control falls through if no match) OR returns
    /// from the enclosing method with a $TSFunction value.
    ///
    /// Compares Ldarg_1 against each well-known RegExp symbol field
    /// (Symbol.match, etc.). On match, constructs a $TSFunction wrapping the
    /// corresponding static helper on $RegExp with the regex bound as
    /// `_target`, and returns it.
    /// </summary>
    private void EmitRegExpSymbolDispatch(ILGenerator il, EmittedRuntime runtime)
    {
        EmitRegExpSymbolCase(il, runtime, runtime.SymbolMatch, runtime.TSRegExpSymMatchHelper);
        EmitRegExpSymbolCase(il, runtime, runtime.SymbolMatchAll, runtime.TSRegExpSymMatchAllHelper);
        EmitRegExpSymbolCase(il, runtime, runtime.SymbolReplace, runtime.TSRegExpSymReplaceHelper);
        EmitRegExpSymbolCase(il, runtime, runtime.SymbolSearch, runtime.TSRegExpSymSearchHelper);
        EmitRegExpSymbolCase(il, runtime, runtime.SymbolSplit, runtime.TSRegExpSymSplitHelper);
    }

    /// <summary>
    /// One symbol-vs-helper comparison: if Ldarg_1 == knownSymbol, return a
    /// $TSFunction(target=Ldarg_0, method=helper). Otherwise fall through.
    /// </summary>
    private void EmitRegExpSymbolCase(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder symbolField, MethodBuilder helperMethod)
    {
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, symbolField);
        il.Emit(OpCodes.Bne_Un, notMatchLabel);

        // return new $TSFunction(rx, MethodInfo of helper)
        il.Emit(OpCodes.Ldarg_0);
        _types.EmitLoadMethodInfo(il, helperMethod);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatchLabel);
    }
}

