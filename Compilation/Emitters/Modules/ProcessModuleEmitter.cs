using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'process' module.
/// Delegates to ProcessStaticEmitter for most operations.
/// </summary>
public sealed class ProcessModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "process";

    private static readonly string[] _exportedMembers =
    [
        "processObject",
        "platform", "arch", "pid", "ppid", "version", "versions", "env",
        "argv", "argv0", "execPath", "execArgv", "exitCode", "title",
        "config", "release", "features", "debugPort", "allowedNodeEnvironmentFlags",
        "stdin", "stdout", "stderr", "report",
        "throwDeprecation", "traceDeprecation", "noDeprecation", "sourceMapsEnabled",
        "connected", "channel", "send", "disconnect",
        "getuid", "geteuid", "getgid", "getegid", "getgroups", "setuid", "setgid",
        "cwd", "chdir", "exit", "hrtime", "uptime", "memoryUsage", "nextTick",
        "kill", "abort", "umask", "cpuUsage", "resourceUsage",
        "availableMemory", "constrainedMemory", "getActiveResourcesInfo",
        "emitWarning", "setSourceMapsEnabled",
        "on", "addListener", "once", "off", "removeListener", "emit",
        "removeAllListeners", "listeners", "rawListeners", "listenerCount",
        "eventNames", "prependListener", "prependOnceListener",
        "setMaxListeners", "getMaxListeners",
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "cwd": return EmitCwd(emitter);
            case "chdir": return EmitChdir(emitter, arguments);
            case "exit": return EmitExit(emitter, arguments);
            case "hrtime": return EmitHrtime(emitter, arguments);
            case "uptime": return EmitUptime(emitter);
            case "memoryUsage": return EmitMemoryUsage(emitter);
            case "nextTick": return EmitNextTick(emitter, arguments);

            case "kill":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessKill);
                return true;

            case "abort":
                il.Emit(OpCodes.Ldstr, "process.abort() called");
                il.Emit(OpCodes.Call, typeof(Environment).GetMethod("FailFast", [ctx.Types.String])!);
                il.Emit(OpCodes.Ldnull);
                return true;

            case "umask":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessUmask);
                return true;

            case "cpuUsage":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessCpuUsage);
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
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 2);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 3);
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessEmitWarning);
                return true;

            case "setSourceMapsEnabled":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProcessObject);
                il.Emit(OpCodes.Ldstr, "sourceMapsEnabled");
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetProperty);
                il.Emit(OpCodes.Ldnull);
                return true;

            // EventEmitter surface: shared dispatch against the $Process
            // singleton — same emitter instance as the bare global.
            case "on" or "addListener" or "once" or "off" or "removeListener"
                or "emit" or "removeAllListeners" or "listeners" or "listenerCount"
                or "eventNames" or "prependListener" or "prependOnceListener"
                or "setMaxListeners" or "getMaxListeners":
                ProcessStaticEmitter.EmitProcessEventEmitterCall(emitter, methodName, arguments);
                return true;

            case "rawListeners":
                // Alias of listeners in the compiled emitter surface.
                ProcessStaticEmitter.EmitProcessEventEmitterCall(emitter, "listeners", arguments);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "platform": return EmitPlatform(emitter);
            case "arch": return EmitArch(emitter);
            case "pid": return EmitPid(emitter);
            case "version": return EmitVersion(emitter);
            case "env": return EmitEnv(emitter);
            case "argv": return EmitArgv(emitter);
            case "exitCode": return EmitExitCode(emitter);
            case "stdin": return EmitStdin(emitter);
            case "stdout": return EmitStdout(emitter);
            case "stderr": return EmitStderr(emitter);
            case "nextTick": return EmitNextTickProperty(emitter);

            // The live process object — the module facade's default export.
            case "processObject":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProcessObject);
                return true;

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

            // Function-with-members values (hrtime.bigint / memoryUsage.rss).
            case "hrtime":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetHrtimeFn);
                return true;

            case "memoryUsage":
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetMemoryUsageFn);
                return true;

            // Flags + IPC state through the live object's dynamic path.
            case "throwDeprecation" or "traceDeprecation" or "noDeprecation"
                or "sourceMapsEnabled" or "connected" or "channel" or "send" or "disconnect":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProcessObject);
                il.Emit(OpCodes.Castclass, ctx.Runtime!.IHasFieldsInterface);
                il.Emit(OpCodes.Ldstr, propertyName);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.IHasFieldsGetProperty);
                return true;

            // POSIX identity: interpreter-only (undefined in compiled mode —
            // Windows parity is exact; POSIX standalone is a documented ceiling).
            case "getuid" or "geteuid" or "getgid" or "getegid"
                or "getgroups" or "setuid" or "setgid":
                il.Emit(OpCodes.Ldnull);
                return true;

            default:
                return false;
        }
    }

    #region Method Emitters

    private static bool EmitCwd(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Types.GetMethodNoParams(ctx.Types.Directory, "GetCurrentDirectory"));
        return true;
    }

    private static bool EmitChdir(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Directory, "SetCurrentDirectory", ctx.Types.String));
        }
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitExit(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpressionAsDouble(arguments[0]);
            il.Emit(OpCodes.Conv_I4);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Environment, "Exit", ctx.Types.Int32));
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitHrtime(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessHrtime);
        return true;
    }

    private static bool EmitUptime(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessUptime);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitMemoryUsage(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessMemoryUsage);
        return true;
    }

    /// <summary>
    /// Emits: process.nextTick(callback, ...args)
    /// Implemented as setTimeout(callback, 0, ...args) - runs as soon as possible.
    /// Throws if callback is absent or null, matching the interpreter and Node.
    /// </summary>
    private static bool EmitNextTick(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Zero-arg call: throw at runtime. Matches ProcessModuleInterpreter.NextTick.
        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "Runtime Error: process.nextTick requires at least 1 argument");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            // Throw is terminal, but the verifier needs a value-producing expression.
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit callback, save to local so we can null-check and reuse.
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        var cbLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, cbLocal);

        // if (cb == null) throw — matches interpreter "callback must be a function".
        var callbackOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Brtrue, callbackOkLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: process.nextTick callback must be a function");
        il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(callbackOkLabel);

        // Push validated callback for SetTimeout.
        il.Emit(OpCodes.Ldloc, cbLocal);

        // Delay is always 0 for nextTick
        il.Emit(OpCodes.Ldc_R8, 0.0);

        // Emit args array - remaining arguments (starting from index 1)
        EmitArgsArray(emitter, arguments, 1);

        // Call $Runtime.SetTimeout(callback, 0, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeout);

        // nextTick returns undefined, so pop the result and push null
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);

        return true;
    }

    /// <summary>
    /// Emits an object[] array with the remaining arguments starting from startIndex,
    /// expanding any <see cref="Expr.Spread"/> (<c>...args</c>) at runtime via the shared
    /// spread-aware builder. Leaves an <c>object[]</c> on the stack. Forwarding spreads
    /// here is what lets <c>process.nextTick</c>'s TS facade pass <c>...args</c> straight
    /// through instead of hand-unrolling an arity ladder (#1149).
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        int extraArgCount = Math.Max(0, arguments.Count - startIndex);
        var extra = extraArgCount > 0
            ? arguments.GetRange(startIndex, extraArgCount)
            : new List<Expr>();
        emitter.EmitArgsArrayWithSpread(extra);
    }

    #endregion

    #region Property Emitters

    private static bool EmitPlatform(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
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
        return true;
    }

    private static bool EmitArch(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        il.Emit(OpCodes.Ldstr, arch);
        return true;
    }

    private static bool EmitPid(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "ProcessId"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitVersion(IEmitterContext emitter)
    {
        // The emulated Node version (see ProcessBuiltIns.NodeVersion).
        emitter.Context.IL.Emit(OpCodes.Ldstr,
            "v" + SharpTS.Runtime.BuiltIns.ProcessBuiltIns.NodeVersion);
        return true;
    }

    private static bool EmitEnv(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetEnv);
        return true;
    }

    private static bool EmitArgv(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetArgv);
        return true;
    }

    private static bool EmitExitCode(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "ExitCode"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    // process.stdio singletons depend on $Readable/$Writable, gated on
    // UsesNodeStreams. When the gate is off, runtime.GetStdin/Stdout/Stderr
    // are null MethodBuilders. Emit `null` (-> JS `undefined` on read) instead
    // of crashing at IL-emit time.
    //
    // The stdlib `process` shim re-exports stdin/stdout/stderr eagerly even
    // when downstream user code only uses `nextTick`; without this null-emit
    // path the shim wouldn't compile under conservative gating. Programs that
    // actually USE process.stdout.write etc. flip UsesNodeStreams via the
    // member-access trigger in HandleMemberAccess, keeping the helpers alive.
    private static bool EmitStdin(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        var getStdin = emitter.Context.Runtime?.GetStdin;
        if (getStdin is null) il.Emit(OpCodes.Ldnull);
        else il.Emit(OpCodes.Call, getStdin);
        return true;
    }

    private static bool EmitStdout(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        var getStdout = emitter.Context.Runtime?.GetStdout;
        if (getStdout is null) il.Emit(OpCodes.Ldnull);
        else il.Emit(OpCodes.Call, getStdout);
        return true;
    }

    private static bool EmitStderr(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        var getStderr = emitter.Context.Runtime?.GetStderr;
        if (getStderr is null) il.Emit(OpCodes.Ldnull);
        else il.Emit(OpCodes.Call, getStderr);
        return true;
    }

    private static bool EmitNextTickProperty(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        // Return a TSFunction wrapper for nextTick
        il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetNextTick);
        return true;
    }

    #endregion

    public bool IsExportedProperty(string memberName) => memberName is
        "platform" or "arch" or "pid" or "version" or "env" or "argv" or "exitCode" or
        "stdin" or "stdout" or "stderr" or
        "processObject" or "ppid" or "title" or "versions" or "execPath" or
        "execArgv" or "argv0" or "config" or "release" or "features" or
        "debugPort" or "allowedNodeEnvironmentFlags" or "report" or
        "throwDeprecation" or "traceDeprecation" or "noDeprecation" or
        "sourceMapsEnabled" or "connected" or "channel" or "send" or "disconnect" or
        "getuid" or "geteuid" or "getgid" or "getegid" or "getgroups" or
        "setuid" or "setgid" or "hrtime" or "memoryUsage";
}
