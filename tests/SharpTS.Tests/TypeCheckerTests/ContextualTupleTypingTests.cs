using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Contextual tuple typing for array literals (a narrow slice of tsc's
/// checkExpressionWithContextualType): an array literal checked against a tuple — or a
/// union containing tuples — infers a tuple type instead of T[]. The context propagates
/// through ternary branches and groupings. Pinned by the GH39357 case in
/// assignmentCompatWithDiscriminatedUnion.
/// </summary>
public class ContextualTupleTypingTests
{
    [Fact]
    public void ArrayLiteral_AgainstTupleAnnotation_InfersTuple()
    {
        TestHarness.RunInterpreted("""
            declare const ab: "a" | "b";
            const t: ["a" | "b", number] = [ab, 1];
            """);
    }

    [Fact]
    public void ArrayLiteral_AgainstTupleUnionAnnotation_InfersTuple()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["b", number] | ["c", string];
            const a: A = ["c", ""];
            """);
    }

    [Fact]
    public void ContextPropagatesThroughTernary()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            declare const cond: boolean;
            const a: A = cond ? ["a", 1] : ["c", ""];
            """);
    }

    [Fact]
    public void ContextPropagatesThroughGrouping()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            const a: A = (["c", ""]);
            """);
    }

    [Fact]
    public void GH39357_DiscriminatedTupleUnion_WithOrNarrowing()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["b", number] | ["c", string];
            type B = "a" | "b" | "c";
            declare const b: B;
            const a: A = b === "a" || b === "b" ? [b, 1] : ["c", ""];
            """);
    }

    [Fact]
    public void WrongElementType_StillRejected()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            const a: A = ["a", "not-a-number"];
            """));
    }

    [Fact]
    public void ArityMismatch_StillRejected()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            const a: A = ["a", 1, 2];
            """));
    }

    [Fact]
    public void ContextAppliesToReturnStatements()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            declare const cond: boolean;
            function make(): A {
                return cond ? ["a", 1] : ["c", ""];
            }
            """);
    }

    [Fact]
    public void NestedTupleContext_Propagates()
    {
        TestHarness.RunInterpreted("""
            type Pair = ["p", ["q", number]];
            const p: Pair = ["p", ["q", 1]];
            """);
    }

    [Fact]
    public void NonTupleContext_KeepsArrayInference()
    {
        TestHarness.RunInterpreted("""
            const xs: number[] = [1, 2, 3];
            declare const cond: boolean;
            const ys: number[] = cond ? [1] : [2, 3];
            """);
    }

    [Fact]
    public void TupleValueFlowsAtRuntime()
    {
        // Indexing the union directly (m[0]/m[1]) distributes over the constituents
        // (#311). The discriminant and payload flow through to runtime.
        var result = TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            function make(cond: boolean): A {
                return cond ? ["a", 1] : ["c", "z"];
            }
            const m = make(true);
            console.log(m[0]);
            console.log(m[1]);
            console.log(make(false)[0]);
            console.log(make(false)[1]);
            """);
        Assert.Equal("a\n1\nc\nz\n", result);
    }

    [Fact]
    public void IndexingUnionOfTuples_DistributesOverConstituents()
    {
        // tsc accepts numeric indexing of a union of tuples when every constituent
        // supports the index, unioning the element types: m[0]: "a" | "c",
        // m[1]: number | string. The annotated slots pin the inferred element types.
        var result = TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            const m: A = ["c", "z"];
            const d: "a" | "c" = m[0];
            const v: number | string = m[1];
            console.log(d, v);
            """);
        Assert.Equal("c z\n", result);
    }

    [Fact]
    public void IndexingUnionOfTuples_DynamicIndex_UnionsAllElements()
    {
        TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            const m: A = ["a", 1];
            const i: number = 0;
            const v: string | number = m[i];
            """);
    }

    [Fact]
    public void IndexingUnionOfTuples_OutOfBounds_StillRejected()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = ["a", number] | ["c", string];
            declare const m: A;
            const v = m[5];
            """));
    }
}
