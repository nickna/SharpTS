using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'perf_hooks' module: performance.now(), timeOrigin,
/// mark(), measure(), getEntries*(), clear*(), and PerformanceObserver.
/// </summary>
public class PerfHooksModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void PerfHooks_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as perf from 'perf_hooks';
                console.log(typeof perf === 'object');
                console.log(typeof perf.performance === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                console.log(typeof performance === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Import_PerformanceObserver(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { PerformanceObserver } from 'perf_hooks';
                console.log(typeof PerformanceObserver === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region performance.now() Tests

    [Theory, ModeData]
    public void PerfHooks_PerformanceNow_ReturnsNumber(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(typeof now === 'number');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_PerformanceNow_NonNegative(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const now = performance.now();
                console.log(now >= 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_PerformanceNow_Increasing(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_PerformanceNow_MeasuresElapsed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const start = performance.now();
                let sum = 0;
                for (let i = 0; i < 100000; i++) {
                    sum += Math.sqrt(i);
                }
                const elapsed = performance.now() - start;
                console.log(elapsed > 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region performance.timeOrigin Tests

    [Theory, ModeData]
    public void PerfHooks_TimeOrigin_ReturnsNumber(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const origin = performance.timeOrigin;
                console.log(typeof origin === 'number');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_TimeOrigin_ReasonableValue(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region performance.mark() Tests

    [Theory, ModeData]
    public void PerfHooks_Mark_CreatesEntry(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const mark = performance.mark('test-mark');
                console.log(mark.name === 'test-mark');
                console.log(mark.entryType === 'mark');
                console.log(typeof mark.startTime === 'number');
                console.log(mark.duration === 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Mark_StartTimeIncreases(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const mark1 = performance.mark('first');
                let sum = 0;
                for (let i = 0; i < 10000; i++) sum += i;
                const mark2 = performance.mark('second');
                console.log(mark2.startTime >= mark1.startTime);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Mark_AppearsInGetEntries(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('a');
                performance.mark('b');
                const entries = performance.getEntries();
                console.log(entries.length >= 2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region performance.measure() Tests

    [Theory, ModeData]
    public void PerfHooks_Measure_BetweenMarks(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('start');
                let sum = 0;
                for (let i = 0; i < 10000; i++) sum += i;
                performance.mark('end');
                const measure = performance.measure('test', 'start', 'end');
                console.log(measure.name === 'test');
                console.log(measure.entryType === 'measure');
                console.log(measure.duration >= 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Measure_FromMarkToNow(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('start');
                let sum = 0;
                for (let i = 0; i < 10000; i++) sum += i;
                const measure = performance.measure('elapsed', 'start');
                console.log(measure.name === 'elapsed');
                console.log(measure.entryType === 'measure');
                console.log(measure.duration >= 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Measure_NameOnly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const measure = performance.measure('total');
                console.log(measure.name === 'total');
                console.log(measure.entryType === 'measure');
                console.log(typeof measure.duration === 'number');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region performance.getEntries*() Tests

    [Theory, ModeData]
    public void PerfHooks_GetEntries_ReturnsAll(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('m1');
                performance.mark('m2');
                performance.measure('measure1', 'm1', 'm2');
                const entries = performance.getEntries();
                // Should have at least 3 entries (2 marks + 1 measure)
                console.log(entries.length >= 3);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_GetEntriesByName_FiltersCorrectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('target');
                performance.mark('other');
                performance.mark('target');
                const entries = performance.getEntriesByName('target');
                console.log(entries.length === 2);
                console.log(entries[0].name === 'target');
                console.log(entries[1].name === 'target');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_GetEntriesByName_WithType(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                // Use unique names to avoid interference from other tests
                performance.mark('unique-wt-mark');
                performance.measure('unique-wt-measure');
                const marks = performance.getEntriesByName('unique-wt-mark', 'mark');
                const measures = performance.getEntriesByName('unique-wt-measure', 'measure');
                console.log(marks.length === 1);
                console.log(marks[0].entryType === 'mark');
                console.log(measures.length === 1);
                console.log(measures[0].entryType === 'measure');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_GetEntriesByType_FiltersCorrectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('a');
                performance.mark('b');
                performance.measure('m1');
                const marks = performance.getEntriesByType('mark');
                const measures = performance.getEntriesByType('measure');
                console.log(marks.length >= 2);
                console.log(measures.length >= 1);
                // All returned entries have the correct type
                let allMarks = true;
                for (const e of marks) {
                    if (e.entryType !== 'mark') allMarks = false;
                }
                console.log(allMarks);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region performance.clearMarks() / clearMeasures() Tests

    [Theory, ModeData]
    public void PerfHooks_ClearMarks_RemovesAll(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('a');
                performance.mark('b');
                performance.measure('m1');
                performance.clearMarks();
                const marks = performance.getEntriesByType('mark');
                const measures = performance.getEntriesByType('measure');
                console.log(marks.length === 0);
                console.log(measures.length >= 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_ClearMarks_ByName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                // Use unique names and filter by name to avoid interference
                performance.mark('cmbn-keep');
                performance.mark('cmbn-remove');
                performance.mark('cmbn-keep');
                performance.clearMarks('cmbn-remove');
                const keepEntries = performance.getEntriesByName('cmbn-keep', 'mark');
                const removeEntries = performance.getEntriesByName('cmbn-remove', 'mark');
                console.log(keepEntries.length === 2);
                console.log(removeEntries.length === 0);
                console.log(keepEntries[0].name === 'cmbn-keep');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_ClearMeasures_RemovesAll(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                performance.mark('a');
                performance.measure('m1');
                performance.measure('m2');
                performance.clearMeasures();
                const measures = performance.getEntriesByType('measure');
                const marks = performance.getEntriesByType('mark');
                console.log(measures.length === 0);
                console.log(marks.length >= 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region PerformanceObserver Tests

    [Theory, ModeData]
    public void PerfHooks_Observer_ReceivesEntries(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance, PerformanceObserver } from 'perf_hooks';
                // Use console.log directly from callback
                const observer = new PerformanceObserver((list: any) => {
                    console.log('callback-fired');
                });
                observer.observe({ entryTypes: ['mark'] });
                performance.mark('obs-test');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("callback-fired", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Observer_Disconnect(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance, PerformanceObserver } from 'perf_hooks';
                let count = 0;
                const observer = new PerformanceObserver((list: any) => {
                    count = count + 1;
                });
                observer.observe({ entryTypes: ['mark'] });
                performance.mark('first');
                observer.disconnect();
                performance.mark('second');
                console.log(count === 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_Observer_FiltersByEntryType(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance, PerformanceObserver } from 'perf_hooks';
                let markCount = 0;
                const observer = new PerformanceObserver((list: any) => {
                    markCount = markCount + 1;
                });
                observer.observe({ entryTypes: ['measure'] });
                performance.mark('test');
                performance.measure('test-measure');
                console.log(markCount === 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Practical Usage Tests

    [Theory, ModeData]
    public void PerfHooks_MeasureFunctionDuration(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PerfHooks_MarkAndMeasureWorkflow(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';

                performance.mark('workflow-start');
                let sum = 0;
                for (let i = 0; i < 10000; i++) sum += i;
                performance.mark('step1-done');
                for (let i = 0; i < 10000; i++) sum += i;
                performance.mark('step2-done');

                performance.measure('step1', 'workflow-start', 'step1-done');
                performance.measure('step2', 'step1-done', 'step2-done');
                performance.measure('total', 'workflow-start', 'step2-done');

                const measures = performance.getEntriesByType('measure');
                console.log(measures.length === 3);

                const total = performance.getEntriesByName('total')[0];
                console.log(total.duration >= 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion
}
