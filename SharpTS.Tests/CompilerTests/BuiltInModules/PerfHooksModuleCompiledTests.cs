using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'perf_hooks' module (compiled mode).
/// </summary>
public class PerfHooksModuleCompiledTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void Compiled_PerfHooks_Import_Namespace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as perf from 'perf_hooks';
                console.log(typeof perf === 'object');
                console.log(typeof perf.performance === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_PerfHooks_Import_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                console.log(typeof performance === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ performance.now() TESTS ============

    [Fact]
    public void Compiled_PerfHooks_PerformanceNow_ReturnsNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(typeof now === 'number');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_PerfHooks_PerformanceNow_NonNegative()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(now >= 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_PerfHooks_PerformanceNow_Increasing()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const start = performance.now();
                let sum = 0;
                for (let i = 0; i < 10000; i++) {
                    sum += i;
                }
                const end = performance.now();
                console.log(end >= start);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ performance.timeOrigin TESTS ============

    [Fact]
    public void Compiled_PerfHooks_TimeOrigin_ReturnsNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const origin = performance.timeOrigin;
                console.log(typeof origin === 'number');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_PerfHooks_TimeOrigin_ReasonableValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const origin = performance.timeOrigin;
                const year2020 = 1577836800000;
                const year3000 = 32503680000000;
                console.log(origin > year2020 && origin < year3000);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    // ============ PRACTICAL USAGE TESTS ============

    [Fact]
    public void Compiled_PerfHooks_MeasureFunctionDuration()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';

                function slowFunction(): number {
                    let sum = 0;
                    for (let i = 0; i < 50000; i++) {
                        sum += i * i;
                    }
                    return sum;
                }

                const start = performance.now();
                const result = slowFunction();
                const duration = performance.now() - start;

                console.log(duration >= 0);
                console.log(typeof result === 'number');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }
}
