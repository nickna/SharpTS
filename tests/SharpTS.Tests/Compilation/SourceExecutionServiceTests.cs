using SharpTS.Execution;
using SharpTS.Diagnostics;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Compilation;

[Collection("CompilationService")]
public class SourceExecutionServiceTests
{
    [Fact]
    public void Interpret_ValidSource_CapturesOutput()
    {
        var result = SourceExecutionService.Interpret("console.log('hello');");

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal("hello\n", Normalize(result.Output));
        Assert.True(result.ExecutionTimeMs >= 0);
        Assert.Null(result.CompileTimeMs);
        Assert.Equal(
            ["tokenize", "parse", "validateModules", "typeCheck", "prepareInterpreter", "execute"],
            result.Timings.Select(timing => timing.Name));
        Assert.All(result.Timings, timing =>
        {
            Assert.True(double.IsFinite(timing.DurationMs));
            Assert.True(timing.DurationMs >= 0);
            Assert.Equal(ExecutionPhaseTiming.CompletedStatus, timing.Status);
        });
        Assert.Equal(
            result.ExecutionTimeMs,
            (long)result.Timings.Single(timing => timing.Name == "execute").DurationMs);
    }

    [Fact]
    public void Interpret_TypeErrors_ReturnFormattedErrors()
    {
        var result = SourceExecutionService.Interpret(
            "let first: number = 'x';\nlet second: boolean = 42;");

        Assert.False(result.Success);
        Assert.Equal(2, result.Errors.Length);
        Assert.All(result.Errors, error => Assert.Contains("Type Error", error));
        Assert.Equal(0, result.ExecutionTimeMs);
        Assert.Equal(["tokenize", "parse", "validateModules", "typeCheck"], result.Timings.Select(timing => timing.Name));
        Assert.Equal(ExecutionPhaseTiming.FailedStatus, result.Timings[^1].Status);
    }

    [Fact]
    public void Interpret_UncaughtGuestError_IsFailure()
    {
        var result = SourceExecutionService.Interpret("throw new Error('boom');");

        Assert.False(result.Success);
        Assert.Contains("boom", Assert.Single(result.Errors));
        Assert.Contains("Runtime Error", result.Output);
        Assert.Equal("execute", result.Timings[^1].Name);
        Assert.Equal(ExecutionPhaseTiming.FailedStatus, result.Timings[^1].Status);
    }

    [Fact]
    public void CompileAndExecute_ValidSource_ReturnsBothTimings()
    {
        var result = SourceExecutionService.CompileAndExecute("console.log(6 * 7);");

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal("42\n", Normalize(result.Output));
        Assert.True(result.ExecutionTimeMs >= 0);
        Assert.NotNull(result.CompileTimeMs);
        Assert.True(result.CompileTimeMs >= 0);
        Assert.Equal(
            [
                "tokenize", "parse", "validateModules", "typeCheck", "analyzeDeadCode",
                "initializeCompiler", "prepareCompilation", "extractNamespaces",
                "emitRuntimeTypes", "analyzeClosures", "defineProgramStructure",
                "analyzeModuleBindings", "defineDeclarations", "collectFunctions",
                "emitFunctionBodies", "emitMethodBodies", "emitEntryPoint", "finalizeTypes",
                "serializeAssembly", "load", "execute"
            ],
            result.Timings.Select(timing => timing.Name));
        Assert.DoesNotContain(result.Timings, timing => timing.Name == "compile");
    }

    [Fact]
    public void CompileAndExecute_RuntimeError_IsFailure()
    {
        var result = SourceExecutionService.CompileAndExecute("throw new Error('compiled boom');");

        Assert.False(result.Success);
        Assert.Contains("compiled boom", Assert.Single(result.Errors));
        Assert.Equal("execute", result.Timings[^1].Name);
        Assert.Equal(ExecutionPhaseTiming.FailedStatus, result.Timings[^1].Status);
    }

