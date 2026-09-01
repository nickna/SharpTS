using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Atomics operations as pure-IL for standalone DLLs.
/// These work with emitted $TypedArray types instead of SharpTS runtime types.
/// </summary>
public partial class RuntimeEmitter
{
    private delegate ref int UnsafeByteToInt32Delegate(ref byte source);

    private MethodBuilder _atomicsLoadLocked = null!;
    private MethodBuilder _atomicsStoreLocked = null!;
    private MethodBuilder _atomicsUpdateLocked = null!;
    private MethodBuilder _atomicsUpdateInt32 = null!;
    private MethodBuilder _atomicsConvertInt32Operand = null!;

    /// <summary>
    /// Emits Atomics static method helpers with pure-IL implementations for emitted types.
    /// Falls back to reflection-based SharpTS calls only if input is not an emitted type.
    /// </summary>
    private void EmitAtomicsHelpersPure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Every emitted realm sees the same byte[] for a shared buffer. Locking that backing
        // object provides a cross-AssemblyLoadContext correctness fallback for integer element
        // kinds without a directly usable CLR Interlocked primitive.
        _atomicsLoadLocked = EmitAtomicsLoadLocked(runtimeType, runtime);
        _atomicsStoreLocked = EmitAtomicsStoreLocked(runtimeType, runtime);
        _atomicsUpdateLocked = EmitAtomicsUpdateLocked(runtimeType, runtime);
        _atomicsConvertInt32Operand = EmitAtomicsConvertInt32Operand(runtimeType);

        // Unboxed hot path used when the compiler knows the receiver is Int32Array/Uint32Array.
        runtime.AtomicsAddInt32 = EmitAtomicsAddInt32(runtimeType, runtime);
        runtime.AtomicsIncrementInt32Discarded = EmitAtomicsIncrementInt32Discarded(runtimeType, runtime);
        _atomicsUpdateInt32 = EmitAtomicsUpdateInt32(runtimeType, runtime);

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

