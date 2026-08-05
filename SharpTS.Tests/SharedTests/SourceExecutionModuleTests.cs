using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Proves that trusted TypeScript hosts can consume the source-execution bridge in
/// both SharpTS execution modes without a hard SharpTS.dll metadata reference.
/// </summary>
[Collection("CompilationService")]
public class SourceExecutionModuleTests
{
    [Theory, ModeData]
    public void Interpret_IsCallableFromTypeScript(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { runSourceJson } from "sharpts:execution";
                const result = JSON.parse(runSourceJson("console.log('nested interpret');", "interpret", 1024));
                console.log(result.Success);
                console.log(result.Output.trim());
                console.log(result.Errors.length);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);

        Assert.Equal("true\nnested interpret\n0\n", output.Replace("\r\n", "\n"));
    }

    [Theory, ModeData]
    public void CompileAndExecute_IsCallableFromTypeScript(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { runSourceJson } from "sharpts:execution";
                const result = JSON.parse(runSourceJson("console.log(40 + 2);", "compile", 1024));
                console.log(result.Success);
                console.log(result.Output.trim());
                console.log(result.Errors.length);
                console.log(result.CompileTimeMs !== null);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);

        Assert.Equal("true\n42\n0\ntrue\n", output.Replace("\r\n", "\n"));
    }
}
