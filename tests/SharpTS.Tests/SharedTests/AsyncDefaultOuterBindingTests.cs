using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncDefaultOuterBindingTests
{
    public const string IssueSource = """
        const value = "outside";
        const make = async function(read: any = () => value) {
            var value = "body";
            return read();
        };
        make().then(
            value => console.log(value),
            (error: any) => console.log("rejected", error.name, error.message)
        );
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-async-expression-default-outer-binding", IssueSource, "outside\n"];
        yield return ["body-created-global-closure-without-defaults", """
            const value = "outside";
            const make = async function() { return () => value; };
            make().then(read => console.log(read())).then(
                undefined,
                (error: any) => console.log("rejected", error.name, error.message)
            );
            """, "outside\n"];
        yield return ["constant-default-and-supplied-callback-controls", """
            const none = async function() { return 2; };
            const scalar = async function(value: number = 3) { return value; };
            const callable = async function(read: any = () => "constant") {
                var value = "body";
                return read();
            };
            none().then(first => {
                console.log("none", first);
                return scalar();
            }).then(second => {
                console.log("scalar", second);
                return callable();
            }).then(third => {
                console.log("default", third);
                return callable(() => "supplied");
            }).then(fourth => console.log("supplied", fourth));
            """, "none 2\nscalar 3\ndefault constant\nsupplied supplied\n"];
        yield return ["retained-default-and-body-closures-after-await", """
            let value = "outside";
            const make = async function(read: any = () => value) {
                var value = "body";
                const body = () => value;
                await Promise.resolve();
                return [read, body, (next: any) => value = next];
            };
            make().then((pair: any) => {
                console.log(pair[0](), pair[1]());
                pair[2]("written");
                value = "changed";
                console.log(pair[0](), pair[1]());
            });
            """, "outside body\nchanged written\n"];
        yield return ["direct-body-assignments-keep-default-global-binding", """
            const value = "outside";
            const local = async function(read: any = () => value) {
                var value = "body";
                value = "written";
                return read() + ":" + value;
            };
            const hoisted = async function(read: any = () => value) {
                var value = "body";
                await Promise.resolve();
                value = "written";
                return read() + ":" + value;
            };
            local().then(first => {
                console.log("local", first);
                return hoisted();
            }).then(second => console.log("hoisted", second));
            """, "local outside:written\nhoisted outside:written\n"];
        yield return ["async-arrow-declaration-and-nested-expression-outer-binding", """
            const value = "outside";
            async function declaration(read: any = () => value) {
                var value = "declaration";
                await Promise.resolve();
                return read() + ":" + value;
            }
            const arrow = async (read: any = () => value) => {
                var value = "arrow";
                await Promise.resolve();
                return read() + ":" + value;
            };
            async function nested() {
                const make = async function(read: any = () => value) {
                    var value = "nested";
                    return read() + ":" + value;
                };
                return await make();
            }
            declaration().then(first => {
                console.log(first);
                return arrow();
            }).then(second => {
                console.log(second);
                return nested();
            }).then(third => console.log(third));
            """, "outside:declaration\noutside:arrow\noutside:nested\n"];
    }

    public static IEnumerable<object[]> StandaloneCases() => Cases().Concat(
        ParameterDefaultBodyScopeTests.Cases()
            .Where(row => (string)row[0] == "async-and-generator-default-outer-binding"));

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncDefaultsRetainOuterBindingsBeforeBodyVariables(
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
