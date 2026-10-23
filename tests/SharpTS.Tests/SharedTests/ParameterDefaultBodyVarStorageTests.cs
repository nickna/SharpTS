using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ParameterDefaultBodyVarStorageTests
{
    public const string IssueSource = """
        function run(value: any = 1, read: any = () => value) {
            var value = 2;
            console.log(value, read());
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-default-read-and-body-var", IssueSource, "2 1\n"];
        yield return ["typed-number-and-boolean-parameter-cells", """
            function numeric(value: number = 1, read = () => value) {
                var value: number = 2;
                console.log(value, read());
            }
            function logical(flag: boolean = true, read = () => flag) {
                var flag: boolean = false;
                console.log(flag, read());
            }
            numeric();
            logical();
            """, "2 1\nfalse true\n"];
        yield return ["retained-closures-use-independent-cells", """
            function make(value: any = 1, read: any = () => value,
                write: any = (next: any) => value = next) {
                var value;
                const bodyRead = () => value;
                const bodyWrite = (next: any) => value = next;
                return { read, write, bodyRead, bodyWrite };
            }
            const pair: any = make();
            console.log(pair.read(), pair.bodyRead());
            pair.bodyWrite(2);
            console.log(pair.read(), pair.bodyRead());
            pair.write(3);
            console.log(pair.read(), pair.bodyRead());
            """, "1 1\n1 2\n3 2\n"];
        yield return ["body-var-copies-supplied-and-defaulted-parameter", """
            const state = { defaults: 0 };
            function run(value: any = (state.defaults++, 1), read: any = () => value) {
                var value;
                console.log(value, read(), state.defaults);
                value = 2;
                console.log(value, read(), state.defaults);
            }
            run(7);
            run(undefined);
            """, "7 7 0\n2 7 0\n1 1 1\n2 1 1\n"];
        yield return ["without-body-var-default-closure-shares-parameter", """
            function run(value: any, read: any = () => value,
                write: any = (next: any) => value = next) {
                value = 2;
                console.log(value, read());
                write(3);
                console.log(value, read());
            }
            run(1);
            """, "2 2\n3 3\n"];
        yield return ["simple-and-rest-only-parameters-remain-shared", """
            function simple(value: any) {
                var value;
                const read = () => value;
                console.log(value, read());
                value = 2;
                console.log(value, read());
            }
            function rest(...items: any[]) {
                var items: any[];
                const read = () => items;
                console.log(items.join(","), read() === items);
                items = [3];
                console.log(read() === items, read()[0]);
            }
            simple(1);
            rest(1, 2);
            """, "1 1\n2 2\n1,2 true\ntrue 3\n"];
        yield return ["function-expression-arrow-and-object-method", """
            const expression = function(value: any = 1, read: any = () => value) {
                var value: any = 2;
                return [value, read()];
            };
            const arrow = (value: any = 1, read: any = () => value) => {
                var value: any = 2;
                return [value, read()];
            };
            const object = { run(value: any = 1, read: any = () => value) {
                var value: any = 2;
                return [value, read()];
            } };
            console.log(expression().join(","), arrow().join(","), object.run().join(","));
            """, "2,1 2,1 2,1\n"];
        yield return ["instance-and-static-methods", """
            class Owner {
                run(value: any = 1, read: any = () => value) {
                    var value: any = 2;
                    return [value, read()];
                }
                static run(value: any = 1, read: any = () => value) {
                    var value: any = 2;
                    return [value, read()];
                }
            }
            console.log(new Owner().run().join(","), Owner.run().join(","));
            """, "2,1 2,1\n"];
        yield return ["callable-body-var-starts-with-parameter-value", """
            function run(read: any = () => 1, original: any = () => read) {
                var read;
                console.log(read === original(), read());
                read = () => 2;
                console.log(read(), original()());
            }
            run();
            """, "true 1\n2 1\n"];
        yield return ["nested-shadows-and-body-captures-stay-distinct", """
            function run(value: any = 1, read: any = () => value,
                write: any = (next: any) => value = next) {
                var value;
                const bodyRead = () => value;
                const bodyWrite = (next: any) => value = next;
                function nested(value: any) {
                    var value;
                    const nestedRead = () => value;
                    value = 40;
                    return nestedRead();
                }
                {
                    let value = 50;
                    const blockRead = () => value;
                    console.log(blockRead());
                }
                console.log(nested(30), value, read());
                bodyWrite(2);
                write(3);
                console.log(bodyRead(), read());
            }
            run();
            """, "50\n40 1 1\n2 3\n"];
        yield return ["later-default-copy-and-repeated-body-var-cell", """
            function run(value: any = 1, read: any = () => value, ignored: any = (value = 4)) {
                var value: any;
                function bodyRead() { return value; }
                console.log(value, read());
                for (var value: any = 2; value < 3; value++) {}
                var value: any;
                return [bodyRead, read];
            }
            const pair: any = run();
            console.log(pair[0](), pair[1]());
            """, "4 4\n3 4\n"];
        yield return ["typed-record-parameter-and-body-var", """
            function run(value: { count: number }, read = () => value) {
                var value: { count: number } = { count: 2 };
                console.log(value.count, read().count);
            }
            run({ count: 1 });
            """, "2 1\n"];
        yield return ["ordinary-and-live-constructor-body-vars", """
            class Plain {
                body: any;
                read: any;
                constructor(value: any = 1, read: any = () => value) {
                    var value: any = 2;
                    this.body = value;
                    this.read = read;
                }
            }
            class Base {}
            class Live extends Base {
                body: any;
                read: any;
                constructor(value: any = 1, read: any = () => value) {
                    super();
                    var value: any = 2;
                    this.body = value;
                    this.read = read;
                }
            }
            Object.setPrototypeOf(Live, Base);
            const plain: any = new Plain(), live: any = new Live();
            console.log(plain.body, plain.read());
            console.log(live.body, live.read());
            """, "2 1\n2 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ParameterExpressionsKeepSameNamedBodyVarsInSeparateStorage(
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
