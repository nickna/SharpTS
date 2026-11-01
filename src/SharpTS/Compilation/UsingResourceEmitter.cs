using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Emits acquisition and invocation for a captured synchronous disposer.</summary>
internal static class UsingResourceEmitter
{
    public static void Acquire(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod, Action storeMethod) =>
        AcquireCore(il, types, runtime, loadResource, loadMethod, storeMethod, null);

    public static void AcquireAsync(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod, Action storeMethod, Action<bool> storeAwaitResult) =>
        AcquireCore(il, types, runtime, loadResource, loadMethod, storeMethod, storeAwaitResult);

    private static void AcquireCore(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod, Action storeMethod, Action<bool>? storeAwaitResult)
    {
        var acquired = il.DefineLabel();
        var missingMethod = il.DefineLabel();
        var invalidMethod = il.DefineLabel();
        storeAwaitResult?.Invoke(false);
        loadResource();
        il.Emit(OpCodes.Brfalse, acquired);
        loadResource();
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, acquired);
        loadResource();
        il.Emit(OpCodes.Ldsfld, storeAwaitResult is null ? runtime.Symbols.Dispose : runtime.Symbols.AsyncDispose);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Index);
        storeMethod();
        if (storeAwaitResult is not null)
        {
            var fallback = il.DefineLabel();
            var validate = il.DefineLabel();
            loadMethod();
            il.Emit(OpCodes.Brfalse, fallback);
            loadMethod();
            il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
            il.Emit(OpCodes.Brtrue, fallback);
            storeAwaitResult(true);
            il.Emit(OpCodes.Br, validate);
            il.MarkLabel(fallback);
            loadResource();
            il.Emit(OpCodes.Ldsfld, runtime.Symbols.Dispose);
            il.Emit(OpCodes.Call, runtime.ObjectRead.Index);
            storeMethod();
            il.MarkLabel(validate);
        }
        loadMethod();
        il.Emit(OpCodes.Brfalse, missingMethod);
        loadMethod();
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, missingMethod);
        loadMethod();
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, types.GetMethod(types.String, "op_Equality", types.String, types.String));
        il.Emit(OpCodes.Brfalse, invalidMethod);
        il.Emit(OpCodes.Br, acquired);
        il.MarkLabel(missingMethod);
        il.Emit(OpCodes.Ldnull);
        storeMethod();
        loadResource();
        il.Emit(OpCodes.Isinst, typeof(IDisposable));
        il.Emit(OpCodes.Brtrue, acquired);
        il.MarkLabel(invalidMethod);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Resource Symbol.dispose must be callable");
        il.MarkLabel(acquired);
    }

    // Leaves the disposal result, or undefined for null/synchronous fallback.
    // The caller performs the ordinary async suspension even for undefined.
    public static void DisposeAsync(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod, Action loadAwaitResult)
    {
        var managed = il.DefineLabel();
        var done = il.DefineLabel();
        loadMethod();
        il.Emit(OpCodes.Brfalse, managed);
        loadResource();
        loadMethod();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, types.Object);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        loadAwaitResult();
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(managed);
        Dispose(il, types, runtime, loadResource, loadMethod);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.MarkLabel(done);
    }

    public static void Dispose(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod)
    {
        var done = il.DefineLabel();
        var managed = il.DefineLabel();
        loadMethod();
        il.Emit(OpCodes.Brfalse, managed);
        loadResource();
        loadMethod();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, types.Object);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(managed);
        loadResource();
        il.Emit(OpCodes.Isinst, typeof(IDisposable));
        il.Emit(OpCodes.Brfalse, done);
        loadResource();
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Callvirt, types.DisposableDispose);
        il.MarkLabel(done);
    }
}
