using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SettledNativeAwaitSchedulingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["settled-native-promise-await-control", """
            async function run() {
                console.log("before");
                const value = await Promise.resolve(7);
                console.log("after", value);
            }
            const pending = run();
            queueMicrotask(() => console.log("queued"));
            console.log("tail");
            """ + "\n", "before\ntail\nafter 7\nqueued\n"];
        yield return ["settled-await-forms-and-preservation-v3", """
            class Receiver {
                tag: string = "before";
                start() {
                    const before = this.tag;
                    let local = 4;
                    const operation = async () => {
                        console.log("arrow-before", this.tag, local);
                        const value = await Promise.resolve(7);
                        local++;
                        console.log("arrow-after", value, this.tag, before, local);
                    };
                    return operation();
                }
            }

            async function leaf(): Promise<number> {
                return 9;
            }

            async function callOperation() {
                console.log("call-before");
                const value = await leaf();
                console.log("call-after", value);
            }

            async function* readGenerator() {
                console.log("generator-before");
                const value = await Promise.resolve(8);
                console.log("generator-after", value);
                yield value;
            }

            async function main() {
                const receiver = new Receiver();
                const arrow = receiver.start();
                receiver.tag = "changed";
                console.log("arrow-tail", receiver.tag);
                await arrow;

                const call = callOperation();
                queueMicrotask(() => console.log("call-queued"));
                console.log("call-tail");
                await call;

                const iterator = readGenerator();
                const first = iterator.next();
                console.log("generator-tail");
                const yielded = await first;
                console.log("generator-result", yielded.value, yielded.done);
                const closed = await iterator.next();
                console.log("generator-closed", closed.value === undefined, closed.done);

                const marker = {};
                const rejected: Promise<number> = Promise.reject(marker);
                const rejectOperation = async () => {
                    console.log("rejection-before");
                    try { await rejected; }
                    catch (error) { console.log("rejection-catch", error === marker); }
                };
                const caught = rejectOperation();
                console.log("rejection-tail");
                await caught;

                let primitiveLocal = 4;
                const primitiveOperation = async () => {
                    const value = await 0;
                    primitiveLocal++;
                    console.log("primitive", value, primitiveLocal);
                };
                const primitive = primitiveOperation();
                await primitive;

                let release: any;
                const pending: Promise<number> = new Promise<number>(resolve => { release = resolve; });
                let pendingLocal = 4;
                const pendingOperation = async () => {
                    try {
                        console.log("pending-before", pendingLocal);
                        const value = await pending;
                        pendingLocal++;
                        console.log("pending-after", value, pendingLocal);
                    } finally {
                        console.log("pending-finally", pendingLocal);
                    }
                };
                const waiting = pendingOperation();
                console.log("pending-tail", pendingLocal);
                release(9);
                await waiting;
                console.log("pending-done", pendingLocal);
            }

            const task = main().catch((error: any) => { console.log("unexpected", error.name); });
            """ + "\n", "arrow-before before 4\narrow-tail changed\narrow-after 7 changed before 5\ncall-before\ncall-tail\ncall-after 9\ncall-queued\ngenerator-before\ngenerator-tail\ngenerator-after 8\ngenerator-result 8 false\ngenerator-closed true true\nrejection-before\nrejection-tail\nrejection-catch true\nprimitive 0 5\npending-before 4\npending-tail 4\npending-after 9 5\npending-finally 5\npending-done 5\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void FulfilledNativePromiseAwaitResumesInPromiseJobOrder(
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
