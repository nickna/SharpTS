using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Construct- and call-signature infer matching in conditional types (#316). A pattern like
/// <c>T extends new (...) =&gt; infer U ? U : ...</c> models the extends side as an object type
/// carrying a construct signature; the match must recurse into the signature so the infer binding
/// in parameter/return position resolves. Two collaborating fixes make these work: substitution
/// preserves a record's construct/call signatures (they were dropped, collapsing both sides to an
/// empty object), and a conditional's infer names are bound in scope for its true branch (the
/// reference otherwise falls back to <c>any</c>).
/// </summary>
public class ConstructSignatureInferTests
{
    [Fact]
    public void ConstructSignatureReturn_InfersInstanceType()
    {
        // U binds to the construct signature's return type; assigning a number errors (TS2322).
        var source = """
            type InstanceOf<T> = T extends new () => infer U ? U : "no-match";
            let x: InstanceOf<new () => { a: string }> = { a: "hi" };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConstructSignatureReturn_WrongAssignment_IsTypeError()
    {
        var source = """
            type InstanceOf<T> = T extends new () => infer U ? U : "no-match";
            let x: InstanceOf<new () => { a: string }> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConstructSignatureParameter_InfersParamType()
    {
        // P binds to the constructor's parameter type (number); a string assignment errors.
        var source = """
            type CtorParam<T> = T extends new (a: infer P) => any ? P : "no-match";
            let p: CtorParam<new (a: number) => object> = "str";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NonConstructable_TakesFalseBranch()
    {
        // `string` carries no construct signature, so the match fails and U never binds: the
        // conditional resolves to its false branch ("no-match"), which a number assignment violates.
        var source = """
            type InstanceOf<T> = T extends new () => infer U ? U : "no-match";
            let y: InstanceOf<string> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NonConstructable_FalseBranchValueAccepted()
    {
        var source = """
            type InstanceOf<T> = T extends new () => infer U ? U : "no-match";
            let y: InstanceOf<string> = "no-match";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void CallSignatureReturn_InfersReturnType()
    {
        // The object-literal call signature `{ (): infer U }` mirrors the construct path: U binds to
        // the return type, so a number assignment to the inferred string errors.
        var source = """
            type Ret<T> = T extends { (): infer U } ? U : "no-match";
            let r: Ret<{ (): string }> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
