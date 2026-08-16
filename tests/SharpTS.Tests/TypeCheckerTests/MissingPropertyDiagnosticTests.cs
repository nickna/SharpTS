using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// An assignment that fails because the source is missing a property the target requires is reported
/// with TS2741 ("Property 'X' is missing …"), distinct from a type mismatch on an existing property
/// (TS2322) — matching tsc and the TS conformance baselines.
/// </summary>
public class MissingPropertyDiagnosticTests
{
    private static Diagnostic CheckExpectingOneError(string source)
    {
        var parseResult = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess);
        var result = new TypeChecker(maxErrors: 50).CheckWithRecovery(parseResult.Statements);
        return Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void MissingRequiredProperty_ReportsTS2741()
    {
        // S has a property the source (typo'd) lacks → missing-property failure.
        var d = CheckExpectingOneError("""
            interface S { foo: string; }
            interface T { fooo: string; }
            let s: S; let t: T;
            s = t;
            """);
        Assert.Equal("TS2741", d.TsCode);
    }

    [Fact]
    public void PresentPropertyWrongType_ReportsTS2322()
    {
        // The property exists but has an incompatible type → type mismatch, not missing.
        var d = CheckExpectingOneError("""
            interface S { foo: string; }
            interface T { foo: number; }
            let s: S; let t: T;
            s = t;
            """);
        Assert.Equal("TS2322", d.TsCode);
    }

    [Fact]
    public void NonObjectSource_ReportsTS2322()
    {
        var d = CheckExpectingOneError("""
            interface S { foo: string; }
            let s: S;
            s = 5;
            """);
        Assert.Equal("TS2322", d.TsCode);
    }
}
