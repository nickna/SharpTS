using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncArrowForAwaitCleanupTests
{
    public static IEnumerable<object[]> Cases()
    {
        // Keep the complete cleanup control shared with the interpreter prerequisite.
        var row = InterpretedAsyncIteratorCloseTests.Cases()
            .Single(row => (string)row[0] == "complete-public-for-await-cleanup");
        yield return ["for-await-arrow-cleanup-and-control-v2", row[1], row[2]];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        // Exact whole source of the existing AsyncArrow_ForAwaitBreakRunsCleanup test.
        yield return ["for-await-break-cleanup", """
            async function* g() { yield 10; yield 20; yield 30; }
            const run = async () => {
                for await (const v of g()) {
                    console.log("v=" + v);
                    if (v === 20) break;
                }
                console.log("done");
            };
            run();
            """, "v=10\nv=20\ndone\n"];
        foreach (var row in Cases())
            yield return row;
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ForAwaitCleanupSuspendsAndPreservesArrowControlFlow(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
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
