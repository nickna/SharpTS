using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'tty' module across interpreter and compiled modes.
/// </summary>
public class TtyModuleTests
{
    [Theory, ModeData]
    public void Tty_Isatty_ReturnsBoolean(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as tty from 'tty';
                const result = tty.isatty(1);
                console.log(typeof result === 'boolean');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Tty_Isatty_InvalidFd_ReturnsFalse(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as tty from 'tty';
                console.log(tty.isatty(999));
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Tty_Isatty_TypeofIsFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as tty from 'tty';
                console.log(typeof tty.isatty);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Tty_Isatty_NamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isatty } from 'tty';
                console.log(typeof isatty);
                console.log(isatty(999));
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Tty_Isatty_Cjs_Require(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const tty = require('tty');
                console.log(typeof tty.isatty);
                console.log(tty.isatty(999));
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\nfalse\n", output);
    }
}
