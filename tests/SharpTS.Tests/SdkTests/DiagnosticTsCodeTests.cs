using SharpTS.Diagnostics;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SdkTests;

/// <summary>
/// Tests the TsCode field added in #95 (TypeScript conformance support).
/// TsCode flows from throw site → SharpTSException → Diagnostic, and is
/// consumed by the conformance runner to diff against tsc's *.errors.txt
/// baselines. These tests pin the contract:
///
///   - Diagnostic exposes TsCode (default null, additive change).
///   - TypeCheckException(tsCode: "TSnnnn") puts the code on its Diagnostic.
///   - Existing callers (no tsCode arg) keep working unchanged.
///   - User-facing message formats do NOT include the TS code — it's a
///     separate field that downstream tooling reads directly. (Otherwise
///     existing snapshot tests and MSBuild output would shift.)
/// </summary>
public class DiagnosticTsCodeTests
{
    [Fact]
    public void Diagnostic_DefaultsTsCodeToNull()
    {
        var d = new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TypeError, "msg");
        Assert.Null(d.TsCode);
    }

    [Fact]
    public void Diagnostic_StoresTsCodeWhenProvided()
    {
        var d = new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TypeError, "msg", TsCode: "TS2339");
        Assert.Equal("TS2339", d.TsCode);
    }

    [Fact]
    public void TypeCheckException_WithoutTsCode_ProducesNullTsCode()
    {
        // Existing call shape — no tsCode arg.
        var ex = new TypeCheckException("Property 'x' does not exist on type 'Foo'.");
        Assert.Null(ex.Diagnostic.TsCode);
    }

    [Fact]
    public void TypeCheckException_WithTsCode_FlowsToDiagnostic()
    {
        var ex = new TypeCheckException(
            "Property 'x' does not exist on type 'Foo'.",
            tsCode: "TS2339");
        Assert.Equal("TS2339", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void TypeCheckException_WithLineAndTsCode_BothPropagate()
    {
        var ex = new TypeCheckException(
            "Type 'string' is not assignable to type 'number'.",
            line: 42,
            tsCode: "TS2322");
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
        Assert.Equal(42, ex.Diagnostic.Line);
    }

    [Fact]
    public void TsCode_DoesNotLeakIntoMsBuildOutput()
    {
        // The conformance runner reads Diagnostic.TsCode directly. User-facing
        // formats stay tied to the SharpTS internal code (SHARPTS001 etc.) so
        // existing IDE integrations and snapshots don't shift.
        var d = new Diagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCode.TypeError,
            "msg",
            new SourceLocation("a.ts", 1, 1),
            TsCode: "TS2339");
        var formatted = d.ToMsBuildFormat();
        Assert.DoesNotContain("TS2339", formatted);
        Assert.Contains("SHARPTS001", formatted);
    }

    [Fact]
    public void TsCode_DoesNotLeakIntoHumanOutput()
    {
        var d = new Diagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCode.TypeError,
            "msg",
            new SourceLocation("a.ts", 1, 1),
            TsCode: "TS2339");
        var formatted = d.ToHumanFormat();
        Assert.DoesNotContain("TS2339", formatted);
    }
}
