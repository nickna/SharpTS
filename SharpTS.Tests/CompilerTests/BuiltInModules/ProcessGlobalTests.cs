using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the process global object.
/// </summary>
public class ProcessGlobalTests
{
    [Fact]
    public void Process_Platform_ReturnsValidPlatform()
    {
        var source = """
            const platform = process.platform;
            console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Arch_ReturnsValidArchitecture()
    {
        var source = """
            const arch = process.arch;
            console.log(arch === 'x64' || arch === 'x86' || arch === 'arm' || arch === 'arm64' || arch === 'ia32' || arch === 'unknown');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Pid_ReturnsPositiveNumber()
    {
        var source = """
            const pid = process.pid;
            console.log(typeof pid === 'number');
            console.log(pid > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_Version_ReturnsVersionString()
    {
        var source = """
            const version = process.version;
            console.log(typeof version === 'string');
            console.log(version.length > 0);
            console.log(version.includes('.'));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Process_Cwd_ReturnsCurrentDirectory()
    {
        var source = """
            const cwd = process.cwd();
            console.log(typeof cwd === 'string');
            console.log(cwd.length > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_Env_ReturnsEnvObject()
    {
        var source = """
            const env = process.env;
            console.log(typeof env === 'object');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Env_ContainsPathVariable()
    {
        // PATH is typically set on most systems (PATH on Windows/Unix, or Path on some systems)
        var source = """
            const env = process.env;
            // Check for PATH or Path (both are valid on different systems)
            const pathValue = env.PATH || env.Path;
            console.log(pathValue !== null && pathValue.length > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Argv_ReturnsArray()
    {
        var source = """
            const argv = process.argv;
            console.log(Array.isArray(argv));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Argv_ContainsElements()
    {
        var source = """
            const argv = process.argv;
            console.log(argv.length > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_MultipleProperties_WorkTogether()
    {
        var source = """
            console.log(process.platform.length > 0);
            console.log(process.arch.length > 0);
            console.log(process.pid > 0);
            console.log(process.cwd().length > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    // ============ PROCESS ENHANCEMENT TESTS ============

    [Fact]
    public void Process_Hrtime_ReturnsArray()
    {
        var source = """
            const hr = process.hrtime();
            console.log(Array.isArray(hr));
            console.log(hr.length === 2);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_Hrtime_ReturnsPositiveSeconds()
    {
        var source = """
            const hr = process.hrtime();
            console.log(hr[0] >= 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_Hrtime_ReturnsValidNanoseconds()
    {
        var source = """
            const hr = process.hrtime();
            // Nanoseconds should be 0-999999999
            console.log(hr[1] >= 0);
            console.log(hr[1] < 1000000000);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_Hrtime_WithPrevious_ReturnsDiff()
    {
        var source = """
            const start = process.hrtime();
            // Small busy loop
            let sum = 0;
            for (let i = 0; i < 10000; i++) {
                sum += i;
            }
            const diff = process.hrtime(start);
            console.log(Array.isArray(diff));
            console.log(diff.length === 2);
            // diff[0] should be >= 0 (seconds elapsed)
            console.log(diff[0] >= 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Process_Uptime_ReturnsPositiveNumber()
    {
        var source = """
            const up = process.uptime();
            console.log(typeof up === 'number');
            console.log(up >= 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_Uptime_IsSmallForNewProcess()
    {
        var source = """
            const up = process.uptime();
            // For a new process, uptime should be small (less than 60 seconds typically)
            console.log(up < 120);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_MemoryUsage_ReturnsObject()
    {
        var source = """
            const mem = process.memoryUsage();
            console.log(typeof mem === 'object');
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_MemoryUsage_HasRss()
    {
        var source = """
            const mem = process.memoryUsage();
            console.log(typeof mem.rss === 'number');
            console.log(mem.rss > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_MemoryUsage_HasHeapTotal()
    {
        var source = """
            const mem = process.memoryUsage();
            console.log(typeof mem.heapTotal === 'number');
            console.log(mem.heapTotal > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_MemoryUsage_HasHeapUsed()
    {
        var source = """
            const mem = process.memoryUsage();
            console.log(typeof mem.heapUsed === 'number');
            console.log(mem.heapUsed > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Process_MemoryUsage_HeapUsedLessThanTotal()
    {
        var source = """
            const mem = process.memoryUsage();
            console.log(mem.heapUsed <= mem.heapTotal);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Process_AllEnhancements_WorkTogether()
    {
        var source = """
            const hr = process.hrtime();
            const up = process.uptime();
            const mem = process.memoryUsage();
            console.log(hr.length === 2);
            console.log(up >= 0);
            console.log(mem.rss > 0);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }
}
