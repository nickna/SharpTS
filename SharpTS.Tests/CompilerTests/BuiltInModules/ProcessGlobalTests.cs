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
}
