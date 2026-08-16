using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Behavioral pins for the `&gt;`-of-`=&gt;` mis-scan family (#462): the retired string-based type
/// resolver's depth scanners treated the <c>&gt;</c> of an arrow <c>=&gt;</c> as a closing bracket,
/// driving nesting depth negative and losing top-level tokens in composites. The scanners are
/// gone (type-AST slice 6 — annotations resolve from parser-built nodes, where the bug class is
/// structurally impossible), but these composites stay pinned: they are exactly the shapes that
/// kept regressing, and the verdicts must hold on the node path too.
/// </summary>
public class StringTypeResolverArrowScanTests
{
    // ---- SplitTupleElements: a tuple element that is itself a function type ----

    [Fact]
    public void TupleWithFunctionElement_RejectsNonFunction()
    {
        // Without the guard, the ',' separating the two elements is read at negative depth (the '>'
        // of '() => number' having consumed a phantom bracket), so the tuple is mis-split and
        // element 0's function type is lost — a string is then wrongly accepted in slot 0.
        var source = """
            type Pair = [() => number, string];
            const bad: Pair = ["notfn", "x"];
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void TupleWithFunctionElement_AcceptsMatchingTuple()
    {
        var source = """
            type Pair = [() => number, string];
            const ok: Pair = [() => 1, "x"];
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- TryParseIndexedAccessType: indexing an inline object whose member is a function type ----

    [Fact]
    public void IndexedAccessIntoArrowMember_ResolvesToFunctionType()
    {
        // `{ a: () => number }["a"]` is `() => number`; the arrow inside the object previously drove
        // the index-bracket scan negative so the `["a"]` was never found and the whole type garbled
        // to something a bare number satisfied.
        var source = """
            type X = { a: () => number }["a"];
            const bad: X = 5;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IndexedAccessIntoArrowMember_AcceptsFunction()
    {
        var source = """
            type X = { a: () => number }["a"];
            const ok: X = () => 1;
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- conditional detection over a function-typed extends clause, via the string path ----
    // A generic alias instantiation expands through string substitution (TypeChecker.Generics.cs),
    // forcing the conditional through FindTopLevelKeyword(" extends ") / FindTopLevelChar('?') /
    // FindConditionalElseColon(':'). A top-level `=> ` in the check or extends type used to hide the
    // `extends`/`?`/`:` from those scans, so the conditional was not recognized and collapsed to `any`.

    [Fact]
    public void ConditionalFunctionExtends_StringPath_InfersReturnType()
    {
        // Ret<() => string> resolves to string; returning a number violates it.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            function f(): Ret<() => string> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConditionalFunctionExtends_StringPath_AcceptsInferredReturn()
    {
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            function f(): Ret<() => string> { return "hello"; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConditionalFunctionExtends_NonFunction_TakesFalseBranch()
    {
        // `string` is not callable, so U never binds and the conditional resolves to its false
        // branch ("no"); a number return violates that literal.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            function f(): Ret<string> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NestedConditionalWithArrows_SelectsOuterElseBranch()
    {
        // The nested conditional in the true branch contributes its own '?'/':' pair; the outer
        // else-colon scan (FindConditionalElseColon) must stay ternary-depth aware AND skip the
        // arrow's '>'. Ret<() => number> -> A=number -> (number extends string ? ...) is false ->
        // "other"; returning "s" (the inner THEN literal) must be rejected.
        var source = """
            type Ret<T> = T extends () => infer A ? (A extends string ? "s" : "other") : "no";
            function f(): Ret<() => number> { return "s"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NestedConditionalWithArrows_AcceptsSelectedBranch()
    {
        var source = """
            type Ret<T> = T extends () => infer A ? (A extends string ? "s" : "other") : "no";
            function f(): Ret<() => number> { return "other"; }
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- TryParseGenericFunctionTypeInfo: arrow inside a type-parameter constraint/default (#510) ----
    // A 7th scanner in the same family, with a different idiom (an early-break `--depth == 0` loop):
    // the '>' of a `T extends () => number` constraint closed the type-parameter list early, so the
    // remainder no longer parsed as `(...) => ...` and the whole type collapsed to `any` (a bare
    // number was then wrongly accepted). FindTopLevelChar also had to skip the arrow's '=' so the
    // constraint is not mis-split at `=>` into a phantom default.

    [Fact]
    public void GenericFunctionType_ArrowConstraint_RejectsNonFunction()
    {
        // F is a generic function type; 5 is not assignable to it. Before #510, F garbled to `any`
        // and 5 was accepted.
        var source = """
            type F = <T extends () => number>(x: T) => T;
            let bad: F = 5;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GenericFunctionType_ArrowConstraint_AcceptsGenericFunction()
    {
        var source = """
            type F = <T extends () => number>(x: T) => T;
            const ok: F = <T extends () => number>(x: T): T => x;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void GenericFunctionType_ObjectConstraint_StillRejectsNonFunction()
    {
        // Control from #510: a non-arrow constraint already resolved to a proper generic function
        // type, so 5 was correctly rejected. This must keep working.
        var source = """
            type G = <T extends { n: number }>(x: T) => T;
            let bad: G = 5;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
