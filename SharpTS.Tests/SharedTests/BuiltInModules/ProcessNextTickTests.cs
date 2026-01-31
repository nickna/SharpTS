using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for process.nextTick.
/// </summary>
[Collection("TimerTests")]
public class ProcessNextTickTests
{
    #region Import Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NextTick_Import_FromProcessModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                console.log(typeof nextTick === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NextTick_ProcessGlobal_IsFunction(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.nextTick === 'function');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Execution Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NextTick_ExecutesCallback(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NextTick_ViaProcessGlobal_ExecutesCallback(ExecutionMode mode)
    {
        var source = """
            let executed = false;
            process.nextTick(() => { executed = true; });
            // Wait for callback
            let start = Date.now();
            while (Date.now() - start < 50) { }
            console.log(executed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NextTick_ReturnsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                const result = nextTick(() => {});
                console.log(result === undefined || result === null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Arguments Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NextTick_PassesArguments_ThreeArgs(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\nhello\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void NextTick_PassesArguments_TwoArgs(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\nhello\n", output);
    }

    #endregion

    #region Multiple Callbacks Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NextTick_MultipleCallbacks_AllExecute(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("3\ntrue\n", output);
    }

    #endregion

    #region Error Handling Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NextTick_ThrowsWithoutCallback(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    #endregion
}
