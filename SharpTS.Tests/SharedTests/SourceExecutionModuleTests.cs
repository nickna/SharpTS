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

    [Theory, ModeData]
    public void Exports_AreFirstClassFunctions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as execution from "sharpts:execution";
                const run = execution.runSourceJson;
                const configure = execution.configureUntrustedProcess;
                const result = JSON.parse(run("console.log('first class');", "interpret", 1024));
                console.log(typeof run);
                console.log(typeof configure);
                console.log(result.Output.trim());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);

        Assert.Equal("function\nfunction\nfirst class\n", output.Replace("\r\n", "\n"));
    }

    [Theory, ModeData]
    public void CommonJsRequire_ExposesCallableFunctions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.cjs"] = """
                const execution = require("sharpts:execution");
                const result = JSON.parse(
                    execution.runSourceJson("console.log('required');", "interpret", 1024)
                );
                console.log(result.Output.trim());
                """
        };

        var output = TestHarness.RunModules(files, "./main.cjs", mode);

        Assert.Equal("required\n", output.Replace("\r\n", "\n"));
    }
}
