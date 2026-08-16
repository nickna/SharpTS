using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-checking coverage for call/construct signatures on inline object type literals
/// (issue #122). These mirror the behaviour callable/constructable interfaces already have:
/// an object type like <c>{ (x: number): T }</c> is callable, and <c>{ new (x): T }</c> is
/// constructable, and they participate in assignability in both directions.
/// </summary>
public class ObjectLiteralSignatureTests
{
    // ---- Call signatures ------------------------------------------------

    [Fact]
    public void CallableObjectType_AcceptsMatchingFunction()
    {
        // A function whose signature matches is assignable to a callable object type.
        TestHarness.RunInterpreted("let f: { (x: number): string }; f = (x: number) => \"\" + x;");
    }

    [Fact]
    public void CallableObjectType_VoidReturn_AcceptsValueReturningFunction()
    {
        // void-returning call signature accepts a function that returns a value.
        TestHarness.RunInterpreted("let f: { (x: number): void }; f = (x: number) => \"ignored\";");
    }

    [Fact]
    public void CallableObjectType_FewerParameters_IsAccepted()
    {
        // A function taking fewer parameters than the signature is assignable.
        TestHarness.RunInterpreted("let f: { (x: number): void }; f = () => {};");
    }

    [Fact]
    public void CallableObjectType_RejectsIncompatibleParameter()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let f: { (x: number): void }; f = (x: string) => {};"));
    }

    [Fact]
    public void CallableObjectType_IsInvocable_ReturningSignatureReturnType()
    {
        // Calling a value typed as a callable object type yields the signature's return type.
        TestHarness.RunInterpreted("let f: { (x: number): string }; f = (x: number) => \"\" + x; let r: string = f(5);");
    }

    [Fact]
    public void CallableObjectType_CallResultRespectsReturnType()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let f: { (x: number): string }; f = (x: number) => \"\" + x; let n: number = f(5);"));
    }

    [Fact]
    public void CallableInterface_AssignableToFunctionVariable()
    {
        // Reverse direction: a callable interface is assignable to a function-typed target.
        TestHarness.RunInterpreted("interface T { (x: number): void } let t: T; let a: (x: number) => void; a = t;");
    }

    // ---- Construct signatures ------------------------------------------

    [Fact]
    public void ConstructableObjectType_RejectsPlainFunction()
    {
        // A plain (arrow) function is not a constructor.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let C: { new (x: number): object }; C = (x: number) => ({});"));
    }

    [Fact]
    public void ConstructableObjectType_RejectsCallableOnlySource()
    {
        // A callable-only object type is not constructable.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let C: { new (x: number): object }; let f: { (x: number): object }; C = f;"));
    }

    // ---- Generic call/construct signatures (modeling) -------------------

    [Fact]
    public void GenericConstructSignatureType_IsModeled()
    {
        // `new <T>(x: T) => T[]` previously collapsed to `{}` (the generic prefix was dropped);
        // it now type-checks as a modeled construct signature.
        TestHarness.RunInterpreted("let c: new <T>(x: T) => T[];");
    }

    [Fact]
    public void GenericCallSignatureType_IsModeled()
    {
        TestHarness.RunInterpreted("let f: <T>(x: T) => T[];");
    }

    [Fact]
    public void GenericConstructSignatureType_RetainsConstructSignature()
    {
        // A modeled generic construct signature is still constructable, so a plain function (no
        // construct signature) is not assignable to it.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let c: new <T>(x: T) => T[]; let f: (x: number) => number[]; c = f;"));
    }
}
