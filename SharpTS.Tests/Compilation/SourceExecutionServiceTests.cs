using SharpTS.Execution;
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
    }

    [Fact]
    public void Interpret_UncaughtGuestError_IsFailure()
    {
        var result = SourceExecutionService.Interpret("throw new Error('boom');");

        Assert.False(result.Success);
        Assert.Contains("boom", Assert.Single(result.Errors));
        Assert.Contains("Runtime Error", result.Output);
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
    }

    [Fact]
    public void CompileAndExecute_RuntimeError_IsFailure()
    {
        var result = SourceExecutionService.CompileAndExecute("throw new Error('compiled boom');");

        Assert.False(result.Success);
        Assert.Contains("compiled boom", Assert.Single(result.Errors));
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
