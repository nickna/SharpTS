using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

[Collection("TimerTests")]
public sealed class NextTickTimerSchedulingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-nexttick-before-earlier-timer", """
            setTimeout(() => console.log("timer"), 0);
            process.nextTick(() => console.log("tick"));
            console.log("sync");
            """, "sync\ntick\ntimer\n"];
        yield return ["global-fifo-arguments-and-nested-ticks", """
            setTimeout(() => console.log("timer:first"), 0);
            setTimeout(() => console.log("timer:second"), 0);
            const marker = {};
            process.nextTick((value: number, text: string, flag: boolean, received: any) => {
                console.log("first", value, text, flag, received === marker);
                process.nextTick(() => console.log("nested"));
            }, 7, "text", false, marker);
            process.nextTick(() => console.log("second"));
            console.log("sync");
            """, "sync\nfirst 7 text false true\nsecond\nnested\ntimer:first\ntimer:second\n"];
        yield return ["process-module-fifo-and-ten-arguments", """
            import { nextTick } from "process";
            setTimeout(() => console.log("timer"), 0);
            nextTick((...received: any[]) => {
                console.log("arguments", received.length, received.join(","));
            }, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
            nextTick(() => console.log("second"));
            console.log("sync");
            """, "sync\narguments 10 1,2,3,4,5,6,7,8,9,10\nsecond\ntimer\n"];
        yield return ["timer-callback-nexttick-before-following-timer", """
            setTimeout(() => {
                console.log("timer:first");
                process.nextTick(() => console.log("tick:from-timer"));
                console.log("timer:first:end");
            }, 0);
            setTimeout(() => console.log("timer:second"), 0);
            console.log("sync");
            """, "sync\ntimer:first\ntimer:first:end\ntick:from-timer\ntimer:second\n"];
        yield return ["held-nexttick-arguments-before-earlier-timer", """
            const tick = process.nextTick;
            const marker = {};
            setTimeout(() => console.log("timer"), 0);
            tick((received: any, value: number) => {
                console.log("tick", received === marker, value);
            }, marker, 29);
            console.log("sync");
            """, "sync\ntick true 29\ntimer\n"];
    }

    // Imported modules require module compilation. The full source receives compiled
    // coverage in Isolated_Issue2743NextTickTimerScheduling_VerifyBeforeExecution,
    // which verifies saved IL and checks standalone references before execution.
    public static IEnumerable<object[]> CompiledCases() => Cases()
        .Where(row => (string)row[0] != "process-module-fifo-and-ten-arguments");

    public static IEnumerable<object[]> ModeCases() => Cases()
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(CompiledCases()
            .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NextTickCallbacksDrainBeforeTimers(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else if (name == "process-module-fifo-and-ten-arguments")
        {
            Assert.Equal(expected, TestHarness.RunModules(
                new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
