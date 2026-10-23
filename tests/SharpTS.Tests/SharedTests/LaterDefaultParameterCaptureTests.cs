using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class LaterDefaultParameterCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-later-default-calls-captured-parameter", """
            function run(value: number = 1, read: any = () => value, next: any = read()) {
                console.log(value, next, read());
            }
            run();
            """, "1 1 1\n"];
        yield return ["explicit-earlier-parameter-later-default", """
            function run(value: number = 1, read: any = () => value, next: any = read()) {
                console.log(value, next, read());
            }
            run(7);
            """, "7 7 7\n"];
        yield return ["two-earlier-parameter-captures", """
            function run(value: number = 1, other: number = 2, read: any = () => value + other, next: any = read()) {
                console.log(value, other, next, read());
            }
            run();
            """, "1 2 3 3\n"];
        yield return ["body-mutation-keeps-captured-parameter-live", """
            function run(value: number = 1, read: any = () => value, next: any = read()) {
                value = 3;
                console.log(value, next, read());
            }
            run();
            """, "3 1 3\n"];
        yield return ["default-invocation-mutates-earlier-parameter", """
            function run(value: number = 1, change: any = () => ++value, next: any = change()) {
                console.log(value, next, change(), value);
            }
            run();
            """, "2 2 3 3\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LaterDefaultsObserveInitializedCapturedParameters(
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
