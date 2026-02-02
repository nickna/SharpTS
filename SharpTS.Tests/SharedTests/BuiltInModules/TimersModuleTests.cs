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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Timers_SetTimeout_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // Note: Cannot use captured variable mutation due to known closure limitation.
        // Instead, directly console.log from the callback.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers';
                timers.setTimeout(() => { console.log('executed'); }, 0);
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Timers_ClearTimeout_CancelsCallback(ExecutionMode mode)
    {
        // Note: This test relies on captured variable mutation which doesn't work in compiled mode.
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Timers_SetImmediate_ExecutesCallback_Compiled(ExecutionMode mode)
    {
        // Note: Cannot use captured variable mutation due to known closure limitation.
        // Instead, directly console.log from the callback.
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Timers_ClearImmediate_CancelsCallback(ExecutionMode mode)
    {
        // Note: This test relies on captured variable mutation which doesn't work in compiled mode.
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
}
