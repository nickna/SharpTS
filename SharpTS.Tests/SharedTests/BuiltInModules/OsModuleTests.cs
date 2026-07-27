using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public class OsModuleTests
{
    [Theory, ModeData]
    public void Os_Platform_ReturnsValidPlatform(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Arch_ReturnsValidArchitecture(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const arch = os.arch();
                console.log(arch === 'x64' || arch === 'x86' || arch === 'arm' || arch === 'arm64' || arch === 'ia32' || arch === 'unknown');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Hostname_ReturnsNonEmpty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const hostname = os.hostname();
                console.log(hostname.length > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Homedir_ReturnsNonEmpty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const homedir = os.homedir();
                console.log(homedir.length > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Tmpdir_ReturnsNonEmpty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const tmpdir = os.tmpdir();
                console.log(tmpdir.length > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Type_ReturnsValidType(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const osType = os.type();
                console.log(osType === 'Windows_NT' || osType === 'Linux' || osType === 'Darwin');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Release_ReturnsNonEmpty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const release = os.release();
                console.log(release.length > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Cpus_ReturnsNonEmptyArray(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const cpus = os.cpus();
                console.log(cpus.length > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Totalmem_ReturnsPositiveNumber(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const totalmem = os.totalmem();
                console.log(totalmem > 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Freemem_ReturnsPositiveNumber(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                console.log(freemem >= 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_EOL_ReturnsValidLineEnding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const eol = os.EOL;
                console.log(eol === '\n' || eol === '\r\n');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_UserInfo_ReturnsValidObject(ExecutionMode mode)
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_Freemem_IsLessThanTotalmem(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const totalmem = os.totalmem();
                console.log(freemem < totalmem);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Freemem_IsReasonableAmount(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const tenMB = 10 * 1024 * 1024;
                console.log(freemem >= tenMB);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Totalmem_IsReasonableAmount(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const totalmem = os.totalmem();
                const hundredMB = 100 * 1024 * 1024;
                console.log(totalmem >= hundredMB);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Memory_ValuesAreRealistic(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const freemem = os.freemem();
                const totalmem = os.totalmem();
                const tenMB = 10 * 1024 * 1024;
                const oneGB = 1024 * 1024 * 1024;
                console.log(freemem >= tenMB);
                console.log(totalmem >= oneGB);
                console.log(freemem < totalmem);
                const oneTB = 1024 * 1024 * 1024 * 1024;
                console.log(totalmem < oneTB);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_Cpus_HaveValidProperties(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const cpus = os.cpus();
                const cpu = cpus[0];
                console.log(typeof cpu.model === 'string');
                console.log(typeof cpu.speed === 'number');
                console.log(cpu.speed >= 0);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_Homedir_IsAbsolutePath(ExecutionMode mode)
    {
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Tmpdir_IsAbsolutePath(ExecutionMode mode)
    {
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Platform_MatchesType(ExecutionMode mode)
    {
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_Loadavg_ReturnsArrayOfThreeNumbers(ExecutionMode mode)
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_Loadavg_WindowsReturnsZeros(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                const loadavg = os.loadavg();
                if (platform === 'win32') {
                    console.log(loadavg[0] === 0 && loadavg[1] === 0 && loadavg[2] === 0);
                } else {
                    console.log(loadavg[0] >= 0 && loadavg[1] >= 0 && loadavg[2] >= 0);
                }
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Os_NetworkInterfaces_ReturnsObject(ExecutionMode mode)
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_NetworkInterfaces_IsValidObject(ExecutionMode mode)
    {
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
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Os_DefaultImport_ExposesNamespace(ExecutionMode mode)
    {
        // #66: `import os from 'node:os'` used to fail type-checking with
        // "no default export" because the facade only had named exports.
        // Runs against `node:os` specifically to pin the node-prefixed form.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import os from 'node:os';
                const platform = os.platform();
                console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin');
                console.log(typeof os.EOL === 'string');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
