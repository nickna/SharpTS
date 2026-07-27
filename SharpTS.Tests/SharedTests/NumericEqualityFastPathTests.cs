using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Pins the compiled numeric-equality fast path in <c>ILEmitter.EmitEqualityBinary</c>: when both
/// operands are statically <c>number</c>, <c>===</c>/<c>!==</c>/<c>==</c>/<c>!=</c> emit a raw IEEE
/// <c>ceq</c> on doubles instead of boxing both operands, dispatching <c>Object.Equals</c>, and
/// boxing the boolean result. This mirrors the long-standing numeric relational fast path
/// (<c>&lt;</c>/<c>&gt;</c>) directly above it and was the dominant per-iteration cost in
/// comparison-heavy loops (e.g. the brainfuck benchmark's dispatch chain).
///
/// <para><c>ceq</c> matches ECMA-262 number equality exactly: NaN is never equal to anything
/// including itself (ceq → false), and +0/-0 compare equal (ceq → true). The values below are
/// produced at runtime (loops, arithmetic, parsing) so the constant folder cannot pre-evaluate the
/// comparison — the emitted fast path is what actually runs.</para>
/// </summary>
public class NumericEqualityFastPathTests
{
    [Theory, ModeData]
    public void RuntimeNaN_NeverEqual(ExecutionMode mode)
    {
        // NaN from a runtime computation (not the literal) — must never equal itself.
        var source = """
            const n: number = Math.sqrt(-1);
            const m: number = 0 / 0;
            console.log(n === n);
            console.log(n !== n);
            console.log(n === m);
            console.log(n != m);
            """;
        Assert.Equal("false\ntrue\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RuntimeSignedZero_ComparesEqual(ExecutionMode mode)
    {
        // +0 === -0 is true per ===, even though Object.is would say false.
        var source = """
            const z: number = 0;
            const neg: number = z * -1;
            console.log(z === neg);
            console.log(neg === 0);
            console.log(z !== neg);
            """;
        Assert.Equal("true\ntrue\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RuntimeInfinity_Equality(ExecutionMode mode)
    {
        var source = """
            const big: number = 1 / 0;
            const negBig: number = -1 / 0;
            console.log(big === big);
            console.log(big === negBig);
            console.log(big !== negBig);
            """;
        Assert.Equal("true\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OrdinaryNumberEquality(ExecutionMode mode)
    {
        // Variables defeat constant folding; covers strict + loose + negated forms.
        var source = """
            let a: number = 2;
            let b: number = 2;
            let c: number = 3;
            console.log(a === b);
            console.log(a === c);
            console.log(a !== c);
            console.log(a !== b);
            console.log(a == b);
            console.log(a == c);
            console.log(a != c);
            """;
        Assert.Equal("true\nfalse\ntrue\nfalse\ntrue\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DispatchChain_LikeBrainfuck(ExecutionMode mode)
    {
        // The shape the fast path was built for: a tight loop whose control flow is driven by
        // numeric === comparisons. Asserts the aggregate is correct, exercising every branch.
        var source = """
            function classify(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const c: number = i % 5;
                    if (c === 0) total = total + 1;
                    else if (c === 1) total = total + 2;
                    else if (c === 2) total = total + 3;
                    else if (c === 3) total = total + 4;
                    else if (c === 4) total = total + 5;
                }
                return total;
            }
            console.log(classify(100));
            """;
        // Each block of 5 contributes 1+2+3+4+5 = 15; 100/5 = 20 blocks -> 300.
        Assert.Equal("300\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CharCodeEquality(ExecutionMode mode)
    {
        // charCodeAt returns number; comparing it against numeric literals is the brainfuck idiom.
        var source = """
            const s: string = "AB+-";
            let acc: number = 0;
            for (let i: number = 0; i < s.length; i++) {
                const c: number = s.charCodeAt(i);
                if (c === 43) acc = acc + 100;      // '+'
                else if (c === 45) acc = acc + 10;  // '-'
                else acc = acc + 1;
            }
            console.log(acc);
            """;
        // 'A','B' -> +1 each (2), '+' -> +100, '-' -> +10  => 112.
        Assert.Equal("112\n", TestHarness.Run(source, mode));
    }
}
