using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionBodyForwardVarScopeTests
{
    public const string IssueSource = """
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
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-own-forward-numeric-and-boolean", IssueSource, "true\n2 2\ntrue\nfalse false\n"];
        yield return ["synchronous-callable-body-forms", """
            function declaration() {
                const read = () => value;
                var padding = 0, value = 7;
                return read();
            }
            const expression = function() {
                const read = () => value;
                var value = 8;
                return read();
            };
            const arrow = () => {
                const read = () => value;
                var value = 9;
                return read();
            };
            const object = {
                get current() {
                    const read = () => value;
                    var value = 10;
                    return read();
                },
                read() {
                    const read = () => value;
                    var value = 11;
                    return read();
                }
            };
            console.log(declaration(), expression(), arrow(), object.current, object.read());
            """, "7 8 9 10 11\n"];
        yield return ["initializer-effects-remain-after-early-capture", """
            const state = { calls: 0 };
            function run() {
                const read = () => value;
                const readOther = () => other;
                console.log(read() === undefined, readOther() === undefined, state.calls);
                var value: number = (state.calls++, 7), other: number = (state.calls++, 11);
                console.log(read(), readOther(), state.calls);
            }
            run();
            """, "true true 0\n7 11 2\n"];
        yield return ["parameter-default-keeps-outer-scope", """
            const value = "outside";
            function run(initial: any = value) {
                const read = () => value;
                console.log(initial, read() === undefined);
                var value: any = "body";
                console.log(initial, read());
            }
            run();
            """, "outside true\noutside body\n"];
        yield return ["lexical-declarations-retain-tdz", """
            function lexicalLet() {
                const read = () => value;
                try { read(); } catch (error) { console.log(error instanceof ReferenceError); }
                let value = 7;
                console.log(read());
            }
            function lexicalConst() {
                const read = () => value;
                try { read(); } catch (error) { console.log(error instanceof ReferenceError); }
                const value = 9;
                console.log(read());
            }
            lexicalLet();
            lexicalConst();
            """, "true\n7\ntrue\n9\n"];
        yield return ["class-callable-body-forms", """
            class Declared {
                constructor() {
                    const read = () => value;
                    var value = 1;
                    console.log("constructor", read());
                }
                method() {
                    const read = () => value;
                    var value = 2;
                    return read();
                }
                get current() {
                    const read = () => value;
                    var value = 3;
                    return read();
                }
                set current(next: number) {
                    const read = () => value;
                    var value = next;
                    console.log("setter", read());
                }
                #read() {
                    const read = () => value;
                    var value = 4;
                    return read();
                }
                readPrivate() { return this.#read(); }
            }
            const Expression = class {
                method() {
                    const read = () => value;
                    var value = 5;
                    return read();
                }
            };
            const owner = new Declared();
            console.log(owner.method(), owner.current, owner.readPrivate(), new Expression().method());
            owner.current = 6;
            """, "constructor 1\n2 3 4 5\nsetter 6\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeferredBodiesCanReadTheirFunctionsLaterVars(
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
