using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'perf_hooks' module (interpreter mode).
/// </summary>
public class PerfHooksModuleTests
{
    // ============ IMPORT TESTS ============

    [Fact]
    public void PerfHooks_Import_Namespace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as perf from 'perf_hooks';
                console.log(typeof perf === 'object');
                console.log(typeof perf.performance === 'object');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void PerfHooks_Import_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                console.log(typeof performance === 'object');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ performance.now() TESTS ============

    [Fact]
    public void PerfHooks_PerformanceNow_ReturnsNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(typeof now === 'number');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void PerfHooks_PerformanceNow_NonNegative()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(now >= 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void PerfHooks_PerformanceNow_Increasing()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const start = performance.now();
                // Do some work to ensure time passes
                let sum = 0;
                for (let i = 0; i < 10000; i++) {
                    sum += i;
                }
                const end = performance.now();
                console.log(end >= start);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void PerfHooks_PerformanceNow_MeasuresElapsed()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const start = performance.now();
                // Busy wait for a short period
                let sum = 0;
                for (let i = 0; i < 100000; i++) {
                    sum += Math.sqrt(i);
                }
                const elapsed = performance.now() - start;
                // Should be positive (some time passed)
                console.log(elapsed > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ performance.timeOrigin TESTS ============

    [Fact]
    public void PerfHooks_TimeOrigin_ReturnsNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const origin = performance.timeOrigin;
                console.log(typeof origin === 'number');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void PerfHooks_TimeOrigin_ReasonableValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const origin = performance.timeOrigin;
                // Should be a Unix timestamp in the past but not too far
                // 2020-01-01 = 1577836800000
                // Check it's after 2020 and before 3000
                const year2020 = 1577836800000;
                const year3000 = 32503680000000;
                console.log(origin > year2020 && origin < year3000);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ PRACTICAL USAGE TESTS ============

    [Fact]
    public void PerfHooks_MeasureFunctionDuration()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