    [Theory]
    [InlineData("let value = 0755;", "tokenize")]
    [InlineData("let value = ;", "parse")]
    [InlineData("let value: number = 'wrong';", "typeCheck")]
    public void Execution_AnalysisFailures_ReturnOnlyReachedPhases(string source, string failedPhase)
    {
        foreach (var result in new[]
        {
            SourceExecutionService.Interpret(source),
            SourceExecutionService.CompileAndExecute(source)
        })
        {
            Assert.False(result.Success);
            Assert.Equal(failedPhase, result.Timings[^1].Name);
            Assert.Equal(ExecutionPhaseTiming.FailedStatus, result.Timings[^1].Status);
            Assert.DoesNotContain(result.Timings, timing => timing.Name == "execute");
            Assert.All(result.Timings, timing => Assert.True(timing.DurationMs >= 0));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execution_CapsOutput(bool compile)
    {
        const int limit = 32;
        var result = compile
            ? SourceExecutionService.CompileAndExecute("console.log('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ');", limit)
            : SourceExecutionService.Interpret("console.log('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ');", limit);

        Assert.True(result.Success);
        Assert.Equal("abcdefghijkl\n[Output truncated]\n", Normalize(result.Output));
        Assert.True(result.Output.Length <= limit);
    }

    [Theory]
    [InlineData(false, "import { readFileSync } from 'fs';")]
    [InlineData(true, "import { readFileSync } from 'fs';")]
    [InlineData(false, "import fs = require('fs');")]
    [InlineData(true, "import fs = require('fs');")]
    [InlineData(false, "require('fs');")]
    [InlineData(true, "require('fs');")]
    [InlineData(false, "import('fs');")]
    [InlineData(true, "import('fs');")]
    [InlineData(false, "export { readFileSync } from 'fs';")]
    [InlineData(true, "export { readFileSync } from 'fs';")]
    public void Execution_RejectsEveryModuleLoadingForm(bool compile, string source)
    {
        var result = compile
            ? SourceExecutionService.CompileAndExecute(source)
            : SourceExecutionService.Interpret(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("Module loading is not available", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execution_HonorsTypeScriptLineDirectives(bool compile)
    {
        const string source = "// @ts-ignore\nlet value: number = 'x'; console.log('ok');";

        var result = compile
            ? SourceExecutionService.CompileAndExecute(source)
            : SourceExecutionService.Interpret(source);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal("ok\n", Normalize(result.Output));
    }

    [Fact]
    public void Execution_NeverWritesToHostConsole()
    {
        using var capture = AsyncLocalConsoleRedirector.Capture();

        SourceExecutionService.Interpret("console.log('interpreted');");
        SourceExecutionService.CompileAndExecute("console.log('compiled');");

        Assert.Equal("", capture.GetOutput());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execution_RejectsInvalidOutputLimit(bool compile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (compile)
                SourceExecutionService.CompileAndExecute("console.log(1);", 0);
            else
                SourceExecutionService.Interpret("console.log(1);", 0);
        });
    }

    [Fact]
    public void RunJson_UsesStableProtocolCasing()
    {
        var json = SourceExecutionService.RunJson("console.log(42);", "interpret", 1024.0);

        Assert.Contains("\"Success\":true", json);
        Assert.Contains("\"Output\":\"42", json);
        Assert.Contains("\"Errors\":[]", json);
        Assert.Contains("\"ExecutionTimeMs\":", json);
        Assert.Contains("\"CompileTimeMs\":null", json);
        Assert.Contains("\"Timings\":[", json);
        Assert.Contains("\"Name\":\"tokenize\"", json);
        Assert.Contains("\"Status\":\"completed\"", json);
    }

    [Fact]
    public void Result_TimingsAreNonNull_AndDeconstructionRemainsFiveValues()
    {
        var result = new SourceExecutionResult(true, "", [], 1, 2);
        var (success, output, errors, executionTime, compileTime) = result;

        Assert.True(success);
        Assert.Equal("", output);
        Assert.Empty(errors);
        Assert.Equal(1, executionTime);
        Assert.Equal(2, compileTime);
        Assert.Empty(result.Timings);
    }

    [Fact]
    public async Task CompileAndExecute_ConcurrentCalls_DoNotCrossWireOutput()
    {
        var tasks = Enumerable.Range(0, 3)
            .Select(index => Task.Run(() =>
                SourceExecutionService.CompileAndExecute($"console.log('run-{index}');")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        for (var index = 0; index < results.Length; index++)
        {
            Assert.True(results[index].Success);
            Assert.Equal($"run-{index}\n", Normalize(results[index].Output));
        }
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
