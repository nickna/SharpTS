using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NestedForAwaitLabeledCleanupTests
{
    public static IEnumerable<object[]> Cases()
    {
        // Preserve every public function, the three passing controls, and the final blank line.
        yield return ["public", """
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
                        if (outerValue === 1) continue outer;
                        break outer;
                    }
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
            """.ReplaceLineEndings("\n") + "\n\n", "ordinary 1:1; ordinary-closed\narrow 30 body10:10,body20:20,cleanup-start,cleanup-end\nreject true body1,cleanup-start,cleanup-end,caught\nlabels 1:1,1:2,inner1-closed,2:1,2:2,inner2-closed,outer-closed\n"];
        yield return ["pending-and-rejected-crossed-close", """
            const token: any = { tag: 8 };
            async function* values(label: string, events: string[], fail: boolean) {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    events.push(label + '-start');
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    events.push(label + '-end');
                    if (fail) throw token;
                }
            }
            async function continuing() {
                const events: string[] = [];
                outer: for await (const outerValue of values('outer', events, false)) {
                    for await (const innerValue of values('inner' + outerValue, events, false)) {
                        events.push(outerValue + ':' + innerValue);
                        if (outerValue === 1) continue outer;
                        break outer;
                    }
                }
                console.log('continue', events.join(','));
            }
            async function rejecting() {
                const events: string[] = [];
                try {
                    outer: for await (const outerValue of values('outer-reject', events, false)) {
                        for await (const innerValue of values('inner-reject', events, true)) {
                            events.push(outerValue + ':' + innerValue);
                            break outer;
                        }
                    }
                } catch (error: any) {
                    events.push('caught:' + String(error === token));
                }
                console.log('reject', events.join(','));
            }
            async function main() {
                await continuing();
                await rejecting();
            }
            const task = main().catch((error: any) => console.log('unexpected', error === token));
            """.ReplaceLineEndings("\n") + "\n", "continue 1:1,inner1-start,inner1-end,2:1,inner2-start,inner2-end,outer-start,outer-end\nreject 1:1,inner-reject-start,inner-reject-end,outer-reject-start,outer-reject-end,caught:true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LabeledExitsCloseInnerIteratorsBeforeOuterControlFlow(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
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

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "nested-for-await-labeled-cleanup-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
