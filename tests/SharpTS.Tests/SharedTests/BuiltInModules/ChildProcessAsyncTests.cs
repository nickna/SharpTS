using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the async child_process methods (exec, spawn, execFile).
/// These methods return ChildProcess objects and use callbacks/events.
/// </summary>
public class ChildProcessAsyncTests
{
    [Theory, ModeData]
    public void Exec_ReturnsChildProcess(ExecutionMode mode)
    {
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo test"
            : "echo test";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { exec } from 'child_process';
                const child = exec('{{echoCommand}}');
                console.log(typeof child === 'object');
                console.log(typeof child.on === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Spawn_ReturnsChildProcess(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'spawned']"
            : "['spawned']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const child = spawn('{{command}}', {{args}});
                console.log(typeof child === 'object');
                console.log(typeof child.on === 'function');
                console.log(child.stdout !== null && child.stdout !== undefined);
                console.log(child.stderr !== null && child.stderr !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SpawnSync_Input_FeedsStdin(ExecutionMode mode)
    {
        // The `input` option feeds the synchronous child's stdin; `sort` echoes it back sorted.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { spawnSync } from 'child_process';
                const r = spawnSync('sort', [], { input: 'banana\napple\n' });
                console.log('sorted:' + r.stdout.trim());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("sorted:apple\nbanana\n", output);
    }

    [Theory, ModeData]
    public void ExecSync_StillWorks(ExecutionMode mode)
    {
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo verify"
            : "echo verify";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execSync } from 'child_process';
                const result = execSync('{{echoCommand}}');
                console.log(result.trim() === 'verify');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Exec_ChildProcess_HasKillMethod(ExecutionMode mode)
    {
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo test"
            : "echo test";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { exec } from 'child_process';
                const child = exec('{{echoCommand}}');
                console.log(typeof child.kill === 'function');
                console.log(typeof child.send === 'function');
                console.log(typeof child.disconnect === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Exec_ChildProcess_Properties(ExecutionMode mode)
    {
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo test"
            : "echo test";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { exec } from 'child_process';
                const child = exec('{{echoCommand}}');
                console.log(child.killed === false);
                console.log(child.connected === false);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Stdio_Ignore_NullsStreams(ExecutionMode mode)
    {
        // stdio:'ignore' discards the child's output and sets the stdio streams to null,
        // while the child still runs to completion.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { spawn } from 'child_process';
                const c = spawn('echo hi', [], { shell: true, stdio: 'ignore' });
                console.log('out=' + (c.stdout === null || c.stdout === undefined) + ' err=' + (c.stderr === null || c.stderr === undefined));
                c.on('close', (code: any) => console.log('close:' + code));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("out=true err=true\nclose:0\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Stdio_Array_PipesOnlyStdout(ExecutionMode mode)
    {
        // stdio array: [stdin=ignore, stdout=pipe, stderr=ignore] — only stdout is a stream.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { spawn } from 'child_process';
                const c = spawn('echo arrayform', [], { shell: true, stdio: ['ignore', 'pipe', 'ignore'] });
                let out = '';
                c.stdout.on('data', (d: any) => { out += d.toString(); });
                c.stdout.on('end', () => console.log('out:' + out.trim() + ' stdin=' + (c.stdin === null || c.stdin === undefined)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("out:arrayform stdin=true\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Shell_RunsThroughShell(ExecutionMode mode)
    {
        // `echo shellworks` is a shell builtin / single shell command line — it only resolves
        // with shell:true (there is no executable literally named "echo shellworks").
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { spawn } from 'child_process';
                const c = spawn('echo shellworks', [], { shell: true });
                let out = '';
                c.stdout.on('data', (d: any) => { out += d.toString(); });
                c.stdout.on('end', () => console.log('shell:' + out.trim()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("shell:shellworks\n", output);
    }

    [Theory, ModeData]
    public void Spawn_MissingCommand_EmitsEnoentError(ExecutionMode mode)
    {
        // A missing executable emits an async 'error' event with code 'ENOENT' (+ syscall/path),
        // rather than throwing a generic exception.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { spawn } from 'child_process';
                const c = spawn('this_command_does_not_exist_xyz');
                c.on('error', (err: any) => console.log('code=' + err.code + ' syscall=' + err.syscall + ' path=' + err.path));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("code=ENOENT syscall=spawn this_command_does_not_exist_xyz path=this_command_does_not_exist_xyz\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Kill_SetsKilledAndSignal(ExecutionMode mode)
    {
        // Spawn a long-running process, kill it, and observe live killed/signalCode in 'exit'.
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ping" : "sleep";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['-n', '10', '127.0.0.1']"
            : "['10']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const c = spawn('{{command}}', {{args}});
                c.on('exit', () => console.log('killed=' + c.killed + ' signal=' + c.signalCode));
                c.kill();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("killed=true signal=SIGTERM\n", output);
    }

    [Theory, ModeData]
    public void Exec_ExitCode_IsLiveAfterClose(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { exec } from 'child_process';
                const c = exec('echo hi');
                c.on('close', () => console.log('exitCode=' + c.exitCode));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("exitCode=0\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Stdout_DataAndClose(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'spawned']"
            : "['spawned']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const c = spawn('{{command}}', {{args}});
                let out = '';
                c.stdout.on('data', (d: any) => { out += d.toString(); });
                c.stdout.on('end', () => console.log('end:' + out.trim()));
                c.on('close', (code: any) => console.log('close:' + code));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("end:spawned\nclose:0\n", output);
    }

    [Theory, ModeData]
    public void Spawn_Stdin_RoundTrip(ExecutionMode mode)
    {
        // `sort` reads all of stdin and writes it back; a single line round-trips unchanged.
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "sort" : "sort";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const c = spawn('{{command}}');
                let out = '';
                c.stdout.on('data', (d: any) => { out += d.toString(); });
                c.stdout.on('end', () => console.log('got:' + out.trim()));
                c.stdin.write('hello\n');
                c.stdin.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("got:hello\n", output);
    }

    [Theory, ModeData]
    public void Spawn_HasStdinStream(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'test']"
            : "['test']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const child = spawn('{{command}}', {{args}});
                console.log(child.stdin !== null && child.stdin !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ExecFile_ReturnsChildProcess(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'execfile_test']"
            : "['execfile_test']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execFile } from 'child_process';
                const child = execFile('{{command}}', {{args}});
                console.log(typeof child === 'object');
                console.log(typeof child.on === 'function');
                console.log(typeof child.kill === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Fork_IpcRoundTrip(ExecutionMode mode)
    {
        // Parent forks a child .ts module, exchanges a JSON message over IPC, then disconnects.
        // fork spawns a real `dotnet exec SharpTS.dll <child>` process, so the harness runs this
        // on disk (interp) / via a real subprocess with the SharpTS runtime co-located (compiled).
        var files = new Dictionary<string, string>
        {
            ["child.ts"] = """
                process.on('message', (m: any) => {
                  process.send({ reply: 'got ' + m.hello });
                  process.disconnect();
                });
                """,
            ["main.ts"] = """
                import { fork } from 'child_process';
                const c = fork(__dirname + '/child.ts');
                c.on('message', (m: any) => {
                  console.log('parent got: ' + m.reply);
                  c.disconnect();
                });
                c.send({ hello: 'world' });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("parent got: got world\n", output);
    }

    [Theory, ModeData]
    public void Fork_TypeIsFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { fork } from 'child_process';
                console.log(typeof fork === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ExecFileSync_CapturesOutput(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'sync_output']"
            : "['sync_output']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execFileSync } from 'child_process';
                const result = execFileSync('{{command}}', {{args}});
                console.log(result.trim());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("sync_output", output);
    }

    [Theory, ModeData]
    public void Exec_Callback_FiresWithStdout(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { exec } from 'child_process';
                exec('echo hello', (err: any, stdout: any, stderr: any) => {
                    console.log('cb:' + (err === null) + ':' + stdout.trim());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("cb:true:hello\n", output);
    }

    [Theory, ModeData]
    public void Exec_EmitsCloseThenExit(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { exec } from 'child_process';
                const c = exec('echo hi');
                c.on('close', (code: any) => console.log('close:' + code));
                c.on('exit', (code: any) => console.log('exit:' + code));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("close:0\nexit:0\n", output);
    }

    [Theory, ModeData]
    public void Exec_Encoding_Buffer_ReturnsBuffer(ExecutionMode mode)
    {
        // encoding:'buffer' makes stdout a Buffer instead of a string.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { exec } from 'child_process';
                exec('echo hello', { encoding: 'buffer' }, (err: any, stdout: any) => {
                  console.log('isBuf:' + Buffer.isBuffer(stdout) + ' val:' + stdout.toString().trim());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("isBuf:true val:hello\n", output);
    }

    [Theory, ModeData]
    public void ExecSync_Input_FeedsStdin(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { execSync } from 'child_process';
                const out = execSync('sort', { input: 'banana\napple\n' });
                console.log('execSync:' + out.trim());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("execSync:apple\nbanana\n", output);
    }

    [Theory, ModeData]
    public void ExecFileSync_Input_FeedsStdin(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { execFileSync } from 'child_process';
                const out = execFileSync('sort', [], { input: 'cherry\nberry\n' });
                console.log('execFileSync:' + out.trim());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("execFileSync:berry\ncherry\n", output);
    }

    [Theory, ModeData]
    public void Spawn_WindowsVerbatimArguments_Honored(ExecutionMode mode)
    {
        // windowsVerbatimArguments passes args as a raw command line; verify a normal spawn
        // still produces correct output through that path.
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'vbworks']"
            : "['vbworks']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawn } from 'child_process';
                const c = spawn('{{command}}', {{args}}, { windowsVerbatimArguments: true });
                let out = '';
                c.stdout.on('data', (d: any) => { out += d.toString(); });
                c.stdout.on('end', () => console.log('vb:' + out.trim()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("vb:vbworks\n", output);
    }

    [Theory, ModeData]
    public void Exec_MaxBuffer_Overflow_Errors(ExecutionMode mode)
    {
        // Output longer than maxBuffer kills the child and surfaces ERR_CHILD_PROCESS_STDIO_MAXBUFFER.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { exec } from 'child_process';
                exec('echo aaaaaaaaaaaaaaaa', { maxBuffer: 3 }, (err: any, stdout: any) => {
                  console.log('code:' + (err ? err.code : 'none'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("code:ERR_CHILD_PROCESS_STDIO_MAXBUFFER\n", output);
    }

    [Theory, ModeData]
    public void Exec_NonZeroExit_CallbackReceivesError(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "exit 3"
            : "exit 3";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { exec } from 'child_process';
                exec('{{command}}', (err: any, stdout: any, stderr: any) => {
                    console.log('err:' + (err !== null) + ':code:' + (err ? err.code : 'none'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("err:true:code:3\n", output);
    }

    [Theory, ModeData]
    public void ExecFile_Callback_FiresWithStdout(ExecutionMode mode)
    {
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'filecb']"
            : "['filecb']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execFile } from 'child_process';
                execFile('{{command}}', {{args}}, (err: any, stdout: any, stderr: any) => {
                    console.log('filecb:' + (err === null) + ':' + stdout.trim());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("filecb:true:filecb\n", output);
    }

    [Theory, ModeData]
    public void Exec_ChildProcess_HasPidProperty(ExecutionMode mode)
    {
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo pid_test"
            : "echo pid_test";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { exec } from 'child_process';
                const child = exec('{{echoCommand}}');
                console.log(typeof child.pid === 'number');
                console.log(child.pid !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
