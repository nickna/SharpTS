using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Emits acquisition and invocation for a captured synchronous disposer.</summary>
internal static class UsingResourceEmitter
{
    public static void Acquire(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        Action loadResource, Action loadMethod, Action storeMethod)
    {
        var acquired = il.DefineLabel();
        var missingMethod = il.DefineLabel();
        var invalidMethod = il.DefineLabel();
        loadResource();
        il.Emit(OpCodes.Brfalse, acquired);
        loadResource();
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, acquired);
        loadResource();
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Dispose);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Index);
        storeMethod();
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
