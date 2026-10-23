using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NestedParameterDefaultCaptureTests
{
    public const string IssueSource = """
        function enclosing() {
            const value = "enclosing";
            function make(read: any = () => value) { var value = "body"; return [read, () => value]; }
            const pair: any = make();
            console.log(pair[0](), pair[1]());
        }
        enclosing();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-nested-default-retains-enclosing-local", IssueSource, "enclosing body\n"];
        yield return ["retained-closures-keep-live-independent-owner-storage", """
            function factory(initial: string) {
                let value = initial;
                function make(read: any = () => value) {
                    var value = "body";
                    return [read, () => value, (next: string) => value = next];
                }
                const pair: any = make();
                return [pair, (next: string) => value = next];
            }
            const first: any = factory("first");
            const second: any = factory("second");
            console.log(first[0][0](), first[0][1](), second[0][0](), second[0][1]());
            first[1]("changed");
            first[0][2]("written");
            console.log(first[0][0](), first[0][1](), second[0][0](), second[0][1]());
            """, "first body second body\nchanged written second body\n"];
        yield return ["nested-expression-and-arrow-default-capture-controls", """
            function enclosing() {
                const value = "enclosing";
                const expression = function(read: any = () => value) {
                    var value = "expression";
                    return [read, () => value];
                };
                const arrow = (read: any = () => value) => {
                    var value = "arrow";
                    return [read, () => value];
                };
                return [expression(), arrow()];
            }
            const pairs: any = enclosing();
            console.log(pairs[0][0](), pairs[0][1](), pairs[1][0](), pairs[1][1]());
            """, "enclosing expression enclosing arrow\n"];
        yield return ["supplied-callback-skips-default-closure-creation", """
            const state = { defaults: 0 };
            function enclosing() {
                const value = "enclosing";
                function make(read: any = (state.defaults++, () => value)) {
                    var value = "body";
                    return [read, () => value];
                }
                const supplied: any = make(() => "supplied");
                console.log(supplied[0](), supplied[1](), state.defaults);
                const omitted: any = make();
                console.log(omitted[0](), omitted[1](), state.defaults);
            }
            enclosing();
            """, "supplied body 0\nenclosing body 1\n"];
        yield return ["default-capture-crosses-an-ordinary-relay", """
            function enclosing() {
                const value = "deep";
                function middle() {
                    function make(read: any = () => value) {
                        var value = "body";
                        return [read, () => value];
                    }
                    return make();
                }
                return middle();
            }
            const pair: any = enclosing();
            console.log(pair[0](), pair[1]());
            """, "deep body\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NestedDefaultClosuresRetainTheirEnclosingBindings(
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
