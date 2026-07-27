using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'timers' module: setTimeout, clearTimeout, setInterval, clearInterval, setImmediate, clearImmediate.
/// </summary>
[Collection("TimerTests")]
public class TimersModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void Timers_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                console.log(typeof timers === 'object');
                console.log(typeof timers.setTimeout === 'function');
                console.log(typeof timers.clearTimeout === 'function');
                console.log(typeof timers.setInterval === 'function');
                console.log(typeof timers.clearInterval === 'function');
                console.log(typeof timers.setImmediate === 'function');
                console.log(typeof timers.clearImmediate === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Timers_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout, clearTimeout } from 'timers';
                console.log(typeof setTimeout === 'function');
                console.log(typeof clearTimeout === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region setTimeout Tests

    [Theory, ModeData]
    public void Timers_SetTimeout_ReturnsHandle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                const handle = timers.setTimeout(() => {}, 100);
                console.log(typeof handle === 'object');
                console.log(handle !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Timers_SetTimeout_ExecutesCallback_Interpreted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                let executed = false;
                timers.setTimeout(() => { executed = true; }, 0);
                // Wait for callback
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timers_SetTimeout_ExecutesCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                let executed = false;
                timers.setTimeout(() => { executed = true; }, 0);
                // Wait for callback
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timers_ClearTimeout_CancelsCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                let executed = false;
                const handle = timers.setTimeout(() => { executed = true; }, 100);
                timers.clearTimeout(handle);
                // Wait longer than timeout
                let start = Date.now();
                while (Date.now() - start < 200) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region setInterval Tests

    [Theory, ModeData]
    public void Timers_SetInterval_ReturnsHandle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                const handle = timers.setInterval(() => {}, 100);
                console.log(typeof handle === 'object');
                console.log(handle !== null);
                timers.clearInterval(handle);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region setImmediate Tests

    [Theory, ModeData]
    public void Timers_SetImmediate_ReturnsHandle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                const handle = timers.setImmediate(() => {});
                console.log(typeof handle === 'object');
                console.log(handle !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Timers_SetImmediate_ExecutesCallback_Interpreted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                let executed = false;
                timers.setImmediate(() => { executed = true; });
                // Wait for callback
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Timers_SetImmediate_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                timers.setImmediate(() => { console.log('executed'); });
                // Wait for callback
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void Timers_ClearImmediate_CancelsCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                let executed = false;
                const handle = timers.setImmediate(() => { executed = true; });
                timers.clearImmediate(handle);
                // Wait to ensure callback would have fired
                let start = Date.now();
                while (Date.now() - start < 50) { }
                console.log(executed);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Trailing-Args Forwarding Tests

    // Regression for #1149: the timers facade forwards `...args` to the primitive
    // instead of hand-unrolling an arity ladder, so more than 8 trailing args now
    // survive in both interpreter and compiled modes (the old ladder capped at 8).
    [Theory, ModeData]
    public void Timers_SetTimeout_ForwardsBeyondEightArgs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers';
                let received: any[] = [];
                setTimeout((...rest: any[]) => { received = rest; }, 0,
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

    // Regression for #1149: setImmediate forwards a caller-side spread (`...payload`).
    [Theory, ModeData]
    public void Timers_SetImmediate_ForwardsCallerSpread(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setImmediate } from 'timers';
                let received: any[] = [];
                const payload = ['a', 'b', 'c'];
                setImmediate((...rest: any[]) => { received = rest; }, ...payload);
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
}
