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

            case "exit":
                // Environment.Exit(code)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpressionAsDouble(arguments[0]);
                    il.Emit(OpCodes.Conv_I4); // Convert to int
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0); // Default exit code 0
                }
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Environment, "Exit", ctx.Types.Int32));
                // Exit never returns, but we need to push something for the stack
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
                // "v" + Environment.Version.ToString()
                il.Emit(OpCodes.Ldstr, "v");
                il.Emit(OpCodes.Call, ctx.Types.GetPropertyGetter(ctx.Types.Environment, "Version"));
                var versionLocal = il.DeclareLocal(ctx.Types.Version);
                il.Emit(OpCodes.Stloc, versionLocal);
                il.Emit(OpCodes.Ldloca, versionLocal);
                il.Emit(OpCodes.Constrained, ctx.Types.Version);
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.String, "Concat", ctx.Types.String, ctx.Types.String));
                return true;

            case "env":
                // Call runtime helper to create env object
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetEnv);
                return true;

            case "argv":
                // Call runtime helper to create argv array
                il.Emit(OpCodes.Call, ctx.Runtime!.ProcessGetArgv);
                return true;

            // Stream objects - return marker strings that can be detected in method calls
            case "stdin":
                il.Emit(OpCodes.Ldstr, "__$stdin$__");
                return true;

            case "stdout":
                il.Emit(OpCodes.Ldstr, "__$stdout$__");
                return true;

            case "stderr":
                il.Emit(OpCodes.Ldstr, "__$stderr$__");
                return true;

            default:
                return false;
        }
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
}
