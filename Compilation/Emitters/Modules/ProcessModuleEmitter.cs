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
        "platform", "arch", "pid", "version", "env", "argv", "exitCode",
        "stdin", "stdout", "stderr",
        "cwd", "chdir", "exit", "hrtime", "uptime", "memoryUsage", "nextTick"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
            "cwd" => EmitCwd(emitter),
            "chdir" => EmitChdir(emitter, arguments),
            "exit" => EmitExit(emitter, arguments),
            "hrtime" => EmitHrtime(emitter, arguments),
            "uptime" => EmitUptime(emitter),
            "memoryUsage" => EmitMemoryUsage(emitter),
            "nextTick" => EmitNextTick(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "platform" => EmitPlatform(emitter),
            "arch" => EmitArch(emitter),
            "pid" => EmitPid(emitter),
            "version" => EmitVersion(emitter),
            "env" => EmitEnv(emitter),
            "argv" => EmitArgv(emitter),
            "exitCode" => EmitExitCode(emitter),
            "stdin" => EmitStdin(emitter),
            "stdout" => EmitStdout(emitter),
            "stderr" => EmitStderr(emitter),
            "nextTick" => EmitNextTickProperty(emitter),
            _ => false
        };
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
    /// </summary>
    private static bool EmitNextTick(IEmitterContext emitter, List<Expr> arguments)
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

        return true;
    }

    /// <summary>
    /// Emits an object[] array with remaining arguments starting from startIndex.
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        int extraArgCount = Math.Max(0, arguments.Count - startIndex);

        if (extraArgCount > 0)
        {
            // Create array with remaining arguments
            il.Emit(OpCodes.Ldc_I4, extraArgCount);
            il.Emit(OpCodes.Newarr, ctx.Types.Object);

            for (int i = startIndex; i < arguments.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i - startIndex);
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Empty args array
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, ctx.Types.Object);
        }
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
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldstr, "v");
        il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "Version"));
        var versionLocal = il.DeclareLocal(ctx.Types.Version);
        il.Emit(OpCodes.Stloc, versionLocal);
        il.Emit(OpCodes.Ldloca, versionLocal);
        il.Emit(OpCodes.Constrained, ctx.Types.Version);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.String, "Concat", ctx.Types.String, ctx.Types.String));
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

    private static bool EmitStdin(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        il.Emit(OpCodes.Ldstr, "__$stdin$__");
        return true;
    }

    private static bool EmitStdout(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        il.Emit(OpCodes.Ldstr, "__$stdout$__");
        return true;
    }

    private static bool EmitStderr(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        il.Emit(OpCodes.Ldstr, "__$stderr$__");
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
}
