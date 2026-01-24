using SharpTS.Diagnostics;
using Xunit;

namespace SharpTS.Tests.SdkTests;

/// <summary>
/// Tests for DiagnosticReporter structured error reporting.
/// </summary>
public class DiagnosticReporterTests
{
    [Fact]
    public void ReportError_MsBuildFormat_OutputsCorrectFormat()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.TypeError, "Type mismatch", new SourceLocation("src/app.ts", 15, 10)));

        Assert.Equal("src/app.ts(15,10): error SHARPTS001: Type mismatch", output.Trim());
    }

    [Fact]
    public void ReportError_HumanFormat_OutputsCorrectFormat()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = false };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.TypeError, "Type mismatch", new SourceLocation("src/app.ts", 15, 10)));

        Assert.Equal("Type Error at src/app.ts:15:10: Type mismatch", output.Trim());
    }

    [Fact]
    public void ReportError_HumanFormat_NoFile_OutputsSimpleMessage()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = false };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.ConfigError, "Invalid configuration"));

        Assert.Equal("Config Error: Invalid configuration", output.Trim());
    }

    [Fact]
    public void ReportWarning_MsBuildFormat_OutputsWarning()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdOut(() =>
            reporter.ReportWarning(DiagnosticCode.TypeError, "Unused variable", new SourceLocation("src/app.ts", 5, 1)));

        Assert.Equal("src/app.ts(5,1): warning SHARPTS001: Unused variable", output.Trim());
    }

    [Fact]
    public void ReportWarning_HumanFormat_OutputsWarning()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = false };
        var output = CaptureStdOut(() =>
            reporter.ReportWarning(DiagnosticCode.TypeError, "Unused variable", new SourceLocation("src/app.ts", 5, 1)));

        Assert.Equal("Warning at src/app.ts:5:1: Unused variable", output.Trim());
    }

    [Fact]
    public void ReportInfo_QuietMode_SuppressesOutput()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = false, QuietMode = true };
        var output = CaptureStdOut(() =>
            reporter.ReportInfo("Compiling src/app.ts"));

        Assert.Equal(string.Empty, output.Trim());
    }

    [Fact]
    public void ReportInfo_NotQuiet_OutputsMessage()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = false, QuietMode = false };
        var output = CaptureStdOut(() =>
            reporter.ReportInfo("Compiling src/app.ts"));

        Assert.Equal("Info: Compiling src/app.ts", output.Trim());
    }

    [Fact]
    public void DiagnosticCode_General_FormatsAs000()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.General, "Unknown error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS000", output);
    }

    [Fact]
    public void DiagnosticCode_TypeError_FormatsAs001()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.TypeError, "Type error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS001", output);
    }

    [Fact]
    public void DiagnosticCode_ParseError_FormatsAs002()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.ParseError, "Parse error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS002", output);
    }

    [Fact]
    public void DiagnosticCode_ModuleError_FormatsAs003()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.ModuleError, "Module error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS003", output);
    }

    [Fact]
    public void DiagnosticCode_CompileError_FormatsAs004()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.CompileError, "Compile error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS004", output);
    }

    [Fact]
    public void DiagnosticCode_ConfigError_FormatsAs005()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var output = CaptureStdErr(() =>
            reporter.ReportError(DiagnosticCode.ConfigError, "Config error", new SourceLocation("file.ts", 1, 1)));

        Assert.Contains("SHARPTS005", output);
    }

    [Fact]
    public void StaticHelper_TypeError_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.TypeError("Type mismatch", new SourceLocation("app.ts", 10, 5));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticCode.TypeError, diagnostic.Code);
        Assert.Equal("Type mismatch", diagnostic.Message);
        Assert.Equal("app.ts", diagnostic.FilePath);
        Assert.Equal(10, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Fact]
    public void StaticHelper_ParseError_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.ParseError("Unexpected token", new SourceLocation("app.ts", 20, 15));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticCode.ParseError, diagnostic.Code);
        Assert.Equal("Unexpected token", diagnostic.Message);
    }

    [Fact]
    public void StaticHelper_ModuleError_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.ModuleError("Cannot resolve module");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticCode.ModuleError, diagnostic.Code);
        Assert.Equal("Cannot resolve module", diagnostic.Message);
    }

    [Fact]
    public void StaticHelper_CompileError_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.CompileError("IL emission failed");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticCode.CompileError, diagnostic.Code);
    }

    [Fact]
    public void StaticHelper_ConfigError_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.ConfigError("Invalid tsconfig.json");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticCode.ConfigError, diagnostic.Code);
    }

    [Fact]
    public void Report_Diagnostic_MsBuildFormat_FormatsCorrectly()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCode.TypeError,
            "Cannot assign string to number",
            new SourceLocation("src/index.ts", 42, 8)
        );

        var output = CaptureStdErr(() => reporter.Report(diagnostic));

        Assert.Equal("src/index.ts(42,8): error SHARPTS001: Cannot assign string to number", output.Trim());
    }

    [Fact]
    public void Report_Diagnostic_WithDefaults_UsesLineOneColumnOne()
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = true };
        var diagnostic = new Diagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCode.General,
            "General error"
        );

        var output = CaptureStdErr(() => reporter.Report(diagnostic));

        // Default line and column should be 1,1
        Assert.Contains("(1,1)", output);
    }

    private static string CaptureStdOut(Action action)
    {
        // Use shared console lock to prevent race conditions with parallel tests
        lock (Infrastructure.TestHarness.ConsoleLock)
        {
            var originalOut = Console.Out;
            try
            {
                using var sw = new StringWriter();
                Console.SetOut(sw);
                action();
                return sw.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    private static string CaptureStdErr(Action action)
    {
        // Use shared console lock to prevent race conditions with parallel tests
        lock (Infrastructure.TestHarness.ConsoleLock)
        {
            var originalErr = Console.Error;
            try
            {
                using var sw = new StringWriter();
                Console.SetError(sw);
                action();
                return sw.ToString();
            }
            finally
            {
                Console.SetError(originalErr);
            }
        }
    }
}
