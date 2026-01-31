using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for process.nextTick in interpreter mode.
/// </summary>
[Collection("TimerTests")]
public class ProcessNextTickTests
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ProcessGlobal_IsFunction()
    {
        var source = """
            console.log(typeof process.nextTick === 'function');
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ReturnsUndefined()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                const result = nextTick(() => {});
                console.log(result === undefined || result === null);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
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
                nextTick((a: number, b: string, c: boolean) => {
                    receivedArgs = [a, b, c];
                }, 42, 'hello', true);
                // Wait for callback to execute
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(receivedArgs[0]);
                console.log(receivedArgs[1]);
                console.log(receivedArgs[2]);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("42\nhello\ntrue\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("3\ntrue\n", output);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NextTick_ThrowsWithoutCallback()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                try {
                    (nextTick as any)();
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }
}
