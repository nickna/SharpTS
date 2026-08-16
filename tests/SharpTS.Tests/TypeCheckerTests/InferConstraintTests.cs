using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// `infer U extends C` constraints (TS 4.7) from the #226 cluster: the constraint gates the
/// conditional's match, repeated same-named infers union their candidates, and the parser's
/// speculative lookahead re-reads `infer U extends T ?` as a conditional type where conditional
/// types are allowed. Declaration-site rules: TS2838 (non-identical constraints) and TS2304
/// (constraint referencing a sibling infer).
/// </summary>
public class InferConstraintTests
{
    [Fact]
    public void InferConstraint_Satisfied_TakesTrueBranch()
    {
        var source = """
            type First<T> = T extends [infer U extends string] ? U : "fell-through";
            let x: First<["a"]> = "a";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InferConstraint_Violated_TakesFalseBranch()
    {
        var source = """
            type First<T> = T extends [infer U extends string] ? U : "fell-through";
            let x: First<[1]> = "fell-through";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void RepeatedInfer_UnionsCandidates()
    {
        var source = """
            type Both<T> = T extends { a: infer U, b: infer U } ? U : never;
            let x: Both<{ a: string, b: number }> = 1;
            let y: Both<{ a: string, b: number }> = "s";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void RepeatedInfer_ConstraintAppliesToMergedUnion()
    {
        // The constraint on one declaration governs the merged inference: "a" | 1 fails
        // `extends string`, so the conditional takes its false branch.
        var source = """
            type Q<T> = T extends { a: infer U extends string, b: infer U } ? U : "no";
            let x: Q<{ a: "a", b: 1 }> = "no";
            let y: Q<{ a: "a", b: "b" }> = "a";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InferExtendsFollowedByQuestion_ParsesAsConditional_WhereAllowed()
    {
        // Inside parentheses conditional types are allowed, so `infer U extends number ? 1 : 0`
        // is a conditional whose check type is the bare `infer U` (tsc speculative lookahead) —
        // with constraint parsing this line would be a parse error ("Expect ')'"). The unbound
        // inner infer resolves the inner conditional to 0, so the outer match is false (as tsc).
        var source = """
            type X10<T> = T extends (infer U extends number ? 1 : 0) ? "matched" : "not";
            let x: X10<5> = "not";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InferExtendsFollowedByQuestion_KeepsConstraint_InExtendsClause()
    {
        // Directly in an extends clause conditional types are NOT allowed, so the `extends`
        // binds to the infer as a constraint and the `?` belongs to the outer conditional.
        var source = """
            type X13<T> = T extends infer U extends number ? U : "no";
            let x: X13<5> = 5;
            let y: X13<"s"> = "no";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void SameInfer_DifferentConstraints_IsTS2838()
    {
        var source = """
            type X1<T> = T extends { a: infer U extends string, b: infer U extends number } ? U : never;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("identical constraints", ex.Message);
    }

    [Fact]
    public void SameInfer_OneMissingConstraint_IsNotAnError()
    {
        var source = """
            type X8<T> = T extends { a: infer U extends string, b: infer U } ? U : never;
            type X9<T> = T extends { a: infer U, b: infer U extends string } ? U : never;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConstraintReferencingSiblingInfer_IsTS2304()
    {
        var source = """
            type X2<T> = T extends { a: infer U, b: infer V extends U } ? [U, V] : never;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Cannot find name 'U'", ex.Message);
    }

    [Fact]
    public void ConstraintReferencingOuterTypeParameter_IsNotAnError()
    {
        // A name that is also an outer type parameter resolves to the outer declaration.
        var source = """
            type Pick2<U, T> = T extends { a: infer V extends U } ? V : never;
            """;
        TestHarness.RunInterpreted(source);
    }
}
