using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'os' module.
/// </summary>
public class OsModuleTests
{
    [Fact]
    public void Os_Platform_ReturnsValidPlatform()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Arch_ReturnsValidArchitecture()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const arch = os.arch();
                console.log(arch === 'x64' || arch === 'x86' || arch === 'arm' || arch === 'arm64' || arch === 'ia32' || arch === 'unknown');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Hostname_ReturnsNonEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const hostname = os.hostname();
                console.log(hostname.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Homedir_ReturnsNonEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const homedir = os.homedir();
                console.log(homedir.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Tmpdir_ReturnsNonEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const tmpdir = os.tmpdir();
                console.log(tmpdir.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Type_ReturnsValidType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const osType = os.type();
                console.log(osType === 'Windows_NT' || osType === 'Linux' || osType === 'Darwin');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Release_ReturnsNonEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const release = os.release();
                console.log(release.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Cpus_ReturnsNonEmptyArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const cpus = os.cpus();
                console.log(cpus.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Totalmem_ReturnsPositiveNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const totalmem = os.totalmem();
                console.log(totalmem > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Freemem_ReturnsPositiveNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                // Free memory should be positive (or at least non-negative)
                console.log(freemem >= 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_EOL_ReturnsValidLineEnding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const eol = os.EOL;
                console.log(eol === '\n' || eol === '\r\n');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_UserInfo_ReturnsValidObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const userInfo = os.userInfo();
                console.log(userInfo.username.length > 0);
                console.log(userInfo.homedir.length > 0);
                console.log(typeof userInfo.uid === 'number');
                console.log(typeof userInfo.gid === 'number');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }
}
