using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Function-type infer matching in conditional types (#346) — the bare-function sibling of
/// <see cref="ConstructSignatureInferTests"/> (#316). A pattern like
/// <c>T extends () =&gt; infer U ? U : ...</c> resolves the infer binding in parameter/return
/// position.
///
/// The node path (a variable/return annotation) already resolved these correctly. The bug was in
/// the STRING resolver, reached by an <c>as</c>-cast target: its top-level scanners treated the
/// <c>&gt;</c> of an arrow <c>=&gt;</c> as a closing bracket, so the conditional's trailing
/// <c>? :</c> was never seen and the whole alias garbled to
/// <c>() =&gt; (any) =&gt; infer U ? U : "no"</c> instead of resolving to <c>string</c>. The fix routes
/// an <c>as</c>-cast target through the same node resolver the annotation path uses
/// (<c>Expr.TypeAssertion.TargetTypeNode</c>), bypassing the string scanner entirely.
/// </summary>
public class FunctionSignatureInferTests
{
    // ---- as-cast target (the form the bug reproduced on; exercises the node-routing fix) ----

    [Fact]
    public void AsCast_FunctionReturnInfer_ResolvesToInferredType()
    {
        // Ret<() => string> is string, so casting through it and assigning to a string is fine.
        // Before the fix the cast target garbled to a function type, which a string assignment rejects.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            const s: string = (null as any as Ret<() => string>);
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsCast_FunctionReturnInfer_WrongAssignment_IsTypeError()
    {
        // The issue's canonical repro: Ret<() => string> is string, so assigning it to a number errors.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            let b: number = (null as any as Ret<() => string>);
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsCast_FunctionParameterInfer_ResolvesToInferredType()
    {
        // Param<(a: number) => void> is number; a number assignment is accepted.
        var source = """
            type Param<T> = T extends (a: infer P) => any ? P : "no";
            const n: number = (null as any as Param<(a: number) => void>);
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsCast_NonFunction_TakesFalseBranch()
    {
        // string is not a function, so the conditional resolves to its false branch ("no"); a
        // number assignment violates it.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            let y: number = (null as any as Ret<string>);
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Satisfies_FunctionReturnInfer_ResolvesToInferredType()
    {
        // `satisfies` shares the string-resolver garble; its constraint now resolves node-first too.
        // Ret<() => string> is string, which the string literal "hello" satisfies.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            const r = ("hello" satisfies Ret<() => string>);
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Satisfies_FunctionReturnInfer_Violation_IsTypeError()
    {
        // 42 is not a string, so it does not satisfy Ret<() => string>.
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no";
            const r = (42 satisfies Ret<() => string>);
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- annotation target (node path; locks in the underlying conditional resolution) ----

    [Fact]
    public void Annotation_FunctionReturnInfer_InfersReturnType()
    {
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no-match";
            let x: Ret<() => { a: string }> = { a: "hi" };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Annotation_FunctionReturnInfer_WrongAssignment_IsTypeError()
    {
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no-match";
            let x: Ret<() => { a: string }> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Annotation_FunctionParameterAndReturn_BothInfer()
    {
        // Two infer placeholders across param and return positions resolve into a tuple [A, R];
        // the second element (R = string) rejects a number.
        var source = """
            type FP<T> = T extends (a: infer A) => infer R ? [A, R] : never;
            let m: FP<(a: number) => string> = [1, 2];
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Annotation_NonFunction_FalseBranchValueAccepted()
    {
        var source = """
            type Ret<T> = T extends () => infer U ? U : "no-match";
            let y: Ret<string> = "no-match";
            """;
        TestHarness.RunInterpreted(source);
    }
}
