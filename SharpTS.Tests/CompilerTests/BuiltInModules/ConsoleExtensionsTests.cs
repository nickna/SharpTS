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

    // ===================== Phase 2: Assert =====================

    [Fact]
    public void Console_Assert_TruthyCondition_NoOutput()
    {
        var source = """
            console.assert(true);
            console.assert(1);
            console.assert('hello');
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Done\n", output);
    }

    [Fact]
    public void Console_Assert_FalsyCondition_DoesNotCrash()
    {
        var source = """
            console.assert(false, 'this should fail');
            console.log('Still running');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Still running\n", output);
    }

    // ===================== Phase 2: Count =====================

    [Fact]
    public void Console_Count_IncrementsCounter()
    {
        var source = """
            console.count('calls');
            console.count('calls');
            console.count('calls');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("calls: 1\n", output);
        Assert.Contains("calls: 2\n", output);
        Assert.Contains("calls: 3\n", output);
    }

    [Fact]
    public void Console_Count_DefaultLabel()
    {
        var source = """
            console.count();
            console.count();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("default: 1\n", output);
        Assert.Contains("default: 2\n", output);
    }

    [Fact]
    public void Console_CountReset_ResetsCounter()
    {
        var source = """
            console.count('myCount');
            console.count('myCount');
            console.countReset('myCount');
            console.count('myCount');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("myCount: 1\n", output);
        Assert.Contains("myCount: 2\n", output);
        // After reset, should start from 1 again
        var lines = output.Split('\n').Where(l => l.StartsWith("myCount:")).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal("myCount: 1", lines[2]);
    }

    // ===================== Phase 2: Table =====================

    [Fact]
    public void Console_Table_ArrayOfObjects()
    {
        var source = """
            console.table([{a: 1}, {a: 2}]);
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Done\n", output);
    }

    // ===================== Phase 2: Dir =====================

    [Fact]
    public void Console_Dir_Object()
    {
        var source = """
            console.dir({name: 'test', value: 42});
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Done\n", output);
    }

    // ===================== Phase 2: Group =====================

    [Fact]
    public void Console_Group_PrintsLabel()
    {
        var source = """
            console.group('MyGroup');
            console.log('Inside');
            console.groupEnd();
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("MyGroup\n", output);
        Assert.Contains("Inside\n", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_GroupEnd_DoesNotCrash()
    {
        var source = """
            console.groupEnd();
            console.groupEnd();
            console.log('No crash');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("No crash\n", output);
    }

    // ===================== Phase 2: Trace =====================

    [Fact]
    public void Console_Trace_PrintsMessage()
    {
        var source = """
            console.trace('Test trace');
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Trace: Test trace\n", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Trace_NoMessage()
    {
        var source = """
            console.trace();
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Trace:", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Trace_MultipleArguments()
    {
        var source = """
            console.trace('Message', 1, 2, 3);
            console.log('Done');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Trace: Message 1 2 3\n", output);
        Assert.Contains("Done\n", output);
    }

    // ===================== Format Specifiers =====================

    [Fact]
    public void Console_Log_FormatSpecifier_String()
    {
        var source = """
            console.log('Hello %s!', 'World');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello World!\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Integer()
    {
        var source = """
            console.log('Value: %d', 42.7);
            console.log('Negative: %i', -3.9);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Value: 42\n", output);
        Assert.Contains("Negative: -3\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Object()
    {
        var source = """
            console.log('Object: %o', {a: 1});
            """;

        var output = TestHarness.RunCompiled(source);
        // Compiler uses different object representation
        Assert.Contains("Object:", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Float()
    {
        var source = """
            console.log('Float: %f', 3.14159);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("Float: 3.14159\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_MultipleSpecifiers()
    {
        var source = """
            console.log('Name: %s, Age: %d, Score: %f', 'Alice', 30, 95.5);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Name: Alice, Age: 30, Score: 95.5\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_EscapedPercent()
    {
        var source = """
            console.log('100%% complete');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100% complete\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_ExtraArgs()
    {
        var source = """
            console.log('Value: %s', 'one', 'two', 'three');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Value: one two three\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_MissingArgs()
    {
        var source = """
            console.log('Hello %s %s', 'World');
            """;

        var output = TestHarness.RunCompiled(source);
        // Missing specifiers are output literally
        Assert.Equal("Hello World %s\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_NoSpecifiers_NoFormatting()
    {
        // Ensure strings without format specifiers are not treated as format strings
        var source = """
            console.log('Hello', 'World');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello World\n", output);
    }
}
