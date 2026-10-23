using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorBodyCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["body-capture-suspension-and-shadows", """
            function pause(): Promise<number> {
                return new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
            }

            async function* declaration(parameter: any) {
                const value = "body";
                await pause();
                yield [() => value, () => parameter];

                const afterYield = () => value;
                {
                    const value = "block";
                    await pause();
                    yield () => value;
                }

                const parameterOnly = (value: any) => value;
                const localOnly = () => { const value = "local"; return value; };
                const functionOnly = () => { function value() { return "function"; } return value(); };
                const classOnly = () => { class value { read() { return "class"; } } return new value().read(); };
                yield [afterYield, () => parameter, parameterOnly, localOnly, functionOnly, classOnly];
            }

            function* synchronous(parameter: any) {
                const value = "sync";
                yield [() => value, () => parameter];
                const afterYield = () => value;
                yield afterYield;
            }

            async function run() {
                const iterator = declaration("parameter");
                const first: any = (await iterator.next()).value;
                console.log("declaration", first[0](), first[1]());
                const block: any = (await iterator.next()).value;
                console.log("shadow", block());
                const resumed: any = (await iterator.next()).value;
                console.log("resumed", resumed[0](), resumed[1]());
                console.log("nested", resumed[2]("argument"), resumed[3](), resumed[4](), resumed[5](), first[0]());

                {
                    const tag = "retained";
                    const expression = async function*(parameter: any) {
                        const value = "expression";
                        await pause();
                        yield [() => value, () => parameter, tag];
                    };
                    const pair: any = (await expression("parameter").next()).value;
                    console.log("expression", pair[0](), pair[1](), pair[2]);
                }

                const sync = synchronous("parameter");
                const pair: any = sync.next().value;
                const afterYield: any = sync.next().value;
                console.log("synchronous", pair[0](), pair[1](), afterYield());
            }

            const task = run();
            const observed = task.catch((error: any) => console.log("unexpected", error.name, error.message));
            """ + "\n", "declaration body parameter\nshadow block\nresumed body parameter\nnested argument local function class body\nexpression expression parameter retained\nsynchronous sync parameter sync\n"];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        var original = GeneratorDefaultGlobalCaptureTests.Cases().Single(row =>
            (string)row[0] == "async-generator-declaration-and-expression-global-captures");
        yield return ["public-generator-body-capture", original[1], original[2]];
        foreach (var row in Cases()) yield return row;
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BodyClosuresPreserveOwnerBindingsAfterSuspension(
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
