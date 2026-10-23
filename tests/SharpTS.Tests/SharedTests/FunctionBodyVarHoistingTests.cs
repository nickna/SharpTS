using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionBodyVarHoistingTests
{
    public const string IssueSource = """
        const value = "outside";
        function run() {
            console.log(value === undefined);
            var value = "body";
            console.log(value);
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-top-level-body-var-masks-outer", IssueSource, "true\nbody\n"];
        yield return ["initializer-effects-stay-in-source-order", """
            const value = "outside", state = { calls: 0 };
            function initialize(): any { state.calls++; console.log("initialize"); return "body"; }
            function run() {
                console.log(value === undefined, state.calls);
                var value: any = initialize();
                console.log(value, state.calls);
                var value: any;
                console.log(value, state.calls);
            }
            run();
            """, "true 0\ninitialize\nbody 1\nbody 1\n"];
        yield return ["nested-blocks-and-repeated-vars-share-body-binding", """
            const value = "outside";
            function run() {
                console.log(value === undefined);
                { var value: any = "nested"; }
                console.log(value);
                { var value: any; }
                console.log(value);
                var value: any = "last";
                console.log(value);
            }
            run();
            """, "true\nnested\nnested\nlast\n"];
        yield return ["function-expression-arrow-and-object-method", """
            const value = "outside";
            const expression = function() {
                const before = value === undefined;
                var value: any = "expression";
                return [before, value].join(",");
            };
            const arrow = () => {
                const before = value === undefined;
                var value: any = "arrow";
                return [before, value].join(",");
            };
            const object = { run() {
                const before = value === undefined;
                var value: any = "method";
                return [before, value].join(",");
            } };
            console.log(expression(), arrow(), object.run());
            """, "true,expression true,arrow true,method\n"];
        yield return ["instance-and-static-methods", """
            const value = "outside";
            class Owner {
                run() {
                    const before = value === undefined;
                    var value: any = "instance";
                    return [before, value].join(",");
                }
                static run() {
                    const before = value === undefined;
                    var value: any = "static";
                    return [before, value].join(",");
                }
                #read() {
                    const before = value === undefined;
                    var value: any = "private";
                    return [before, value].join(",");
                }
                readPrivate() { return this.#read(); }
            }
            console.log(new Owner().run(), Owner.run(), new Owner().readPrivate());
            """, "true,instance true,static true,private\n"];
        yield return ["simple-and-defaulted-parameters-keep-initial-values", """
            function simple(value: any) {
                console.log(value);
                var value: any;
                console.log(value);
                var value: any = 2;
                console.log(value);
            }
            function defaulted(value: any = 3) {
                console.log(value);
                var value: any;
                console.log(value);
                var value: any = 4;
                console.log(value);
            }
            simple(1);
            defaulted();
            defaulted(7);
            """, "1\n1\n2\n3\n3\n4\n7\n7\n4\n"];
        yield return ["early-arrow-and-nested-function-capture-body-var", """
            const value = "outside";
            function arrow() {
                const read = () => value;
                console.log(read() === undefined);
                var value: any = "arrow";
                console.log(read());
                return read;
            }
            function named() {
                console.log(read() === undefined);
                var value: any = "named";
                function read() { return value; }
                console.log(read());
                return read;
            }
            const first = arrow(), second = named();
            console.log(first(), second());
            """, "true\narrow\ntrue\nnamed\narrow named\n"];
        yield return ["loop-var-initializer-and-later-declaration", """
            const value = "outside";
            function run() {
                console.log(value === undefined);
                for (var value: any = 0; value < 2; value++) {}
                console.log(value);
                var value: any;
                console.log(value);
            }
            run();
            """, "true\n2\n2\n"];
        yield return ["typed-captures-are-undefined-until-initialization", """
            const value = 100, flag = true;
            function numeric() {
                const read = () => value;
                console.log(read() === undefined);
                var value: number = 2;
                console.log(read(), value);
            }
            function logical() {
                const read = () => flag;
                console.log(read() === undefined);
                var flag: boolean = false;
                console.log(read(), flag);
            }
            numeric();
            logical();
            """, "true\n2 2\ntrue\nfalse false\n"];
        yield return ["block-class-shadow-keeps-outer-body-var", """
            const value = "outside";
            function run() {
                console.log(value === undefined);
                var value: any = "body";
                {
                    class value { read() { return "inner"; } }
                    const selected: any = value;
                    console.log(selected === value, new selected().read());
                }
                console.log(value);
            }
            run();
            """, "true\ntrue inner\nbody\n"];
        yield return ["class-accessors-initialize-own-forward-var-storage", """
            const value = "outside";
            class Owner {
                get current() {
                    const read = () => value;
                    console.log(read() === undefined);
                    var value: any = "getter";
                    return read();
                }
                set current(next: any) {
                    const read = () => value;
                    console.log(read() === undefined);
                    var value: any = next;
                    console.log(read());
                }
            }
            const owner = new Owner();
            console.log(owner.current);
            owner.current = "setter";
            """, "true\ngetter\ntrue\nsetter\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void FunctionBodyVarsExistBeforeTheirSourceDeclarations(
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
