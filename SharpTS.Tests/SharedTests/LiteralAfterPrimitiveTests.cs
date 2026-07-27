using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiled-mode miscompilation: empty object/array literals
/// declared after a numeric-typed local reused the stale stack-type tracker
/// (<c>StackType.Double</c>) from the prior expression, so <c>EmitBoxIfNeeded</c>
/// emitted <c>Box Double</c> on the fresh <c>Dictionary</c>/<c>List</c> reference.
/// The boxed result was a double whose bits were the reinterpreted heap
/// address, which surfaced as <c>typeof x === 'number'</c> for what should
/// have been an object/array.
/// </summary>
/// <remarks>
/// Fixed by adding <c>SetStackUnknown()</c> at the end of <c>EmitObjectLiteral</c>,
/// <c>EmitArrayLiteral</c>, and <c>EmitObjectLiteralWithAccessors</c> in
/// <c>Compilation/ILEmitter.Properties.Literals.cs</c> — matching the convention
/// already used by every other reference-leaving emitter in the base class.
/// </remarks>
public class LiteralAfterPrimitiveTests
{
    [Theory, ModeData]
    public void EmptyObject_AfterNumericConst_IsObject(ExecutionMode mode)
    {
        var source = @"
            function f(): any {
                const x = 0;
                const values = {};
                return values;
            }
            console.log(typeof f());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void EmptyObject_AfterBooleanConst_IsObject(ExecutionMode mode)
    {
        // Parallel check for the Boolean arm of the stack-type tracker.
        var source = @"
            function f(): any {
                const b = true;
                const values = {};
                return values;
            }
            console.log(typeof f());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void EmptyArray_AfterNumericConst_IsArray(ExecutionMode mode)
    {
        var source = @"
            function f(): any {
                const x = 0;
                const values = [];
                return values;
            }
            console.log(Array.isArray(f()));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void EmptyObject_AfterLetNumber_IsObject(ExecutionMode mode)
    {
        // `let i = 0;` is the pattern that surfaced this in util.parseArgs —
        // a loop counter followed by an `any`-typed empty object.
        var source = @"
            function f(): any {
                let i = 0;
                const values: any = {};
                values.x = 42;
                return values;
            }
            const r = f();
            console.log(typeof r);
            console.log(r.x);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n42\n", output);
    }

    [Theory, ModeData]
    public void EmptyObject_UsedAsMapAfterLoop_BehavesAsDictionary(ExecutionMode mode)
    {
        var source = @"
            function tally(words: string[]): any {
                const counts: any = {};
                for (let i = 0; i < words.length; i++) {
                    const w = words[i];
                    counts[w] = (counts[w] || 0) + 1;
                }
                return counts;
            }
            const r = tally(['a', 'b', 'a', 'c', 'a']);
            console.log(r.a);
            console.log(r.b);
            console.log(r.c);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n1\n", output);
    }

    [Theory, ModeData]
    public void NonEmptyObject_AfterNumericConst_StillWorks(ExecutionMode mode)
    {
        // Baseline — non-empty literals already worked; make sure we didn't break them.
        var source = @"
            function f(): any {
                const x = 0;
                const values = { a: 1, b: 2 };
                return values;
            }
            const r = f();
            console.log(typeof r);
            console.log(r.a);
            console.log(r.b);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n1\n2\n", output);
    }
}
