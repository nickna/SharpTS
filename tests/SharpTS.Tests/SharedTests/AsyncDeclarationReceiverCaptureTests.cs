using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncDeclarationReceiverCaptureTests
{
    public const string IssueSource = """
        function start() {
            const captured = 47;
            async function answer(): Promise<number> { return captured; }
            const forms = [answer];
            forms[0]().then(value => console.log("member", value));
            const selected = forms[0];
            selected().then(value => console.log("selected", value));
        }
        start();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-array-member", IssueSource, "member 47\nselected 47\n"];
        yield return ["member-defaults-await-and-explicit-receivers", """
            function make() {
                const captured = 47;
                async function answer(this: any, value: number = captured): Promise<string> {
                    await Promise.resolve(0);
                    return this.tag + ":" + captured + ":" + value;
                }
                return { tag: "member", answer };
            }
            async function run() {
                const owner = make();
                console.log(await owner.answer());
                console.log(await owner["answer"]());
                console.log(await owner.answer.call({ tag: "call" }));
                console.log(await owner.answer.apply({ tag: "apply" }, [3]));
                const bound: any = owner.answer.bind({ tag: "bound" }, 4);
                console.log(await bound());
            }
            run().catch(error => console.log(String(error)));
            """, "member:47:47\nmember:47:47\ncall:47:47\napply:47:3\nbound:47:4\n"];
        yield return ["captured-writes-and-independent-closures", """
            function make(initial: number) {
                let captured = initial;
                async function step(this: any, amount: number): Promise<number> {
                    captured += amount;
                    await Promise.resolve(0);
                    return captured + this.offset;
                }
                return { offset: 10, step };
            }
            async function run() {
                const first = make(1), second = make(20);
                console.log(await first.step(2));
                console.log(await second.step(3));
                console.log(await first.step.call({ offset: 100 }, 4));
                console.log(await first.step(0));
            }
            run().catch(error => console.log(String(error)));
            """, "13\n33\n107\n17\n"];
        yield return ["nested-declaration-in-method", """
            class Factory {
                make() {
                    const captured = 47;
                    async function answer(): Promise<number> {
                        await Promise.resolve(0);
                        return captured;
                    }
                    return { answer };
                }
            }
            const owner = new Factory().make();
            owner.answer().then(value => console.log(value));
            """, "47\n"];
        yield return ["class-private-super-and-static-controls", """
            function make(captured: number) {
                class Base {
                    async base(): Promise<number> { return captured; }
                }
                class Derived extends Base {
                    #value: number = 3;
                    async answer(): Promise<number> {
                        const first = await super.base();
                        await Promise.resolve(0);
                        return first + this.#value + captured;
                    }
                    static async read(): Promise<number> {
                        await Promise.resolve(0);
                        return captured;
                    }
                }
                return Derived;
            }
            async function run() {
                const Class = make(7);
                const owner = new Class();
                console.log(await owner.answer());
                const borrowed: any = owner.answer;
                console.log(await borrowed.call(owner));
                console.log(await Class.read());
            }
            run().catch(error => console.log(String(error)));
            """, "17\n17\n7\n"];
        yield return ["exact-captured-nested-async-own-this-await", """
            function make(captured: number) {
                async function answer(this: any): Promise<number> {
                    await Promise.resolve(0);
                    return captured + this.offset;
                }
                return { offset: 1, answer };
            }
            make(46).answer().then(value => console.log(value));
            """, "47\n"];
        yield return ["own-arguments-default-rest-and-receivers-across-await", """
            function make(captured: number) {
                async function read(this: any, first: number = captured, ...rest: number[]): Promise<string> {
                    const own = arguments;
                    const before = arguments.length;
                    await Promise.resolve(0);
                    return this.tag + ':' + first + ':' + rest.join(',') + ':' + before + ':' + arguments.length + ':' + (own === arguments) + ':' + captured;
                }
                return { tag: 'owner', read };
            }
            async function run() {
                const owner = make(7);
                console.log(await owner.read());
                console.log(await owner.read.call({ tag: 'call' }, 2, 3, 4));
                const bound: any = owner.read.bind({ tag: 'bound' }, 5);
                console.log(await bound(6));
            }
            run().catch(error => console.log(String(error)));
            """, "owner:7::0:0:true:7\ncall:2:3,4:3:3:true:7\nbound:5:6:2:2:true:7\n"];
        yield return ["equivalent-async-function-expression", """
            function make(captured: number) {
                const read = async function(this: any, first: number = captured, ...rest: number[]): Promise<string> {
                    const own = arguments;
                    const before = arguments.length;
                    await Promise.resolve(0);
                    return this.tag + ':' + first + ':' + rest.join(',') + ':' + before + ':' + arguments.length + ':' + (own === arguments) + ':' + captured;
                };
                return { tag: 'owner', read };
            }
            async function run() {
                const owner = make(7);
                console.log(await owner.read());
                console.log(await owner.read.call({ tag: 'call' }, 2, 3, 4));
                const bound: any = owner.read.bind({ tag: 'bound' }, 5);
                console.log(await bound(6));
            }
            run().catch(error => console.log(String(error), error.stack));
            """, "owner:7::0:0:true:7\ncall:2:3,4:3:3:true:7\nbound:5:6:2:2:true:7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases().Where(row => (string)row[0] is
        "exact-captured-nested-async-own-this-await" or
        "own-arguments-default-rest-and-receivers-across-await" or
        "member-defaults-await-and-explicit-receivers" or
        "captured-writes-and-independent-closures" or
        "equivalent-async-function-expression");

    [Theory, MemberData(nameof(ModeCases))]
    public void MemberReceiversPreserveResolvedDeclarationCaptures(
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
            // Production resolution is essential: dynamic lookup masks the extra receiver scope.
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void RawInterpreterPreservesDeclarationCaptureBehavior(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
