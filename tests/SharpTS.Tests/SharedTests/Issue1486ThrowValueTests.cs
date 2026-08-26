using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>Semantic coverage for the allocation-light guest-throw carrier.</summary>
public class Issue1486ThrowValueTests
{
    [Theory, ModeData]
    public void GuestThrows_PreserveEveryValueKind(ExecutionMode mode)
    {
        var source = """
            class CustomError extends Error { }
            const symbolValue = Symbol("marker");
            const objectValue: any = { marker: true };
            const errorValue = new CustomError("custom");
            const values: any[] = [
                42, "text", true, false, null, undefined,
                symbolValue, objectValue, errorValue
            ];
            function fail(value: any): void { throw value; }

            for (const expected of values) {
                try {
                    throw expected;
                } catch (actual) {
                    console.log(actual === expected);
                }

                try {
                    fail(expected);
                } catch (actual) {
                    console.log(actual === expected);
                }
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(string.Concat(Enumerable.Repeat("true\n", 18)), output);
    }

    [Theory, ModeData]
    public void GuestThrow_CrossesAsyncGeneratorAndPromiseBoundaries(ExecutionMode mode)
    {
        var source = """
            const marker: any = { kind: "marker" };

            async function failAsync(): Promise<void> { throw marker; }
            function* failGenerator(): Generator<number> { yield 1; throw marker; }
            async function* failAsyncGenerator(): AsyncGenerator<number> { yield 1; throw marker; }

            async function main(): Promise<void> {
                try { await failAsync(); }
                catch (error) { console.log(error === marker); }

                const generator = failGenerator();
                generator.next();
                try { generator.next(); }
                catch (error) { console.log(error === marker); }

                const asyncGenerator = failAsyncGenerator();
                await asyncGenerator.next();
                try { await asyncGenerator.next(); }
                catch (error) { console.log(error === marker); }

                try {
                    await Promise.resolve(0).then(() => { throw marker; });
                } catch (error) {
                    console.log(error === marker);
                }
            }
            main();
            """;

        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GuestThrow_RunsNestedFinallyBeforePreservingValue(ExecutionMode mode)
    {
        var source = """
            const marker: any = {};
            try {
                try {
                    try { throw marker; }
                    finally { console.log("inner"); }
                } finally {
                    console.log("outer");
                }
            } catch (error) {
                console.log(error === marker);
            }
            """;

        Assert.Equal("inner\nouter\ntrue\n", TestHarness.Run(source, mode));
    }
}
