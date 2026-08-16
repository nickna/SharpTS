using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for process.stdin, process.stdout, and process.stderr as proper stream objects.
/// Validates EventEmitter integration, Writable/Readable stream methods, and property access.
/// </summary>
public class ProcessStdioTests
{
    #region process.stdout

    [Theory, ModeData]
    public void ProcessStdout_Write_OutputsData(ExecutionMode mode)
    {
        var source = """
            process.stdout.write("hello");
            process.stdout.write(" world\n");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_Write_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            const result = process.stdout.write("test");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("testtrue\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_IsObject(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdout);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_HasWritableProperties(ExecutionMode mode)
    {
        var source = """
            console.log(process.stdout.writable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_HasOnMethod(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdout.on);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_OnDrain_RegistersListener(ExecutionMode mode)
    {
        var source = """
            let drainCalled = false;
            process.stdout.on('drain', () => {
                drainCalled = true;
            });
            console.log(typeof process.stdout.on);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_IsTTY_IsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdout.isTTY);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("boolean\n", output);
    }

    #endregion

    #region process.stderr

    [Theory, ModeData]
    public void ProcessStderr_IsObject(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stderr);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void ProcessStderr_IsTTY_IsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stderr.isTTY);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("boolean\n", output);
    }

    [Theory, ModeData]
    public void ProcessStderr_HasWritableProperties(ExecutionMode mode)
    {
        var source = """
            console.log(process.stderr.writable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ProcessStderr_HasOnMethod(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stderr.on);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    #endregion

    #region process.stdin

    [Theory, ModeData]
    public void ProcessStdin_IsObject(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_IsTTY_IsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.isTTY);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("boolean\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_HasReadableProperties(ExecutionMode mode)
    {
        var source = """
            console.log(process.stdin.readable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_HasOnMethod(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.on);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_HasPauseResume(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.pause);
            console.log(typeof process.stdin.resume);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_HasSetEncoding(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.setEncoding);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    #endregion

    #region Stream method chaining

    [Theory, ModeData]
    public void ProcessStdin_SetEncoding_ReturnsThis(ExecutionMode mode)
    {
        var source = """
            const result = process.stdin.setEncoding('utf8');
            console.log(result === process.stdin);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Module import patterns

    [Theory, ModeData]
    public void ProcessModule_StdoutWrite(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import process from 'process';
                process.stdout.write("module test\n");
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("module test\n", output);
    }

    [Theory, ModeData]
    public void ProcessModule_StdinType(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import process from 'process';
                console.log(typeof process.stdin);
                console.log(typeof process.stdout);
                console.log(typeof process.stderr);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\nobject\nobject\n", output);
    }

    #endregion

    #region EventEmitter integration

    [Theory, ModeData]
    public void ProcessStdout_EventEmitter_Methods(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdout.on);
            console.log(typeof process.stdout.once);
            console.log(typeof process.stdout.off);
            console.log(typeof process.stdout.emit);
            console.log(typeof process.stdout.removeListener);
            console.log(typeof process.stdout.removeAllListeners);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_EventEmitter_Methods(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.on);
            console.log(typeof process.stdin.once);
            console.log(typeof process.stdin.off);
            console.log(typeof process.stdin.emit);
            console.log(typeof process.stdin.removeListener);
            console.log(typeof process.stdin.removeAllListeners);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    #endregion

    #region Writable stream interface

    [Theory, ModeData]
    public void ProcessStdout_WritableInterface(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdout.write);
            console.log(typeof process.stdout.end);
            console.log(typeof process.stdout.cork);
            console.log(typeof process.stdout.uncork);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdout_WritableStateProperties(ExecutionMode mode)
    {
        var source = """
            console.log(process.stdout.writable);
            console.log(process.stdout.writableEnded);
            console.log(process.stdout.writableFinished);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    #endregion

    #region Readable stream interface

    [Theory, ModeData]
    public void ProcessStdin_ReadableInterface(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.stdin.read);
            console.log(typeof process.stdin.pause);
            console.log(typeof process.stdin.resume);
            console.log(typeof process.stdin.pipe);
            console.log(typeof process.stdin.unpipe);
            console.log(typeof process.stdin.setEncoding);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void ProcessStdin_ReadableStateProperties(ExecutionMode mode)
    {
        var source = """
            console.log(process.stdin.readable);
            console.log(process.stdin.readableEnded);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion
}
