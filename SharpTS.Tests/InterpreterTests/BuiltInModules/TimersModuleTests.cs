using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'timers' module (interpreter mode).
/// </summary>
[Collection("TimerTests")]
public class TimersModuleTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void Timers_Import_Namespace()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Timers_Import_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout, clearTimeout } from 'timers';
                console.log(typeof setTimeout === 'function');
                console.log(typeof clearTimeout === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ setTimeout TESTS ============

    [Fact]
    public void Timers_SetTimeout_ReturnsHandle()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Timers_SetTimeout_ExecutesCallback()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timers_ClearTimeout_CancelsCallback()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ setInterval TESTS ============

    [Fact]
    public void Timers_SetInterval_ReturnsHandle()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ setImmediate TESTS ============

    [Fact]
    public void Timers_SetImmediate_ReturnsHandle()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Timers_SetImmediate_ExecutesCallback()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Timers_ClearImmediate_CancelsCallback()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }
}
