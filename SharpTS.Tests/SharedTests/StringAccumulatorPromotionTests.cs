using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the append-only string-accumulator promotion optimization (#857): a provably
/// non-escaping <c>string</c> local with a string-literal initializer, used only via
/// <c>s = s + str</c>/<c>s += str</c> (statement position), <c>s.length</c>, and
/// <c>s.charCodeAt(i)</c>, is compiled to a <c>StringBuilder</c> slot (O(n²) String.Concat → O(n) Append).
///
/// These run against BOTH the interpreter and the compiler. The positive cases exercise the
/// promoted fast paths (append / length / charCodeAt); the non-promotable cases must fall back to
/// the general string path and still produce correct results — interpreter/compiled parity must
/// hold whether or not promotion fires. A wrong escape rule or a wrong fast-path lowering would
/// surface here as a compiled-mode mismatch.
/// </summary>
public class StringAccumulatorPromotionTests
{
    // ── Positive cases: promotable shapes ──────────────────────────────────

    [Theory, ModeData]
    public void Promoted_BuildThenScan_CharCodeSum(ExecutionMode mode)
    {
        // The stringWork shape: append in a loop, then sweep length + charCodeAt.
        var source = """
            function stringWork(n: number): number {
                let s: string = "";
                for (let i: number = 0; i < n; i++) { s = s + "ab"; }
                let sum: number = 0;
                for (let i: number = 0; i < s.length; i++) { sum = sum + s.charCodeAt(i); }
                return sum;
            }
            console.log(stringWork(3));
            """;

        // "ababab": 3×('a'97 + 'b'98) = 3×195 = 585
        Assert.Equal("585\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_CompoundAppend_Length(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                let s: string = "x";
                for (let i: number = 0; i < 3; i++) { s += "y"; }
                return s.length;
            }
            console.log(f());
            """;

        // "xyyy" → length 4
        Assert.Equal("4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_CharCodeAt_OutOfRange_IsNaN(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                let s: string = "";
                s = s + "AB";
                return s.charCodeAt(5);
            }
            console.log(f());
            """;

        // charCodeAt past the end → NaN (JS semantics)
        Assert.Equal("NaN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_PerScope_NameCollisionDoesNotPoison(ExecutionMode mode)
    {
        // `s` in build() is a clean append-only accumulator and must promote even though an
        // unrelated, escaping `s` exists in other() — the per-function-scope candidacy guards
        // against the whole-program per-lexeme collision (e.g. perf_hooks's `const s` in a bundle).
        var source = """
            function makeName(): string { return "hi"; }
            function build(): number {
                let s: string = "";
                for (let i: number = 0; i < 4; i++) { s = s + "ab"; }
                return s.length;
            }
            function other(): string {
                let s: string = makeName();
                return s.toUpperCase();
            }
            console.log(build());
            console.log(other());
            """;

        // "abababab" → 8 ; "hi".toUpperCase() → "HI"
        Assert.Equal("8\nHI\n", TestHarness.Run(source, mode));
    }

    // ── Non-promotable cases: must fall back and stay correct ───────────────

    [Theory, ModeData]
    public void NotPromoted_Returned_StillCorrect(ExecutionMode mode)
    {
        // `return s` escapes the accumulator (a StringBuilder slot can't be returned as a string
        // without materialization) — must fall back to the general path, still correct.
        var source = """
            function f(): string {
                let s: string = "";
                for (let i: number = 0; i < 3; i++) { s = s + "z"; }
                return s;
            }
            console.log(f());
            """;

        Assert.Equal("zzz\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NotPromoted_Reassigned_StillCorrect(ExecutionMode mode)
    {
        // `s = "b"` is a non-append reassignment → disqualified.
        var source = """
            function f(): string {
                let s: string = "a";
                s = "b";
                s = s + "c";
                return s;
            }
            console.log(f());
            """;

        Assert.Equal("bc\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NotPromoted_Captured_StillCorrect(ExecutionMode mode)
    {
        // `s` is captured by a closure → routed to an object display-class field, never a
        // StringBuilder slot. The closure must observe the live value after the append.
        var source = """
            function f(): number {
                let s: string = "";
                const get = (): number => s.length;
                s = s + "abc";
                return get();
            }
            console.log(f());
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NotPromoted_NonStringAppend_StillCorrect(ExecutionMode mode)
    {
        // `s = s + i` appends a number — not statically string, so not the promotable shape;
        // must fall back to the general coercing concat path (JS ToString on the number).
        var source = """
            function f(): string {
                let s: string = "";
                for (let i: number = 0; i < 3; i++) { s = s + i; }
                return s;
            }
            console.log(f());
            """;

        Assert.Equal("012\n", TestHarness.Run(source, mode));
    }
}
