using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Fills PromiseResolveValue and emits the queued thenable job used by
    /// Promise.resolve's intrinsic path. The `then` getter is observed before
    /// Promise.resolve returns; invocation is posted as a Promise job.
    /// </summary>
    private void EmitPromiseResolveValue(
        ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var jobType = EmitPromiseResolveThenableJob(moduleBuilder, runtime);
        var method = runtime.PromiseResolveValueMethod;
        var il = method.GetILGenerator();
        var tcsType = _types.TaskCompletionSourceOfObject;
        var resultLocal = il.DeclareLocal(_types.TaskOfObject);
        var thenLocal = il.DeclareLocal(_types.Object);
        var tcsLocal = il.DeclareLocal(tcsType);
        var jobLocal = il.DeclareLocal(jobType.Type);
        var contextLocal = il.DeclareLocal(typeof(SynchronizationContext));
        var exceptionLocal = il.DeclareLocal(_types.Exception);
        var wrapValueLabel = il.DefineLabel();
        var thenCallableLabel = il.DefineLabel();
        var runWithoutContextLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, wrapValueLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, thenLocal);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, thenCallableLabel);

        il.MarkLabel(wrapValueLabel);
        // A JavaScript promise has unique object identity. Task.FromResult may
        // return a cached completed Task (notably for null/undefined), which
        // would make distinct Promise.resolve calls share own properties.
        il.Emit(OpCodes.Ldc_I4, (int)TaskCreationOptions.RunContinuationsAsynchronously);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(tcsType, typeof(TaskCreationOptions)));
        il.Emit(OpCodes.Stloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tcsType, "TrySetResult", _types.Object));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, doneLabel);

        il.MarkLabel(thenCallableLabel);
        il.Emit(OpCodes.Ldc_I4, (int)TaskCreationOptions.RunContinuationsAsynchronously);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(tcsType, typeof(TaskCreationOptions)));
        il.Emit(OpCodes.Stloc, tcsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Newobj, jobType.Constructor);
        il.Emit(OpCodes.Stloc, jobLocal);

        il.Emit(OpCodes.Call, typeof(SynchronizationContext)
            .GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, contextLocal);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Brfalse, runWithoutContextLabel);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Ldloc, jobLocal);
        il.Emit(OpCodes.Ldftn, jobType.Run);
        il.Emit(OpCodes.Newobj, typeof(SendOrPostCallback)
            .GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(SynchronizationContext).GetMethod(
            "Post", [typeof(SendOrPostCallback), _types.Object])!);
        var haveScheduledLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, haveScheduledLabel);

        // Standalone hosts without an event-loop SynchronizationContext still
        // make progress; normal SharpTS execution always takes the Post path.
        il.MarkLabel(runWithoutContextLabel);
        il.Emit(OpCodes.Ldloc, jobLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, jobType.Run);
        il.MarkLabel(haveScheduledLabel);

        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, doneLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(
            typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, doneLabel);
        il.EndExceptionBlock();

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Run)
        EmitPromiseResolveThenableJob(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$PromiseResolveThenableJob",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object);
        var valueField = typeBuilder.DefineField("Value", _types.Object, FieldAttributes.Private);
        var thenField = typeBuilder.DefineField("Then", _types.Object, FieldAttributes.Private);
        var resolveField = typeBuilder.DefineField(
            "Resolve", runtime.PromiseResolveCallbackType, FieldAttributes.Private);
        var rejectField = typeBuilder.DefineField(
            "Reject", runtime.PromiseRejectCallbackType, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object, _types.TaskCompletionSourceOfObject]);
        {
            var il = ctor.GetILGenerator();
            var boxLocal = il.DeclareLocal(typeof(System.Runtime.CompilerServices.StrongBox<bool>));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, valueField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, thenField);
            il.Emit(OpCodes.Newobj, typeof(System.Runtime.CompilerServices.StrongBox<bool>)
                .GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, boxLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, boxLocal);
            il.Emit(OpCodes.Newobj, runtime.PromiseResolveCallbackCtor);
            il.Emit(OpCodes.Stfld, resolveField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, boxLocal);
            il.Emit(OpCodes.Newobj, runtime.PromiseRejectCallbackCtor);
            il.Emit(OpCodes.Stfld, rejectField);
            il.Emit(OpCodes.Ret);
        }

        var run = typeBuilder.DefineMethod(
            "Run", MethodAttributes.Public, _types.Void, [_types.Object]);
        {
            var il = run.GetILGenerator();
            var exceptionLocal = il.DeclareLocal(_types.Exception);
            var doneLabel = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, thenField);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resolveField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Leave, doneLabel);

            il.BeginCatchBlock(_types.Exception);
            il.Emit(OpCodes.Stloc, exceptionLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.Emit(OpCodes.Call, runtime.WrapException);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.PromiseRejectCallbackInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Leave, doneLabel);
            il.EndExceptionBlock();
            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        return (typeBuilder, ctor, run);
    }
}
