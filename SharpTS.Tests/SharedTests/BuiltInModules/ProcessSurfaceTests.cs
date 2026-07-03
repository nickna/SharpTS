using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Surface tests for the process object additions from epic #1078:
/// module===global unification (#1079), diagnostics (#1082),
/// emitWarning/umask flags (#1083), report (#1084), identity props (#1085),
/// and platform-conditional POSIX identity (#1086). These register no
/// process-level listeners, so they can run in parallel.
/// </summary>
public class ProcessSurfaceTests
{
    // ---- #1079: module facade === global ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ProcessModule_DefaultExport_IsTheGlobalProcess(ExecutionMode mode)
    {
        var source = """
            import process from 'process';
            console.log(process === (globalThis as any).process);
            console.log(typeof process.on, typeof process.emit);
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        Assert.Equal("true\nfunction function\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ProcessModule_ExitCode_SettableThroughDefaultExport(ExecutionMode mode)
    {
        var source = """
            import process from 'process';
            process.exitCode = 7;
            console.log(process.exitCode);
            process.exitCode = 0;
            console.log(process.exitCode);
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        Assert.Equal("7\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ProcessModule_NamedExports_ExposeTheFullSurface(ExecutionMode mode)
    {
        var source = """
            import { kill, cpuUsage, versions, emitWarning, umask, hrtime } from 'process';
            console.log(typeof kill, typeof cpuUsage, typeof emitWarning, typeof umask);
            console.log(versions.node);
            console.log(typeof hrtime);
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        Assert.Equal("function function function function\n24.15.0\nfunction\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_ValuePosition_IsLiveObject(ExecutionMode mode)
    {
        var source = """
            const p: any = process;
            console.log(p === process, typeof p.on, p.platform === process.platform);
            console.log(String(p));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true function true\n[object process]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_ExpandoAssignment_RoundTrips(ExecutionMode mode)
    {
        var source = """
            (process as any).myCustomFlag = 42;
            console.log((process as any).myCustomFlag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // ---- #1082: diagnostics ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_CpuUsage_ReturnsMicrosecondsAndSupportsDelta(ExecutionMode mode)
    {
        var source = """
            const first = process.cpuUsage();
            console.log(typeof first.user, typeof first.system);
            console.log(first.user >= 0 && first.system >= 0);
            const delta = process.cpuUsage(first);
            console.log(delta.user >= 0 && delta.system >= 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("number number\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_ResourceUsage_HasNodeShape(ExecutionMode mode)
    {
        var source = """
            const usage: any = process.resourceUsage();
            console.log(usage.userCPUTime >= 0, usage.systemCPUTime >= 0, usage.maxRSS > 0);
            console.log(typeof usage.minorPageFault, typeof usage.fsRead);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true true true\nnumber number\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_HrtimeBigint_ReturnsBigint(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.hrtime.bigint());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bigint\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Process_HrtimeBigint_IsMonotonic(ExecutionMode mode)
    {
        // Interpreted-only: compiled dynamic comparison of two any-typed
        // bigints routes through JsLessThan → ToNumber, which (correctly for
        // numbers, but pre-existing for bigint) refuses BigInt operands.
        var source = """
            const a = process.hrtime.bigint();
            const b = process.hrtime.bigint();
            console.log(b >= a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_MemoryUsageRss_ReturnsPositiveNumber(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.memoryUsage.rss(), process.memoryUsage.rss() > 0);
            const usage = process.memoryUsage();
            console.log(usage.rss > 0, usage.heapUsed > 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("number true\ntrue true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_MemoryQueries_ReturnNumbers(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.availableMemory(), process.availableMemory() >= 0);
            console.log(process.constrainedMemory());
            console.log(Array.isArray(process.getActiveResourcesInfo()));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("number true\n0\ntrue\n", output);
    }

    // ---- #1083: umask ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Umask_GetAndSetRoundTrip(ExecutionMode mode)
    {
        var source = """
            const current = process.umask();
            console.log(typeof current);
            const previous = process.umask(0o777);
            console.log(previous === current);
            process.umask(previous);
            console.log(process.umask() === current);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\ntrue\ntrue\n", output);
    }

    // ---- #1084: report ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Report_GetReport_HasDocumentedShape(ExecutionMode mode)
    {
        var source = """
            const report: any = process.report.getReport();
            console.log(typeof report.header, typeof report.javascriptStack);
            console.log(report.header.processId === process.pid);
            console.log(report.header.nodejsVersion === process.version);
            console.log(typeof report.resourceUsage, Array.isArray(report.nativeStack));
            console.log(typeof process.report.writeReport, process.report.signal);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object object\ntrue\ntrue\nobject true\nfunction SIGUSR2\n", output);
    }

    // ---- #1085: identity / info properties ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_IdentityProperties_HaveExpectedShapes(ExecutionMode mode)
    {
        var source = """
            console.log(typeof process.ppid, typeof process.title);
            console.log(process.title.length > 0);
            console.log(process.versions.node, typeof process.versions.sharpts);
            console.log(process.version === 'v' + process.versions.node);
            console.log(typeof process.execPath, process.execPath.length > 0);
            console.log(Array.isArray(process.execArgv), typeof process.argv0);
            console.log(process.release.name, typeof process.features, typeof process.config);
            console.log(process.debugPort);
            console.log(process.allowedNodeEnvironmentFlags.has('--anything'));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "number string\ntrue\n24.15.0 string\ntrue\nstring true\ntrue string\nnode object object\n9229\nfalse\n",
            output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Title_SetAndGetRoundTrips(ExecutionMode mode)
    {
        var source = """
            const original = process.title;
            process.title = 'sharpts-epic-1078';
            console.log(process.title);
            process.title = original;
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("sharpts-epic-1078\n", output);
    }

    // ---- #1086: POSIX identity (platform-conditional; compiled POSIX is a
    //      documented interpreter-first deferral, so interp-only) ----

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Process_PosixIdentity_MatchesPlatform(ExecutionMode mode)
    {
        var source = """
            const p: any = process;
            if (process.platform === 'win32') {
                console.log(typeof p.getuid === 'undefined');
                console.log(typeof p.setuid === 'undefined');
            } else {
                console.log(typeof p.getuid === 'function' && p.getuid() >= 0);
                console.log(typeof p.getgid === 'function' && Array.isArray(p.getgroups()));
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_SourceMapsFlag_Settable(ExecutionMode mode)
    {
        var source = """
            console.log(process.sourceMapsEnabled);
            process.setSourceMapsEnabled(true);
            console.log(process.sourceMapsEnabled);
            process.setSourceMapsEnabled(false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }
}
