using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PrimitiveAwaitSchedulingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-stored-primitive-await", """
            async function run() {
                console.log("before");
                await 0;
                console.log("after");
            }
            const pending = run();
            console.log("tail");
            """, "before\ntail\nafter\n"];
        yield return ["primitive-forms-and-repeated-await-fifo", """
            async function declared() {
                console.log("declaration:start");
                const zero = await 0;
                console.log("zero", zero);
                const missing = await undefined;
                console.log("undefined", missing === undefined);
                const text = await "text";
                console.log("text", text);
            }
            const arrow = async () => {
                console.log("arrow:start");
                const empty = await null;
                console.log("null", empty === null);
                const flag = await false;
                console.log("false", flag);
            };
            const expression = async function () {
                console.log("expression:start");
                const number = await 3;
                console.log("number", number);
                const empty = await "";
                console.log("empty", empty.length);
            };
            const declaredPending = declared();
            const arrowPending = arrow();
            const expressionPending = expression();
            queueMicrotask(() => console.log("queued"));
            console.log("tail");
            """, "declaration:start\narrow:start\nexpression:start\ntail\nzero 0\nnull true\nnumber 3\nqueued\nundefined true\nfalse false\nempty 0\ntext text\n"];
        yield return ["lexical-receiver-capture-and-finally", """
            class AsyncReceiver {
                tag: string = "receiver";
                run() {
                    const before = this.tag;
                    let local = 5;
                    const operation = async () => {
                        try {
                            console.log("start", this.tag, local);
                            await 0;
                            local++;
                            console.log("body", this.tag, before, local);
                        } finally {
                            console.log("finally", this.tag, local);
                        }
                        console.log("done", this.tag, local);
                    };
                    return operation();
                }
            }
            const worker = new AsyncReceiver();
            const pending = worker.run();
            worker.tag = "changed";
            console.log("tail", worker.tag);
            """, "start receiver 5\ntail changed\nbody changed receiver 6\nfinally changed 6\ndone changed 6\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ResolvedInterpreterResumesPrimitiveAwaitInPromiseOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void RawInterpreterResumesPrimitiveAwaitInPromiseOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    // New compiler/saved profiles use the exact qualified LF/finalLF programs;
    // existing interpreted providers and their physical source payloads stay unchanged.
    public static IEnumerable<object[]> CompiledCases() => Cases().Select(row =>
        new object[] { row[0], ((string)row[1]).ReplaceLineEndings("\n") + "\n", row[2] });

    [Theory, MemberData(nameof(CompiledCases))]
    public void CompiledPrimitiveAwaitResumesInPromiseOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
