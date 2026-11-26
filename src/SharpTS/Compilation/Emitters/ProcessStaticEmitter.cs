using System.Diagnostics;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for process static method calls.
/// Handles process.exit() and other directly lowered calls.
/// </summary>
public sealed class ProcessStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a process static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "chdir":
                // Directory.SetCurrentDirectory(path)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    // Convert to string if needed
                    il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
                    il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Directory, "SetCurrentDirectory", ctx.Types.String));
                }
                il.Emit(OpCodes.Ldnull);
                return true;

            case "exit":
                // $Runtime.Process.Exit(code): emits 'exit' on the process
                // singleton before Environment.Exit (matches the interpreter).
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull); // → process.exitCode
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.Process.Exit);
                return true;

            case "abort":
                il.Emit(OpCodes.Ldstr, "process.abort() called");
                il.Emit(OpCodes.Call, typeof(Environment).GetMethod("FailFast", [ctx.Types.String])!);
                il.Emit(OpCodes.Ldnull);
                return true;

            case "nextTick":
                EmitNextTick(emitter, arguments);
                return true;

            case "kill":
                EmitTwoArgHelperCall(emitter, arguments, ctx.Runtime!.Process.Kill);
                return true;

            case "umask":
                EmitOneArgHelperCall(emitter, arguments, ctx.Runtime!.Process.Umask);
                return true;

            case "emitWarning":
                EmitFourArgHelperCall(emitter, arguments, ctx.Runtime!.Process.EmitWarning);
                return true;

            case "setSourceMapsEnabled":
                // Route through the $Process property setter semantics via the
                // live object (SetProperty handles the bool coercion).
                il.Emit(OpCodes.Call, ctx.Runtime!.Process.GetObject);
                il.Emit(OpCodes.Ldstr, "sourceMapsEnabled");
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectWrite.Property);
                il.Emit(OpCodes.Ldnull);
                return true;

            case "on":
            case "addListener":
            case "once":
            case "off":
            case "removeListener":
            case "removeAllListeners":
            case "prependListener":
            case "prependOnceListener":
            case "emit":
            case "listeners":
            case "listenerCount":
            case "eventNames":
            case "setMaxListeners":
            case "getMaxListeners":
                EmitProcessEventEmitterCall(emitter, methodName, arguments);
                return true;

            default:
                return false;
        }
    }

    // The ordinary property path reads the current process receiver.
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName) => false;

    /// <summary>Emits a call to a one-object-arg $Runtime helper (missing arg → null).</summary>
    private static void EmitOneArgHelperCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.Emit.MethodBuilder helper)
    {
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
        emitter.Context.IL.Emit(OpCodes.Call, helper);
    }

    /// <summary>Emits a call to a two-object-arg $Runtime helper (missing args → null).</summary>
    private static void EmitTwoArgHelperCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.Emit.MethodBuilder helper)
    {
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
        emitter.Context.IL.Emit(OpCodes.Call, helper);
    }

    /// <summary>Emits a call to a four-object-arg $Runtime helper (missing args → null).</summary>
    private static void EmitFourArgHelperCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.Emit.MethodBuilder helper)
    {
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 2);
        EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 3);
        emitter.Context.IL.Emit(OpCodes.Call, helper);
    }

    /// <summary>
    /// Emits IL for process.nextTick(callback, ...args).
    /// Queues the callback before due timers.
    /// </summary>
    private static void EmitNextTick(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit args array - remaining arguments (starting from index 1)
        EmitArgsArray(emitter, arguments, 1);

        il.Emit(OpCodes.Call, ctx.Runtime!.Process.QueueNextTick);

        // nextTick has no result, retaining its existing undefined representation.
        il.Emit(OpCodes.Ldnull);
    }

    /// <summary>
    /// Emits an object[] array with the remaining arguments starting from startIndex,
    /// expanding any <see cref="Expr.Spread"/> (<c>...args</c>) at runtime via the shared
    /// spread-aware builder. Leaves an <c>object[]</c> on the stack (#1149).
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        int extraArgCount = Math.Max(0, arguments.Count - startIndex);
        var extra = extraArgCount > 0
            ? arguments.GetRange(startIndex, extraArgCount)
            : new List<Expr>();
        emitter.EmitArgsArrayWithSpread(extra);
    }

    /// <summary>
    /// Emits IL for EventEmitter method calls on process (on, once, off, emit, etc.).
    /// Uses the compiled $Process singleton for process events. Shared with
    /// ProcessModuleEmitter so the module facade's forwarding functions hit the
    /// same emitter instance as the bare global.
    /// </summary>
    internal static void EmitProcessEventEmitterCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        var runtime = ctx.Runtime!;

        switch (methodName)
        {
            case "on":
            case "addListener":
                // On(string eventName, object listener) -> $EventEmitter
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.On);
                break;

            case "once":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Once);
                break;

            case "off":
            case "removeListener":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Off);
                break;

            case "emit":
                // Emit(string eventName, object[] args) -> bool. The payload
                // array is built with the spread-aware builder so the facade's
                // `emit(event, ...args)` forwarding works (#1149 pattern).
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                emitter.EmitArgsArrayWithSpread(
                    arguments.Count > 1 ? arguments.GetRange(1, arguments.Count - 1) : []);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                break;

            case "removeAllListeners":
                // RemoveAllListeners(string eventName) -> $EventEmitter
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.RemoveAllListeners);
                break;

            case "listenerCount":
                // ListenerCount(string eventName) -> double
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.ListenerCount);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "listeners":
                // Listeners(string eventName) -> TSArray
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Listeners);
                break;

            case "eventNames":
                // EventNames() -> TSArray
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.EventNames);
                break;

            case "prependListener":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.PrependListener);
                break;

            case "prependOnceListener":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.PrependOnceListener);
                break;

            case "setMaxListeners":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                if (arguments.Count > 0)
                    emitter.EmitExpressionAsDouble(arguments[0]);
                else
                    il.Emit(OpCodes.Ldc_R8, 10.0);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.SetMaxListeners);
                break;

            case "getMaxListeners":
                il.Emit(OpCodes.Call, runtime.Process.GetEventEmitter);
                il.Emit(OpCodes.Callvirt, runtime.EventEmitter.GetMaxListeners);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            default:
                il.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private static void EmitStringArg(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var il = emitter.Context.IL;
        if (index < arguments.Count)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
            il.Emit(OpCodes.Callvirt, emitter.Context.Types.GetMethodNoParams(emitter.Context.Types.Object, "ToString"));
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
    }

    public bool HasStaticProperty(string memberName) => memberName is
        "platform" or "arch" or "pid" or "version" or "env" or "argv" or
        "exitCode" or "stdin" or "stdout" or "stderr" or
        "ppid" or "title" or "versions" or "execPath" or "execArgv" or "argv0" or
        "config" or "release" or "features" or "debugPort" or
        "allowedNodeEnvironmentFlags" or "report" or "hrtime" or "memoryUsage" or
        "throwDeprecation" or "traceDeprecation" or "noDeprecation" or
        "sourceMapsEnabled" or "connected" or "channel" or "send" or "disconnect";
}
