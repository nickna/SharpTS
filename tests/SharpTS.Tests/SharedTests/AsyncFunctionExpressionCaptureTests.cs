using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncFunctionExpressionCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["serial-workflow-captured-refs", """
            function make() {
                const task = { current: { async run(work: () => Promise<void>) { await work(); return true; } } };
                const alive = { current: true };
                return {
                    async run(work: () => Promise<void>) {
                        return await task.current.run(async () => {
                            try { await work(); }
                            finally { console.log("alive", alive.current); }
                        });
                    }
                };
            }
            const workflow = make();
            workflow.run(async () => { console.log("work"); }).then(
                result => console.log("result", result),
                error => console.log(String(error))
            );
            """, "work\nalive true\nresult true\n"];
        yield return ["receiver-defaults-and-local-captures-across-await", """
            function make() {
                const state = { current: 7 };
                const owner: any = {
                    tag: "owner",
                    async method(this: any) {
                        const before = state.current;
                        await Promise.resolve(0);
                        return before + ":" + state.current + ":" + this.tag;
                    },
                    expression: async function(this: any, value: any = state.current + ":" + this.tag) {
                        await Promise.resolve(0);
                        return value + "|" + state.current + ":" + this.tag;
                    },
                    makeArrow: function(this: any) {
                        return async () => { await Promise.resolve(0); return state.current + ":" + this.tag; };
                    }
                };
                return owner;
            }
            async function run() {
                const owner = make();
                console.log(await owner.method());
                console.log(await owner["expression"]());
                console.log(await owner.method?.());
                console.log(await owner.expression.call({ tag: "explicit" }));
                console.log(await owner.expression.apply({ tag: "applied" }, []));
                const other: any = { tag: "wrong", read: owner.makeArrow() };
                console.log(await other.read());
            }
            run().then(() => console.log("done"), error => console.log(String(error)));
            """, "7:7:owner\n7:owner|7:owner\n7:7:owner\n7:explicit|7:explicit\n7:applied|7:applied\n7:owner\ndone\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReceiverViewsPreserveLocalCaptureDistances(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            // #2731 requires the production resolver; raw interpretation can mask the extra receiver frame.
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void RawInterpretationPreservesTheSameReceiverAndClosure(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    public const string ReceiverViewsSource = """
            const target: any = async function self(this: any) {
                await Promise.resolve(0);
                return [self === target, (self as any).tag, Object.getPrototypeOf(self) === selectedPrototype, this.tag];
            };
            const selectedPrototype: any = Object.getPrototypeOf(target);
            target.tag = 7;
            const owner: any = { tag: "owner", read: target };
            async function run() {
                const first: any = await owner.read();
                console.log(first[0], first[1], first[2], first[3]);
                delete target.tag;
                const second: any = await target.call({ tag: "explicit" });
                console.log(second[0], second[1] === undefined, second[2], second[3]);
            }
            run().catch(error => console.log(String(error)));
            """;

    public const string ReceiverViewsExpected = "true 7 true owner\ntrue true true explicit\n";

    [Fact]
    public void ReceiverViewsKeepCanonicalNamedSelfAndFunctionState()
    {
        const string source = ReceiverViewsSource;
        const string expected = ReceiverViewsExpected;
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void CompiledReceiverViewsKeepCanonicalNamedSelfAndFunctionState()
    {
        var (errors, output) = TestHarness.CompileVerifyAndRun(ReceiverViewsSource);
        Assert.Empty(errors);
        Assert.Equal(ReceiverViewsExpected, output);
    }
}
