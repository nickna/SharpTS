using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for console extensions (error, warn, info, debug, time, timeEnd, timeLog, clear).
/// </summary>
public class ConsoleExtensionsTests
{
    [Fact]
    public void Console_Log_OutputsToStdout()
    {
        var source = """
            console.log('Hello');
            console.log('World');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\nWorld\n", output);
    }

    [Fact]
    public void Console_Log_MultipleArguments()
    {
        var source = """
            console.log('Hello', 'World', 123);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello World 123\n", output);
    }

    [Fact]
    public void Console_Info_AliasForLog()
    {
        var source = """
            console.info('Info message');
            console.info('Multiple', 'args', 42);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Info message\nMultiple args 42\n", output);
    }

    [Fact]
    public void Console_Debug_AliasForLog()
    {
        var source = """
            console.debug('Debug message');
            console.debug('Multiple', 'args');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Debug message\nMultiple args\n", output);
    }

    [Fact]
    public void Console_Time_And_TimeEnd_WorkTogether()
    {
        var source = """
            console.time('test');
            let sum = 0;
            for (let i = 0; i < 1000; i++) {
                sum += i;
            }
            console.timeEnd('test');
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        // Should contain timer output with "test:" and "ms"
        Assert.Contains("test:", output);
        Assert.Contains("ms", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Time_DefaultLabel()
    {
        var source = """
            console.time();
            let x = 1;
            console.timeEnd();
            console.log('Finished');
            """;

        var output = TestHarness.RunCompiled(source);
        // Should use "default" as label
        Assert.Contains("default:", output);
        Assert.Contains("ms", output);
        Assert.Contains("Finished\n", output);
    }

    [Fact]
    public void Console_TimeLog_PrintsWithoutStopping()
    {
        var source = """
            console.time('myTimer');
            console.timeLog('myTimer');
            console.timeLog('myTimer');
            console.timeEnd('myTimer');
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        // Should have multiple "myTimer:" outputs
        var timerOccurrences = output.Split("myTimer:").Length - 1;
        Assert.True(timerOccurrences >= 3, $"Expected at least 3 timer outputs, got {timerOccurrences}");
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Error_OutputsMessage()
    {
        // Note: console.error goes to stderr, which is captured separately
        // The test harness captures both stdout and stderr
        var source = """
            console.log('Before error');
            console.error('Error message');
            console.log('After error');
            """;

        var output = TestHarness.RunCompiled(source);
        // stdout should have the log messages
        Assert.Contains("Before error\n", output);
        Assert.Contains("After error\n", output);
    }

    [Fact]
    public void Console_Warn_OutputsMessage()
    {
        var source = """
            console.log('Before warn');
            console.warn('Warning message');
            console.log('After warn');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Before warn\n", output);
        Assert.Contains("After warn\n", output);
    }

    [Fact]
    public void Console_Log_NoArguments_PrintsNewline()
    {
        var source = """
            console.log('Line1');
            console.log();
            console.log('Line2');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Fact]
    public void Console_Info_NoArguments_PrintsNewline()
    {
        var source = """
            console.log('Line1');
            console.info();
            console.log('Line2');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Fact]
    public void Console_Debug_NoArguments_PrintsNewline()
    {
        var source = """
            console.log('Line1');
            console.debug();
            console.log('Line2');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Fact]
    public void Console_Time_MultipleLabeledTimers()
    {
        var source = """
            console.time('timer1');
            console.time('timer2');
            console.timeEnd('timer1');
            console.timeEnd('timer2');
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("timer1:", output);
        Assert.Contains("timer2:", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_TimeEnd_NoMatchingTimer_DoesNotCrash()
    {
        var source = """
            console.timeEnd('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Still running\n", output);
    }

    [Fact]
    public void Console_TimeLog_NoMatchingTimer_DoesNotCrash()
    {
        var source = """
            console.timeLog('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Still running\n", output);
    }
}
