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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    // Regression for #1149: the facade forwards `...args` to the primitive instead
    // of hand-unrolling an arity ladder, so more than 8 trailing args now survive in
    // both interpreter and compiled modes (the old ladder capped at 8).
    [Theory, ModeData]
    public void NextTick_PassesArguments_BeyondEightArgs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                let received: any[] = [];
                nextTick((...rest: any[]) => { received = rest; },
                    1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(received.length);
                console.log(received.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10\n1,2,3,4,5,6,7,8,9,10\n", output);
    }

    // Regression for #1149: a caller-side spread (`...payload`) is expanded by the
    // built-in module emitter rather than packed as a single nested-array element.
    [Theory, ModeData]
    public void NextTick_ForwardsCallerSpread(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { nextTick } from 'process';
                let received: any[] = [];
                const payload = ['a', 'b', 'c'];
                nextTick((...rest: any[]) => { received = rest; }, ...payload);
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(received.length);
                console.log(received.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("3\na,b,c\n", output);
    }

    #endregion

    #region Multiple Callbacks Tests

    [Theory, ModeData]
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

    [Theory, ModeData]
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
