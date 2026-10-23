using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AnnotatedAsyncPromiseReturnTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["module-promise-passthrough", """
            const pending = new Promise<number>(resolve => resolve(7));
            async function passthrough(): Promise<number> { return pending; }
            passthrough().then(value => console.log(value));
            """ + "\r\n", "7\n"];
        yield return ["nested-annotated-promise-passthrough", """
            async function outer() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                async function nested(): Promise<number> { return pending; }
                const task = nested();
                release(7);
                console.log(await task);
            }
            outer();
            """ + "\n", "7\n"];
        yield return ["promise-declaration-inferred", """
            async function outer() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                async function nested() { return pending; }
                const task = nested();
                release(7);
                console.log(await task);
            }
            outer();
            """ + "\n", "7\n"];
        yield return ["primitive-declaration", """
            async function outer() {
                const value = 7;
                async function nested() { return value; }
                console.log(await nested());
            }
            outer();
            """ + "\n", "7\n"];
        yield return ["annotated-async-return-shape-matrix", """
            const pending: Promise<number> = new Promise<number>(resolve => resolve(7));
            async function declaration(): Promise<number> { return pending; }
            const expression = async function (): Promise<number> { return pending; };
            const blockArrow = async (): Promise<number> => { return pending; };
            const conciseArrow = async (): Promise<number> => pending;
            const contextual: () => Promise<number> = async () => pending;
            const holder = { async method(): Promise<number> { return pending; } };
            async function compatibleUnion(value: number | Promise<number>): Promise<number> { return value; }
            async function explicitNested(): Promise<Promise<number>> { return pending; }
            async function main() {
                console.log(await declaration(), await expression(), await blockArrow(),
                    await conciseArrow(), await contextual(), await holder.method(),
                    await compatibleUnion(3), await compatibleUnion(pending), await explicitNested());
            }
            main();
            """ + "\n", "7 7 7 7 7 7 3 7 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncReturnsAdoptCompatiblePromiseFulfillmentTypes(
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
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
