using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for process.nextTick in compiler mode.
/// </summary>
[Collection("TimerTests")]
public class ProcessNextTickCompiledTests
{
    [Fact]
    public void NextTick_Import_FromProcessModule()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                console.log(typeof nextTick === 'function');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ProcessGlobal_IsFunction()
    {
        var source = """
            console.log(typeof process.nextTick === 'function');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ExecutesCallback()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                let executed = false;
                nextTick(() => { executed = true; });
                // Wait for callback to execute
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ViaProcessGlobal_ExecutesCallback()
    {
        var source = """
            let executed = false;
            process.nextTick(() => { executed = true; });
            // Wait for callback
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_PassesArguments()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                let receivedArgs: any[] = [];
                nextTick((a: number, b: string) => {
                    receivedArgs = [a, b];
                }, 42, 'hello');
                // Wait for callback to execute
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(receivedArgs[0]);
                console.log(receivedArgs[1]);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("42\nhello\n", output);
    }

    [Fact]
    public void NextTick_MultipleCallbacks_AllExecute()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                const results: number[] = [];
                nextTick(() => results.push(1));
                nextTick(() => results.push(2));
                nextTick(() => results.push(3));
                // Wait for callbacks to execute
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(results.length);
                console.log(results.includes(1) && results.includes(2) && results.includes(3));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("3\ntrue\n", output);
    }
}
