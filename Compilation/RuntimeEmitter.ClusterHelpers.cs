using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the compiled cluster module's late-bound bridge helpers (#1171).
///
/// Compiled cluster routes the entire module surface through
/// <c>SharpTS.Runtime.Types.ClusterCompiledBridge</c>, resolved at runtime via
/// <c>Type.GetType("…, SharpTS")</c> — the worker_threads pattern (#354): workers run
/// the original entry script interpreted on a thread inside the same process, so the
/// compiled primary and its workers share one ClusterSingleton (coherent workers map,
/// settings, scheduling policy, events, shared-port round-robin). SharpTS.dll must be
/// co-located; each cluster emit site records RequireSharpTSRuntime("cluster") so the
/// CLI copies it, and --standalone yields a clear runtime error instead of a silent
/// degrade (the tls lesson, #1033).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Absolute path of the entry module, baked into the emitted ClusterFork helper so
    /// a compiled primary knows which script its workers re-execute. Null when
    /// compiling a single script without a module context (fork then fails with a
    /// clear error at runtime).
    /// </summary>
    public string? EntryModulePath { get; set; }

    private const string BridgeTypeName = "SharpTS.Runtime.Types.ClusterCompiledBridge, SharpTS";
    private const string ClusterRuntimeMissingMessage =
        "cluster requires the SharpTS runtime (SharpTS.dll) to be present. " +
        "Compile without --standalone so it is co-located with the output.";

    /// <summary>
    /// Emits $Runtime.ClusterFork / $Runtime.ClusterInvoke.
    /// Called from RuntimeEmitter.RuntimeClass.cs when the program uses cluster.
    /// </summary>
    internal void EmitClusterHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitClusterForkHelper(runtimeType, runtime);
        EmitClusterInvokeHelper(runtimeType, runtime);
    }

    /// <summary>
    /// public static object ClusterFork(object env)
    ///   → ClusterCompiledBridge.Fork(entryPath, env, loop.Ref, loop.Unref, loop.Schedule)
    /// The $EventLoop delegates keep the compiled loop alive while workers run and
    /// marshal worker events onto it.
    /// </summary>
    private void EmitClusterForkHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClusterFork",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = method.GetILGenerator();

        var typeLocal = il.DeclareLocal(_types.Type);
        var loopLocal = il.DeclareLocal(runtime.EventLoopType);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var actionCtor = typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!;
        var actionOfActionCtor = typeof(Action<Action>).GetConstructor([_types.Object, typeof(IntPtr)])!;

        EmitLoadBridgeTypeOrThrow(il, typeLocal);

        // var loop = $EventLoop.GetInstance();
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Stloc, loopLocal);

        // object[] args = { entryPath, env, loop.Ref, loop.Unref, loop.Schedule };
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        if (EntryModulePath != null)
            il.Emit(OpCodes.Ldstr, EntryModulePath);
        else
            il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0); // env
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopRef);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopUnref);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopSchedule);
        il.Emit(OpCodes.Newobj, actionOfActionCtor);
        il.Emit(OpCodes.Stelem_Ref);

        // return t.GetMethod("Fork").Invoke(null, args);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Fork");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        runtime.ClusterFork = method;
    }

    /// <summary>
    /// public static object ClusterInvoke(string member, object[] args)
    ///   → ClusterCompiledBridge.Invoke(member, args)
    /// Single dispatch point for the non-fork cluster surface (events, settings,
    /// workers, scheduling policy).
    /// </summary>
    private void EmitClusterInvokeHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClusterInvoke",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.ObjectArray]);

        var il = method.GetILGenerator();

        var typeLocal = il.DeclareLocal(_types.Type);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        EmitLoadBridgeTypeOrThrow(il, typeLocal);

        // object[] callArgs = { member, args };
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        // return t.GetMethod("Invoke").Invoke(null, callArgs);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        runtime.ClusterInvoke = method;
    }

    /// <summary>
    /// Emits: Type t = Type.GetType("…ClusterCompiledBridge, SharpTS");
    /// if (t == null) throw — the clear --standalone failure mode.
    /// </summary>
    private void EmitLoadBridgeTypeOrThrow(ILGenerator il, LocalBuilder typeLocal)
    {
        il.Emit(OpCodes.Ldstr, BridgeTypeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, typeLocal);

        var typeOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, typeOk);
        il.Emit(OpCodes.Ldstr, ClusterRuntimeMissingMessage);
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(typeOk);
    }
}
