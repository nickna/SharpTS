using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorFinallyExceptionStateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["awaited-finally-exception-lifetime", """
            async function* plain(events: string[]) {
                try {
                    yield 1;
                } finally {
                    await Promise.resolve(0);
                    events.push("cleanup");
                }
            }

            async function* nested(events: string[]) {
                try {
                    try {
                        yield 1;
                    } finally {
                        await Promise.resolve(0);
                        events.push("inner");
                    }
                } finally {
                    await Promise.resolve(0);
                    events.push("outer");
                }
            }

            async function* replacementThrow(events: string[], replacement: any) {
                try {
                    yield 1;
                } finally {
                    await Promise.resolve(0);
                    events.push("replace");
                    throw replacement;
                }
            }

            async function* replacementReturn(events: string[]) {
                try {
                    yield 1;
                } finally {
                    await Promise.resolve(0);
                    events.push("return");
                    return 9;
                }
            }

            async function main() {
                const cases: any[] = [
                    { label: "null", value: null },
                    { label: "undefined", value: undefined },
                    { label: "false", value: false },
                    { label: "zero", value: 0 },
                    { label: "empty", value: "" }
                ];
                for (let index = 0; index < cases.length; index++) {
                    const entry = cases[index];
                    const events: string[] = [];
                    const generator = plain(events);
                    const first = await generator.next();
                    let caught = false;
                    let same = false;
                    try {
                        await generator.throw(entry.value);
                    } catch (error: any) {
                        caught = true;
                        same = error === entry.value;
                    }
                    const last = await generator.next();
                    console.log(entry.label, first.value, first.done, caught, same, events.join(","), last.value === undefined, last.done);
                }

                const original: any = { tag: "original" };
                const nestedEvents: string[] = [];
                const nestedGenerator = nested(nestedEvents);
                const nestedFirst = await nestedGenerator.next();
                let nestedCaught = false;
                let nestedSame = false;
                try {
                    await nestedGenerator.throw(original);
                } catch (error: any) {
                    nestedCaught = true;
                    nestedSame = error === original;
                }
                const nestedLast = await nestedGenerator.next();
                console.log("nested", nestedFirst.value, nestedCaught, nestedSame, nestedEvents.join(","), nestedLast.value === undefined, nestedLast.done);

                const replacement: any = { tag: "replacement" };
                const throwEvents: string[] = [];
                const throwingGenerator = replacementThrow(throwEvents, replacement);
                const throwingFirst = await throwingGenerator.next();
                let replacementCaught = false;
                let replacementSame = false;
                let originalReplaced = false;
                try {
                    await throwingGenerator.throw(original);
                } catch (error: any) {
                    replacementCaught = true;
                    replacementSame = error === replacement;
                    originalReplaced = error !== original;
                }
                const throwingLast = await throwingGenerator.next();
                console.log("throw", throwingFirst.value, replacementCaught, replacementSame, originalReplaced, throwEvents.join(","), throwingLast.value === undefined, throwingLast.done);

                const returnEvents: string[] = [];
                const returningGenerator = replacementReturn(returnEvents);
                const returningFirst = await returningGenerator.next();
                let returnRejected = false;
                let returned: any;
                try {
                    returned = await returningGenerator.throw(original);
                } catch (error: any) {
                    returnRejected = true;
                }
                const returningLast = await returningGenerator.next();
                console.log("return", returningFirst.value, returnRejected, returned.value, returned.done, returnEvents.join(","), returningLast.value === undefined, returningLast.done);
            }

            const task = main().catch((error: any) => console.log("unexpected", error.name));
            """ + "\n", "null 1 false true true cleanup true true\nundefined 1 false true true cleanup true true\nfalse 1 false true true cleanup true true\nzero 1 false true true cleanup true true\nempty 1 false true true cleanup true true\nnested 1 true true inner,outer true true\nthrow 1 true true true replace true true\nreturn 1 false 9 true return true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuspendingFinallyPreservesOrOverridesTheCapturedException(
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
