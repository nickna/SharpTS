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

    // ============ ENHANCED MEMORY VALIDATION TESTS ============

    [Fact]
    public void Os_Freemem_IsLessThanTotalmem()
    {
        // Verifies that freemem returns a value less than totalmem
        // This catches bugs where freemem incorrectly returns totalmem
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const totalmem = os.totalmem();
                console.log(freemem < totalmem);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Freemem_IsReasonableAmount()
    {
        // Free memory should be at least 10MB on any modern system
        // This catches bugs where freemem returns 0 or near-zero values
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const tenMB = 10 * 1024 * 1024;
                console.log(freemem >= tenMB);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Totalmem_IsReasonableAmount()
    {
        // Total memory should be at least 100MB on any system running tests
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const totalmem = os.totalmem();
                const hundredMB = 100 * 1024 * 1024;
                console.log(totalmem >= hundredMB);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Memory_ValuesAreRealistic()
    {
        // Combined test: both values reasonable, freemem < totalmem, and
        // freemem is not more than 99% of totalmem (indicating actually used memory)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const totalmem = os.totalmem();
                const tenMB = 10 * 1024 * 1024;
                const oneGB = 1024 * 1024 * 1024;
                // Free memory should be at least 10MB
                console.log(freemem >= tenMB);
                // Total memory should be at least 1GB for modern CI systems
                console.log(totalmem >= oneGB);
                // Free memory must be less than total
                console.log(freemem < totalmem);
                // Memory values should be reasonable (less than 1TB)
                const oneTB = 1024 * 1024 * 1024 * 1024;
                console.log(totalmem < oneTB);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Os_Cpus_HaveValidProperties()
    {
        // Each CPU object should have model (string) and speed (number >= 0)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const cpus = os.cpus();
                // Check first CPU has valid properties
                const cpu = cpus[0];
                console.log(typeof cpu.model === 'string');
                console.log(typeof cpu.speed === 'number');
                console.log(cpu.speed >= 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ ENHANCED PATH VALIDATION TESTS ============

    [Fact]
    public void Os_Homedir_IsAbsolutePath()
    {
        // homedir should return an absolute path
        // Windows: contains ':' (e.g., C:\Users\...)
        // Unix: starts with '/' (e.g., /home/...)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const homedir = os.homedir();
                const isAbsoluteWindows = homedir.includes(':');
                const isAbsoluteUnix = homedir.startsWith('/');
                console.log(isAbsoluteWindows || isAbsoluteUnix);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Tmpdir_IsAbsolutePath()
    {
        // tmpdir should return an absolute path
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const tmpdir = os.tmpdir();
                const isAbsoluteWindows = tmpdir.includes(':');
                const isAbsoluteUnix = tmpdir.startsWith('/');
                console.log(isAbsoluteWindows || isAbsoluteUnix);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Platform_MatchesType()
    {
        // platform and type should be consistent
        // win32 -> Windows_NT, linux -> Linux, darwin -> Darwin
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                const osType = os.type();
                let consistent = false;
                if (platform === 'win32' && osType === 'Windows_NT') consistent = true;
                if (platform === 'linux' && osType === 'Linux') consistent = true;
                if (platform === 'darwin' && osType === 'Darwin') consistent = true;
                console.log(consistent);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ LOADAVG TESTS ============

    [Fact]
    public void Os_Loadavg_ReturnsArrayOfThreeNumbers()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const loadavg = os.loadavg();
                console.log(Array.isArray(loadavg));
                console.log(loadavg.length === 3);
                console.log(typeof loadavg[0] === 'number');
                console.log(typeof loadavg[1] === 'number');
                console.log(typeof loadavg[2] === 'number');
                console.log(loadavg[0] >= 0);
                console.log(loadavg[1] >= 0);
                console.log(loadavg[2] >= 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Os_Loadavg_WindowsReturnsZeros()
    {
        // On Windows, loadavg should return [0, 0, 0]
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                const loadavg = os.loadavg();
                if (platform === 'win32') {
                    console.log(loadavg[0] === 0 && loadavg[1] === 0 && loadavg[2] === 0);
                } else {
                    // On other platforms, just verify it's valid numbers
                    console.log(loadavg[0] >= 0 && loadavg[1] >= 0 && loadavg[2] >= 0);
                }
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ NETWORKINTERFACES TESTS ============

    [Fact]
    public void Os_NetworkInterfaces_ReturnsObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const interfaces = os.networkInterfaces();
                console.log(typeof interfaces === 'object');
                console.log(interfaces !== null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Os_NetworkInterfaces_IsValidObject()
    {
        // In compiled mode, networkInterfaces returns an empty object
        // This test verifies the function is callable and returns a valid object
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const interfaces = os.networkInterfaces();
                console.log(typeof interfaces === 'object');
                console.log(interfaces !== null);
                const keys = Object.keys(interfaces);
                console.log(Array.isArray(keys));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }
}
