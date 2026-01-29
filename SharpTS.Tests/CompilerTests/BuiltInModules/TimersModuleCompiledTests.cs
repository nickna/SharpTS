using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'timers' module (compiled mode).
/// </summary>
[Collection("TimerTests")]
public class TimersModuleCompiledTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void Compiled_Timers_Import_Namespace()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ setTimeout TESTS ============

    [Fact]
    public void Compiled_Timers_SetTimeout_ReturnsHandle()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Timers_SetTimeout_ExecutesCallback()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }

    // ============ setInterval TESTS ============

    [Fact]
    public void Compiled_Timers_SetInterval_ReturnsHandle()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ setImmediate TESTS ============

    [Fact]
    public void Compiled_Timers_SetImmediate_ReturnsHandle()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Timers_SetImmediate_ExecutesCallback()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("executed", output);
        Assert.Contains("done", output);
    }
}
