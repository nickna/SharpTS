using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// An array method whose argument contains a nested `await` (e.g. `arr.push(String(await f()))`).
/// In compiled mode the array fast-path emitters used to evaluate the argument while the receiver
/// list (and, for the args-array methods, a partially built `object[]`) sat on the IL evaluation
/// stack; across the state-machine suspension that produced invalid IL
/// ("PathStackDepth - Stack depth differs depending on path", #850). The fix pre-spills the receiver
/// and every argument into await-safe locals before the suspension. These tests pin every affected
/// method (interp == compiled; compiled mode also IL-verifies on load).
/// </summary>
public class ArrayMethodAwaitArgTests
{
    // The exact #850 repro: an async arrow that WRITES a captured variable, awaited inside push().
    [Theory, ModeData]
    public void Push_AwaitedWriteCaptureArrow(ExecutionMode mode)
    {
        var source = """
            async function af(): Promise<string> {
              const out: string[] = [];
              let r = 5;
              const f = async () => { r = r + 1; return r; };
              out.push(String(await f()));
              out.push(String(r));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }

    // Same shape but a READ-ONLY capture — confirms the bug was never about write-capture promotion.
    [Theory, ModeData]
    public void Push_AwaitedReadOnlyCaptureArrow(ExecutionMode mode)
    {
        var source = """
            async function af(): Promise<string> {
              const out: string[] = [];
              let r = 5;
              const f = async () => { return r + 1; };
              out.push(String(await f()));
              out.push(String(r));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Push_MultipleAwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a: number[] = [];
              a.push(await n(1), await n(2), await n(3));
              console.log(a.join(","));
            }
            main();
            """;

        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Unshift_AwaitedArg(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [3, 4];
              a.unshift(await n(1), await n(2));
              console.log(a.join(","));
            }
            main();
            """;

        Assert.Equal("1,2,3,4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Slice_AwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [10, 20, 30, 40];
              console.log(a.slice(await n(1), await n(3)).join(","));
            }
            main();
            """;

        Assert.Equal("20,30\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Splice_AwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [10, 20, 30];
              a.splice(await n(1), await n(1), await n(99));
              console.log(a.join(","));
            }
            main();
            """;

        Assert.Equal("10,99,30\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Concat_AwaitedArg(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [1, 2];
              console.log(a.concat([await n(3)]).join(","));
            }
            main();
            """;

        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Reduce_AwaitedInitialValue(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [1, 2, 3];
              console.log(a.reduce((p, c) => p + c, await n(100)));
            }
            main();
            """;

        Assert.Equal("106\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void IndexOf_AwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [5, 6, 7, 6];
              console.log(a.indexOf(await n(6), await n(2)));
            }
            main();
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Includes_AwaitedArg(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [1, 2, 3];
              console.log(a.includes(await n(2)));
            }
            main();
            """;

        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Fill_AwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [0, 0, 0, 0];
              a.fill(await n(7), await n(1), await n(3));
              console.log(a.join(","));
            }
            main();
            """;

        Assert.Equal("0,7,7,0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CopyWithin_AwaitedArgs(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [1, 2, 3, 4, 5];
              a.copyWithin(await n(0), await n(3));
              console.log(a.join(","));
            }
            main();
            """;

        Assert.Equal("4,5,3,4,5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void At_AwaitedArg(ExecutionMode mode)
    {
        var source = """
            async function n(x: number): Promise<number> { return x; }
            async function main() {
              const a = [11, 22, 33];
              console.log(a.at(await n(-1)));
            }
            main();
            """;

        Assert.Equal("33\n", TestHarness.Run(source, mode));
    }

    // The original issue surfaced via a sync arrow capture plus an unrelated awaited call inside push
    // (variant D) — also fixed, since the trigger is purely the nested await in the array argument.
    [Theory, ModeData]
    public void Push_SyncArrowCaptureWithUnrelatedAwait(ExecutionMode mode)
    {
        var source = """
            async function g(): Promise<number> { return 0; }
            async function af(): Promise<string> {
              const out: string[] = [];
              let r = 5;
              const f = () => { r = r + 1; return r; };
              out.push(String(f() + (await g())));
              out.push(String(r));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }
}
