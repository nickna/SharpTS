using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GeneratorExpressionBodyClosureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-tag-supplied-callback", """
            const value = "outside";
            {
                const tag = "retained";
                const make = function*(read: any) {
                    var value = "body";
                    yield [read, () => value, tag];
                };
                try {
                    const pair: any = make(() => value).next().value;
                    console.log("first", pair[0]());
                    console.log("second", pair[1](), pair[2]);
                } catch (error: any) { console.log("caught", error.name, error.message); }
            }
            """, "first outside\nsecond body retained\n"];
        yield return ["async-tag-supplied-callback", """
            const value = "outside";
            {
                const tag = "retained";
                const make = async function*(read: any) {
                    var value = "body";
                    await Promise.resolve();
                    yield [read, () => value, tag];
                };
                make(() => value).next().then(result => {
                    const pair: any = result.value;
                    console.log("first", pair[0]());
                    console.log("second", pair[1](), pair[2]);
                }).then(undefined, (error: any) => console.log("caught", error.name, error.message));
            }
            """, "first outside\nsecond body retained\n"];
        yield return ["sync-lifted-global-default", """
            const value = "outside";
            {
                const make = function*(read: any = () => value) {
                    var value = "body";
                    yield [read, () => value];
                };
                try {
                    const pair: any = make().next().value;
                    console.log("first", pair[0]());
                    console.log("second", pair[1]());
                } catch (error: any) { console.log("caught", error.name, error.message); }
            }
            """, "first outside\nsecond body\n"];
        yield return ["async-lifted-global-default", """
            const value = "outside";
            {
                const make = async function*(read: any = () => value) {
                    var value = "body";
                    await Promise.resolve();
                    yield [read, () => value];
                };
                make().next().then(result => {
                    const pair: any = result.value;
                    console.log("first", pair[0]());
                    console.log("second", pair[1]());
                }).then(undefined, (error: any) => console.log("caught", error.name, error.message));
            }
            """, "first outside\nsecond body\n"];
        yield return ["sync-tag-multiple-invocation-scope", """
            {
                const tag = "retained";
                const make = function*(value: any) {
                    yield [() => value, tag];
                    value = 2;
                    yield value;
                };
                try {
                    const first = make(1);
                    const second = make(10);
                    const firstPair: any = first.next().value;
                    const secondPair: any = second.next().value;
                    const resumed = first.next();
                    console.log("reads", firstPair[0](), secondPair[0]());
                    console.log("tags", firstPair[1], secondPair[1]);
                } catch (error: any) { console.log("caught", error.name, error.message); }
            }
            """, "reads 2 10\ntags retained retained\n"];
        yield return ["async-tag-multiple-invocation-scope", """
            {
                const tag = "retained";
                const make = async function*(value: any) {
                    yield [() => value, tag];
                    value = 2;
                    yield value;
                };
                const first = make(1);
                const second = make(10);
                const audit = first.next().then(firstResult => {
                    const firstPair: any = firstResult.value;
                    return second.next().then(secondResult => {
                        const secondPair: any = secondResult.value;
                        return first.next().then(resumed => {
                            console.log("reads", firstPair[0](), secondPair[0]());
                            console.log("tags", firstPair[1], secondPair[1]);
                        });
                    });
                }).then(undefined, (error: any) => console.log("caught", error.name, error.message));
            }
            """, "reads 2 10\ntags retained retained\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BodyClosuresRetainInvocationScope(
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
