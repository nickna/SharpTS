using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Member-access increment/decrement (obj.prop++, ++obj.prop, arr[i]--, --arr[i]) inside
/// async/generator bodies, exercised in both interpreter and compiler modes.
///
/// Regression guard for #357: in compiled mode the state-machine MoveNext emitter shared a base
/// implementation of EmitPrefixIncrement/EmitPostfixIncrement that only handled plain-variable
/// operands; member-access operands emitted nothing and underflowed the IL stack. The interpreter
/// was already correct, so these parity theories pin both modes to the same JS semantics.
/// </summary>
public class AsyncMemberIncrementTests
{
    [Theory, ModeData]
    public void AsyncFunction_PropertyAndIndex_AllForms(ExecutionMode mode)
    {
        var source = """
            async function go(): Promise<void> {
                const obj = { x: 1, y: 10 };
                const arr = [5, 6, 7];
                obj.x++;     // 2
                ++obj.y;     // 11
                arr[0]--;    // 4
                --arr[1];    // 5
                console.log(obj.x, obj.y, arr[0], arr[1], arr[2]);
            }
            go();
            """;

        Assert.Equal("2 11 4 5 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_ResultValueSemantics(ExecutionMode mode)
    {
        // Postfix yields the (ToNumber-coerced) original; prefix yields the new value.
        var source = """
            async function go(): Promise<void> {
                const obj = { x: 5 };
                const a = obj.x++;   // a = 5, obj.x = 6
                const b = ++obj.x;   // b = 7, obj.x = 7
                console.log(a, b, obj.x);
            }
            go();
            """;

        Assert.Equal("5 7 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_MemberIncrement(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                const o = { n: 5 };
                const arr = [1, 2];
                o.n++;
                arr[0]--;
                yield o.n;     // 6
                yield arr[0];  // 0
            }
            const g = gen();
            console.log(g.next().value, g.next().value);
            """;

        Assert.Equal("6 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_MemberIncrement(ExecutionMode mode)
    {
        var source = """
            const run = async (): Promise<void> => {
                const o = { c: 41 };
                ++o.c;
                console.log(o.c);
            };
            run();
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_MemberIncrement(ExecutionMode mode)
    {
        var source = """
            async function* agen(): AsyncGenerator<number> {
                const o = { n: 100 };
                o.n++;
                yield o.n;
            }
            async function main(): Promise<void> {
                const ag = agen();
                console.log((await ag.next()).value);
            }
            main();
            """;

        Assert.Equal("101\n", TestHarness.Run(source, mode));
    }
}
