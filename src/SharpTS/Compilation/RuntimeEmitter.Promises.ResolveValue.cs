using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Fills PromiseResolveValue and emits the queued thenable job used by
    /// Promise.resolve's intrinsic path. The `then` getter is observed before
    /// Promise.resolve returns; invocation is queued as a Promise job.
    /// </summary>
    private void EmitPromiseResolveValue(
        ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var jobType = EmitPromiseResolveThenableJob(moduleBuilder, runtime);
        var promise = runtime.RequirePromise();
        var entryIl = promise.ResolveValueMethod.GetILGenerator();
        entryIl.Emit(OpCodes.Ldarg_0);
        entryIl.Emit(OpCodes.Ldnull);
        entryIl.Emit(OpCodes.Call, promise.ResolveValueWithRootMethod);
        entryIl.Emit(OpCodes.Ret);

        var il = promise.ResolveValueWithRootMethod.GetILGenerator();
        var tcsType = _types.TaskCompletionSourceOfObject;
        var resultLocal = il.DeclareLocal(_types.TaskOfObject);
        var thenLocal = il.DeclareLocal(_types.Object);
        var tcsLocal = il.DeclareLocal(tcsType);
        var rootLocal = il.DeclareLocal(_types.TaskOfObject);
        var jobLocal = il.DeclareLocal(jobType.Type);
        var exceptionLocal = il.DeclareLocal(_types.Exception);
        var wrapValueLabel = il.DefineLabel();
        var thenCallableLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, wrapValueLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, thenLocal);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
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
        // Internal adoption transfers must settle within this Promise checkpoint.
        // Thenable invocation and guest reactions still have their own queued jobs;
        // AwaitValueToTask also gives a pending guest await a reaction-job facade.
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(tcsType));
        il.Emit(OpCodes.Stloc, tcsLocal);

        // Recursive jobs retain the public Promise.resolve task. A fresh entry
        // owns its newly created task; each job still has a separate resolving pair.
        var haveRootLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, rootLocal);
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Brtrue, haveRootLabel);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(tcsType, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, rootLocal);
        il.MarkLabel(haveRootLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Newobj, jobType.Constructor);
        il.Emit(OpCodes.Stloc, jobLocal);

        // Thenable invocation shares the Promise reaction FIFO, including jobs
        // queued by recursive adoption during the same microtask checkpoint.
        // The shared queue defers invocation even without a current SyncContext.
        il.Emit(OpCodes.Ldloc, jobLocal);
        il.Emit(OpCodes.Ldftn, jobType.Run);
        il.Emit(OpCodes.Newobj, typeof(Action)
            .GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Call, runtime.Microtasks.QueuePromiseJob);

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
            "Resolve", runtime.RequirePromise().ResolveCallbackType, FieldAttributes.Private);
        var rejectField = typeBuilder.DefineField(
            "Reject", runtime.RequirePromise().RejectCallbackType, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object, _types.TaskCompletionSourceOfObject, _types.TaskOfObject]);
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
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Newobj, runtime.RequirePromise().ResolveCallbackWithRootCtor);
            il.Emit(OpCodes.Stfld, resolveField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldloc, boxLocal);
            il.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectCallbackCtor);
            il.Emit(OpCodes.Stfld, rejectField);
            il.Emit(OpCodes.Ret);
        }

        var run = typeBuilder.DefineMethod(
            "Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
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
            il.Emit(OpCodes.Call, runtime.Invocation.Method);
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
            il.Emit(OpCodes.Call, runtime.Errors.WrapException);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.RequirePromise().RejectCallbackInvoke);
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
