using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Pins the compiled nullish-equality fast path in <c>ILEmitter.EmitEqualityBinary</c>: when exactly
/// one operand is the statically-typed <c>null</c> or <c>undefined</c> literal, the comparison is a
/// direct reference check rather than boxing the value operand and dispatching
/// <c>Object.Equals</c>/<c>runtime.Equals</c>. In compiled output JS <c>null</c> is CLR null and JS
/// <c>undefined</c> is the <c>$Undefined</c> singleton, so <c>x === null</c> is <c>ldnull; ceq</c>,
/// <c>x === undefined</c> is a singleton reference compare, and loose <c>== null</c>/<c>== undefined</c>
/// is the nullish test (null OR undefined).
///
/// <para>Strict <c>===</c> must keep <c>null</c> and <c>undefined</c> distinct; loose <c>==</c> must
/// treat them as interchangeable; and <c>0</c>/<c>""</c>/<c>NaN</c>/objects must never be nullish.
/// Values are produced at runtime so the constant folder can't pre-evaluate the comparison.</para>
/// </summary>
public class NullishEqualityFastPathTests
{
    [Theory, ModeData]
    public void StrictNull_DistinguishesUndefined(ExecutionMode mode)
    {
        var source = """
            function probe(x: any): string {
                return (x === null) + "," + (x !== null);
            }
            let n: any = null;
            let u: any = undefined;
            let o: any = { a: 1 };
            console.log(probe(n));
            console.log(probe(u));
            console.log(probe(o));
            """;
        // null -> true,false ; undefined -> false,true ; object -> false,true
        Assert.Equal("true,false\nfalse,true\nfalse,true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StrictUndefined_DistinguishesNull(ExecutionMode mode)
    {
        var source = """
            function probe(x: any): string {
                return (x === undefined) + "," + (x !== undefined);
            }
            let n: any = null;
            let u: any = undefined;
            console.log(probe(n));
            console.log(probe(u));
            console.log(probe(5));
            """;
        // null -> false,true ; undefined -> true,false ; 5 -> false,true
        Assert.Equal("false,true\ntrue,false\nfalse,true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LooseNullish_NullAndUndefinedInterchangeable(ExecutionMode mode)
    {
        var source = """
            function probe(x: any): string {
                return (x == null) + "," + (x != null) + "," + (x == undefined);
            }
            let n: any = null;
            let u: any = undefined;
            console.log(probe(n));
            console.log(probe(u));
            console.log(probe(0));
            console.log(probe(""));
            console.log(probe({ }));
            """;
        // null/undefined -> true,false,true ; 0,"",{} are NOT nullish -> false,true,false
        Assert.Equal(
            "true,false,true\ntrue,false,true\nfalse,true,false\nfalse,true,false\nfalse,true,false\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NaN_IsNotNullish(ExecutionMode mode)
    {
        // A NaN value operand must compare unequal to null/undefined (and the fast path
        // needs no NaN special-casing because null/undefined are never NaN).
        var source = """
            const x: any = Math.sqrt(-1);
            console.log(x === null, x === undefined, x == null);
            """;
        Assert.Equal("false false false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullVsUndefinedLiterals(ExecutionMode mode)
    {
        // Both-literal comparisons (handled by the fall-through boxed path, not the fast
        // path) must still be exactly right.
        var source = """
            console.log(null === null, undefined === undefined);
            console.log(null === undefined, null !== undefined);
            console.log(null == undefined, null != undefined);
            """;
        Assert.Equal("true true\nfalse true\ntrue false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReversedOperandOrder(ExecutionMode mode)
    {
        // The literal may be on either side.
        var source = """
            let o: any = { };
            let n: any = null;
            console.log(null === o, undefined === o, null == n);
            """;
        Assert.Equal("false false true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LooseEquality_RuntimeFallbackOnlyCoercesObjectAgainstPrimitive(ExecutionMode mode)
    {
        // Keep both operands behind function parameters so compiled mode must use
        // $Runtime.Equals instead of the literal-nullish fast path.
        var source = """
            function equal(left: any, right: any): boolean {
                return left == right;
            }
            function notEqual(left: any, right: any): boolean {
                return left != right;
            }
            function unexpectedCoercion(): any {
                throw new Error("unexpected coercion");
            }

            const objectValue: any = {
                [Symbol.toPrimitive]: unexpectedCoercion,
                valueOf: unexpectedCoercion,
                toString: unexpectedCoercion
            };
            const otherObject: any = {
                [Symbol.toPrimitive]: unexpectedCoercion,
                valueOf: unexpectedCoercion,
                toString: unexpectedCoercion
            };

            console.log(
                equal(objectValue, null), notEqual(objectValue, null),
                equal(null, objectValue), notEqual(null, objectValue));
            console.log(
                equal(objectValue, undefined), notEqual(objectValue, undefined),
                equal(undefined, objectValue), notEqual(undefined, objectValue));
            console.log(equal(objectValue, objectValue), notEqual(objectValue, objectValue));
            console.log(equal(objectValue, otherObject), notEqual(objectValue, otherObject));

            const firstFunction: any = function () { };
            const secondFunction: any = function () { };
            firstFunction.valueOf = unexpectedCoercion;
            firstFunction.toString = unexpectedCoercion;
            secondFunction.valueOf = unexpectedCoercion;
            secondFunction.toString = unexpectedCoercion;
            console.log(equal(firstFunction, firstFunction), notEqual(firstFunction, firstFunction));
            console.log(equal(firstFunction, secondFunction), notEqual(firstFunction, secondFunction));
            console.log(equal(objectValue, firstFunction), notEqual(firstFunction, objectValue));

            let valueOfCalls = 0;
            const numericObject: any = {
                valueOf: function () { valueOfCalls++; return 7; },
                toString: unexpectedCoercion
            };
            console.log(equal(numericObject, 7), notEqual(7, numericObject), valueOfCalls);
            """;

        Assert.Equal(
            "false true false true\n" +
            "false true false true\n" +
            "true false\n" +
            "false true\n" +
            "true false\n" +
            "false true\n" +
            "false true\n" +
            "true false 2\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullGuardedTreeTraversal(ExecutionMode mode)
    {
        // The binary-trees idiom: `node === null` driving recursion. Exercises the fast
        // path on a real object-vs-null reference compare.
        var source = """
            type Node = { left: Node | null; right: Node | null };
            function build(depth: number): Node {
                if (depth <= 0) return { left: null, right: null };
                return { left: build(depth - 1), right: build(depth - 1) };
            }
            function count(node: Node | null): number {
                if (node === null) return 1;
                return 1 + count(node.left) + count(node.right);
            }
            console.log(count(build(5)));
            """;
        // A perfect tree of depth d has 2^(d+1)-1 internal {left,right} nodes plus the
        // null leaves counted by the base case: count = 2^(d+2)-1. For d=5 -> 127 nodes.
        Assert.Equal("127\n", TestHarness.Run(source, mode));
    }
}
