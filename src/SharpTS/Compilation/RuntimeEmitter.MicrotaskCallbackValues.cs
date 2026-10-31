using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitQueueMicrotaskValueAdapter(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Keep the typed enqueue ABI and existing Promise-job queue unchanged.
        // Only programs that can reach the global function value need this adapter.
        var closure = EmitTypeDefinitions.DefineType((ModuleBuilder)runtimeType.Module,
            "$MicrotaskValueCallback", TypeAttributes.NotPublic | TypeAttributes.Sealed,
            _types.Object);
        var callback = closure.DefineField("Callback", _types.Object, FieldAttributes.Public);
        var constructor = closure.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ret);
        var run = closure.DefineMethod("Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        var runIl = run.GetILGenerator();
        runIl.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        runIl.Emit(OpCodes.Ldarg_0);
        runIl.Emit(OpCodes.Ldfld, callback);
        runIl.Emit(OpCodes.Ldc_I4_0);
        runIl.Emit(OpCodes.Newarr, _types.Object);
        runIl.Emit(OpCodes.Call, runtime.Invocation.Method);
        runIl.Emit(OpCodes.Pop);
        runIl.Emit(OpCodes.Ret);
        closure.CreateType();

        var method = runtimeType.DefineMethod("QueueMicrotaskValue",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [_types.Object]);
        runtime.Microtasks.QueueMicrotaskValue = method;
        var il = method.GetILGenerator();
        var function = il.DeclareLocal(runtime.FunctionValues.Type);
        var otherCallable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Stloc, function);
        il.Emit(OpCodes.Ldloc, function);
        il.Emit(OpCodes.Brfalse, otherCallable);
        il.Emit(OpCodes.Ldloc, function);
        il.Emit(OpCodes.Call, runtime.Microtasks.QueueMicrotask);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(otherCallable);
        EmitRequireCallable(il, new CallableValidationInputs(
            runtime.Operators.TypeOf, runtime.ReflectedMethods.CallableType,
            runtime.TypedArrays.Implementation?.BoundMethodType,
            runtime.TextEncoding?.DecodeMethodType,
            runtime.NodeStreams?.TransformDoneCallbackType,
            runtime.NodeStreams?.WriteCallbackWrapperType, runtime.Errors.CreateException,
            runtime.Errors.TypeErrorConstructor), () => il.Emit(OpCodes.Ldarg_0),
            "queueMicrotask callback must be a function");
        var pending = il.DeclareLocal(closure);
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Stloc, pending);
        il.Emit(OpCodes.Ldloc, pending);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, callback);
        il.Emit(OpCodes.Ldloc, pending);
        il.Emit(OpCodes.Ldftn, run);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.Microtasks.QueuePromiseJob);
        il.Emit(OpCodes.Ret);
    }
}
