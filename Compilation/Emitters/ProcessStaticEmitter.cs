using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for process static method calls and property access.
/// Handles process.cwd(), process.exit(), process.platform, process.env, etc.
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
            case "cwd":
                // Directory.GetCurrentDirectory()
                il.Emit(OpCodes.Call, ctx.Types.GetMethodNoParams(ctx.Types.Directory, "GetCurrentDirectory"));
                return true;

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
                // $Runtime.ProcessExit(code): emits 'exit' on the process
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
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessExit);
                return true;

            case "abort":
                il.Emit(OpCodes.Ldstr, "process.abort() called");
                il.Emit(OpCodes.Call, typeof(Environment).GetMethod("FailFast", [ctx.Types.String])!);
                il.Emit(OpCodes.Ldnull);
                return true;

            case "hrtime":
                EmitHrtime(emitter, arguments);
                return true;

            case "uptime":
                EmitUptime(emitter);
                return true;

            case "memoryUsage":
                EmitMemoryUsage(emitter);
                return true;

            case "nextTick":
                EmitNextTick(emitter, arguments);
                return true;

            case "kill":
                EmitTwoArgHelperCall(emitter, arguments, ctx.Runtime!.ProcessKill);
                return true;

            case "umask":
                EmitOneArgHelperCall(emitter, arguments, ctx.Runtime!.ProcessUmask);
                return true;

            case "cpuUsage":
                EmitOneArgHelperCall(emitter, arguments, ctx.Runtime!.ProcessCpuUsage);
                return true;

            case "resourceUsage":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessResourceUsage);
                return true;

            case "availableMemory":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessAvailableMemory);
                return true;

            case "constrainedMemory":
                il.Emit(OpCodes.Ldc_R8, 0.0);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getActiveResourcesInfo":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetActiveResourcesInfoM);
                return true;

            case "emitWarning":
                EmitFourArgHelperCall(emitter, arguments, ctx.Runtime!.ProcessEmitWarning);
                return true;

            case "setSourceMapsEnabled":
                // Route through the $Process property setter semantics via the
                // live object (SetProperty handles the bool coercion).
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProcessObject);
                il.Emit(OpCodes.Ldstr, "sourceMapsEnabled");
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetProperty);
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

    /// <summary>
    /// Attempts to emit IL for a process static property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "platform":
                // Emit platform string based on current OS
                EmitPlatformString(il);
                return true;

            case "arch":
                // Emit architecture string based on current architecture
                EmitArchString(il);
                return true;

            case "pid":
                // Environment.ProcessId
                il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "ProcessId"));
                il.Emit(OpCodes.Conv_R8); // Convert to double for JS number
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "version":
                // The emulated Node version (see ProcessBuiltIns.NodeVersion) —
                // feature-detection code must not parse the CLR version here.
                il.Emit(OpCodes.Ldstr, "v" + SharpTS.Runtime.BuiltIns.ProcessBuiltIns.NodeVersion);
                return true;

            case "env":
                // Call runtime helper to create env object
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetEnv);
                return true;

            case "argv":
                // Call runtime helper to create argv array
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetArgv);
                return true;

            case "exitCode":
                // Environment.ExitCode
                il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "ExitCode"));
                il.Emit(OpCodes.Conv_R8); // Convert to double for JS number
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // Stream objects - return cached $Writable/$Readable singleton instances.
            // Member access (`process.stdin`) flips UsesNodeStreams via the detector's
            // HandleMemberAccess hook, so the helper is normally non-null when this
            // path runs. Null-guard regardless for defense-in-depth, mirroring
            // ProcessModuleEmitter's tolerant emission — emit Ldnull (-> JS undefined)
            // if the helper somehow wasn't generated.
            case "stdin":
                {
                    var m = ctx.Runtime?.GetStdin;
                    if (m is null) il.Emit(OpCodes.Ldnull); else il.Emit(OpCodes.Call, m);
                    return true;
                }

            case "stdout":
                {
                    var m = ctx.Runtime?.GetStdout;
                    if (m is null) il.Emit(OpCodes.Ldnull); else il.Emit(OpCodes.Call, m);
                    return true;
                }

            case "stderr":
                {
                    var m = ctx.Runtime?.GetStderr;
                    if (m is null) il.Emit(OpCodes.Ldnull); else il.Emit(OpCodes.Call, m);
                    return true;
                }

            // Methods accessible as properties (for typeof checks)
            case "nextTick":
                // Return a TSFunction wrapper for nextTick
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetNextTick);
                return true;

            // Function-with-members properties: process.hrtime.bigint(),
            // process.memoryUsage.rss() work through these cached functions.
            case "hrtime":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetHrtimeFn);
                return true;

            case "memoryUsage":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetMemoryUsageFn);
                return true;

            // Identity / info properties (#1085)
            case "ppid":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetPpid);
                return true;

            case "title":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetTitle);
                return true;

            case "versions":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetVersions);
                return true;

            case "execPath":
                {
                    var haveIt = il.DefineLabel();
                    il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "ProcessPath"));
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue, haveIt);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Call, ctx.Types.GetMethodNoParams(ctx.Types.Environment, "GetCommandLineArgs"));
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.MarkLabel(haveIt);
                    return true;
                }

            case "execArgv":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetExecArgv);
                return true;

            case "argv0":
                il.Emit(OpCodes.Call, ctx.Types.GetMethodNoParams(ctx.Types.Environment, "GetCommandLineArgs"));
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                return true;

            case "config":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetConfig);
                return true;

            case "release":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetRelease);
                return true;

            case "features":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetFeatures);
                return true;

            case "debugPort":
                il.Emit(OpCodes.Ldc_R8, 9229.0);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "allowedNodeEnvironmentFlags":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetAllowedFlags);
                return true;

            case "report":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetReport);
                return true;

            // Deprecation / source-map flags and IPC state: read through the
            // live $Process object's dynamic property path (single source of
            // truth for coercion + expando semantics).
            case "throwDeprecation" or "traceDeprecation" or "noDeprecation"
                or "sourceMapsEnabled" or "connected" or "channel" or "send" or "disconnect":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProcessObject);
                il.Emit(OpCodes.Castclass, ctx.Runtime!.IHasFieldsInterface);
                il.Emit(OpCodes.Ldstr, propertyName);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.IHasFieldsGetProperty);
                return true;

            default:
                return false;
        }
    }

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

    private static void EmitPlatformString(ILGenerator il)
    {
        // At compile time, we know the platform, so emit the string directly
        string platform;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            platform = "win32";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            platform = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            platform = "darwin";
        else
            platform = "unknown";

        il.Emit(OpCodes.Ldstr, platform);
    }

    private static void EmitArchString(ILGenerator il)
    {
        // At compile time, we know the architecture, so emit the string directly
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };

        il.Emit(OpCodes.Ldstr, arch);
    }

    /// <summary>
    /// Emits IL for process.hrtime(prev?).
    /// Returns a [seconds, nanoseconds] array.
    /// </summary>
    private static void EmitHrtime(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper that handles hrtime logic
        // The helper takes an optional previous time array
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessHrtime);
    }

    /// <summary>
    /// Emits IL for process.uptime().
    /// Returns the number of seconds the process has been running.
    /// </summary>
    private static void EmitUptime(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessUptime);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>
    /// Emits IL for process.memoryUsage().
    /// Returns an object with memory usage information.
    /// </summary>
    private static void EmitMemoryUsage(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessMemoryUsage);
    }

    /// <summary>
    /// Emits IL for process.nextTick(callback, ...args).
    /// Schedules callback to run via SetTimeout with delay 0.
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

        // Delay is always 0 for nextTick
        il.Emit(OpCodes.Ldc_R8, 0.0);

        // Emit args array - remaining arguments (starting from index 1)
        EmitArgsArray(emitter, arguments, 1);

        // Call $Runtime.SetTimeout(callback, 0, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeout);

        // nextTick returns undefined, so pop the result and push null
        il.Emit(OpCodes.Pop);
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
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
                break;

            case "once":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOnce);
                break;

            case "off":
            case "removeListener":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOff);
                break;

            case "emit":
                // Emit(string eventName, object[] args) -> bool. The payload
                // array is built with the spread-aware builder so the facade's
                // `emit(event, ...args)` forwarding works (#1149 pattern).
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                emitter.EmitArgsArrayWithSpread(
                    arguments.Count > 1 ? arguments.GetRange(1, arguments.Count - 1) : []);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                break;

            case "removeAllListeners":
                // RemoveAllListeners(string eventName) -> $EventEmitter
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterRemoveAllListeners);
                break;

            case "listenerCount":
                // ListenerCount(string eventName) -> double
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterListenerCount);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "listeners":
                // Listeners(string eventName) -> TSArray
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterListeners);
                break;

            case "eventNames":
                // EventNames() -> TSArray
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEventNames);
                break;

            case "prependListener":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterPrependListener);
                break;

            case "prependOnceListener":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                EmitStringArg(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterPrependOnceListener);
                break;

            case "setMaxListeners":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                if (arguments.Count > 0)
                    emitter.EmitExpressionAsDouble(arguments[0]);
                else
                    il.Emit(OpCodes.Ldc_R8, 10.0);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterSetMaxListeners);
                break;

            case "getMaxListeners":
                il.Emit(OpCodes.Call, runtime.GetProcessEventEmitter);
                il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterGetMaxListeners);
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
