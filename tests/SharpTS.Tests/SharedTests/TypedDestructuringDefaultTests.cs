using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedDestructuringDefaultTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-empty-typed-array", """
            function run([first = 1, next = first + 1]: number[] = []) { console.log(first, next); }
            run();
            """, "1 2\n"];
        yield return ["exact-inferred-object-default", """
            function run(value: number, { next = value } = {}) { console.log(value, next); }
            run(1);
            """, "1 1\n"];
        yield return ["provided-typed-array-control", """
            function run([first = 1, next = first + 1]: number[] = []) { console.log(first, next); }
            run([4, 7]);
            """, "4 7\n"];
        yield return ["provided-inferred-object-control", """
            function run(value: number, { next = value } = {}) { console.log(value, next); }
            run(1, { next: 7 });
            """, "1 7\n"];
        yield return ["undefined-null-and-provided-object-control", """
            function run({ next = 3 }: { next: number | null | undefined }) {
                console.log(next);
            }
            run({ next: undefined });
            run({ next: null });
            run({ next: 7 });
            """, "3\nnull\n7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void MatchedValuesApplyUndefinedOnlyDefaults(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
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
