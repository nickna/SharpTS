using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for console methods in the interpreter.
/// Note: Tests for stderr methods (error, warn, assert) verify no crash occurs.
/// Stderr content cannot be asserted as TestHarness.RunInterpreted only captures stdout.
/// Timer/count tests use unique labels to avoid state conflicts in parallel test runs.
/// </summary>
public class ConsoleTests
{

    // ===================== Phase 1: Basic Output =====================

    [Fact]
    public void Console_Log_OutputsToStdout()
    {
        var source = """
            console.log('Hello');
            console.log('World');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello\nWorld\n", output);
    }

    [Fact]
    public void Console_Log_MultipleArguments()
    {
        var source = """
            console.log('Hello', 'World', 123);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello World 123\n", output);
    }

    [Fact]
    public void Console_Log_NoArguments_PrintsNewline()
    {
        var source = """
            console.log('Line1');
            console.log();
            console.log('Line2');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Line1\n\nLine2\n", output);
    }

    [Fact]
    public void Console_Info_AliasForLog()
    {
        var source = """
            console.info('Info message');
            console.info('Multiple', 'args', 42);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Info message\nMultiple args 42\n", output);
    }

    [Fact]
    public void Console_Debug_AliasForLog()
    {
        var source = """
            console.debug('Debug message');
            console.debug('Multiple', 'args');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Debug message\nMultiple args\n", output);
    }

    // ===================== Phase 1: Stderr Methods =====================
    // Note: These verify no crash; stderr content cannot be asserted

    [Fact]
    public void Console_Error_DoesNotCrash()
    {
        var source = """
            console.log('Before');
            console.error('Error message');
            console.log('After');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Before\n", output);
        Assert.Contains("After\n", output);
    }

