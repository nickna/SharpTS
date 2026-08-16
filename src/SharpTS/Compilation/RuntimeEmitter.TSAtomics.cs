using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Atomics operations as pure-IL for standalone DLLs.
/// These work with emitted $TypedArray types instead of SharpTS runtime types.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Atomics static method helpers with pure-IL implementations for emitted types.
    /// Falls back to reflection-based SharpTS calls only if input is not an emitted type.
    /// </summary>
    private void EmitAtomicsHelpersPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Atomics.load(typedArray, index) -> object
        runtime.AtomicsLoad = EmitAtomicsLoadPure(runtimeType, runtime);

        // Atomics.store(typedArray, index, value) -> object (returns value)
        runtime.AtomicsStore = EmitAtomicsStorePure(runtimeType, runtime);

        // Atomics.add(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsAdd = EmitAtomicsAddPure(runtimeType, runtime);

        // Atomics.sub(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsSub = EmitAtomicsSubPure(runtimeType, runtime);

        // Atomics.and(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsAnd = EmitAtomicsAndPure(runtimeType, runtime);

        // Atomics.or(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsOr = EmitAtomicsOrPure(runtimeType, runtime);

        // Atomics.xor(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsXor = EmitAtomicsXorPure(runtimeType, runtime);

        // Atomics.exchange(typedArray, index, value) -> object (returns old value)
        runtime.AtomicsExchange = EmitAtomicsExchangePure(runtimeType, runtime);

        // Atomics.compareExchange(typedArray, index, expected, replacement) -> object (returns old value)
        runtime.AtomicsCompareExchange = EmitAtomicsCompareExchangePure(runtimeType, runtime);

        // Atomics.wait(typedArray, index, value, timeout?) -> string
        runtime.AtomicsWait = EmitAtomicsWaitPure(runtimeType, runtime);

        // Atomics.notify(typedArray, index, count?) -> double
        runtime.AtomicsNotify = EmitAtomicsNotifyPure(runtimeType, runtime);

        // Atomics.isLockFree(size) -> bool
        runtime.AtomicsIsLockFree = EmitAtomicsIsLockFreePure(runtimeType);
    }

    /// <summary>
    /// Emits Atomics.load - reads a value atomically.
    /// </summary>
    private MethodBuilder EmitAtomicsLoadPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsLoad",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double]
        );

        var il = method.GetILGenerator();

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path - use GetTypedArrayElementMethod
        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.store - writes a value atomically and returns that value.
    /// </summary>
    private MethodBuilder EmitAtomicsStorePure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsStore",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path - set and return value
        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);
        // Return the value that was stored
        il.Emit(OpCodes.Ldarg_2);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.add - adds and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsAddPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value = old + value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.sub - subtracts and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsSubPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsSub",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value = old - value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.and - bitwise AND and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsAndPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsAnd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value = old & value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.or - bitwise OR and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsOrPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsOr",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value = old | value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.xor - bitwise XOR and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsXorPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsXor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value = old ^ value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.exchange - exchanges value and returns old value.
    /// </summary>
    private MethodBuilder EmitAtomicsExchangePure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsExchange",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get old value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Set new value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        // Return old value
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.compareExchange - atomically compares and exchanges.
    /// </summary>
    private MethodBuilder EmitAtomicsCompareExchangePure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsCompareExchange",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var noExchangeLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path
        il.MarkLabel(emittedPath);

        // Get current value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // Compare old value with expected value (as doubles)
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_2); // expected
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, noExchangeLabel);

        // Values match - do the exchange
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Ldarg_3); // replacement
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);

        il.MarkLabel(noExchangeLabel);
        // Return old value (whether exchanged or not)
        il.Emit(OpCodes.Ldloc, oldValueLocal);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.wait - waits until value changes.
    /// For emitted types, returns "not-equal" or "ok" based on current value.
    /// </summary>
    private MethodBuilder EmitAtomicsWaitPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsWait",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Double, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var currentValueLocal = il.DeclareLocal(_types.Object);

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var notEqualLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path - simplified implementation
        il.MarkLabel(emittedPath);

        // Get current value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double index to int
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Stloc, currentValueLocal);

        // Compare with expected value
        il.Emit(OpCodes.Ldloc, currentValueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_2); // expected value
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, notEqualLabel);

        // Values match - in standalone mode, return "ok" since we don't have real wait support
        il.Emit(OpCodes.Ldstr, "ok");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notEqualLabel);
        il.Emit(OpCodes.Ldstr, "not-equal");

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.notify - wakes up waiting threads.
    /// For emitted types, returns 0 since we don't have SharedArrayBuffer tracking.
    /// </summary>
    private MethodBuilder EmitAtomicsNotifyPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsNotify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();

        var emittedPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Emitted type path - return 0 (no waiters in standalone mode)
        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldc_R8, 0.0);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Atomics.isLockFree - checks if operations will be lock-free.
    /// </summary>
    private MethodBuilder EmitAtomicsIsLockFreePure(TypeBuilder runtimeType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsIsLockFree",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Double]
        );

        var il = method.GetILGenerator();
        var sizeLocal = il.DeclareLocal(_types.Int32);
        var returnTrue = il.DefineLabel();
        var returnFalse = il.DefineLabel();

        // Convert size to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, sizeLocal);

        // Check if size is 1, 2, 4, or 8 (lock-free sizes)
        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, returnTrue);

        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, returnTrue);

        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, returnTrue);

        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, returnTrue);

        // Not a lock-free size
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitThrowAtomicsTypedArrayRequired(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "Atomics operations require an emitted TypedArray in standalone mode");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }
}
