using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Conditional-type infer matching against a class instance whose member is a <em>method</em> —
/// <c>T extends { toJSON(): infer R } ? R : …</c> (#461). The infer-match property source previously
/// collected a class's public fields and getters but not its methods, so the extends-side field
/// <c>toJSON</c> was never found, the match failed, and the conditional silently took its false
/// branch. The repro mirrors <c>microsoft/TypeScript</c>'s <c>conditional/inferTypes1.ts</c>
/// (the <c>Jsonified</c>/<c>toJSON</c> example). Methods are surfaced only for this infer-match path,
/// not for keyof/mapped-type key domains.
/// </summary>
public class InferMatchClassMethodTests
{
    [Fact]
    public void ClassMethod_InfersReturnType_CorrectAssignmentAccepted()
    {
        // R binds to MyClass.toJSON's return type ("correct"); the matching literal is accepted.
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            declare class MyClass { toJSON(): "correct"; }
            const z: J<MyClass> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ClassMethod_WrongAssignment_IsTypeError()
    {
        // J<MyClass> is "correct" (the true branch), so the false-branch literal "no" is rejected.
        // Before the fix this was wrongly accepted because the match failed and resolved to "no".
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            declare class MyClass { toJSON(): "correct"; }
            const z: J<MyClass> = "no";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ClassMethod_InfersParameterType()
    {
        // P binds to the method's parameter type (number); a string assignment violates it.
        var source = """
            type Arg<T> = T extends { m(a: infer P): void } ? P : "no";
            declare class C { m(a: number): void; }
            const z: Arg<C> = "str";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NoSuchMethod_TakesFalseBranch()
    {
        // The class has no toJSON, so R never binds and the conditional resolves to its false
        // branch ("no"); a number assignment violates that literal.
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            declare class Other { name: string; }
            const z: J<Other> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NoSuchMethod_FalseBranchValueAccepted()
    {
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            declare class Other { name: string; }
            const z: J<Other> = "no";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void PrivateMethod_NotMatched_TakesFalseBranch()
    {
        // A private method is not part of the class's public structural shape, so the extends-side
        // public `toJSON` is not satisfied: the match fails and the false branch ("no") is taken,
        // making the "no" assignment legal.
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            declare class MyClass { private toJSON(): "correct"; }
            const z: J<MyClass> = "no";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InterfaceMethod_StillMatches()
    {
        // Interface members already included methods; this locks that the shared path keeps working
        // (R binds to "correct", so the false-branch literal "no" is rejected).
        var source = """
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            interface I { toJSON(): "correct"; }
            const z: J<I> = "no";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
