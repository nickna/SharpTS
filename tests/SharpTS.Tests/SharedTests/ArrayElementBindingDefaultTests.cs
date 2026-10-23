using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayElementBindingDefaultTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-array-element-object-binding-default", """
            function run([{ count = 3 } = {}]: any[] = [undefined]) {
                console.log(count);
            }
            run();
            """.ReplaceLineEndings("\n") + "\n", "3\n"];
        yield return ["nested-array-element-default", """
            let calls = 0;
            function fallback(): any[] { calls++; return [4]; }
            function run([[count] = fallback()]: any[]) {
                var fallback: any = () => [99];
                console.log(count, calls);
            }
            run([undefined]);
            run([[7]]);
            run([[]]);
            try { run([null]); }
            catch (error: any) { console.log(error instanceof TypeError, calls); }
            """.ReplaceLineEndings("\n") + "\n", "4 1\n7 1\nundefined 1\ntrue 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ArrayElementsApplyNestedBindingDefaultsOnce(
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
