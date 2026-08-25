using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
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
            ["execSync"] = BuiltInMethod.CreateV2("execSync", 1, 2, ExecSync),
            ["spawnSync"] = BuiltInMethod.CreateV2("spawnSync", 1, 3, SpawnSync),
            ["exec"] = BuiltInMethod.CreateV2("exec", 1, 3, Exec),
            ["spawn"] = BuiltInMethod.CreateV2("spawn", 1, 3, Spawn),
            ["execFileSync"] = BuiltInMethod.CreateV2("execFileSync", 1, 3, ExecFileSync),
            ["execFile"] = BuiltInMethod.CreateV2("execFile", 1, 4, ExecFile),
            ["fork"] = BuiltInMethod.CreateV2("fork", 1, 3, Fork)
        };
    }

    private static RuntimeValue ExecSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string command)
            throw new Exception("child_process.execSync requires a command string");

        var options = args.Length > 1 ? args[1].ToObject() as SharpTSObject : null;
        var cwd = GetStringOption(options, "cwd");
        var timeout = GetDoubleOption(options, "timeout", -1);
        var input = GetStringOption(options, "input");

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
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
        interpreter.RegisterOwnedProcess(process);
        try
        {
            if (input != null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();

            if (timeout > 0)
            {
                if (!process.WaitForExit((int)timeout))
                {
                    ProcessTreeTermination.Terminate(process);
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

            return RuntimeValue.FromString(stdout);
        }
        finally
        {
            interpreter.UnregisterOwnedProcess(process);
        }
    }

    private static RuntimeValue SpawnSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string command)
            throw new Exception("child_process.spawnSync requires a command");

        var cmdArgs = new List<string>();
        if (args.Length > 1 && args[1].ToObject() is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray)
            {
                cmdArgs.Add(arg?.ToString() ?? "");
            }
        }

        var options = args.Length > 2 ? args[2].ToObject() as SharpTSObject : null;
        var cwd = GetStringOption(options, "cwd");
        var input = GetStringOption(options, "input");

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
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
            interpreter.RegisterOwnedProcess(process);
            try
            {
                if (input != null)
                {
                    // Feed the synchronous stdin, then close so the child sees EOF.
                    process.StandardInput.Write(input);
                    process.StandardInput.Close();
                }
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
            finally
            {
                interpreter.UnregisterOwnedProcess(process);
            }
        }
        catch (Exception ex)
        {
            return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
            {
                ["stdout"] = "",
                ["stderr"] = "",
                ["status"] = (double)-1,
                ["signal"] = null,
                ["error"] = ex.Message
            }));
        }

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["status"] = (double)exitCode,
            ["signal"] = signal
        }));
    }

    /// <summary>
    /// exec(command, options?, callback?) - Executes a command asynchronously.
    /// Returns a ChildProcess object. Calls callback(error, stdout, stderr) when done.
    /// </summary>
    private static RuntimeValue Exec(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string command)
            throw new Exception("child_process.exec requires a command string");

        SharpTSObject? options = null;
        ISharpTSCallable? callback = null;

        // Parse arguments: exec(command, options?, callback?)
        if (args.Length > 1)
        {
            if (args[1].ToObject() is ISharpTSCallable cb1)
            {
                callback = cb1;
            }
            else
            {
                options = args[1].ToObject() as SharpTSObject;
                if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable cb2)
                {
                    callback = cb2;
                }
            }
        }

        var cwd = GetStringOption(options, "cwd");
        var timeout = GetDoubleOption(options, "timeout", -1);

        // Create the ChildProcess event emitter
        var childProcess = new SharpTSChildProcess();

        // Keep the event loop alive while the child runs; the terminal callback /
        // lifecycle events are marshalled back onto the loop (EnqueueCallback) so they
        // run on the loop thread AFTER the synchronous script registers its listeners —
        // matching Node's "deferred to a future tick" guarantee.
        interpreter.Ref();
        Task.Run(() =>
        {
            object stdout = "", stderr = ""; // string (decoded) or SharpTSBuffer (encoding:'buffer')
            int code = 0, kind = 0; // kind: 0 normal, 1 timeout, 2 exception
            object? error = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

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

                ApplyEnvOptions(options, startInfo);

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                interpreter.RegisterOwnedProcess(process);
                childProcess.SetPid(process.Id);
                childProcess.SetProcess(process);
                try
                {
                    var maxBuffer = (int)GetDoubleOption(options, "maxBuffer", 1024 * 1024);
                    var (asBuffer, encoding) = GetOutputEncoding(options);
                    var outBytes = ReadCappedBytes(process.StandardOutput.BaseStream, maxBuffer, out var oOver);
                    var errBytes = ReadCappedBytes(process.StandardError.BaseStream, maxBuffer, out var eOver);
                    stdout = asBuffer ? new SharpTSBuffer(outBytes) : BufferEncoding.Decode(outBytes, encoding);
                    stderr = asBuffer ? new SharpTSBuffer(errBytes) : BufferEncoding.Decode(errBytes, encoding);
                    if (oOver || eOver)
                    {
                        ProcessTreeTermination.Terminate(process);
                        error = MaxBufferError();
                    }

                    if (timeout > 0)
                    {
                        if (!process.WaitForExit((int)timeout))
                        {
                            ProcessTreeTermination.Terminate(process);
                            kind = 1;
                        }
                        else { code = process.ExitCode; }
                    }
                    else
                    {
                        process.WaitForExit();
                        code = process.ExitCode;
                    }

                    if (kind == 0 && error == null && code != 0)
                        error = new SharpTSObject(new Dictionary<string, object?>
                        {
                            ["message"] = $"Command failed with exit code {code}",
                            ["code"] = (double)code
                        });
                }
                finally
                {
                    interpreter.UnregisterOwnedProcess(process);
                    childProcess.ClearProcess(process);
                }
            }
            catch (Exception ex)
            {
                kind = 2;
                error = new SharpTSObject(new Dictionary<string, object?> { ["message"] = ex.Message });
            }

            interpreter.EnqueueCallback(() =>
            {
                try
                {
                    if (kind == 2)
                    {
                        childProcess.EmitWith(interpreter, "error", error);
                        callback?.CallBoxed(interpreter, [error, "", ""]);
                    }
                    else if (kind == 1)
                    {
                        childProcess.SetExitCode(-1);
                        var timeoutErr = new SharpTSObject(new Dictionary<string, object?> { ["message"] = "Command timed out" });
                        childProcess.EmitWith(interpreter, "error", timeoutErr);
                        callback?.CallBoxed(interpreter, [timeoutErr, stdout, stderr]);
                    }
                    else
                    {
                        childProcess.SetExitCode(code);
                        callback?.CallBoxed(interpreter, [error, stdout, stderr]);
                        childProcess.EmitWith(interpreter, "close", (double)code);
                        childProcess.EmitWith(interpreter, "exit", (double)code);
                    }
                }
                finally { interpreter.Unref(); }
            });
        });

        return RuntimeValue.FromObject(childProcess);
    }

    /// <summary>
    /// spawn(command, args?, options?) - Spawns a process asynchronously.
    /// Returns a ChildProcess object with stdout/stderr streams.
    /// </summary>
    private static RuntimeValue Spawn(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string command)
            throw new Exception("child_process.spawn requires a command");

        var cmdArgs = new List<string>();
        SharpTSObject? options = null;

        if (args.Length > 1 && args[1].ToObject() is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray)
            {
                cmdArgs.Add(arg?.ToString() ?? "");
            }
            if (args.Length > 2)
                options = args[2].ToObject() as SharpTSObject;
        }
        else if (args.Length > 1)
        {
            options = args[1].ToObject() as SharpTSObject;
        }

        var cwd = GetStringOption(options, "cwd");
        var (useShell, shellPath) = GetShellOption(options);
        var (stdinMode, stdoutMode, stderrMode) = ParseStdioModes(options);

        // Create the ChildProcess; only 'pipe' fds get a real stream (Node sets the others
        // to null). 'inherit' shares the parent's fd; 'ignore' discards.
        var childProcess = new SharpTSChildProcess();
        var stdoutStream = stdoutMode == "pipe" ? new SharpTSReadable() : null;
        var stderrStream = stderrMode == "pipe" ? new SharpTSReadable() : null;
        var stdinStream = stdinMode == "pipe" ? new SharpTSWritable() : null;
        if (stdoutStream != null) childProcess.SetStdoutStream(stdoutStream);
        if (stderrStream != null) childProcess.SetStderrStream(stderrStream);
        if (stdinStream != null) childProcess.SetStdinStream(stdinStream);

        // Start synchronously so child.stdin.write()/.end() on the same tick reach a live
        // StandardInput; a spawn failure is surfaced as an async 'error' event.
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                // 'inherit' shares the parent's fd (no redirect); 'pipe'/'ignore' redirect.
                RedirectStandardOutput = stdoutMode != "inherit",
                RedirectStandardError = stderrMode != "inherit",
                RedirectStandardInput = stdinMode != "inherit",
                CreateNoWindow = true
            };

            if (useShell)
            {
                // Run "command args" through a shell (cmd.exe / sh), matching exec().
                ApplyShellCommand(startInfo, command, cmdArgs, shellPath);
            }
            else
            {
                startInfo.FileName = command;
                if (GetBoolOption(options, "windowsVerbatimArguments", false))
                    // Pass args verbatim (no .NET quoting), as Node's windowsVerbatimArguments asks.
                    startInfo.Arguments = string.Join(" ", cmdArgs);
                else
                    foreach (var arg in cmdArgs)
                        startInfo.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrEmpty(cwd))
                startInfo.WorkingDirectory = cwd;

            ApplyEnvOptions(options, startInfo);

            process = new Process { StartInfo = startInfo };
            process.Start();
            interpreter.RegisterOwnedProcess(process);
            childProcess.SetPid(process.Id);
            childProcess.SetProcess(process);

            // Wire stdin (only when piped): forwarding write() to the started process.
            if (stdinStream != null)
            {
                stdinStream.SetWriteCallback(BuiltInMethod.CreateV2("write", 1, 3, (interp, recv, wargs) =>
                {
                    // A dead/closed child stdin must surface through the write
                    // callback (err-first). Swallowing it and invoking the
                    // callback with null made a failed child.stdin.write()
                    // undetectable from guest code.
                    object? writeError = null;
                    if (wargs.Length > 0 && !wargs[0].IsNull)
                    {
                        try
                        {
                            process.StandardInput.Write(wargs[0].ToObject()!.ToString());
                            process.StandardInput.Flush();
                        }
                        catch
                        {
                            writeError = new SharpTSError("write EPIPE") { Code = "EPIPE" };
                        }
                    }
                    if (wargs.Length > 2 && wargs[2].ToObject() is ISharpTSCallable cb)
                        cb.Call(interpreter, [writeError]);
                    else if (wargs.Length > 1 && wargs[1].ToObject() is ISharpTSCallable cb2)
                        cb2.Call(interpreter, [writeError]);
                    return RuntimeValue.FromBoolean(writeError == null);
                }));

                // stdin.end() closes the child's StandardInput so it sees EOF.
                stdinStream.SetFinalCallback(BuiltInMethod.CreateV2("final", 0, 1, (interp, recv, fargs) =>
                {
                    try { process.StandardInput.Close(); }
                    catch { }
                    if (fargs.Length > 0 && fargs[0].ToObject() is ISharpTSCallable done)
                        done.Call(interp, [null]);
                    return RuntimeValue.Undefined;
                }));
            }
        }
        catch (Exception ex)
        {
            if (process != null)
            {
                ProcessTreeTermination.Terminate(process);
                interpreter.UnregisterOwnedProcess(process);
                childProcess.ClearProcess(process);
                process.Dispose();
            }
            var spawnErr = SpawnError(ex, command, "spawn");
            interpreter.Ref();
            interpreter.EnqueueCallback(() =>
            {
                try { childProcess.EmitWith(interpreter, "error", spawnErr); }
                finally { interpreter.Unref(); }
            });
            return RuntimeValue.FromObject(childProcess);
        }

        // Keep the loop alive while the child runs; stream chunks and lifecycle events are
        // marshalled onto the loop thread (PushFromHost / EmitWith) so they run with a real
        // interpreter and after the synchronous script has attached its listeners.
        var startedProcess = process!;
        interpreter.Ref();
        Task.Run(() =>
        {
            try
            {
                // Pump each redirected pipe: 'pipe' feeds the stream; 'ignore' drains and
                // discards; 'inherit' isn't redirected, so it isn't read here.
                Task? stdoutTask = stdoutMode == "inherit" ? null : Task.Run(() =>
                {
                    var buffer = new char[4096];
                    int read;
                    while ((read = startedProcess.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (stdoutStream != null)
                        {
                            var chunk = new string(buffer, 0, read);
                            interpreter.EnqueueCallback(() => stdoutStream.PushFromHost(interpreter, chunk));
                        }
                    }
                    if (stdoutStream != null)
                        interpreter.EnqueueCallback(() => stdoutStream.PushFromHost(interpreter, null));
                });

                Task? stderrTask = stderrMode == "inherit" ? null : Task.Run(() =>
                {
                    var buffer = new char[4096];
                    int read;
                    while ((read = startedProcess.StandardError.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (stderrStream != null)
                        {
                            var chunk = new string(buffer, 0, read);
                            interpreter.EnqueueCallback(() => stderrStream.PushFromHost(interpreter, chunk));
                        }
                    }
                    if (stderrStream != null)
                        interpreter.EnqueueCallback(() => stderrStream.PushFromHost(interpreter, null));
                });

                startedProcess.WaitForExit();
                stdoutTask?.Wait();
                stderrTask?.Wait();
                int code = startedProcess.ExitCode;

                interpreter.EnqueueCallback(() =>
                {
                    try
                    {
                        childProcess.SetExitCode(code);
                        childProcess.EmitWith(interpreter, "close", (double)code);
                        childProcess.EmitWith(interpreter, "exit", (double)code);
                    }
                    finally { interpreter.Unref(); }
                });
            }
            catch (Exception ex)
            {
                interpreter.EnqueueCallback(() =>
                {
                    try
                    {
                        childProcess.EmitWith(interpreter, "error", new SharpTSObject(new Dictionary<string, object?>
                        {
                            ["message"] = ex.Message
                        }));
                    }
                    finally { interpreter.Unref(); }
                });
            }
            finally
            {
                interpreter.UnregisterOwnedProcess(startedProcess);
                childProcess.ClearProcess(startedProcess);
                startedProcess.Dispose();
            }
        });

        return RuntimeValue.FromObject(childProcess);
    }

    /// <summary>
    /// execFileSync(file, args?, options?) - Executes a file synchronously without a shell.
    /// Returns stdout as a string. Throws on non-zero exit code.
    /// </summary>
    private static RuntimeValue ExecFileSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string file)
            throw new Exception("child_process.execFileSync requires a file path");

        var cmdArgs = new List<string>();
        SharpTSObject? options = null;

        if (args.Length > 1 && args[1].ToObject() is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray)
                cmdArgs.Add(arg?.ToString() ?? "");
            if (args.Length > 2)
                options = args[2].ToObject() as SharpTSObject;
        }
        else if (args.Length > 1)
        {
            options = args[1].ToObject() as SharpTSObject;
        }

        var cwd = GetStringOption(options, "cwd");
        var timeout = GetDoubleOption(options, "timeout", -1);
        var input = GetStringOption(options, "input");

        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
            CreateNoWindow = true
        };

        foreach (var arg in cmdArgs)
            startInfo.ArgumentList.Add(arg);

        if (!string.IsNullOrEmpty(cwd))
            startInfo.WorkingDirectory = cwd;

        ApplyEnvOptions(options, startInfo);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        interpreter.RegisterOwnedProcess(process);
        try
        {
            if (input != null)
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();

            if (timeout > 0)
            {
                if (!process.WaitForExit((int)timeout))
                {
                    ProcessTreeTermination.Terminate(process);
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

            return RuntimeValue.FromString(stdout);
        }
        finally
        {
            interpreter.UnregisterOwnedProcess(process);
        }
    }

    /// <summary>
    /// execFile(file, args?, options?, callback?) - Executes a file asynchronously without a shell.
    /// Returns a ChildProcess object. Calls callback(error, stdout, stderr) when done.
    /// </summary>
    private static RuntimeValue ExecFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string file)
            throw new Exception("child_process.execFile requires a file path");

        var cmdArgs = new List<string>();
        SharpTSObject? options = null;
        ISharpTSCallable? callback = null;

        // Parse: execFile(file, args?, options?, callback?)
        var nextIdx = 1;
        if (args.Length > nextIdx && args[nextIdx].ToObject() is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray)
                cmdArgs.Add(arg?.ToString() ?? "");
            nextIdx++;
        }

        if (args.Length > nextIdx)
        {
            if (args[nextIdx].ToObject() is ISharpTSCallable cb)
            {
                callback = cb;
            }
            else
            {
                options = args[nextIdx].ToObject() as SharpTSObject;
                nextIdx++;
                if (args.Length > nextIdx && args[nextIdx].ToObject() is ISharpTSCallable cb2)
                    callback = cb2;
            }
        }

        var cwd = GetStringOption(options, "cwd");
        var timeout = GetDoubleOption(options, "timeout", -1);

        var childProcess = new SharpTSChildProcess();

        interpreter.Ref();
        Task.Run(() =>
        {
            object stdout = "", stderr = ""; // string (decoded) or SharpTSBuffer (encoding:'buffer')
            int code = 0, kind = 0; // kind: 0 normal, 1 timeout, 2 exception
            object? error = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                foreach (var arg in cmdArgs)
                    startInfo.ArgumentList.Add(arg);

                if (!string.IsNullOrEmpty(cwd))
                    startInfo.WorkingDirectory = cwd;

                ApplyEnvOptions(options, startInfo);

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                interpreter.RegisterOwnedProcess(process);
                childProcess.SetPid(process.Id);
                childProcess.SetProcess(process);
                try
                {
                    var maxBuffer = (int)GetDoubleOption(options, "maxBuffer", 1024 * 1024);
                    var (asBuffer, encoding) = GetOutputEncoding(options);
                    var outBytes = ReadCappedBytes(process.StandardOutput.BaseStream, maxBuffer, out var oOver);
                    var errBytes = ReadCappedBytes(process.StandardError.BaseStream, maxBuffer, out var eOver);
                    stdout = asBuffer ? new SharpTSBuffer(outBytes) : BufferEncoding.Decode(outBytes, encoding);
                    stderr = asBuffer ? new SharpTSBuffer(errBytes) : BufferEncoding.Decode(errBytes, encoding);
                    if (oOver || eOver)
                    {
                        ProcessTreeTermination.Terminate(process);
                        error = MaxBufferError();
                    }

                    if (timeout > 0)
                    {
                        if (!process.WaitForExit((int)timeout))
                        {
                            ProcessTreeTermination.Terminate(process);
                            kind = 1;
                        }
                        else { code = process.ExitCode; }
                    }
                    else
                    {
                        process.WaitForExit();
                        code = process.ExitCode;
                    }

                    if (kind == 0 && error == null && code != 0)
                        error = new SharpTSObject(new Dictionary<string, object?>
                        {
                            ["message"] = $"Command failed with exit code {code}",
                            ["code"] = (double)code
                        });
                }
                finally
                {
                    interpreter.UnregisterOwnedProcess(process);
                    childProcess.ClearProcess(process);
                }
            }
            catch (Exception ex)
            {
                kind = 2;
                error = new SharpTSObject(new Dictionary<string, object?> { ["message"] = ex.Message });
            }

            interpreter.EnqueueCallback(() =>
            {
                try
                {
                    if (kind == 2)
                    {
                        childProcess.EmitWith(interpreter, "error", error);
                        callback?.CallBoxed(interpreter, [error, "", ""]);
                    }
                    else if (kind == 1)
                    {
                        childProcess.SetExitCode(-1);
                        var timeoutErr = new SharpTSObject(new Dictionary<string, object?> { ["message"] = "Command timed out" });
                        childProcess.EmitWith(interpreter, "error", timeoutErr);
                        callback?.CallBoxed(interpreter, [timeoutErr, stdout, stderr]);
                    }
                    else
                    {
                        childProcess.SetExitCode(code);
                        callback?.CallBoxed(interpreter, [error, stdout, stderr]);
                        childProcess.EmitWith(interpreter, "close", (double)code);
                        childProcess.EmitWith(interpreter, "exit", (double)code);
                    }
                }
                finally { interpreter.Unref(); }
            });
        });

        return RuntimeValue.FromObject(childProcess);
    }

    /// <summary>
    /// fork(modulePath, args?, options?) - Spawns a new SharpTS process with IPC channel.
    /// Returns a ChildProcess with send()/on('message') support.
    /// </summary>
    private static RuntimeValue Fork(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not string modulePath)
            throw new Exception("child_process.fork requires a module path");

        var forkArgs = new List<string>();
        SharpTSObject? options = null;

        if (args.Length > 1 && args[1].ToObject() is SharpTSArray argsArray)
        {
            foreach (var arg in argsArray)
                forkArgs.Add(arg?.ToString() ?? "");
            if (args.Length > 2)
                options = args[2].ToObject() as SharpTSObject;
        }
        else if (args.Length > 1)
        {
            options = args[1].ToObject() as SharpTSObject;
        }

        var cwd = GetStringOption(options, "cwd");
        var childProcess = RunFork(modulePath, forkArgs, options, cwd, interpreter,
            interpreter.Ref, interpreter.Unref, interpreter.EnqueueCallback,
            (cp, ev, arg) => cp.EmitWith(interpreter, ev, arg),
            interpreter.RegisterOwnedProcess, interpreter.UnregisterOwnedProcess);
        return RuntimeValue.FromObject(childProcess);
    }

    /// <summary>
    /// Compiled-output bridge for fork (#1017). The emitted <c>$Runtime.ChildProcessFork</c>
    /// resolves this by reflection (keeping the standalone DLL free of a hard SharpTS.dll
    /// reference) and passes the compiled <c>$EventLoop</c>'s Ref/Unref/Schedule so IPC and
    /// lifecycle events marshal onto the compiled loop — mirroring SharpTSWorker.CreateForCompiledLoop.
    /// </summary>
    public static SharpTSChildProcess ForkForCompiledLoop(
        string modulePath, object? argsObj, object? optionsObj,
        Action eventLoopRef, Action eventLoopUnref, Action<Action> eventLoopSchedule,
        Action<Process> registerProcess, Action<Process> unregisterProcess)
    {
        var forkArgs = new List<string>();
        if (argsObj is SharpTSArray a)
            foreach (var x in a)
                forkArgs.Add(x?.ToString() ?? "");
        var options = optionsObj as SharpTSObject;
        var cwd = GetStringOption(options, "cwd");

        return RunFork(modulePath, forkArgs, options, cwd, interp: null,
            eventLoopRef, eventLoopUnref, eventLoopSchedule,
            (cp, ev, arg) => cp.EmitDirect(ev, arg), registerProcess, unregisterProcess);
    }

    /// <summary>
    /// Shared fork core: spawns the SharpTS runtime to run the child module over a named-pipe
    /// IPC channel, keeping the owning event loop alive (refLoop/unrefLoop) and marshalling
    /// IPC messages + stream chunks + lifecycle events onto it (post). <paramref name="interp"/>
    /// is the interpreter for interp-mode (null in compiled mode — stream pushes/events then
    /// dispatch compiled listeners directly).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "The empty bundled location is an expected branch: a standalone CLI runs itself, while an embedded single-file runtime reports that fork needs the managed SharpTS.dll.")]
    private static SharpTSChildProcess RunFork(
        string modulePath, List<string> forkArgs, SharpTSObject? options, string? cwd,
        Interp? interp, Action refLoop, Action unrefLoop, Action<Action> post,
        Action<SharpTSChildProcess, string, object?> emit,
        Action<Process> registerProcess, Action<Process> unregisterProcess)
    {
        var resolvedModule = modulePath;
        if (!Path.IsPathRooted(resolvedModule))
        {
            var basePath = cwd ?? Directory.GetCurrentDirectory();
            resolvedModule = Path.GetFullPath(Path.Combine(basePath, resolvedModule));
        }

        var pipeName = $"sharpts-ipc-{Guid.NewGuid():N}";
        var childProcess = new SharpTSChildProcess();
        var cts = new CancellationTokenSource();
        var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // The child must run the SharpTS interpreter on the .ts module. SharpTS.dll is always
        // the assembly hosting this method — the CLI entry in interp mode, and the co-located
        // runtime in compiled mode (RequireSharpTSRuntime). (Using the *entry* assembly would
        // be wrong when SharpTS is embedded, e.g. the xUnit testhost.)
        var sharpTsPath = typeof(ChildProcessModuleInterpreter).Assembly.Location;
        var processPath = Environment.ProcessPath;
        var processName = processPath != null ? Path.GetFileNameWithoutExtension(processPath) : "";
        bool processIsDotnet = processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        // "This process IS the SharpTS CLI" — detected structurally (the entry assembly is the
        // one hosting this method and the process is a real apphost, not the dotnet muxer)
        // rather than by executable name: the shipped binary is renamed per SKU (sharpts,
        // SharpTS.exe, user copies), so a name sniff breaks on the managed single-file SKU
        // and on Native AOT, where there is no SharpTS.dll on disk at all (#1324).
        bool processIsSharpTs = !processIsDotnet
            && !string.IsNullOrEmpty(processPath)
            && Assembly.GetEntryAssembly() == typeof(ChildProcessModuleInterpreter).Assembly;

        if (processIsSharpTs)
        {
            // Running as a self-contained SharpTS executable — run it directly on the module.
            startInfo.FileName = processPath!;
        }
        else
        {
            // Everything else (interp via `dotnet SharpTS.dll`, compiled output, or SharpTS
            // embedded as a library e.g. the test host): run the SharpTS.dll interpreter via
            // the dotnet host — ProcessPath when it is dotnet, otherwise the muxer on PATH.
            if (string.IsNullOrEmpty(sharpTsPath))
            {
                throw new InvalidOperationException(
                    "child_process.fork() needs SharpTS.dll on disk to launch the child " +
                    "interpreter, but the SharpTS runtime is loaded from a single-file bundle. " +
                    "Run the program with the SharpTS CLI, or use the managed (framework-dependent) build.");
            }
            startInfo.FileName = processIsDotnet ? processPath! : "dotnet";
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(sharpTsPath);
        }

        startInfo.ArgumentList.Add(resolvedModule);
        foreach (var arg in forkArgs)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment["SHARPTS_IPC_PIPE"] = pipeName;
        if (!string.IsNullOrEmpty(cwd))
            startInfo.WorkingDirectory = cwd;
        ApplyEnvOptions(options, startInfo);
        startInfo.Environment["SHARPTS_IPC_PIPE"] = pipeName; // survive an env override

        var stdoutStream = new SharpTSReadable();
        var stderrStream = new SharpTSReadable();
        childProcess.SetStdoutStream(stdoutStream);
        childProcess.SetStderrStream(stderrStream);

        refLoop();
        Task.Run(() =>
        {
            Process? process = null;
            try
            {
                process = new Process { StartInfo = startInfo };
                process.Start();
                registerProcess(process);
                childProcess.SetPid(process.Id);
                childProcess.SetProcess(process);

                var connectTask = pipeServer.WaitForConnectionAsync(cts.Token);
                if (!connectTask.Wait(10_000))
                {
                    pipeServer.Dispose();
                    throw new Exception("Child process failed to connect to IPC pipe");
                }

                var writer = new StreamWriter(pipeServer) { AutoFlush = true };
                childProcess.SetupIpc(pipeServer, writer, cts);

                // IPC message reader — marshal each message onto the owning loop. The loop reads
                // until EOF (the child closing its write-end / exiting) rather than polling the
                // cancellation token, so a final message buffered in the pipe (e.g. a reply sent
                // right before the child exits) is always drained — see the WaitForExit handshake
                // below, which awaits this task before emitting 'close'/'exit' + unref'ing.
                var ipcReader = Task.Run(() =>
                {
                    try
                    {
                        using var reader = new StreamReader(pipeServer, leaveOpen: true);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var message = IpcSerializer.Deserialize(line);
                            post(() => emit(childProcess, "message", message));
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                });

                var stdoutTask = Task.Run(() =>
                {
                    var buffer = new char[4096];
                    int read;
                    while ((read = process.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        var chunk = new string(buffer, 0, read);
                        post(() => stdoutStream.PushFromHost(interp, chunk));
                    }
                    post(() => stdoutStream.PushFromHost(interp, null));
                });

                var stderrTask = Task.Run(() =>
                {
                    var buffer = new char[4096];
                    int read;
                    while ((read = process.StandardError.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        var chunk = new string(buffer, 0, read);
                        post(() => stderrStream.PushFromHost(interp, chunk));
                    }
                    post(() => stderrStream.PushFromHost(interp, null));
                });

                process.WaitForExit();

                // The child has exited, so its IPC write-end is closed: the reader is guaranteed
                // to drain any remaining buffered message and then see EOF (null), completing
                // promptly. Awaiting it here ensures a reply written just before the child exited
                // is posted as 'message' BEFORE we post 'close'/'exit' and drop the keep-alive ref
                // — otherwise the loop could unref and drain out before the 'message' callback runs
                // (the Fork_IpcRoundTrip empty-output race under CPU contention).
                ipcReader.Wait();
                cts.Cancel();
                stdoutTask.Wait();
                stderrTask.Wait();
                int code = process.ExitCode;

                post(() =>
                {
                    try
                    {
                        childProcess.SetExitCode(code);
                        emit(childProcess, "close", (double)code);
                        emit(childProcess, "exit", (double)code);
                    }
                    finally { unrefLoop(); }
                });
            }
            catch (Exception ex)
            {
                ProcessTreeTermination.Terminate(process);
                post(() =>
                {
                    try
                    {
                        emit(childProcess, "error", new SharpTSObject(new Dictionary<string, object?>
                        {
                            ["message"] = ex.Message
                        }));
                    }
                    finally { unrefLoop(); }
                });
            }
            finally
            {
                cts.Cancel();
                if (process != null)
                {
                    unregisterProcess(process);
                    childProcess.ClearProcess(process);
                    process.Dispose();
                }
            }
        });

        return childProcess;
    }

    private static string? GetStringOption(SharpTSObject? options, string name)
    {
        if (options == null)
            return null;
        var value = options.GetProperty(name);
        // Handle null and undefined as "not set"
        if (value == null || value is SharpTSUndefined)
            return null;
        return value.ToString();
    }

    private static double GetDoubleOption(SharpTSObject? options, string name, double defaultValue)
    {
        if (options == null)
            return defaultValue;
        var value = options.GetProperty(name);
        return value is double d ? d : defaultValue;
    }

    /// <summary>
    /// Reads the `stdio` option into per-fd modes (stdin, stdout, stderr), each "pipe",
    /// "inherit", or "ignore". Accepts the string shorthand (applied to all three) or the
    /// array form. fd numbers / streams / 'ipc' fall back to "pipe" (documented limitation).
    /// </summary>
    /// <summary>
    /// Reads a stream up to <paramref name="maxBuffer"/> bytes (Node's exec maxBuffer is in
    /// bytes). Sets <paramref name="overflowed"/> and stops at the cap; a negative cap is unbounded.
    /// </summary>
    private static byte[] ReadCappedBytes(System.IO.Stream stream, int maxBuffer, out bool overflowed)
    {
        overflowed = false;
        using var ms = new System.IO.MemoryStream();
        var buf = new byte[4096];
        int total = 0, n;
        while ((n = stream.Read(buf, 0, buf.Length)) > 0)
        {
            if (maxBuffer >= 0 && total + n > maxBuffer)
            {
                ms.Write(buf, 0, Math.Max(0, maxBuffer - total));
                overflowed = true;
                break;
            }
            ms.Write(buf, 0, n);
            total += n;
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Reads the `encoding` option: 'buffer' → raw Buffer output; any other name → decode the
    /// bytes with that encoding; absent/unrecognised → 'utf8' (Node's exec default). (Explicit
    /// `encoding: null` → Buffer is not distinguished from absent and decodes as utf8.)
    /// </summary>
    private static (bool asBuffer, string encoding) GetOutputEncoding(SharpTSObject? options)
    {
        var value = options?.GetProperty("encoding");
        if (value is string s)
            return s == "buffer" ? (true, "utf8") : (false, s);
        return (false, "utf8");
    }

    private static SharpTSObject MaxBufferError() => new(new Dictionary<string, object?>
    {
        ["message"] = "stdout maxBuffer length exceeded",
        ["code"] = "ERR_CHILD_PROCESS_STDIO_MAXBUFFER"
    });

    /// <summary>
    /// Converts a process-start failure into a Node-style error object. A missing executable
    /// (Win32 ERROR_FILE_NOT_FOUND) becomes an ENOENT error carrying code/errno/syscall/path.
    /// </summary>
    private static SharpTSObject SpawnError(Exception ex, string command, string syscall)
    {
        if (ex is System.ComponentModel.Win32Exception w32 && w32.NativeErrorCode == 2)
        {
            // libuv reports UV_ENOENT as -4058 on Windows, -2 on POSIX.
            var errno = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? -4058.0 : -2.0;
            return new(new Dictionary<string, object?>
            {
                ["message"] = $"{syscall} {command} ENOENT",
                ["code"] = "ENOENT",
                ["errno"] = errno,
                ["syscall"] = $"{syscall} {command}",
                ["path"] = command
            });
        }
        return new(new Dictionary<string, object?> { ["message"] = ex.Message });
    }

    private static (string stdin, string stdout, string stderr) ParseStdioModes(SharpTSObject? options)
    {
        var value = options?.GetProperty("stdio");
        static string Norm(object? v) => v is string s && (s == "inherit" || s == "ignore" || s == "pipe") ? s : "pipe";

        if (value is string str)
        {
            var m = Norm(str);
            return (m, m, m);
        }
        if (value is SharpTSArray arr)
        {
            string At(int i) => i < arr.Count ? Norm(arr[i]) : "pipe";
            return (At(0), At(1), At(2));
        }
        return ("pipe", "pipe", "pipe");
    }

    /// <summary>
    /// Reads the `shell` option: true → default platform shell; a string → that shell path;
    /// false/absent → no shell. Returns (useShell, shellPath?).
    /// </summary>
    private static (bool useShell, string? shellPath) GetShellOption(SharpTSObject? options)
    {
        if (options == null)
            return (false, null);
        var value = options.GetProperty("shell");
        if (value is bool b)
            return (b, null);
        if (value is string s && !string.IsNullOrEmpty(s))
            return (true, s);
        return (false, null);
    }

    /// <summary>
    /// Configures a ProcessStartInfo to run "command args" through a shell (cmd.exe /c on
    /// Windows, /bin/sh -c on Unix, or an explicit shell path), mirroring exec().
    /// </summary>
    private static void ApplyShellCommand(ProcessStartInfo startInfo, string command, List<string> cmdArgs, string? shellPath)
    {
        var fullCommand = cmdArgs.Count > 0 ? command + " " + string.Join(" ", cmdArgs) : command;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = string.IsNullOrEmpty(shellPath) ? "cmd.exe" : shellPath;
            startInfo.Arguments = "/d /s /c " + fullCommand;
        }
        else
        {
            startInfo.FileName = string.IsNullOrEmpty(shellPath) ? "/bin/sh" : shellPath;
            startInfo.Arguments = "-c \"" + fullCommand.Replace("\"", "\\\"") + "\"";
        }
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
