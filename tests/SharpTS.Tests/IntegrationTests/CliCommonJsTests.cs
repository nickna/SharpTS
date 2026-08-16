using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests for CommonJS support in interpreter mode. Each test writes .cjs/.ts
/// files to a temp directory and runs the SharpTS CLI against them.
/// </summary>
public class CliCommonJsTests
{
    [Fact]
    public void CjsFile_ModuleExportsAndConsoleLog()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("bare.cjs", """
            console.log("hello");
            module.exports = 42;
            console.log("exports =", module.exports);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
        Assert.Contains("exports = 42", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_ObjectLiteralExports()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("obj.cjs", """
            module.exports = { a: 1, b: "two" };
            console.log(module.exports.a, module.exports.b);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 two", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_ExportsShorthand()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("shorthand.cjs", """
            exports.x = 10;
            exports.y = 20;
            exports.add = function (a, b) { return a + b; };
            console.log(exports.x, exports.y, exports.add(exports.x, exports.y));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("10 20 30", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_RequireOtherCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            function add(a, b) { return a + b; }
            module.exports = { add: add };
            """);
        var script = tempDir.CreateFile("main.cjs", """
            const util = require("./util.cjs");
            console.log(util.add(2, 3));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("5", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_CircularRequire_PartialExportsVisible()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("a.cjs", """
            exports.name = "a";
            const b = require("./b.cjs");
            exports.greet = function () { return "from a, " + b.name; };
            console.log("a done");
            """);
        var bScript = tempDir.CreateFile("b.cjs", """
            exports.name = "b";
            const a = require("./a.cjs");
            console.log("b sees a.name=" + a.name + " a.greet=" + typeof a.greet);
            """);

        // Run b.cjs as the entry — its require of a.cjs triggers the cycle.
        var result = CliTestHelper.RunCli($"\"{bScript}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        // When b runs first, b loads a, a re-requires b, sees b.name="b" (set before the require),
        // a finishes, then b sees a.name="a" and a.greet=function.
        Assert.Contains("b sees a.name=a a.greet=function", result.StandardOutput);
        Assert.Contains("a done", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_OptionalDep_ThrowsModuleNotFound()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("optional.cjs", """
            let optional = null;
            try {
              optional = require("./does-not-exist.cjs");
            } catch (e) {
              console.log("caught:", e.code);
            }
            console.log("optional:", optional);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("caught: MODULE_NOT_FOUND", result.StandardOutput);
        Assert.Contains("optional: null", result.StandardOutput);
    }

    [Fact]
    public void EsmFile_DefaultImportsCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            function add(a, b) { return a + b; }
            module.exports = { add: add };
            """);
        var script = tempDir.CreateFile("main.ts", """
            import util from "./util.cjs";
            console.log(util.add(4, 5));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("9", result.StandardOutput);
    }

    [Fact]
    public void EsmFile_NamedImportsFromCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            function add(a, b) { return a + b; }
            function mul(a, b) { return a * b; }
            module.exports = { add: add, mul: mul };
            """);
        var script = tempDir.CreateFile("main.ts", """
            import { add, mul } from "./util.cjs";
            console.log(add(2, 3), mul(2, 3));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("5 6", result.StandardOutput);
    }

    [Fact]
    public void EsmFile_NamespaceImportFromCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            function add(a, b) { return a + b; }
            module.exports = { add: add };
            """);
        var script = tempDir.CreateFile("main.ts", """
            import * as util from "./util.cjs";
            console.log(util.add(7, 8));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("15", result.StandardOutput);
    }

    [Fact]
    public void CjsFile_ModuleExportsReassignment_NewValueWins()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("lib.cjs", """
            exports.old = 1;
            module.exports = { fresh: 2 };
            """);
        var script = tempDir.CreateFile("main.cjs", """
            const lib = require("./lib.cjs");
            console.log("old =", lib.old, "fresh =", lib.fresh);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        // After `module.exports = { fresh: 2 }`, the original `exports.old = 1` is dropped
        // because the exports slot now points to a brand new object.
        Assert.Contains("old = undefined fresh = 2", result.StandardOutput);
    }
}
