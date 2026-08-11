using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

// Per-receiver branches sliced out of EmitGetProperty (RuntimeEmitter.Objects.Properties.cs).
//
// Each Emit<Receiver>GetBranch helper follows the shape already established by
// EmitProxyGetPropertyCheck (RuntimeEmitter.Proxy.cs): it emits the receiver-type
// test itself, runs the branch body + `ret` on a match, and otherwise falls through
// to the caller-supplied `notMatch` label. Callers pair each helper with
// `il.MarkLabel(notMatch)` so the dispatch table in EmitGetProperty reads as a flat
// sequence of "is it <receiver>? → handle it, else keep looking" arms.
//
// The receiver arg is always arg0 (the object) and the property name is always arg1,
// matching the GetProperty signature, so these are loaded directly rather than through
// arg-loader delegates. Helpers that recurse into GetProperty for prototype-chain /
// primitive-wrapper lookups take the forward-declared MethodBuilder as `method`.
//
// Only self-contained arms live here — a branch qualifies when its receiver type is
// disjoint from every other arm and its body neither shares locals with, nor is a jump
// target of, any other arm. Interdependent arms (the System.Type/class-instance pair,
// the three-way Promise dispatch, the typed-array family, the bound-callable multi-check,
// and the primitive-prototype fall-throughs) stay inline in EmitGetProperty for now.
public partial class RuntimeEmitter
{
    /// <summary>
    /// $TSNamespace arm: <c>if (obj is $TSNamespace ns) return ns.Get(name);</c>
    /// Falls through to <paramref name="notMatch"/> otherwise.
    /// </summary>
    private void EmitNamespaceGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSNamespaceType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Namespace handler - call ns.Get(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSNamespaceType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSNamespaceGet);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Callable arm shared by $TSFunction and $BoundTSFunction: both route
    /// bind/call/apply/length/name through GetFunctionMethod. <paramref name="receiverType"/>
    /// selects which callable type this arm tests for. Falls through to
    /// <paramref name="notMatch"/> on a miss.
    /// </summary>
    private void EmitFunctionGetBranch(ILGenerator il, EmittedRuntime runtime, Type receiverType, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, receiverType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Function handler - call GetFunctionMethod(func, name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $CJSModule arm: <c>if (obj is $CJSModule mod) return mod.GetMember(name);</c>
    /// Only reached when the program uses CommonJS require/module/exports (the caller
    /// gates both this arm and its dispatch on _features.UsesCjsRequire).
    /// </summary>
    private void EmitCjsModuleGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // $CJSModule handler - call module.GetMember(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.CjsModuleType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(runtime.CjsModuleType, "GetMember", [_types.String])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Map ($Dictionary&lt;object,object&gt;) arm: "size" → Count as a boxed double; every other
    /// name dispatches through GetMapProperty (which returns $BoundMapMethod wrappers). Caller
    /// gates this arm and its dispatch on _features.UsesMap.
    /// </summary>
    private void EmitMapGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Brfalse, notMatch);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notMapSizeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMapSizeLabel);
        // Return map.Count as double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryObjectObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notMapSizeLabel);
        // For other Map properties, dispatch via GetMapProperty — returns a $BoundMapMethod
        // wrapper for known methods (get/set/has/...) so that `typeof m.get === 'function'`
        // and `m.get.call(m, k)` work on a Map received from another module.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetMapProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set (HashSet&lt;object&gt;) arm: dispatch via GetSetProperty (size + $BoundSetMethod
    /// wrappers). Caller gates this arm and its dispatch on _features.UsesSet.
    /// </summary>
    private void EmitSetGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
        il.Emit(OpCodes.Brfalse, notMatch);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetSetProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object[] arm (compiled `arguments`-shape receiver): "length" → array length; numeric-string
    /// keys return the in-bounds element; anything else → undefined.
    /// </summary>
    private void EmitObjectArrayGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Brfalse, notMatch);

        var objArrNotLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, objArrNotLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objArrNotLengthLabel);
        // Try numeric-string index: int.TryParse(name, out i)
        var objArrIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, objArrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var objArrNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, objArrNotIndexLabel);
        // Bounds check: i >= 0 && i < arr.Length
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, objArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Bge, objArrNotIndexLabel);
        // Read element at index
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objArrNotIndexLabel);
        // Other property → undefined
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $Array arm: "length" → sparse-aware LongLength; "constructor" → typeof(IList&lt;object&gt;);
    /// numeric-string keys index the backing list; anything else routes through GetListProperty
    /// ($BoundArrayMethod wrappers). MUST be tested before the plain-List arm since $Array : List.
    /// </summary>
    private void EmitSharpTSArrayGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Object.defineProperty stores array index accessors in the shared
        // descriptor store. They must win over raw list slots for ordinary
        // Get operations and array-iteration element reads.
        var tsArrayDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var noTSArrayDescriptor = il.DefineLabel();
        // `length` is an Array exotic internal slot whose live value changes
        // with mutations; a PDS entry only records its attributes and must not
        // shadow the sparse-aware LongLength getter below.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, noTSArrayDescriptor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noTSArrayDescriptor);
        var tsArrayDataDescriptor = il.DefineLabel();
        var tsArrayGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, tsArrayGetterLocal);
        il.Emit(OpCodes.Ldloc, tsArrayGetterLocal);
        il.Emit(OpCodes.Brfalse, tsArrayDataDescriptor);
        il.Emit(OpCodes.Ldloc, tsArrayGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var tsArrayInvokeGetter = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsArrayInvokeGetter);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayInvokeGetter);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tsArrayGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayDataDescriptor);
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        var tsArrayDataHasNoSetter = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsArrayDataHasNoSetter);
        // Setter-only accessor: own property shadows storage/prototype but Get
        // returns undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayDataHasNoSetter);

        // Valid array-index data properties mirror their live value into the
        // array backing store. Other descriptor-backed expandos (including
        // 2^32-1, which is explicitly not an array index) keep their value in
        // PDS and must return it directly.
        var tsArrayPropertyIndexLocal = il.DeclareLocal(_types.UInt32);
        var returnTSArrayDescriptorValue = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsArrayPropertyIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, returnTSArrayDescriptorValue);
        il.Emit(OpCodes.Ldloc, tsArrayPropertyIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Bne_Un, noTSArrayDescriptor);
        il.MarkLabel(returnTSArrayDescriptorValue);
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        var returnTSArrayDescriptorValuePresent = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, returnTSArrayDescriptorValuePresent);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(returnTSArrayDescriptorValuePresent);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noTSArrayDescriptor);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notSharpTSArrayLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSharpTSArrayLengthLabel);
        // Use the LongLength getter — not the int-clamped Length — so `.length`
        // reads up to 2^32 - 1 survive (M3 acceptance: `a.length === 2147483649`).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSharpTSArrayLengthLabel);
        // ECMA-262: `[].constructor === Array`. Mirror the list branch.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notTSArrayCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTSArrayCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSArrayCtorLabel);
        // Numeric-string index — same purpose as the list branch above:
        // proto-chain walks (`f.__proto__ === [1,2,3]; f[0]`) bottom out here
        // when the prototype is a $Array. Without this, GetListProperty returns
        // null for any digit-string name and the array element is invisible.
        var tsArrIdxLocal = il.DeclareLocal(_types.Int32);
        var tsArrayPrototypeFallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsArrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var tsArrNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, tsArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, tsArrayPrototypeFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        var tsArrayIndexPresent = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, tsArrayIndexPresent);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, tsArrayPrototypeFallback);
        il.MarkLabel(tsArrayPrototypeFallback);
        var tsArrayNoDescriptorFallback = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Brfalse, tsArrayNoDescriptorFallback);
        il.Emit(OpCodes.Ldloc, tsArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayNoDescriptorFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayIndexPresent);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrNotIndexLabel);
        // For other properties on $Array (method names like push/pop/sort/etc.),
        // reuse GetListProperty — it returns the $BoundArrayMethod wrapper, and
        // $Array IS a List<object?> by inheritance, so the cast works.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetListProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Plain List&lt;object&gt; arm: "length" → Count; "constructor" → typeof(IList&lt;object&gt;);
    /// numeric-string keys index in-bounds; then a PDS own-descriptor (RegExp.exec metadata) wins;
    /// everything else routes through GetListProperty. Tested after the $Array arm.
    /// </summary>
    private void EmitListGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Descriptor-backed expandos/accessors win over raw list slots, with
        // the live exotic length slot as the sole exception.
        var listNoDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, listNoDescriptorLabel);
        var listOwnDescriptor = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, listOwnDescriptor);
        il.Emit(OpCodes.Ldloc, listOwnDescriptor);
        il.Emit(OpCodes.Brfalse, listNoDescriptorLabel);
        var listDataDescriptorLabel = il.DefineLabel();
        var listGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, listOwnDescriptor);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, listGetterLocal);
        il.Emit(OpCodes.Ldloc, listGetterLocal);
        il.Emit(OpCodes.Brfalse, listDataDescriptorLabel);
        il.Emit(OpCodes.Ldloc, listGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var listInvokeGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listInvokeGetterLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listInvokeGetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, listGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listDataDescriptorLabel);
        il.Emit(OpCodes.Ldloc, listOwnDescriptor);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, listNoDescriptorLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listNoDescriptorLabel);

        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLengthLabel);
        // ECMA-262: `[].constructor === Array`. Compiled `Array` resolves to
        // typeof(IList<object>) — return that here.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notListCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notListCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notListCtorLabel);
        // Numeric-string index — `GetProperty(list, "0")` must return list[0] so
        // that `f[0]` for `f.__proto__ === [1,2,3]` walks the prototype chain
        // and finds the array element. Without this branch the proto-chain walk
        // bottoms out in GetListProperty's null fallback.
        var listIdxLocal = il.DeclareLocal(_types.Int32);
        var listPrototypeFallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var listNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotIndexLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, listNotIndexLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listPrototypeFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        var listIndexPresentLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, listIndexPresentLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, listPrototypeFallback);
        il.MarkLabel(listPrototypeFallback);
        var listNoDescriptorFallback = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listOwnDescriptor);
        il.Emit(OpCodes.Brfalse, listNoDescriptorFallback);
        il.Emit(OpCodes.Ldloc, listOwnDescriptor);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listNoDescriptorFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listIndexPresentLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listNotIndexLabel);
        // PDS-stored own descriptor (e.g., RegExp.exec result has `index` /
        // `input` / `groups` attached via PropertyDescriptorStore so the
        // returned value can be a real Array exotic — `instanceof Array` true
        // — while still answering `result.index` / `result.input` correctly).
        // Without this, those metadata properties are invisible.
        var listPdsLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, listPdsLocal);
        il.Emit(OpCodes.Ldloc, listPdsLocal);
        var listSkipPdsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listSkipPdsLabel);
        il.Emit(OpCodes.Ldloc, listPdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listSkipPdsLabel);
        // For other properties on List (like methods push, pop, etc.), use GetListProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetListProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $Buffer arm: "length" → boxed double; "toString" → a $TSFunction over ToEncodedString;
    /// anything else → null. Caller gates this arm and its dispatch on _features.UsesBuffer.
    /// </summary>
    private void EmitBufferGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notBufferLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferLenLabel);
        // Get buf.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBufferLenLabel);
        // Check for "toString" - return a wrapper that calls ToEncodedString
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notBufferToStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferToStringLabel);
        // Create a TSFunction wrapper for ToEncodedString
        // For dynamically generated types, we need both method and type tokens
        il.Emit(OpCodes.Ldarg_0);  // target (the buffer)
        il.Emit(OpCodes.Ldtoken, runtime.TSBufferToString);
        il.Emit(OpCodes.Ldtoken, runtime.TSBufferType);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBufferToStringLabel);
        // Unknown buffer property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $Stats arm: "size" → boxed double; each isX() predicate → a $TSFunction over the Stats
    /// helper method; anything else → null. Caller gates this arm and its dispatch on _features.UsesFs.
    /// </summary>
    private void EmitStatsGetBranch(ILGenerator il, EmittedRuntime runtime, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.StatsType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Check for "size" property
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsSizeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsSizeLabel);
        // Return stats.size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.StatsType);
        il.Emit(OpCodes.Call, runtime.StatsSizeGetter);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStatsSizeLabel);

        // Each isX() predicate returns a $TSFunction wrapper over the Stats helper method.
        void EmitStatsMethodWrapper(string jsName, MethodBuilder helper)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, helper);
            il.Emit(OpCodes.Ldtoken, runtime.StatsType);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitStatsMethodWrapper("isFile", runtime.StatsIsFile);
        EmitStatsMethodWrapper("isDirectory", runtime.StatsIsDirectory);
        EmitStatsMethodWrapper("isSymbolicLink", runtime.StatsIsSymbolicLink);
        EmitStatsMethodWrapper("isBlockDevice", runtime.StatsIsBlockDevice);
        EmitStatsMethodWrapper("isCharacterDevice", runtime.StatsIsCharacterDevice);
        EmitStatsMethodWrapper("isFIFO", runtime.StatsIsFIFO);
        EmitStatsMethodWrapper("isSocket", runtime.StatsIsSocket);

        // Unknown stats property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// String arm: "length" → boxed double; "constructor" → typeof(string); numeric-string keys
    /// return the single-char string in-bounds; anything else walks String.prototype (recursing
    /// through <paramref name="method"/>) for borrowed methods (valueOf/toLowerCase/...).
    /// </summary>
    private void EmitStringGetBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStrLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrLenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrLenLabel);
        // ECMA-262: `"hello".constructor === String`. Compiled mode resolves
        // bare `String` to `typeof(string)` — returning that here makes the
        // strict-equality check hold.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStrCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrCtorLabel);

        // Numeric index: `"hello"[0]` returns "h". Pre-fix returned null
        // because the string fallback didn't honor numeric-string keys —
        // only the typed-string dispatch did.
        var notNumericKeyLabel = il.DefineLabel();
        var strIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notNumericKeyLabel);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notNumericKeyLabel);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, notNumericKeyLabel);
        // Return str[idx].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        var charLocalStr = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocalStr);
        il.Emit(OpCodes.Ldloca, charLocalStr);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumericKeyLabel);

        // ECMA-262 7.3.2: walk String.prototype for borrowed-method patterns
        // (`s.valueOf`, `s.toString`, `s.toLowerCase`, etc.). Pre-fix returned
        // null for any property other than length/constructor.
        il.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $Object arm: obj.GetProperty(name) (honouring getters), then an own PDS descriptor,
    /// then a prototype-chain walk (recursing through <paramref name="method"/>), finally the
    /// Object.prototype singleton. Falls through to <paramref name="notMatch"/> on a miss.
    /// </summary>
    private void EmitTSObjectGetBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Object.prototype method short-circuits (Stage 4z15 follow-on):
        // expose hasOwnProperty + isPrototypeOf as $TSFunction wrappers
        // bound to this $Object.
        void EmitTSObjProtoCheck(string jsName, MethodBuilder helper)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitTSObjProtoCheck("hasOwnProperty", runtime.HasOwnPropertyHelperMethod);
        EmitTSObjProtoCheck("isPrototypeOf",  runtime.IsPrototypeOfHelperMethod);

        var tsObjectInstanceLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Stloc, tsObjectInstanceLocal);
        // if (obj.HasProperty(name)) return obj.GetProperty(name)
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        var tsObjectCheckPDS = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsObjectCheckPDS);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Ret);
        // Check PDS for own data descriptor / accessor before walking chain
        il.MarkLabel(tsObjectCheckPDS);
        var tsObjectPDSDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsObjectPDSDescLocal);
        var tsObjectWalkProto = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Brfalse, tsObjectWalkProto);
        // Has own descriptor: getter wins, else return value
        var tsObjectPDSValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, tsObjectPDSValue);
        // Invoke getter via InvokeMethodValue(obj, getter, [])
        var tsObjectGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, tsObjectGetterLocal);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldloc, tsObjectGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjectPDSValue);
        il.Emit(OpCodes.Pop); // discard the null getter
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        // Walk prototype chain
        il.MarkLabel(tsObjectWalkProto);
        var tsObjectProtoLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, tsObjectProtoLocal);
        var tsObjectNoProto = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectProtoLocal);
        il.Emit(OpCodes.Brfalse, tsObjectNoProto);
        // Recursively call GetProperty(prototype, name)
        il.Emit(OpCodes.Ldloc, tsObjectProtoLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjectNoProto);
        // No own prototype — fall back to Object.prototype singleton (mirrors
        // the dict-branch fallback). Catches `({}.toString)` style accesses on
        // $Object instances created without an explicit prototype link.
        var tsObjProtoFallbackMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        var tsObjProtoFallbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsObjProtoFallbackLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tsObjProtoFallbackMissLabel);
        il.Emit(OpCodes.Ldloc, tsObjProtoFallbackLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjProtoFallbackMissLabel);
        // Property absent on object and Object.prototype — return undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// $RegExp arm: an own PDS descriptor wins first (user Object.defineProperty overrides),
    /// then the built-in slots — lastIndex/source/flags/global/ignoreCase/multiline plus the
    /// flag-string-parsed sticky/unicode/hasIndices/dotAll/unicodeSets — else GetFieldsProperty.
    /// Recurses through <paramref name="method"/> to reassemble "flags" from the per-flag reads.
    /// Caller gates this arm and its dispatch on _features.UsesRegExp.
    /// </summary>
    private void EmitRegExpGetBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Brfalse, notMatch);

            var rxLocal = il.DeclareLocal(runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Stloc, rxLocal);

            // ECMA-262 §22.2.6.* read paths go through ordinary Get, so
            // user-installed Object.defineProperty(r, 'flags', {get}) etc.
            // must win over the internal slot. Check PDS first for any name;
            // when a descriptor is present, surface its value (data) or
            // invoke its getter (accessor) before reaching the typed slot
            // fast-paths below. Symbol.match's get-flags-err.js,
            // builtin-coerce-lastindex.js and friends rely on the override
            // path running before the internal-slot read.
            var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, pdsDescLocal);
            var noPdsDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Brfalse, noPdsDescLabel);
            // Accessor descriptor? Getter != null → invoke fn(thisArg=rx).
            var dataDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            var regexpGetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, regexpGetterLocal);
            il.Emit(OpCodes.Ldloc, regexpGetterLocal);
            il.Emit(OpCodes.Brfalse, dataDescLabel);
            // Cast getter to $TSFunction and InvokeWithThis(rx). If the
            // descriptor's getter slot isn't a $TSFunction (shouldn't
            // happen normally), fall through to the data path.
            var regexpFnLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, regexpGetterLocal);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Stloc, regexpFnLocal);
            il.Emit(OpCodes.Ldloc, regexpFnLocal);
            il.Emit(OpCodes.Brfalse, dataDescLabel);
            il.Emit(OpCodes.Ldloc, regexpFnLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(System.Array), "Empty"), _types.Object));
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(dataDescLabel);
            // Data descriptor — return descriptor.Value.
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noPdsDescLabel);

            // Helper closure to emit a name-equality test + branch to a
            // labelled body. Keeps the dispatch table readable.
            void NameMatchBranch(string propName, System.Action emitBody)
            {
                var notThisName = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, propName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, notThisName);
                emitBody();
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notThisName);
            }

            // "lastIndex" — return the raw boxed value when a non-numeric value
            // was assigned (object identity preserved per spec); otherwise the
            // typed int as a boxed double.
            NameMatchBranch("lastIndex", () =>
            {
                var numericLabel = il.DefineLabel();
                var doneLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexBoxedField);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, numericLabel);
                il.Emit(OpCodes.Br, doneLabel);            // boxed non-null → return it
                il.MarkLabel(numericLabel);
                il.Emit(OpCodes.Pop);                      // drop the null
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexGetter);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, _types.Double);
                il.MarkLabel(doneLabel);
            });
            // "constructor" — inherited from RegExp.prototype.constructor, which
            // is the RegExp constructor (the $RegExp Type token, == what `RegExp`
            // evaluates to as a value). Without this an instance read returns
            // undefined and `re.constructor === RegExp` is false, blocking the
            // §22.2.4.1 call-form same-object check. PDS is checked above, so a
            // user `Object.defineProperty(re,'constructor',…)` / `re.constructor=x`
            // still wins.
            NameMatchBranch("constructor", () =>
            {
                il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            });
            // "source" / "flags" — string fields.
            NameMatchBranch("source", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
            });
            // Spec-aligned ECMA-262 §22.2.6.4 — assemble the flags string from
            // individual property reads so user-installed `Object.defineProperty
            // (r, 'global', {get})` overrides participate in the chain. Each
            // Get(rx, propName) goes through this very GetProperty recursively,
            // so PDS-first lookup on the per-flag property fires first; the
            // typed slot fallback returns the same boxed bool we'd have read
            // directly, keeping the assembled string identical to _flags for
            // ordinary $RegExp without overrides. Unlocks Symbol.match/replace/
            // split/search's get-global-err / coerce-global / get-unicode-error
            // test262 family.
            NameMatchBranch("flags", () =>
            {
                var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
                il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Stloc, sbLocal);

                var sbAppendChar = typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(char)])!;
                void AppendIfTruthy(string propName, char ch)
                {
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldstr, propName);
                    il.Emit(OpCodes.Call, method);
                    il.Emit(OpCodes.Call, runtime.IsTruthy);
                    il.Emit(OpCodes.Brfalse, skipLabel);
                    il.Emit(OpCodes.Ldloc, sbLocal);
                    il.Emit(OpCodes.Ldc_I4, (int)ch);
                    il.Emit(OpCodes.Callvirt, sbAppendChar);
                    il.Emit(OpCodes.Pop);
                    il.MarkLabel(skipLabel);
                }

                // Order per ECMA-262 §22.2.6.4.
                AppendIfTruthy("hasIndices", 'd');
                AppendIfTruthy("global", 'g');
                AppendIfTruthy("ignoreCase", 'i');
                AppendIfTruthy("multiline", 'm');
                AppendIfTruthy("dotAll", 's');
                AppendIfTruthy("unicode", 'u');
                AppendIfTruthy("unicodeSets", 'v');
                AppendIfTruthy("sticky", 'y');

                il.Emit(OpCodes.Ldloc, sbLocal);
                il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
            });
            // "global" / "ignoreCase" / "multiline" — boolean fields.
            NameMatchBranch("global", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });
            NameMatchBranch("ignoreCase", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpIgnoreCaseGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });
            NameMatchBranch("multiline", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpMultilineGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });

            // "sticky" / "unicode" / "hasIndices" / "dotAll" / "unicodeSets"
            // — parsed from the flags string. There's no dedicated field for
            // these, so we Contains-check the appropriate char (per ECMA-262
            // §22.2.5.3 flags-string assembly).
            void FlagCharBranch(string propName, char ch)
            {
                NameMatchBranch(propName, () =>
                {
                    il.Emit(OpCodes.Ldloc, rxLocal);
                    il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
                    // s.Contains(ch) – use Contains(char) overload to dodge
                    // string-literal allocation for the single-char arg.
                    il.Emit(OpCodes.Ldc_I4, (int)ch);
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.Char));
                    il.Emit(OpCodes.Box, _types.Boolean);
                });
            }
            FlagCharBranch("sticky", 'y');
            FlagCharBranch("unicode", 'u');
            FlagCharBranch("hasIndices", 'd');
            FlagCharBranch("dotAll", 's');
            FlagCharBranch("unicodeSets", 'v');

            // Other property names fall through to GetFieldsProperty so
            // user-set data and the shared intrinsic-prototype fallback resolve.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
            il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Dictionary&lt;string,object&gt; (plain object) arm: Object.prototype method wrappers, then a
    /// PDS getter, then own dict entry (binding $TSFunction values to the dict as `this`), then the
    /// $AbortSignal duck-typed surface, then a prototype-chain walk (recursing through
    /// <paramref name="method"/>), finally the Object.prototype singleton. Falls through to
    /// <paramref name="notMatch"/> on a miss.
    /// </summary>
    private void EmitDictGetBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, Label notMatch)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notMatch);

        // Object.prototype methods short-circuit: return $TSFunction wrappers
        // for the helper. target=null + cached name+length means the wrapper
        // dispatches via InvokeWithThis (the helper's first param is "__this"
        // so _expectsThis=true), letting .call(receiver, ...) inject the right
        // receiver instead of being shadowed by a target-bound prepending
        // that double-applies and trims the wrong tail. Direct dispatch
        // (`obj.method(args)`) still works because compiled-mode method calls
        // route through InvokeMethodValue → InvokeWithThis with the receiver
        // as thisArg. JS-spec name + length surface to user code via fn.name
        // / fn.length introspection.
        void EmitObjProtoMethodCheck(string jsName, MethodBuilder helper, int jsLength)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldnull);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitObjProtoMethodCheck("hasOwnProperty", runtime.HasOwnPropertyHelperMethod, 1);
        EmitObjProtoMethodCheck("isPrototypeOf",  runtime.IsPrototypeOfHelperMethod, 1);

        // Check for getter accessor via $PropertyDescriptorStore - fully standalone, no reflection
        var getterLocal = il.DeclareLocal(_types.Object);
        var noGetterLabel = il.DefineLabel();

        // Call PDSTryGetGetter(obj, name, out getter)
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Ldloca, getterLocal);  // out getter
        il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
        il.Emit(OpCodes.Brfalse, noGetterLabel);

        // Getter was found - invoke it via InvokeMethodValue(obj, getter, emptyArgs)
        il.Emit(OpCodes.Ldarg_0);  // receiver (obj)
        il.Emit(OpCodes.Ldloc, getterLocal);  // function (getter)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);  // empty args array
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noGetterLabel);

        // A descriptor without an invokable getter still shadows both the
        // dictionary's ordinary storage and its prototype. This includes
        // setter-only accessors, whose [[Get]] result is undefined, and data
        // descriptors stored only in the PDS.
        var ownDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var noOwnDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, ownDescriptorLocal);
        il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noOwnDescriptorLabel);
        var returnDescriptorValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnDescriptorValueLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnDescriptorValueLabel);
        // With no accessor slots this is a data descriptor. Its value is
        // mirrored into the dictionary so ordinary writable assignments keep
        // that canonical storage current; continue to the TryGetValue path.
        il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noOwnDescriptorLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noOwnDescriptorLabel);

        // dict.TryGetValue(name, out value) ? value : check prototype chain
        var valueLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var protoLocal = il.DeclareLocal(_types.Object);

        // Store the dictionary in a local for later use with BindThis
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);

        // $AbortSignal dict surface (#224, #985): the properties "aborted"/"reason"/
        // "onabort" AND the methods "addEventListener"/"removeEventListener"/
        // "throwIfAborted" on a dynamically-typed (`any`) signal receiver. The typed
        // path intercepts at compile time (AbortSignalEmitter); an `any` receiver lands
        // here. The methods are returned as $TSFunction wrappers (target=null,
        // _expectsThis=true via the "__this" first parameter) so a subsequent
        // InvokeMethodValue injects the signal as the receiver — i.e.
        // `signal.addEventListener('abort', cb)` works from inside a helper, not only at
        // a statically-typed call site. Signals are identified by their "_reasonSet"
        // internal slot — the public keys are computed from the CancellationToken, so
        // they are never own dict entries and always reach this miss path. Name screen
        // runs first to keep ordinary dict misses cheap.
        if (_features.UsesAbortController)
        {
            var notSignalPropLabel = il.DefineLabel();
            var signalNameMatchLabel = il.DefineLabel();
            var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

            // Returns a $TSFunction wrapping `helper`, bound to no target, so the
            // wrapper's _expectsThis is set (helper's first param is "__this") and
            // InvokeMethodValue passes the signal receiver through. Same shape as
            // EmitObjProtoMethodCheck's hasOwnProperty/isPrototypeOf wrappers.
            void EmitSignalMethodWrapper(MethodBuilder helper, string jsName, int jsLength)
            {
                il.Emit(OpCodes.Ldnull);
                _types.EmitLoadMethodInfo(il, helper);
                il.Emit(OpCodes.Ldstr, jsName);
                il.Emit(OpCodes.Ldc_I4, jsLength);
                il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
                il.Emit(OpCodes.Ret);
            }

            foreach (var signalProp in new[]
                { "aborted", "reason", "onabort", "addEventListener", "removeEventListener", "throwIfAborted" })
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, signalProp);
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brtrue, signalNameMatchLabel);
            }
            il.Emit(OpCodes.Br, notSignalPropLabel);

            il.MarkLabel(signalNameMatchLabel);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "_reasonSet");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brfalse, notSignalPropLabel);

            var notSignalAbortedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "aborted");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalAbortedLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalAbortedLabel);

            var notSignalReasonLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "reason");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalReasonLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetReason);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalReasonLabel);

            var notSignalOnAbortLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "onabort");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalOnAbortLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetOnAbort);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalOnAbortLabel);

            var notSignalAelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "addEventListener");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalAelLabel);
            EmitSignalMethodWrapper(runtime.AbortSignalAddEventListenerThis, "addEventListener", 2);
            il.MarkLabel(notSignalAelLabel);

            var notSignalRelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "removeEventListener");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalRelLabel);
            EmitSignalMethodWrapper(runtime.AbortSignalRemoveEventListenerThis, "removeEventListener", 2);
            il.MarkLabel(notSignalRelLabel);

            // Only "throwIfAborted" remains among the screened names.
            EmitSignalMethodWrapper(runtime.AbortSignalThrowIfAbortedThis, "throwIfAborted", 0);

            il.MarkLabel(notSignalPropLabel);
        }

        // Property not found on object - check prototype chain
        // Get prototype: $PropertyDescriptorStore.GetPrototype(obj)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, protoLocal);

        // If prototype is null, return undefined
        var returnUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, protoLocal);
        il.Emit(OpCodes.Brfalse, returnUndefinedLabel);

        // Recursively call GetProperty(prototype, name) to check prototype chain
        il.Emit(OpCodes.Ldloc, protoLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);  // Recursive call to GetProperty
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnUndefinedLabel);
        // ECMA-262: `({}).constructor === Object`. If user hasn't set a custom
        // constructor and no prototype overrides it, return typeof(object) which
        // matches what compiled-mode `Object` resolves to via globalThis.
        var notDictCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notDictCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDictCtorLabel);
        // ECMA-262 19.1.3: every plain object inherits from Object.prototype.
        // For Dictionary literals (`{}` etc.) without an explicit prototype,
        // fall back to the ObjectPrototypeField singleton — that's where
        // `valueOf`, `toString`, `propertyIsEnumerable`, and the toLocaleString
        // wrapper live. Required for Test262 patterns that do
        // `({}).toString.call(receiver)` or for ToPrimitive coercion to find
        // the inherited methods on plain dicts. Lazy-populates on first read.
        var protoFallbackMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        var objProtoFallbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, objProtoFallbackLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, protoFallbackMissLabel);
        il.Emit(OpCodes.Ldloc, objProtoFallbackLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(protoFallbackMissLabel);
        // Return $Undefined.Instance for non-existent properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);

        // If value is a TSFunction, call BindThis(dict) on it
        // to bind 'this' for object method shorthand
        var notTSFunction = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunction);

        // Call func.BindThis(dict)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionBindThis);

        il.MarkLabel(notTSFunction);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }
}