    [Fact]
    public void Console_Warn_DoesNotCrash()
    {
        var source = """
            console.log('Before');
            console.warn('Warning message');
            console.log('After');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Before\n", output);
        Assert.Contains("After\n", output);
    }

    // ===================== Phase 1: Timing Methods =====================

    [Fact]
    public void Console_Time_And_TimeEnd_WorkTogether()
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("default:", output);
        Assert.Contains("ms", output);
        Assert.Contains("Finished\n", output);
    }

    [Fact]
    public void Console_Time_CaseSensitiveLabels()
    {
        var source = """
            console.time('Timer');
            console.time('timer');
            console.timeEnd('Timer');
            console.timeEnd('timer');
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        // Both should be separate timers
        Assert.Contains("Timer:", output);
        Assert.Contains("timer:", output);
        Assert.Contains("Done\n", output);
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

        var output = TestHarness.RunInterpreted(source);
        // Should have 3 timer outputs (2 timeLog + 1 timeEnd)
        var timerOccurrences = output.Split("myTimer:").Length - 1;
        Assert.True(timerOccurrences >= 3, $"Expected at least 3 timer outputs, got {timerOccurrences}");
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_TimeEnd_NoMatchingTimer_DoesNotCrash()
    {
        var source = """
            console.timeEnd('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Still running\n", output);
    }

    [Fact]
    public void Console_TimeLog_NoMatchingTimer_DoesNotCrash()
    {
        var source = """
            console.timeLog('nonexistent');
            console.log('Still running');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Still running\n", output);
    }

    // ===================== Phase 2: Assert =====================
    // Note: Assert writes to stderr; we verify no crash

    [Fact]
    public void Console_Assert_TruthyCondition_NoOutput()
    {
        var source = """
            console.assert(true);
            console.assert(1);
            console.assert('hello');
            console.assert({});
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Done\n", output);
    }

    [Fact]
    public void Console_Assert_FalsyCondition_DoesNotCrash()
    {
        var source = """
            console.assert(false, 'this should fail');
            console.assert(0);
            console.assert('');
            console.assert(null);
            console.log('Still running');
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("myCount: 1\n", output);
        Assert.Contains("myCount: 2\n", output);
        // After reset, should start from 1 again
        var lines = output.Split('\n').Where(l => l.StartsWith("myCount:")).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal("myCount: 1", lines[2]); // Third occurrence after reset
    }

    // ===================== Phase 2: Table =====================

    [Fact]
    public void Console_Table_ArrayOfObjects()
    {
        var source = """
            console.table([{a: 1}, {a: 2}]);
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        // Table outputs ASCII borders
        Assert.Contains("+", output);
        Assert.Contains("|", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Table_WithColumnFilter()
    {
        var source = """
            console.table([{a: 1, b: 2}, {a: 3, b: 4}], ['a']);
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Table_EmptyArray()
    {
        var source = """
            console.table([]);
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("(empty array)", output);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("name", output);
        Assert.Contains("test", output);
        Assert.Contains("Done\n", output);
    }

    // ===================== Phase 2: Group =====================

    [Fact]
    public void Console_Group_IncreasesIndentation()
    {
        var source = """
            console.log('Outside');
            console.group('MyGroup');
            console.log('Inside');
            console.groupEnd();
            console.log('Outside again');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Outside\n", output);
        Assert.Contains("MyGroup\n", output);
        // Inside should be indented (starts with spaces)
        Assert.Contains("  Inside\n", output);
        Assert.Contains("Outside again\n", output);
    }

    [Fact]
    public void Console_GroupCollapsed_SameAsGroup()
    {
        var source = """
            console.groupCollapsed('Collapsed');
            console.log('Inside');
            console.groupEnd();
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Collapsed\n", output);
        Assert.Contains("  Inside\n", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_GroupEnd_DecreasesIndentation()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("  Level 1\n", output);
        Assert.Contains("    Level 2\n", output);
        Assert.Contains("  Back to 1\n", output);
        Assert.Contains("Back to 0\n", output);
    }

    [Fact]
    public void Console_GroupEnd_ExtraCallsIgnored()
    {
        var source = """
            console.groupEnd();
            console.groupEnd();
            console.log('No crash');
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Trace: \n", output);
        Assert.Contains("Done\n", output);
    }

    [Fact]
    public void Console_Trace_MultipleArguments()
    {
        var source = """
            console.trace('Message', 1, 2, 3);
            console.log('Done');
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello World!\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Integer()
    {
        var source = """
            console.log('Value: %d', 42.7);
            console.log('Negative: %i', -3.9);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Value: 42\n", output);
        Assert.Contains("Negative: -3\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Object()
    {
        var source = """
            console.log('Object: %o', {a: 1, b: 2});
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Object: { a: 1, b: 2 }\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Float()
    {
        var source = """
            console.log('Float: %f', 3.14159);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("Float: 3.14159\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Json()
    {
        var source = """
            console.log('JSON: %j', {name: 'test', value: 42});
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("JSON: {\"name\":\"test\",\"value\":42}\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_MultipleSpecifiers()
    {
        var source = """
            console.log('Name: %s, Age: %d, Score: %f', 'Alice', 30, 95.5);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Name: Alice, Age: 30, Score: 95.5\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_EscapedPercent()
    {
        var source = """
            console.log('100%% complete');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100% complete\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_ExtraArgs()
    {
        var source = """
            console.log('Value: %s', 'one', 'two', 'three');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Value: one two three\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_MissingArgs()
    {
        var source = """
            console.log('Hello %s %s', 'World');
            """;

        var output = TestHarness.RunInterpreted(source);
        // Missing specifiers are output literally
        Assert.Equal("Hello World %s\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_Integer_SpecialValues()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("NaN: NaN\n", output);
        Assert.Contains("Infinity: NaN\n", output);
        Assert.Contains("Null: NaN\n", output);
        Assert.Contains("Undefined: NaN\n", output);
        Assert.Contains("String: 42\n", output);
        Assert.Contains("Bool true: 1\n", output);
        Assert.Contains("Bool false: 0\n", output);
    }

    [Fact]
    public void Console_Log_FormatSpecifier_NoSpecifiers_NoFormatting()
    {
        // Ensure strings without format specifiers are not treated as format strings
        var source = """
            console.log('Hello', 'World');
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello World\n", output);
    }
}
