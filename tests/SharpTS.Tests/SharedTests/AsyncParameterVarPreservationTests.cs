using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncParameterVarPreservationTests
{
    public const string IssueSource = """
        async function run(value: any) { var value; console.log(value); }
        run(7);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-async-parameter-noinitializer-var", IssueSource, "7\n"];
        yield return ["expression-arrow-and-repeated-call-preservation", """
            function synchronous(value: any) { var value; return value; }
            const expression = async function(value: any) {
                var value;
                await Promise.resolve();
                var value;
                return value;
            };
            const arrow = async (value: any) => {
                await Promise.resolve();
                var value;
                return value;
            };
            class Owner {
                async run(value: any) {
                    var value;
                    await Promise.resolve();
                    { let value: any = 4; console.log("shadow", value); }
                    console.log("method", value);
                }
            }
            expression(8).then(first => {
                console.log("expression", first, synchronous(7));
                return arrow(9);
            }).then(second => {
                console.log("arrow", second);
                return expression(10);
            }).then(third => {
                console.log("again", third);
                return arrow(11);
            }).then(fourth => {
                console.log("again-arrow", fourth);
                return new Owner().run(12);
            });
            """, "expression 8 7\narrow 9\nagain 10\nagain-arrow 11\nshadow 4\nmethod 12\n"];
        yield return ["defaulted-and-supplied-values-survive-await", """
            async function run(value: any = 7) {
                var value;
                console.log("before", value);
                await Promise.resolve();
                var value;
                console.log("after", value);
            }
            async function main() {
                await run();
                await run(9);
                await run(undefined);
                await run(null);
                await run(false);
                await run(0);
            }
            main();
            """, "before 7\nafter 7\nbefore 9\nafter 9\nbefore 7\nafter 7\nbefore null\nafter null\nbefore false\nafter false\nbefore 0\nafter 0\n"];
        yield return ["explicit-initializers-still-write-parameter-body-binding", """
            async function run(value: any = 1) {
                console.log("parameter", value);
                var value: any = 2;
                const read = () => value;
                await Promise.resolve();
                var value;
                console.log("body", value, read());
                var value: any = 3;
                console.log("written", value, read());
            }
            run();
            """, "parameter 1\nbody 2 2\nwritten 3 3\n"];
        yield return ["typed-number-and-boolean-parameters-remain-initialized", """
            async function numeric(value: number) {
                var value: number;
                await Promise.resolve();
                var value: number;
                console.log("number", value);
            }
            async function logical(flag: boolean) {
                var flag: boolean;
                await Promise.resolve();
                var flag: boolean;
                console.log("boolean", flag);
            }
            numeric(7).then(() => logical(false)).then(() => numeric(0)).then(() => logical(true));
            """, "number 7\nboolean false\nnumber 0\nboolean true\n"];
        yield return ["supplied-pattern-bindings-survive-body-vars", """
            async function declaration([value]: any[]) {
                var value;
                await Promise.resolve();
                var value;
                console.log("declaration", value);
            }
            const arrow = async ([value]: any[]) => {
                await Promise.resolve();
                var value;
                console.log("arrow", value);
            };
            declaration([7]).then(() => arrow([8]));
            """, "declaration 7\narrow 8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InitializerFreeAsyncVarsPreserveInitializedParameters(
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