        // Atomics.pause(iterationNumber?) -> undefined
        runtime.AtomicsPause = EmitAtomicsPausePure(runtimeType, runtime);
    }

    private MethodBuilder EmitAtomicsLoadLocked(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsLoadLocked", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [runtime.TypedArrayBaseType, _types.Int32]);
        var il = method.GetILGenerator();
        var buffer = il.DeclareLocal(typeof(byte[]));
        var lockTaken = il.DeclareLocal(_types.Boolean);
        var result = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
        il.Emit(OpCodes.Stloc, buffer);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lockTaken);
        il.BeginExceptionBlock();
        EmitEnterAtomicBufferLock(il, buffer, lockTaken);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, done);
        EmitAtomicBufferLockFinally(il, buffer, lockTaken);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitAtomicsStoreLocked(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsStoreLocked", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [runtime.TypedArrayBaseType, _types.Int32, _types.Object]);
        var il = method.GetILGenerator();
        var buffer = il.DeclareLocal(typeof(byte[]));
        var lockTaken = il.DeclareLocal(_types.Boolean);
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
        il.Emit(OpCodes.Stloc, buffer);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lockTaken);
        il.BeginExceptionBlock();
        EmitEnterAtomicBufferLock(il, buffer, lockTaken);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        il.Emit(OpCodes.Leave, done);
        EmitAtomicBufferLockFinally(il, buffer, lockTaken);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);
        return method;
    }

    // operation: 0 add, 1 sub, 2 and, 3 or, 4 xor, 5 exchange, 6 compareExchange.
    private MethodBuilder EmitAtomicsUpdateLocked(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsUpdateLocked", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [runtime.TypedArrayBaseType, _types.Int32, _types.Object, _types.Object, _types.Int32]);
        var il = method.GetILGenerator();
        var buffer = il.DeclareLocal(typeof(byte[]));
        var lockTaken = il.DeclareLocal(_types.Boolean);
        var oldValue = il.DeclareLocal(_types.Object);
        var newValue = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();
        var store = il.DefineLabel();
        var operations = Enumerable.Range(0, 7).Select(_ => il.DefineLabel()).ToArray();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
        il.Emit(OpCodes.Stloc, buffer);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lockTaken);
        il.BeginExceptionBlock();
        EmitEnterAtomicBufferLock(il, buffer, lockTaken);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Stloc, oldValue);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Switch, operations);
        il.Emit(OpCodes.Br, done);

        // add / sub
        foreach (int operation in new[] { 0, 1 })
        {
            il.MarkLabel(operations[operation]);
            il.Emit(OpCodes.Ldloc, oldValue);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            il.Emit(operation == 0 ? OpCodes.Add : OpCodes.Sub);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stloc, newValue);
            il.Emit(OpCodes.Br, store);
        }

        // and / or / xor
        OpCode[] bitwiseOperations = [OpCodes.And, OpCodes.Or, OpCodes.Xor];
        for (int operation = 2; operation <= 4; operation++)
        {
            il.MarkLabel(operations[operation]);
            il.Emit(OpCodes.Ldloc, oldValue);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
            il.Emit(bitwiseOperations[operation - 2]);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stloc, newValue);
            il.Emit(OpCodes.Br, store);
        }

        il.MarkLabel(operations[5]);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, newValue);
        il.Emit(OpCodes.Br, store);

        il.MarkLabel(operations[6]);
        il.Emit(OpCodes.Ldloc, oldValue);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, newValue);

        il.MarkLabel(store);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, newValue);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);

        il.MarkLabel(done);
        var afterFinally = il.DefineLabel();
        il.Emit(OpCodes.Leave, afterFinally);
        EmitAtomicBufferLockFinally(il, buffer, lockTaken);
        il.EndExceptionBlock();
        il.MarkLabel(afterFinally);
        il.Emit(OpCodes.Ldloc, oldValue);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitEnterAtomicBufferLock(
        ILGenerator il, LocalBuilder buffer, LocalBuilder lockTaken)
    {
        il.Emit(OpCodes.Ldloc, buffer);
        il.Emit(OpCodes.Ldloca, lockTaken);
        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(Monitor), "Enter", _types.Object, _types.Boolean.MakeByRefType()));
    }

    private void EmitAtomicBufferLockFinally(
        ILGenerator il, LocalBuilder buffer, LocalBuilder lockTaken)
    {
        il.BeginFinallyBlock();
        var skipExit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lockTaken);
        il.Emit(OpCodes.Brfalse, skipExit);
        il.Emit(OpCodes.Ldloc, buffer);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
        il.MarkLabel(skipExit);
        il.Emit(OpCodes.Endfinally);
    }

    private MethodBuilder EmitAtomicsAddInt32(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsAddInt32",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [runtime.TypedArrayBaseType, _types.Int32, _types.Double, _types.Boolean]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var deltaLocal = il.DeclareLocal(_types.Int32);
        var newValueLocal = il.DeclareLocal(_types.Int32);
        var unsignedResult = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _atomicsConvertInt32Operand);
        il.Emit(OpCodes.Stloc, deltaLocal);

        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Add), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Stloc, newValueLocal);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, unsignedResult);
        il.Emit(OpCodes.Ldloc, newValueLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(unsignedResult);
        il.Emit(OpCodes.Ldloc, newValueLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_R_Un);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitAtomicsIncrementInt32Discarded(
        TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsIncrementInt32Discarded",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [runtime.TypedArrayBaseType, _types.Int32]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Increment), [typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return method;
    }

    // operation: 0 add, 1 sub, 2 and, 3 or, 4 xor, 5 exchange, 6 compareExchange.
    // Atomics.add also has a smaller dedicated helper because it dominates shared-counter workloads.
    private MethodBuilder EmitAtomicsUpdateInt32(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsUpdateInt32",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Double,
            [runtime.TypedArrayBaseType, _types.Int32, _types.Double, _types.Double,
                _types.Int32, _types.Boolean]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var operandLocal = il.DeclareLocal(_types.Int32);
        var expectedLocal = il.DeclareLocal(_types.Int32);
        var elementLocal = il.DeclareLocal(typeof(int).MakeByRefType());
        var oldValueLocal = il.DeclareLocal(_types.Int32);
        var observedLocal = il.DeclareLocal(_types.Int32);
        var replacementLocal = il.DeclareLocal(_types.Int32);
        var operations = Enumerable.Range(0, 7).Select(_ => il.DefineLabel()).ToArray();
        var xorRetry = il.DefineLabel();
        var result = il.DefineLabel();
        var unsignedResult = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        EmitConvertAtomicsInt32Operand(il, argument: 2, operandLocal);
        EmitConvertAtomicsInt32Operand(il, argument: 3, expectedLocal);

        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Stloc, elementLocal);

        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Switch, operations);
        il.Emit(OpCodes.Br, result);

        il.MarkLabel(operations[0]);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Add), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, oldValueLocal);
        il.Emit(OpCodes.Br, result);

        // sub: Interlocked.Add(ref element, -operand) returns the new value.
        il.MarkLabel(operations[1]);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Neg);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Add), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, oldValueLocal);
        il.Emit(OpCodes.Br, result);

        // Interlocked.And/Or return the value observed before the update.
        foreach ((int operation, string name) in new[]
                 {
                     (2, nameof(Interlocked.And)),
                     (3, nameof(Interlocked.Or))
                 })
        {
            il.MarkLabel(operations[operation]);
            il.Emit(OpCodes.Ldloc, elementLocal);
            il.Emit(OpCodes.Ldloc, operandLocal);
            il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
                name, [typeof(int).MakeByRefType(), typeof(int)])!);
            il.Emit(OpCodes.Stloc, oldValueLocal);
            il.Emit(OpCodes.Br, result);
        }

        // There is no Interlocked.Xor, so use a compare-and-swap retry loop.
        il.MarkLabel(operations[4]);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.CompareExchange),
            [typeof(int).MakeByRefType(), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, oldValueLocal);
        il.MarkLabel(xorRetry);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Stloc, replacementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.CompareExchange),
            [typeof(int).MakeByRefType(), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, observedLocal);
        il.Emit(OpCodes.Ldloc, observedLocal);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Beq, result);
        il.Emit(OpCodes.Ldloc, observedLocal);
        il.Emit(OpCodes.Stloc, oldValueLocal);
        il.Emit(OpCodes.Br, xorRetry);

        il.MarkLabel(operations[5]);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Exchange), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Stloc, oldValueLocal);
        il.Emit(OpCodes.Br, result);

        il.MarkLabel(operations[6]);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldloc, operandLocal);
        il.Emit(OpCodes.Ldloc, expectedLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.CompareExchange),
            [typeof(int).MakeByRefType(), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        il.MarkLabel(result);
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Brtrue, unsignedResult);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(unsignedResult);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_R_Un);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitConvertAtomicsInt32Operand(
        ILGenerator il, int argument, LocalBuilder destination)
    {
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Call, _atomicsConvertInt32Operand);
        il.Emit(OpCodes.Stloc, destination);
    }

    /// <summary>
    /// Emits ECMAScript ToInt32/ToUint32 as one signed 32-bit bit pattern.
    /// Both conversions have identical low 32 bits; callers choose how to
    /// render the result after the atomic operation.
    /// </summary>
    private MethodBuilder EmitAtomicsConvertInt32Operand(TypeBuilder runtimeType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsConvertInt32Operand",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.Double]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

        var il = method.GetILGenerator();
        var modulo = il.DeclareLocal(_types.Double);
        var zero = il.DefineLabel();
        var nonNegative = il.DefineLabel();
        var signedRange = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brtrue, zero);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(double).GetMethod(
            nameof(double.IsInfinity), [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, zero);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod(
            nameof(Math.Truncate), [typeof(double)])!);
        il.Emit(OpCodes.Ldc_R8, 4294967296d);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, modulo);

        il.Emit(OpCodes.Ldloc, modulo);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Bge, nonNegative);
        il.Emit(OpCodes.Ldloc, modulo);
        il.Emit(OpCodes.Ldc_R8, 4294967296d);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, modulo);

        il.MarkLabel(nonNegative);
        il.Emit(OpCodes.Ldloc, modulo);
        il.Emit(OpCodes.Ldc_R8, 2147483648d);
        il.Emit(OpCodes.Blt, signedRange);
        il.Emit(OpCodes.Ldloc, modulo);
        il.Emit(OpCodes.Ldc_R8, 4294967296d);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, modulo);

        il.MarkLabel(signedRange);
        il.Emit(OpCodes.Ldloc, modulo);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(zero);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitAtomicsPausePure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsPause",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = method.GetILGenerator();
        var valid = il.DefineLabel();
        var invalid = il.DefineLabel();
        var numberLocal = il.DeclareLocal(_types.Double);

        // Omitted/undefined is valid.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, valid);

        // A supplied value must be a finite integral Number. No coercion is
        // performed by Atomics.pause.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, numberLocal);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod(
            nameof(double.IsInfinity), [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod(
            nameof(Math.Truncate), [typeof(double)])!);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Beq, valid);

        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(
            il, runtime, "Atomics.pause iterationNumber must be an integral Number");

        il.MarkLabel(valid);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        return method;
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
        var indexLocal = il.DeclareLocal(_types.Int32);
        var emittedPath = il.DefineLabel();
        var uint32Path = il.DefineLabel();
        var lockedPath = il.DefineLabel();
        var unsignedResult = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Int32Array/Uint32Array use an aligned volatile read; other integer
        // element kinds retain the shared-buffer lock fallback.
        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Int32ArrayType);
        il.Emit(OpCodes.Brfalse, uint32Path);
        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Call, typeof(Volatile).GetMethod(
            nameof(Volatile.Read), [typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(uint32Path);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Uint32ArrayType);
        il.Emit(OpCodes.Brfalse, lockedPath);
        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Call, typeof(Volatile).GetMethod(
            nameof(Volatile.Read), [typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Br, unsignedResult);

        il.MarkLabel(unsignedResult);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_R_Un);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(lockedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Call, _atomicsLoadLocked);

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
        var indexLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Int32);
        var emittedPath = il.DefineLabel();
        var uint32Path = il.DefineLabel();
        var lockedPath = il.DefineLabel();
        var signedResult = il.DefineLabel();
        var unsignedResult = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        // Non-emitted typed arrays are not supported in standalone mode.
        EmitThrowAtomicsTypedArrayRequired(il);
        il.Emit(OpCodes.Br, endLabel);

        // Int32Array/Uint32Array use an aligned volatile write; other integer
        // element kinds retain the shared-buffer lock fallback.
        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Int32ArrayType);
        il.Emit(OpCodes.Brfalse, uint32Path);
        EmitStore(unsigned: false);

        il.MarkLabel(uint32Path);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Uint32ArrayType);
        il.Emit(OpCodes.Brfalse, lockedPath);
        EmitStore(unsigned: true);

        il.MarkLabel(signedResult);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(unsignedResult);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_R_Un);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(lockedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _atomicsStoreLocked);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;

        void EmitStore(bool unsigned)
        {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            il.Emit(OpCodes.Call, _atomicsConvertInt32Operand);
            il.Emit(OpCodes.Stloc, valueLocal);
            EmitInt32ElementReference(il, runtime, indexLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, typeof(Volatile).GetMethod(
                nameof(Volatile.Write), [typeof(int).MakeByRefType(), typeof(int)])!);
            il.Emit(OpCodes.Br, unsigned ? unsignedResult : signedResult);
        }
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
        var indexLocal = il.DeclareLocal(_types.Int32);
        var deltaLocal = il.DeclareLocal(_types.Int32);
        var newValueLocal = il.DeclareLocal(_types.Int32);

        var emittedPath = il.DefineLabel();
        var uint32Path = il.DefineLabel();
        var signedResultPath = il.DefineLabel();
        var uintResultPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Int32Array/Uint32Array are the dominant shared-counter forms. Lower them to the
        // CLR's lock-free atomic primitive instead of the old Get + boxed arithmetic + Set
        // sequence (which both lost updates and paid two virtual calls per increment).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Int32ArrayType);
        il.Emit(OpCodes.Brfalse, uint32Path);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Call, _atomicsConvertInt32Operand);
        il.Emit(OpCodes.Stloc, deltaLocal);
        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Add), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Stloc, newValueLocal);
        il.Emit(OpCodes.Br, signedResultPath);

        il.MarkLabel(uint32Path);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Uint32ArrayType);
        il.Emit(OpCodes.Brfalse, generalPath);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Call, _atomicsConvertInt32Operand);
        il.Emit(OpCodes.Stloc, deltaLocal);
        EmitInt32ElementReference(il, runtime, indexLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(
            nameof(Interlocked.Add), [typeof(int).MakeByRefType(), typeof(int)])!);
        il.Emit(OpCodes.Stloc, newValueLocal);
        il.Emit(OpCodes.Br, uintResultPath);

        il.MarkLabel(signedResultPath);
        il.Emit(OpCodes.Ldloc, newValueLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(uintResultPath);
        il.Emit(OpCodes.Ldloc, newValueLocal);
        il.Emit(OpCodes.Ldloc, deltaLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_R_Un);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        // Get old value
        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Pushes a managed <c>ref int</c> for an aligned Int32/Uint32 typed-array element.
    /// SharedArrayBuffer byte offsets for these views are four-byte aligned by construction.
    /// </summary>
    private void EmitInt32ElementReference(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder indexLocal)
    {
        var validIndex = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Blt_Un, validIndex);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Atomics index is out of range");

        il.MarkLabel(validIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteOffsetGetter);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelema, typeof(byte));

        // A strongly typed method group both roots this exact generic instantiation for
        // Native AOT and avoids the trimming-unsafe MakeGenericMethod path.
        MethodInfo unsafeAs =
            ((UnsafeByteToInt32Delegate)Unsafe.As<byte, int>).Method;
        il.Emit(OpCodes.Call, unsafeAs);
    }

    /// <summary>
    /// Emits the lock-free Int32Array/Uint32Array branch for a read-modify-write operation.
    /// Other integer typed arrays branch to the shared-buffer lock implementation.
    /// </summary>
    private void EmitAtomicsInt32UpdateFastPath(
        ILGenerator il,
        EmittedRuntime runtime,
        int operation,
        int valueArgument,
        int? expectedArgument,
        Label generalPath,
        Label endLabel)
    {
        var uint32Path = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Int32ArrayType);
        il.Emit(OpCodes.Brfalse, uint32Path);
        EmitCall(unsigned: false);

        il.MarkLabel(uint32Path);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Uint32ArrayType);
        il.Emit(OpCodes.Brfalse, generalPath);
        EmitCall(unsigned: true);
        return;

        void EmitCall(bool unsigned)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldarg, valueArgument);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            if (expectedArgument is int argument)
            {
                il.Emit(OpCodes.Ldarg, argument);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            }
            else
            {
                il.Emit(OpCodes.Ldc_R8, 0d);
            }
            il.Emit(OpCodes.Ldc_I4, operation);
            il.Emit(unsigned ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _atomicsUpdateInt32);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Br, endLabel);
        }
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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 1, valueArgument: 2, expectedArgument: null,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 2, valueArgument: 2, expectedArgument: null,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 3, valueArgument: 2, expectedArgument: null,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 4, valueArgument: 2, expectedArgument: null,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 5, valueArgument: 2, expectedArgument: null,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
        var emittedPath = il.DefineLabel();
        var generalPath = il.DefineLabel();
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

        EmitAtomicsInt32UpdateFastPath(
            il, runtime, operation: 6, valueArgument: 3, expectedArgument: 2,
            generalPath, endLabel);

        il.MarkLabel(generalPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_3); // replacement
        il.Emit(OpCodes.Ldarg_2); // expected
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Call, _atomicsUpdateLocked);

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
