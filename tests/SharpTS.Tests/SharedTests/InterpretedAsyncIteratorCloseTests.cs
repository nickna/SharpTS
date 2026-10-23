using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class InterpretedAsyncIteratorCloseTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["complete-public-for-await-cleanup", """
            async function* tracked(label: string, events: string[]) {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    await Promise.resolve(0);
                    events.push(label + "-closed");
                }
            }

            async function* pendingCleanup(events: string[]) {
                try {
                    yield 10;
                    yield 20;
                    yield 30;
                } finally {
                    events.push("cleanup-start");
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    events.push("cleanup-end");
                }
            }

            async function* rejectingCleanup(events: string[], token: any) {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    events.push("cleanup-start");
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    events.push("cleanup-end");
                    throw token;
                }
            }

            async function ordinary() {
                const events: string[] = [];
                let values = "";
                for await (const value of tracked("ordinary", events)) {
                    const before = value;
                    await Promise.resolve(0);
                    values += before + ":" + value + ";";
                    break;
                }
                console.log("ordinary", values, events.join(","));
            }

            const arrow = async () => {
                const events: string[] = [];
                let total = 0;
                for await (const value of pendingCleanup(events)) {
                    const before = value;
                    await Promise.resolve(0);
                    total += value;
                    events.push("body" + value + ":" + before);
                    if (value === 20) break;
                }
                console.log("arrow", total, events.join(","));
            };

            const rejectingArrow = async () => {
                const token: any = { tag: 8 };
                const events: string[] = [];
                let caught = false;
                try {
                    for await (const value of rejectingCleanup(events, token)) {
                        events.push("body" + value);
                        break;
                    }
                } catch (error: any) {
                    await Promise.resolve(0);
                    caught = error === token;
                    events.push("caught");
                }
                console.log("reject", caught, events.join(","));
            };

            const labeledArrow = async () => {
                const events: string[] = [];
                outer: for await (const outerValue of tracked("outer", events)) {
                    inner: for await (const innerValue of tracked("inner" + outerValue, events)) {
                        await Promise.resolve(0);
                        events.push(outerValue + ":" + innerValue);
                        if (innerValue === 1) continue inner;
                        break inner;
                    }
                    if (outerValue === 1) continue outer;
                    break outer;
                }
                console.log("labels", events.join(","));
            };

            async function main() {
                await ordinary();
                await arrow();
                await rejectingArrow();
                await labeledArrow();
            }

            const task = main().catch((error: any) => console.log("unexpected", error.name));
            """ + "\n", "ordinary 1:1; ordinary-closed\narrow 30 body10:10,body20:20,cleanup-start,cleanup-end\nreject true body1,cleanup-start,cleanup-end,caught\nlabels 1:1,1:2,inner1-closed,2:1,2:2,inner2-closed,outer-closed\n"];
        yield return ["async-iterator-close-completions", """
            function makeIterator(kind: string, cleanup: any, state: any): any {
                const iterator: any = {
                    [Symbol.asyncIterator]() {
                        return this;
                    },
                    next() {
                        return Promise.resolve({ value: 1, done: false });
                    }
                };
                if (kind !== "missing") {
                    Object.defineProperty(iterator, "return", {
                        get() {
                            state.gets++;
                            if (kind === "null") return null;
                            if (kind === "undefined") return undefined;
                            if (kind === "noncallable") return 7;
                            if (kind === "getter") throw cleanup;
                            return function(this: any) {
                                state.calls++;
                                state.receiver = this === iterator;
                                state.argCount = arguments.length;
                                if (kind === "call") throw cleanup;
                                if (kind === "reject") {
                                    return new Promise<any>((resolve, reject) => {
                                        setTimeout(() => reject(cleanup), 1);
                                    });
                                }
                                if (kind === "primitive") return Promise.resolve(1);
                                return Promise.resolve({ value: 9, done: false });
                            };
                        }
                    });
                }
                return iterator;
            }

            async function take(iterator: any, mode: string, original: any) {
                for await (const value of iterator) {
                    if (mode === "break") break;
                    if (mode === "return") return 5;
                    throw original;
                }
                return 7;
            }

            async function* nativeCleanup(events: string[], cleanup: any) {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    events.push("start");
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    events.push("end");
                    throw cleanup;
                }
            }

            async function nativeReturn(events: string[], cleanup: any) {
                for await (const value of nativeCleanup(events, cleanup)) {
                    return value;
                }
                return 0;
            }

            async function main() {
                const kinds = ["missing", "null", "undefined", "noncallable", "getter", "call", "reject", "primitive", "success"];
                const modes = ["break", "return", "throw"];
                for (let index = 0; index < kinds.length; index++) {
                    const kind = kinds[index];
                    for (let modeIndex = 0; modeIndex < modes.length; modeIndex++) {
                        const mode = modes[modeIndex];
                        const original: any = index % 2 === 0 ? null : { tag: "body" };
                        const cleanup: any = { tag: "cleanup" };
                        const state: any = { gets: 0, calls: 0, receiver: false, argCount: -1 };
                        const iterator = makeIterator(kind, cleanup, state);
                        let outcome = "unset";
                        try {
                            const result = await take(iterator, mode, original);
                            outcome = "value:" + result;
                        } catch (error: any) {
                            if (mode === "throw" && error === original) {
                                outcome = "body";
                            } else if (error === cleanup) {
                                outcome = "cleanup";
                            } else if (error !== null && error !== undefined && error.name === "TypeError") {
                                outcome = "type";
                            } else {
                                outcome = "unexpected";
                            }
                        }
                        const label = mode === "throw" ? (original === null ? "throw-null" : "throw-object") : mode;
                        console.log(kind, label, outcome, state.gets, state.calls, state.receiver, state.argCount);
                    }
                }
                const events: string[] = [];
                const cleanup: any = { tag: "native-cleanup" };
                let outcome = "unset";
                try {
                    const result = await nativeReturn(events, cleanup);
                    outcome = "value:" + result;
                } catch (error: any) {
                    outcome = error === cleanup ? "cleanup" : "unexpected";
                }
                console.log("native-return", outcome, events.join(","));
            }

            const task = main().catch((error: any) => console.log("unexpected-main", error.name));
            """ + "\n", "missing break value:7 0 0 false -1\nmissing return value:5 0 0 false -1\nmissing throw-null body 0 0 false -1\nnull break value:7 1 0 false -1\nnull return value:5 1 0 false -1\nnull throw-object body 1 0 false -1\nundefined break value:7 1 0 false -1\nundefined return value:5 1 0 false -1\nundefined throw-null body 1 0 false -1\nnoncallable break type 1 0 false -1\nnoncallable return type 1 0 false -1\nnoncallable throw-object body 1 0 false -1\ngetter break cleanup 1 0 false -1\ngetter return cleanup 1 0 false -1\ngetter throw-null body 1 0 false -1\ncall break cleanup 1 1 true 0\ncall return cleanup 1 1 true 0\ncall throw-object body 1 1 true 0\nreject break cleanup 1 1 true 0\nreject return cleanup 1 1 true 0\nreject throw-null body 1 1 true 0\nprimitive break type 1 1 true 0\nprimitive return type 1 1 true 0\nprimitive throw-object body 1 1 true 0\nsuccess break value:7 1 1 true 0\nsuccess return value:5 1 1 true 0\nsuccess throw-null body 1 1 true 0\nnative-return cleanup start,end\n"];
    }

    // These complete programs target interpretation; compiled for-await lowering
    // is covered separately by issue #2848.
    [Theory, MemberData(nameof(Cases))]
    public void AsyncIteratorCloseHonorsCompletionPrecedence(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }
}
