using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncExceptionPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-async-null-rejection", """
            async function run() {
                try {
                    await Promise.reject(null);
                } catch (error) {
                    console.log("caught", error === null);
                }
            }

            const task = run();
            """ + "\n", "caught true\n"];
        yield return ["ordinary-async-exception-presence", """
            async function declaration(token: any, events: string[]) {
                try {
                    await Promise.resolve(0);
                    throw token;
                } catch (error: any) {
                    events.push("caught");
                    return error;
                }
            }

            const arrow = async (token: any, events: string[]) => {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    events.push("caught");
                    return error;
                }
            };

            async function rethrow(token: any, events: string[]) {
                try {
                    await Promise.reject(token);
                } catch (error: any) {
                    events.push("caught");
                    try {
                        throw error;
                    } finally {
                        await Promise.resolve(0);
                        events.push("finally");
                    }
                }
            }

            async function noError(events: string[]) {
                try {
                    await Promise.resolve(0);
                    events.push("body");
                    return 7;
                } catch (error: any) {
                    events.push("unexpected-catch");
                    return -1;
                } finally {
                    await Promise.resolve(0);
                    events.push("finally");
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
                    const declarationEvents: string[] = [];
                    const declarationResult = await declaration(entry.value, declarationEvents);
                    const arrowEvents: string[] = [];
                    const arrowResult = await arrow(entry.value, arrowEvents);
                    const rethrowEvents: string[] = [];
                    let caught = false;
                    let same = false;
                    try {
                        await rethrow(entry.value, rethrowEvents);
                    } catch (error: any) {
                        caught = true;
                        same = error === entry.value;
                    }
                    console.log(entry.label, declarationResult === entry.value, declarationEvents.join(","), arrowResult === entry.value, arrowEvents.join(","), caught, same, rethrowEvents.join(","));
                }
                const events: string[] = [];
                const result = await noError(events);
                console.log("no-error", result, events.join(","));
            }

            const task = main().catch((error: any) => console.log("unexpected", error.name));
            """ + "\n", "null true caught true caught true true caught,finally\nundefined true caught true caught true true caught,finally\nfalse true caught true caught true true caught,finally\nzero true caught true caught true true caught,finally\nempty true caught true caught true true caught,finally\nno-error 7 body,finally\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncCatchDistinguishesExceptionPresenceFromItsPayload(
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
