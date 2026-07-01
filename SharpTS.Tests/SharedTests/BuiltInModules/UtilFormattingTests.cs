using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the util module formatting/inspection additions (issue #1100):
/// styleText, formatWithOptions, debuglog, getSystemErrorMessage, and inspect
/// option completeness. Pure-TS facade, so interpreter == compiled.
/// </summary>
public class UtilFormattingTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StyleText_SingleFormat_WrapsInAnsiCodes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { styleText } from 'util';
                const r = styleText('red', 'hi');
                console.log(r === '\x1b[31mhi\x1b[39m');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StyleText_ArrayOfFormats_Nests(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { styleText } from 'util';
                const r = styleText(['bold', 'red'], 'x');
                console.log(r === '\x1b[1m\x1b[31mx\x1b[39m\x1b[22m');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StyleText_InvalidFormat_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { styleText } from 'util';
                try {
                    styleText('notacolor', 'x');
                    console.log('no-throw');
                } catch (e: any) {
                    console.log('threw');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FormatWithOptions_InspectsObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { formatWithOptions } from 'util';
                console.log(formatWithOptions({ colors: false }, '%o', { a: 1 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("{ a: 1 }\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorMessage_ReturnsDescription(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { getSystemErrorName, getSystemErrorMessage } from 'util';
                console.log(getSystemErrorName(-2));
                console.log(getSystemErrorMessage(-2));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("ENOENT\nno such file or directory\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Debuglog_SectionOff_IsNoOp(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { debuglog } from 'util';
                const log = debuglog('sharptssection');
                log('this should not print');
                console.log('enabled:' + log.enabled);
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("enabled:false\ndone\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_DepthOption_LimitsRecursion(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { inspect } from 'util';
                console.log(inspect({ a: { b: 1 } }, { depth: 0 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("{ a: [Object] }\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_MaxArrayLength_Truncates(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { inspect } from 'util';
                console.log(inspect([1, 2, 3, 4, 5], { maxArrayLength: 2 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("[ 1, 2, ... 3 more items ]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_Colors_AppliesAnsi(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { inspect } from 'util';
                console.log(inspect(42, { colors: true }) === '\x1b[33m42\x1b[39m');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_CustomHook_ControlsOutput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { inspect } from 'util';
                const obj: any = { plain: 1 };
                obj[Symbol.for('nodejs.util.inspect.custom')] = () => 'CUSTOM!';
                console.log(inspect(obj));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("CUSTOM!\n", output);
    }
}
