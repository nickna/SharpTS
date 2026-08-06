using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Defines a serial collection for <see cref="CompilationService"/> tests.
/// <see cref="CompilationService.Execute"/> swaps <see cref="Console.SetOut"/>
/// process-globally for the duration of a run; running these in parallel with
/// other collections would leak their console output into the captured writer
/// (and vice versa through the test suite's AsyncLocal console proxies).
/// </summary>
[CollectionDefinition("CompilationService", DisableParallelization = true)]
public class CompilationServiceCollection
{
}

[Collection("CompilationService")]
public class CompilationServiceTests
{
    // -------------------------------------------------------------------------
    // Compile
    // -------------------------------------------------------------------------

    [Fact]
    public void Compile_ValidSource_ReturnsAssemblyBytes()
    {
        var result = CompilationService.Compile("console.log(\"hello\");");

        Assert.True(result.Success);
        Assert.NotNull(result.AssemblyBytes);
        Assert.NotEmpty(result.AssemblyBytes!);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(result.CompileTimeMs >= 0);
        Assert.Equal(
            result.CompileTimeMs,
            (long)result.Timings.Single(timing => timing.Name == "compile").DurationMs);
    }

    [Fact]
    public void Compile_TypeError_ReturnsDiagnosticWithLocation()
    {
        var result = CompilationService.Compile("let x: number = \"not a number\";");

        Assert.False(result.Success);
        Assert.Null(result.AssemblyBytes);
        var error = Assert.Single(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotNull(error.Location);
        Assert.Equal(1, error.Line);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Compile_MultipleTypeErrors_AllSurface()
    {
        // Recovery mode: both independent errors must come back, not just the first.
        var result = CompilationService.Compile(
            "let a: number = \"one\";\nlet b: boolean = 42;");

        Assert.False(result.Success);
        Assert.Equal(2, result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Compile_ParseError_ReturnsDiagnosticsWithoutThrowing()
    {
        var result = CompilationService.Compile("let x = ;");

        Assert.False(result.Success);
        Assert.Null(result.AssemblyBytes);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Compile_LexError_ReturnsParseErrorDiagnosticWithLine()
    {
        // Legacy octal literal — the Lexer throws a raw Exception with "at line N"
        // in the message; the facade must convert it to a located diagnostic.
        var result = CompilationService.Compile("let x = 0755;");

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.NotNull(error.Location);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void Compile_TsIgnore_SuppressesError()
    {
        var result = CompilationService.Compile(
            "// @ts-ignore\nlet x: number = \"suppressed\";\nconsole.log(x);");

        Assert.True(result.Success);
        Assert.NotNull(result.AssemblyBytes);
    }

    [Fact]
    public void Compile_FileNameOption_StampsDiagnosticLocations()
    {
        var result = CompilationService.Compile(
            "let x: number = \"oops\";",
            new CompileOptions(FileName: "playground.ts"));

        Assert.False(result.Success);
        var error = result.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("playground.ts", error.FilePath);
    }

    [Fact]
    public void Compile_NeverWritesToConsole()
    {
        using var capture = AsyncLocalConsoleRedirector.Capture();

        CompilationService.Compile("console.log(\"ok\");");
        CompilationService.Compile("let x: number = \"type error\";");
        CompilationService.Compile("let x = ;");
        CompilationService.Compile("let x = 0755;");

        Assert.Equal("", capture.GetOutput());
    }

    [Fact]
    public void Compile_PlainProgram_HasNoSharpTSRuntimeRequirement()
    {
        var result = CompilationService.Compile("console.log(1 + 2);");

        Assert.True(result.Success);
        Assert.Empty(result.RequiredSharpTSRuntimeReasons);
    }

    // -------------------------------------------------------------------------
    // Execute
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_CapturesOutputToWriter()
    {
        // The acceptance scenario from issue #171, verbatim shape.
        var result = CompilationService.Compile(
            "console.log(\"hello from compiled\");",
            new CompileOptions(DecoratorMode.None));
        Assert.True(result.Success);

        var sw = new StringWriter();
        var run = CompilationService.Execute(result.AssemblyBytes!, sw);

        Assert.True(run.Success);
        Assert.Null(run.Error);
        Assert.True(run.ExecuteTimeMs >= 0);
        Assert.Equal(["load", "execute"], run.Timings.Select(timing => timing.Name));
        Assert.Equal(
            run.ExecuteTimeMs,
            (long)run.Timings.Single(timing => timing.Name == "execute").DurationMs);
        Assert.Equal("hello from compiled\n", sw.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void Execute_GuestThrow_ReturnsFailedRunResultWithoutThrowing()
    {
        var result = CompilationService.Compile("throw new Error(\"boom\");");
        Assert.True(result.Success);

        var sw = new StringWriter();
        var run = CompilationService.Execute(result.AssemblyBytes!, sw);

        Assert.False(run.Success);
        Assert.NotNull(run.Error);
        Assert.Contains("boom", run.Error);
    }

    [Fact]
    public void Execute_RestoresConsoleAfterRun()
    {
        var priorOut = Console.Out;
        var priorErr = Console.Error;

        var result = CompilationService.Compile("console.log(\"x\");");
        CompilationService.Execute(result.AssemblyBytes!, new StringWriter());

        Assert.Same(priorOut, Console.Out);
        Assert.Same(priorErr, Console.Error);
    }

    [Fact]
    public async Task Execute_InfiniteLoop_CancellationUnwindsCooperatively()
    {
        var result = CompilationService.Compile("let i = 0;\nwhile (true) { i++; }");
        Assert.True(result.Success);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var sw = new StringWriter();

        // WaitAsync guards the test against the cancel hook silently not working —
        // a hang here would otherwise pin the testhost until the runner's timeout.
        var run = await Task.Run(() => CompilationService.Execute(result.AssemblyBytes!, sw, cts.Token))
            .WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(run.Success);
    }

    [Fact]
    public void Execute_RepeatedCompileAndRun_NoAssemblyIdentityCollisions()
    {
        for (var i = 0; i < 5; i++)
        {
            var result = CompilationService.Compile($"console.log({i});");
            Assert.True(result.Success);

            var sw = new StringWriter();
            var run = CompilationService.Execute(result.AssemblyBytes!, sw);

            Assert.True(run.Success);
            Assert.Equal($"{i}\n", sw.ToString().Replace("\r\n", "\n"));
        }
    }
}
