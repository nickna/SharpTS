using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Coverage for the <c>strictNullChecks</c> option on <see cref="TypeChecker"/> (issue #125).
/// Defaults ON (SharpTS's existing strict behavior); the TS conformance runner sets it OFF to
/// match the legacy corpus, where <c>null</c>/<c>undefined</c> are assignable to every type.
/// </summary>
public class StrictNullChecksTests
{
    private static int ErrorCount(string source, bool strictNullChecks)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parsed = new Parser(tokens).Parse();
        var result = new TypeChecker(strictNullChecks: strictNullChecks).CheckWithRecovery(parsed.Statements);
        return result.Diagnostics.Count(d => d.Severity == Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Strict_NullToNumber_IsError()
    {
        // Default behavior (strict): null is not assignable to number.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let x: number = null;"));
    }

    [Fact]
    public void NonStrict_NullAndUndefined_AssignableToEveryType()
    {
        Assert.Equal(0, ErrorCount("let a: number = null; let b: string = undefined; let c: boolean = null;", strictNullChecks: false));
    }

    [Fact]
    public void Strict_NullAndUndefined_ProduceErrors()
    {
        Assert.True(ErrorCount("let a: number = null; let b: string = undefined;", strictNullChecks: true) > 0);
    }

    [Fact]
    public void NonStrict_NullStillNotAssignableToNever()
    {
        Assert.True(ErrorCount("let n: never = null;", strictNullChecks: false) > 0);
    }
}
