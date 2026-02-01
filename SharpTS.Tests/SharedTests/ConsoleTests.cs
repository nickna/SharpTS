using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for console methods: log, info, debug, error, warn, time/timeEnd/timeLog,
/// assert, count/countReset, table, dir, group/groupEnd, trace, and format specifiers.
/// Note: Tests for stderr methods (error, warn, assert) verify no crash occurs.
/// Stderr content cannot be asserted as TestHarness only captures stdout.
/// </summary>
public class ConsoleTests
{
    #region Basic Output

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_OutputsToStdout(ExecutionMode mode)
    {
        var source = """
            console.log('Hello');
            console.log('World');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nWorld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_MultipleArguments(ExecutionMode mode)
    {
        var source = """
            console.log('Hello', 'World', 123);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World 123\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_NoArguments_PrintsNewline(ExecutionMode mode)
    {
        var source = """
            console.log('Line1');
            console.log();
            console.log('Line2');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Info_AliasForLog(ExecutionMode mode)
    {
        var source = """
            console.info('Info message');
            console.info('Multiple', 'args', 42);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Info message\nMultiple args 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Debug_AliasForLog(ExecutionMode mode)
    {
        var source = """
            console.debug('Debug message');
            console.debug('Multiple', 'args');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Debug message\nMultiple args\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Info_NoArguments_PrintsNewline(ExecutionMode mode)
    {
        var source = """
            console.log('Line1');
            console.info();
            console.log('Line2');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Debug_NoArguments_PrintsNewline(ExecutionMode mode)
    {
        var source = """
            console.log('Line1');
            console.debug();
            console.log('Line2');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    #endregion

    #region Stderr Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Error_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            console.log('Before');
            console.error('Error message');
            console.log('After');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Before\n", output);
        Assert.Contains("After\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Warn_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            console.log('Before');
            console.warn('Warning message');
            console.log('After');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Before\n", output);
        Assert.Contains("After\n", output);
    }

    #endregion

    #region Timing Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Time_And_TimeEnd_WorkTogether(ExecutionMode mode)
    {
        var source = """
            console.time('test');
            let sum = 0;
            for (let i = 0; i < 100; i++) {
                sum += i;
            }
            console.timeEnd('test');
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("test:", output);
        Assert.Contains("ms", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Time_DefaultLabel(ExecutionMode mode)
    {
        var source = """
            console.time();
            let x = 1;
            console.timeEnd();
            console.log('Finished');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("default:", output);
        Assert.Contains("ms", output);
        Assert.Contains("Finished\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Time_CaseSensitiveLabels(ExecutionMode mode)
    {
        var source = """
            console.time('Timer');
            console.time('timer');
            console.timeEnd('Timer');
            console.timeEnd('timer');
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        // Both should be separate timers
        Assert.Contains("Timer:", output);
        Assert.Contains("timer:", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_TimeLog_PrintsWithoutStopping(ExecutionMode mode)
    {
        var source = """
            console.time('myTimer');
            console.timeLog('myTimer');
            console.timeLog('myTimer');
            console.timeEnd('myTimer');
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        // Should have 3 timer outputs (2 timeLog + 1 timeEnd)
        var timerOccurrences = output.Split("myTimer:").Length - 1;
        Assert.True(timerOccurrences >= 3, $"Expected at least 3 timer outputs, got {timerOccurrences}");
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_TimeEnd_NoMatchingTimer_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            console.timeEnd('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Still running\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_TimeLog_NoMatchingTimer_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            console.timeLog('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Still running\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Time_MultipleLabeledTimers(ExecutionMode mode)
    {
        var source = """
            console.time('timer1');
            console.time('timer2');
            console.timeEnd('timer1');
            console.timeEnd('timer2');
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("timer1:", output);
        Assert.Contains("timer2:", output);
        Assert.Contains("Done\n", output);
    }

    #endregion

    #region Assert

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Assert_TruthyCondition_NoOutput(ExecutionMode mode)
    {
        var source = """
            console.assert(true);
            console.assert(1);
            console.assert('hello');
            console.assert({});
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Assert_FalsyCondition_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            console.assert(false, 'this should fail');
            console.log('Still running');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Still running\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Assert_MultipleFalsyConditions(ExecutionMode mode)
    {
        var source = """
            console.assert(false, 'this should fail');
            console.assert(0);
            console.assert('');
            console.assert(null);
            console.log('Still running');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Still running\n", output);
    }

    #endregion

    #region Count

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Count_IncrementsCounter(ExecutionMode mode)
    {
        var source = """
            console.count('calls');
            console.count('calls');
            console.count('calls');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("calls: 1\n", output);
        Assert.Contains("calls: 2\n", output);
        Assert.Contains("calls: 3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Count_DefaultLabel(ExecutionMode mode)
    {
        var source = """
            console.count();
            console.count();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("default: 1\n", output);
        Assert.Contains("default: 2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_CountReset_ResetsCounter(ExecutionMode mode)
    {
        var source = """
            console.count('myCount');
            console.count('myCount');
            console.countReset('myCount');
            console.count('myCount');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("myCount: 1\n", output);
        Assert.Contains("myCount: 2\n", output);
        // After reset, should start from 1 again
        var lines = output.Split('\n').Where(l => l.StartsWith("myCount:")).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal("myCount: 1", lines[2]); // Third occurrence after reset
    }

    #endregion

    #region Table

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Table_ArrayOfObjects(ExecutionMode mode)
    {
        var source = """
            console.table([{a: 1}, {a: 2}]);
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Table_ArrayOfObjects_HasTableBorders(ExecutionMode mode)
    {
        var source = """
            console.table([{a: 1}, {a: 2}]);
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        // Table outputs ASCII borders
        Assert.Contains("+", output);
        Assert.Contains("|", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Table_WithColumnFilter(ExecutionMode mode)
    {
        var source = """
            console.table([{a: 1, b: 2}, {a: 3, b: 4}], ['a']);
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Table_EmptyArray(ExecutionMode mode)
    {
        var source = """
            console.table([]);
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("(empty array)", output);
        Assert.Contains("Done\n", output);
    }

    #endregion

    #region Dir

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Dir_Object(ExecutionMode mode)
    {
        var source = """
            console.dir({name: 'test', value: 42});
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Dir_Object_ShowsProperties(ExecutionMode mode)
    {
        var source = """
            console.dir({name: 'test', value: 42});
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("name", output);
        Assert.Contains("test", output);
        Assert.Contains("Done\n", output);
    }

    #endregion

    #region Group

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Group_IncreasesIndentation(ExecutionMode mode)
    {
        var source = """
            console.log('Outside');
            console.group('MyGroup');
            console.log('Inside');
            console.groupEnd();
            console.log('Outside again');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Outside\n", output);
        Assert.Contains("MyGroup\n", output);
        // Inside should be indented (starts with spaces)
        Assert.Contains("  Inside\n", output);
        Assert.Contains("Outside again\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Group_PrintsLabel(ExecutionMode mode)
    {
        var source = """
            console.group('MyGroup');
            console.log('Inside');
            console.groupEnd();
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("MyGroup\n", output);
        Assert.Contains("Inside\n", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_GroupCollapsed_SameAsGroup(ExecutionMode mode)
    {
        var source = """
            console.groupCollapsed('Collapsed');
            console.log('Inside');
            console.groupEnd();
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Collapsed\n", output);
        Assert.Contains("  Inside\n", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_GroupEnd_DecreasesIndentation(ExecutionMode mode)
    {
        var source = """
            console.group();
            console.log('Level 1');
            console.group();
            console.log('Level 2');
            console.groupEnd();
            console.log('Back to 1');
            console.groupEnd();
            console.log('Back to 0');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("  Level 1\n", output);
        Assert.Contains("    Level 2\n", output);
        Assert.Contains("  Back to 1\n", output);
        Assert.Contains("Back to 0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_GroupEnd_ExtraCallsIgnored(ExecutionMode mode)
    {
        var source = """
            console.groupEnd();
            console.groupEnd();
            console.log('No crash');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("No crash\n", output);
    }

    #endregion

    #region Trace

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Trace_PrintsMessage(ExecutionMode mode)
    {
        var source = """
            console.trace('Test trace');
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Trace: Test trace\n", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Trace_NoMessage(ExecutionMode mode)
    {
        var source = """
            console.trace();
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Trace:", output);
        Assert.Contains("Done\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Trace_MultipleArguments(ExecutionMode mode)
    {
        var source = """
            console.trace('Message', 1, 2, 3);
            console.log('Done');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Trace: Message 1 2 3\n", output);
        Assert.Contains("Done\n", output);
    }

    #endregion

    #region Format Specifiers

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_String(ExecutionMode mode)
    {
        var source = """
            console.log('Hello %s!', 'World');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_Integer(ExecutionMode mode)
    {
        var source = """
            console.log('Value: %d', 42.7);
            console.log('Negative: %i', -3.9);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Value: 42\n", output);
        Assert.Contains("Negative: -3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_Object(ExecutionMode mode)
    {
        var source = """
            console.log('Object: %o', {a: 1, b: 2});
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Object: { a: 1, b: 2 }\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_Float(ExecutionMode mode)
    {
        var source = """
            console.log('Float: %f', 3.14159);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("Float: 3.14159\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_Json(ExecutionMode mode)
    {
        var source = """
            console.log('JSON: %j', {name: 'test', value: 42});
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("JSON: {\"name\":\"test\",\"value\":42}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_MultipleSpecifiers(ExecutionMode mode)
    {
        var source = """
            console.log('Name: %s, Age: %d, Score: %f', 'Alice', 30, 95.5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Name: Alice, Age: 30, Score: 95.5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_EscapedPercent(ExecutionMode mode)
    {
        var source = """
            console.log('100%% complete');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100% complete\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_ExtraArgs(ExecutionMode mode)
    {
        var source = """
            console.log('Value: %s', 'one', 'two', 'three');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Value: one two three\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_MissingArgs(ExecutionMode mode)
    {
        var source = """
            console.log('Hello %s %s', 'World');
            """;

        var output = TestHarness.Run(source, mode);
        // Missing specifiers are output literally
        Assert.Equal("Hello World %s\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_Integer_SpecialValues(ExecutionMode mode)
    {
        var source = """
            console.log('NaN: %d', NaN);
            console.log('Infinity: %d', Infinity);
            console.log('Null: %d', null);
            console.log('Undefined: %d', undefined);
            console.log('String: %d', '42');
            console.log('Bool true: %d', true);
            console.log('Bool false: %d', false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("NaN: NaN\n", output);
        Assert.Contains("Infinity: NaN\n", output);
        Assert.Contains("Null: NaN\n", output);
        Assert.Contains("Undefined: NaN\n", output);
        Assert.Contains("String: 42\n", output);
        Assert.Contains("Bool true: 1\n", output);
        Assert.Contains("Bool false: 0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Console_Log_FormatSpecifier_NoSpecifiers_NoFormatting(ExecutionMode mode)
    {
        // Ensure strings without format specifiers are not treated as format strings
        var source = """
            console.log('Hello', 'World');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World\n", output);
    }

    #endregion
}
