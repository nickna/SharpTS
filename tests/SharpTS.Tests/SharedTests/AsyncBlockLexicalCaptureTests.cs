using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncBlockLexicalCaptureTests
{
    public const string IssueSource = """
        let value = 100;
        async function run() {
            await Promise.resolve(0);
            {
                const read = () => value;
                try { read(); } catch (error: any) { console.log(error.name); }
                let value: number;
                console.log(typeof value);
                value = 42;
                console.log(read());
            }
            console.log(value);
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-async-no-initializer", IssueSource, "ReferenceError\nundefined\n42\n100\n"];
        yield return ["synchronous-no-initializer-control", """
            let value = 100;
            function run() {
                {
                    const read = () => value;
                    try { read(); } catch (error: any) { console.log(error.name); }
                    let value: number;
                    console.log(typeof value);
                    value = 42;
                    console.log(read());
                }
                console.log(value);
            }
            run();
            """, "ReferenceError\nundefined\n42\n100\n"];
        yield return ["predeclaration-read-and-typeof", """
            let value = 100;
            async function run() {
                await Promise.resolve(0);
                {
                    const read = () => value;
                    const kind = () => typeof value;
                    try { read(); } catch (error: any) { console.log("read", error.name); }
                    try { kind(); } catch (error: any) { console.log("typeof", error.name); }
                    let value: number;
                    console.log(read() === undefined, kind());
                    value = 42;
                    console.log(read(), kind());
                }
                console.log(value);
            }
            run();
            """, "read ReferenceError\ntypeof ReferenceError\ntrue undefined\n42 number\n100\n"];
        yield return ["retained-disjoint-block-captures", """
            let value = 100;
            async function run() {
                await Promise.resolve(0);
                let first: () => number;
                let second: () => number;
                {
                    first = () => value;
                    let value: number;
                    value = 11;
                }
                {
                    second = () => value;
                    let value: number;
                    value = 22;
                }
                console.log(first(), second(), value);
            }
            run();
            """, "11 22 100\n"];
        yield return ["captured-write-across-timer-await", """
            let value = 100;
            let retained: () => number;
            async function run() {
                {
                    const read = () => value;
                    const write = (next: number) => { value = next; };
                    retained = read;
                    let value: number;
                    value = 1;
                    await new Promise<void>(resolve => {
                        setTimeout(() => resolve(), 0);
                    });
                    write(42);
                    console.log(read());
                }
                console.log(value);
            }
            run().then(() => {
                value = 200;
                console.log(retained(), value);
            });
            """, "42\n100\n42 200\n"];
        yield return ["captured-parameter-keeps-own-storage", """
            let value = 100;
            async function run(value: number) {
                const outer = () => value;
                let inner: () => number;
                {
                    inner = () => value;
                    let value: number;
                    value = 42;
                }
                await new Promise<void>(resolve => {
                    setTimeout(() => resolve(), 0);
                });
                console.log(outer(), inner(), value);
            }
            run(7).then(() => console.log(value));
            """, "7 42 7\n100\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BlockCapturesKeepLexicalIdentityAndTemporalDeadZones(
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
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
