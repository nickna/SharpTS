using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // #971: real thread-pool backgrounding for fs/promises. Each FsXAsync builds
    // its boxed args and calls FsRunAsync(syncMethod, args), which Refs the event
    // loop, runs the sync op on the thread pool via $FsAsyncOp.Worker (reflection),
    // and Unrefs on completion — so the loop stays alive until the op drains
    // (mirrors fetch/DNS/timers and the interpreter's refsEventLoopWhileInFlight).
    private ConstructorBuilder _fsAsyncOpCtor = null!;
    private MethodBuilder _fsAsyncOpWorker = null!;
    private MethodBuilder _fsRunAsync = null!;
    private MethodBuilder _fsAsyncUnref = null!;

    private void EmitFsRunAsyncInfra(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitFsAsyncOpClosure(typeBuilder);
        EmitFsAsyncUnref(typeBuilder, runtime);
        EmitFsRunAsyncHelper(typeBuilder, runtime);
    }

    /// <summary>$FsAsyncOp { MethodInfo _m; object[] _args; object Worker() }</summary>
    private void EmitFsAsyncOpClosure(TypeBuilder typeBuilder)
    {
        var t = ((ModuleBuilder)typeBuilder.Module).DefineType(
            "$FsAsyncOp",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        var mField = t.DefineField("_m", typeof(MethodInfo), FieldAttributes.Private);
        var argsField = t.DefineField("_args", typeof(object[]), FieldAttributes.Private);

        var ctor = t.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [typeof(MethodInfo), typeof(object[])]);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, mField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, argsField);
            il.Emit(OpCodes.Ret);
        }

        // object Worker(): try { return _m.Invoke(null, _args); }
        //                  catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        // Unwrapping the TargetInvocationException preserves the NodeError (with .code),
        // so the faulted Task rejects the promise with the same shape as the sync throw.
        var worker = t.DefineMethod("Worker", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        {
            var il = worker.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, mField);
            il.Emit(OpCodes.Ldnull); // static target
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, argsField);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.BeginCatchBlock(typeof(TargetInvocationException));
            var eLocal = il.DeclareLocal(typeof(TargetInvocationException));
            il.Emit(OpCodes.Stloc, eLocal);
            il.Emit(OpCodes.Ldloc, eLocal);
            il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("InnerException")!.GetGetMethod()!);
            il.Emit(OpCodes.Dup);
            var hasInner = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, hasInner);
            il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldloc, eLocal);
            il.MarkLabel(hasInner);
            il.Emit(OpCodes.Throw);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        t.CreateType();
        _fsAsyncOpCtor = ctor;
        _fsAsyncOpWorker = worker;
    }

    /// <summary>
    /// Unref chain. The op's I/O Task completing fires FsAsyncUnref, but the facade's
    /// downstream `.then`/callback runs as a SEPARATE continuation registered after
    /// ours — so dropping the ref immediately (or on the next tick) can let the loop
    /// go quiescent and exit before a fire-and-forget callback (fs.readFile(path, cb),
    /// no awaiter) drains. We therefore hold the ref for a short grace period via
    /// Task.Delay, then schedule the actual Unref onto the loop. The callback posts
    /// within microseconds of the I/O completing, so it always drains first; the grace
    /// only delays program exit for callback-only programs (awaited ops are unaffected
    /// — their pending top-level task keeps the loop alive regardless).
    /// </summary>
    private void EmitFsAsyncUnref(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static void FsAsyncUnrefNow() => EventLoop.GetInstance().Unref();
        var now = typeBuilder.DefineMethod("FsAsyncUnrefNow",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, Type.EmptyTypes);
        {
            var il = now.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);
            il.Emit(OpCodes.Ret);
        }

        // static void FsAsyncUnrefDrop(Task t) => EventLoop.GetInstance().Schedule(new Action(FsAsyncUnrefNow));
        var drop = typeBuilder.DefineMethod("FsAsyncUnrefDrop",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [typeof(Task)]);
        {
            var il = drop.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, now);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
            il.Emit(OpCodes.Ret);
        }

        // static void FsAsyncUnref(Task t) => Task.Delay(8).ContinueWith(FsAsyncUnrefDrop);
        var m = typeBuilder.DefineMethod("FsAsyncUnref",
            MethodAttributes.Public | MethodAttributes.Static, _types.Void, [typeof(Task)]);
        {
            var il = m.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 8);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("Delay", [_types.Int32])!);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, drop);
            il.Emit(OpCodes.Newobj, typeof(Action<Task>).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("ContinueWith", [typeof(Action<Task>)])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }
        _fsAsyncUnref = m;
    }

    /// <summary>static Task&lt;object&gt; FsRunAsync(MethodInfo m, object[] args)</summary>
    private void EmitFsRunAsyncHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var taskRunOpen = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(x => x.Name == "Run" && x.IsGenericMethodDefinition
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType.IsGenericType
                && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<>));
        var taskRun = taskRunOpen.MakeGenericMethod(_types.Object);

        var m = typeBuilder.DefineMethod("FsRunAsync",
            MethodAttributes.Public | MethodAttributes.Static, _types.TaskOfObject,
            [typeof(MethodInfo), typeof(object[])]);
        _fsRunAsync = m;
        var il = m.GetILGenerator();

        // EventLoop.Ref() — keep the loop alive while the op is on the pool.
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // var t = Task.Run<object>(new Func<object>(new $FsAsyncOp(m, args).Worker))
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, _fsAsyncOpCtor);
        il.Emit(OpCodes.Ldftn, _fsAsyncOpWorker);
        il.Emit(OpCodes.Newobj, typeof(Func<object>).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, taskRun);
        var tLocal = il.DeclareLocal(_types.TaskOfObject);
        il.Emit(OpCodes.Stloc, tLocal);

        // t.ContinueWith((Action<Task>)FsAsyncUnref, ExecuteSynchronously)
        il.Emit(OpCodes.Ldloc, tLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, _fsAsyncUnref);
        il.Emit(OpCodes.Newobj, typeof(Action<Task>).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("ContinueWith", [typeof(Action<Task>), typeof(TaskContinuationOptions)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, tLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Op-body replacement: build object[] from the first argCount params, then
    /// return FsRunAsync(syncMethod, args). The sync op runs on the thread pool.
    /// Void sync methods reflect to null (a resolved promise); throws fault the Task.
    /// </summary>
    private void EmitFsAsyncDispatch(ILGenerator il, MethodInfo syncMethod, int argCount)
    {
        il.Emit(OpCodes.Ldc_I4, argCount);
        il.Emit(OpCodes.Newarr, _types.Object);
        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);
        for (int i = 0; i < argCount; i++)
        {
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldtoken, syncMethod);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _fsRunAsync);
        il.Emit(OpCodes.Ret);
    }
}
