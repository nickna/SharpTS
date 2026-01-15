using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'child_process' module.
/// </summary>
public static class ChildProcessModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the child_process module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["execSync"] = new BuiltInMethod("execSync", 1, 2, ExecSync),
            ["spawnSync"] = new BuiltInMethod("spawnSync", 1, 3, SpawnSync)
        };
    }

    private static object? ExecSync(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string command)
            throw new Exception("child_process.execSync requires a command string");

        var options = args.Count > 1 ? args[1] as SharpTSObject : null;
        var cwd = GetStringOption(options, "cwd");
        var timeout = GetDoubleOption(options, "timeout", -1);

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

        if (!string.IsNullOrEmpty(cwd))
            startInfo.WorkingDirectory = cwd;

        // Apply environment variables from options
        ApplyEnvOptions(options, startInfo);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();

        if (timeout > 0)
        {
            if (!process.WaitForExit((int)timeout))
            {
                process.Kill();
                throw new Exception("Command timed out");
            }
        }
        else
        {
            process.WaitForExit();
        }

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new Exception($"Command failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }

    private static object? SpawnSync(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string command)
            throw new Exception("child_process.spawnSync requires a command");

        var cmdArgs = new List<string>();
        if (args.Count > 1 && args[1] is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray.Elements)
            {
                cmdArgs.Add(arg?.ToString() ?? "");
            }
        }

        var options = args.Count > 2 ? args[2] as SharpTSObject : null;
        var cwd = GetStringOption(options, "cwd");
        var useShell = GetBoolOption(options, "shell", false);

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

        if (!string.IsNullOrEmpty(cwd))
            startInfo.WorkingDirectory = cwd;

        // Apply environment variables from options
        ApplyEnvOptions(options, startInfo);

        string stdout, stderr;
        int exitCode;
        string? signal = null;

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
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["stdout"] = "",
                ["stderr"] = "",
                ["status"] = (double)-1,
                ["signal"] = null,
                ["error"] = ex.Message
            });
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["status"] = (double)exitCode,
            ["signal"] = signal
        });
    }

    private static string? GetStringOption(SharpTSObject? options, string name)
    {
        if (options == null)
            return null;
        var value = options.GetProperty(name);
        return value?.ToString();
    }

    private static double GetDoubleOption(SharpTSObject? options, string name, double defaultValue)
    {
        if (options == null)
            return defaultValue;
        var value = options.GetProperty(name);
        return value is double d ? d : defaultValue;
    }

    private static bool GetBoolOption(SharpTSObject? options, string name, bool defaultValue)
    {
        if (options == null)
            return defaultValue;
        var value = options.GetProperty(name);
        return value is bool b ? b : defaultValue;
    }

    private static void ApplyEnvOptions(SharpTSObject? options, ProcessStartInfo startInfo)
    {
        if (options == null)
            return;

        var env = options.GetProperty("env");
        if (env is SharpTSObject envObj)
        {
            foreach (var kv in envObj.Fields)
            {
                startInfo.Environment[kv.Key] = kv.Value?.ToString();
            }
        }
    }
}
