using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Built-in Error type references (<c>Error</c>, <c>TypeError</c>, <c>RangeError</c>, …) used as
/// annotations resolve to their structured <c>TypeInfo.Error</c> instead of degrading to <c>any</c>
/// (#528). This types member access (<c>e.message: string</c>) and makes the error types participate
/// in assignability — matching the value <c>new Error("x")</c>, which already produced TypeInfo.Error.
/// Type resolution is type-check-time, so both execution modes exercise the same logic.
/// </summary>
public class ErrorTypeReferenceTests
{
    // ----- member access is typed (the core fix) -----

    [Theory, ModeData]
    public void ErrorAnnotation_MessageIsString(ExecutionMode mode)
    {
        var source = """
            const e: Error = new Error("boom");
            const m: string = e.message;
            console.log(m);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ErrorAnnotation_MessageNotAssignableToNumber(ExecutionMode mode)
    {
        // Before #528 `e.message` was `any`, so this was wrongly accepted; now it is a TS2322.
        var source = """
            const e: Error = new Error("boom");
            const n: number = e.message;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypeErrorAnnotation_IsAlsoStructured(ExecutionMode mode)
    {
        // The fix covers every built-in error subtype, not just `Error`.
        var source = """
            const e: TypeError = new TypeError("boom");
            const n: number = e.message;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ErrorAnnotation_UnknownMemberRejected(ExecutionMode mode)
    {
        var source = """
            const e: Error = new Error("boom");
            const x = e.notARealMember;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    // ----- assignability -----

    [Theory, ModeData]
    public void Error_AcceptsSpecificErrorSubtype(ExecutionMode mode)
    {
        // A specific error is assignable to the base Error target.
        var source = """
            const e: Error = new TypeError("boom");
            console.log(e.message);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SiblingErrors_AreMutuallyAssignable(ExecutionMode mode)
    {
        // The built-in error subtypes add no members over Error (tsc models them as empty
        // `interface TypeError extends Error {}`), so they are structurally identical and mutually
        // assignable — assigning a TypeError to a RangeError-typed binding is accepted.
        var source = """
            const e: RangeError = new TypeError("boom");
            console.log(e.message);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Error_AssignableToMatchingStructuralShape(ExecutionMode mode)
    {
        // A built-in error structurally satisfies an object type spelling out its members.
        var source = """
            const o: { message: string; name: string } = new Error("boom");
            console.log(o.message);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UserSubclassOfError_AssignableToError(ExecutionMode mode)
    {
        // `class X extends Error` is a nominal subtype of Error.
        var source = """
            class HttpError extends Error {
                status = 500;
            }
            const e: Error = new HttpError("boom");
            console.log(e.message);
            """;
        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AggregateError_HasErrorsProperty(ExecutionMode mode)
    {
        var source = """
            const ag: AggregateError = new AggregateError([new Error("a")], "agg");
            console.log(ag.errors.length);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PlainError_NotAssignableToAggregateError(ExecutionMode mode)
    {
        // AggregateError adds `errors`, which a plain Error lacks — so the reverse assignment fails.
        var source = """
            const ag: AggregateError = new Error("boom");
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ErrorParameter_TypesArgument(ExecutionMode mode)
    {
        var source = """
            function describe(err: Error): string {
                return err.name + ": " + err.message;
            }
            console.log(describe(new RangeError("oops")));
            """;
        Assert.Equal("RangeError: oops\n", TestHarness.Run(source, mode));
    }
}
