using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FuturePatternParameterCaptureTests
{
    public const string IssueSource = """
        const value = 99;
        function run(read: any = () => value, [value]: any[] = [2]) {
            console.log(read(), value);
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-earlier-default-captures-later-pattern", IssueSource, "2 2\n"];
        yield return ["retained-closures-keep-independent-parameter-cells", """
            const value = 99;
            function make(read: any = () => value, [value]: any[] = [2]) {
                return [read, (next: any) => value = next];
            }
            const first: any = make();
            const second: any = make(undefined, [3]);
            console.log(first[0](), second[0]());
            first[1](7);
            console.log(first[0](), second[0]());
            console.log(first[0]());
            """, "2 3\n7 3\n7\n"];
        yield return ["synchronous-callable-forms-capture-array-and-object-bindings", """
            const value = 99;
            const expression = function(read: any = () => value, [value]: any[] = [2]) { return read(); };
            const arrow = (read: any = () => value, [value]: any[] = [3]) => read();
            const object = { run(read: any = () => value, { source: value }: any = { source: 4 }) { return read(); } };
            class Owner {
                run(read: any = () => value, [value]: any[] = [5]) { return read(); }
                static run(read: any = () => value, { source: value }: any = { source: 6 }) { return read(); }
            }
            console.log(expression(), arrow(), object.run(), new Owner().run(), Owner.run());
            """, "2 3 4 5 6\n"];
        yield return ["object-alias-default-order-and-supplied-callback", """
            const value = 99;
            const trace: string[] = [];
            function mark(name: string, value: number): number { trace.push(name); return value; }
            function run(read: any = (trace.push("read"), () => value),
                { source: value = mark("value", 2) }: any = {}) {
                console.log(read(), value, trace.join(",") || "none");
                trace.length = 0;
            }
            run();
            run(() => 7, { source: 3 });
            """, "2 2 read,value\n7 3 none\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefaultClosuresRetainLaterPatternParameterBindings(
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
