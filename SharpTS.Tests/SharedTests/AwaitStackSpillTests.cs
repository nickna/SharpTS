using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #251: an await suspending inside a subexpression must
/// reach the async state-machine boundary with an empty IL evaluation stack.
/// Each shape here previously compiled to invalid IL (InvalidProgramException
/// at MoveNext JIT) because a value was left stacked below the await.
/// Runs against both interpreter and compiler.
/// </summary>
public class AwaitStackSpillTests
{
    [Theory, ModeData]
    public void ConsoleLog_MultiArg_InlineAwait(ExecutionMode mode)
    {
        var source = """
            async function m() {
                const t: any = 5;
                console.log("t1", await t);
                console.log("m", await t, "n", await t);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("t1 5\nm 5 n 5\n", output);
    }

    [Theory, ModeData]
    public void BinaryOperands_InlineAwait(ExecutionMode mode)
    {
        var source = """
            async function m() {
                const t: any = 5;
                console.log("x" + (await t));
                console.log(1 < await t);
                console.log(7 & await t);
                console.log(1 << await t);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x5\ntrue\n5\n32\n", output);
    }

    [Theory, ModeData]
    public void ArrayAndObjectLiterals_InlineAwait(ExecutionMode mode)
    {
        var source = """
            async function m() {
                const t: any = 5;
                console.log([1, await t]);
                console.log({ a: await t });
                const k: any = "dyn";
                console.log({ [k]: await t });
                console.log([...[await t], 2]);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[1, 5]\n{ a: 5 }\n{ dyn: 5 }\n[5, 2]\n", output);
    }

    [Theory, ModeData]
    public void PropertyAndIndexAssignment_InlineAwait(ExecutionMode mode)
    {
        var source = """
            class C { x: any = 0; }
            async function m() {
                const t: any = 5;
                const c = new C();
                c.x = await t;
                console.log(c.x);
                const arr: any = [9, 8, 7];
                console.log(arr[await t - 4]);
                arr[0] = await t;
                console.log(arr[0]);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n8\n5\n", output);
    }

    [Theory, ModeData]
    public void CompoundAndLogicalAssignment_InlineAwait(ExecutionMode mode)
    {
        var source = """
            class C { x: any = 0; }
            async function m() {
                const t: any = 5;
                const c = new C();
                c.x = 1;
                c.x += await t;
                console.log(c.x);
                const arr: any = [1];
                arr[0] += await t;
                console.log(arr[0]);
                c.x = null;
                c.x ??= await t;
                console.log(c.x);
                arr[0] = 0;
                arr[0] ||= await t;
                console.log(arr[0]);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n6\n5\n5\n", output);
    }

    [Theory, ModeData]
    public void TaggedTemplate_InlineAwait(ExecutionMode mode)
    {
        var source = """
            async function m() {
                const t: any = 5;
                const tag = (s: any, v: any) => s[0] + v;
                console.log(tag`a${await t}`);
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a5\n", output);
    }

    [Theory, ModeData]
    public void PrivateMembers_InlineAwait(ExecutionMode mode)
    {
        var source = """
            class W {
                #p: any = 1;
                #m(a: any) { return a; }
                async go() {
                    const t: any = 5;
                    this.#p = await t;
                    return this.#m(await t) + this.#p;
                }
            }
            async function m() {
                const w = new W();
                console.log(await w.go());
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void ConsoleError_MultiArg_InlineAwait_DoesNotCrash(ExecutionMode mode)
    {
        var source = """
            async function m() {
                const t: any = 5;
                console.log("before");
                console.error("e1", await t);
                console.log("after");
            }
            m();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("before\n", output);
        Assert.Contains("after\n", output);
    }
}
