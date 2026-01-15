using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits child_process module helper methods.
    /// </summary>
    private void EmitChildProcessMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitChildProcessExecSync(typeBuilder, runtime);
        EmitChildProcessSpawnSync(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static string ChildProcessExecSync(string command, object options)
    /// </summary>
    private void EmitChildProcessExecSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessExecSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object]);
        runtime.ChildProcessExecSync = method;

        var il = method.GetILGenerator();

        // Call the static helper method in ChildProcessHelper
        var helperType = typeof(ChildProcessHelper);
        var execMethod = helperType.GetMethod("ExecSync", BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0); // command
        il.Emit(OpCodes.Ldarg_1); // options
        il.Emit(OpCodes.Call, execMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ChildProcessSpawnSync(string command, object args, object options)
    /// </summary>
    private void EmitChildProcessSpawnSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChildProcessSpawnSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.ChildProcessSpawnSync = method;

        var il = method.GetILGenerator();

        // Call the static helper method in ChildProcessHelper
        var helperType = typeof(ChildProcessHelper);
        var spawnMethod = helperType.GetMethod("SpawnSync", BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0); // command
        il.Emit(OpCodes.Ldarg_1); // args
        il.Emit(OpCodes.Ldarg_2); // options
        il.Emit(OpCodes.Call, spawnMethod);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper class for child_process operations.
/// Used by the emitted runtime code.
/// </summary>
public static class ChildProcessHelper
{
    public static string ExecSync(string command, object? options)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Use shell to execute command
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c {command}";
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";
        }

        // Extract options if provided
        if (options is Runtime.Types.SharpTSObject opts)
        {
            var cwd = opts.GetProperty("cwd")?.ToString();
            if (!string.IsNullOrEmpty(cwd))
                startInfo.WorkingDirectory = cwd;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new Exception($"Command failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }

    public static object SpawnSync(string command, object? argsObj, object? options)
    {
        var cmdArgs = new List<string>();
        if (argsObj is Runtime.Types.SharpTSArray argsArray)
        {
            foreach (var arg in argsArray.Elements)
            {
                cmdArgs.Add(arg?.ToString() ?? "");
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in cmdArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Extract options if provided
        if (options is Runtime.Types.SharpTSObject opts)
        {
            var cwd = opts.GetProperty("cwd")?.ToString();
            if (!string.IsNullOrEmpty(cwd))
                startInfo.WorkingDirectory = cwd;
        }

        string stdout, stderr;
        int exitCode;

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            return new Runtime.Types.SharpTSObject(new Dictionary<string, object?>
            {
                ["stdout"] = "",
                ["stderr"] = "",
                ["status"] = (double)-1,
                ["signal"] = null,
                ["error"] = ex.Message
            });
        }

        return new Runtime.Types.SharpTSObject(new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["status"] = (double)exitCode,
            ["signal"] = (object?)null
        });
    }
}
