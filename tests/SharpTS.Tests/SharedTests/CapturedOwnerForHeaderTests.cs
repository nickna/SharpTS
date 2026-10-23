using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CapturedOwnerForHeaderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-private-any", """
            const target: any = function named(): void {
                const identity = named;
                for (let named = 0; named < 1; named++) {}
                const read = (): any => named;
                console.log(typeof read(), read() === identity);
            };
            target();
            """ + "\r\n", "function true\n"];
        yield return ["nonshadowing-positive", """
            const target: any = function named(): void {
                const identity = named;
                for (let other = 0; other < 1; other++) {}
                const read = (): any => named;
                console.log(typeof read(), read() === identity);
            };
            target();
            """ + "\r\n", "function true\n"];
        yield return ["parameter-body-and-iteration-shadow-preservation", """
            const parameter: any = function named(named: any): void {
                const identity = named;
                for (let named = 0; named < 1; named++) {}
                const read = (): any => named;
                console.log(read() === identity, read().value);
            };
            parameter({ value: "parameter" });

            const body: any = function named(): void {
                let named: any = { value: "body" };
                const identity = named;
                for (let named = 0; named < 1; named++) {}
                const read = (): any => named;
                console.log(read() === identity, read().value);
            };
            body();

            const iterations: any = function named(): void {
                const identity = named;
                const reads: any[] = [];
                for (let named = 0; named < 2; named++) {
                    reads.push(() => named);
                }
                const read = (): any => named;
                console.log(reads[0](), reads[1](), typeof read(), read() === identity);
            };
            iterations();
            """ + "\n", "true parameter\ntrue body\n0 1 function true\n"];
        yield return ["mutated-header-cell-handoff-preservation", """
            const reads: any[] = [];
            const target: any = function named(): void {
                const identity = named;
                for (let named = 0; named < 3; named++) {
                    reads.push(() => named);
                    named++;
                }
                const read = () => named;
                console.log(reads[0](), reads[1](), typeof read(), read() === identity);
            };
            target();
            """ + "\n", "1 3 function true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CapturedOwnersAndHeaderBindingsKeepSeparateStorage(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var checkedSource = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(checkedSource.Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
