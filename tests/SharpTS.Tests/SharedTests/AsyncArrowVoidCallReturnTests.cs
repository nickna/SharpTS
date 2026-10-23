using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncArrowVoidCallReturnTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["minimal", """
            function nothing(): void {}
            const returned = async () => { return nothing(); };
            const concise = async () => nothing();
            const bare = async () => { return; };
            const empty = async () => {};
            async function observe(): Promise<void> {
                const a: any = await returned(), b: any = await concise();
                const c: any = await bare(), d: any = await empty();
                console.log("returned", typeof a, a === null, a === undefined);
                console.log("concise", typeof b, b === null, b === undefined);
                console.log("bare", typeof c, c === null, c === undefined);
                console.log("empty", typeof d, d === null, d === undefined);
            }
            observe();
            """.ReplaceLineEndings("\n") + "\n", "returned undefined false true\nconcise undefined false true\nbare undefined false true\nempty undefined false true\n"];
        yield return ["annotated-declaration-control", """
            function nothing(): void {}
            async function returned(): Promise<void> { return nothing(); }
            async function bare(): Promise<void> { return; }
            async function empty(): Promise<void> {}
            async function observe(): Promise<void> {
                const a: any = await returned(), b: any = await bare(), c: any = await empty();
                console.log("annotated-returned", typeof a, a === null, a === undefined);
                console.log("annotated-bare", typeof b, b === null, b === undefined);
                console.log("annotated-empty", typeof c, c === null, c === undefined);
            }
            observe();
            """.ReplaceLineEndings("\n") + "\n", "annotated-returned undefined false true\nannotated-bare undefined false true\nannotated-empty undefined false true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void VoidCallResultsRemainUndefined(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }
}
