using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for CommonJS-style module interop: export = and import x = require('path').
/// Note: The parsing and AST support is implemented. Full IL emission support
/// for compiled mode is in progress.
/// </summary>
public class CommonJSModuleTests
{
    #region Parsing Tests

    /// <summary>
    /// Tests that export = syntax parses correctly.
    /// </summary>
    [Fact]
    public void ExportAssignment_ParsesCorrectly()
    {
        var source = """
            const x = 42;
            export = x;
            """;

        // Should parse without errors
        var statements = TestHarness.Parse(source);
        Assert.Equal(2, statements.Count);
        var exportStmt = Assert.IsType<SharpTS.Parsing.Stmt.Export>(statements[1]);
        Assert.NotNull(exportStmt.ExportAssignment);
    }

    /// <summary>
    /// Tests that import x = require() syntax parses correctly.
    /// </summary>
    [Fact]
    public void ImportRequire_ParsesCorrectly()
    {
        var source = """
            import fs = require('fs');
            """;

        // Should parse without errors
        var statements = TestHarness.Parse(source);
        Assert.Single(statements);
        var importReq = Assert.IsType<SharpTS.Parsing.Stmt.ImportRequire>(statements[0]);
        Assert.Equal("fs", importReq.AliasName.Lexeme);
        Assert.Equal("fs", importReq.ModulePath);
        Assert.False(importReq.IsExported);
    }

    /// <summary>
    /// Tests that export import x = require() syntax parses correctly.
    /// </summary>
    [Fact]
    public void ExportImportRequire_ParsesCorrectly()
    {
        var source = """
            export import fs = require('fs');
            """;

        // Should parse without errors
        var statements = TestHarness.Parse(source);
        Assert.Single(statements);
        var importReq = Assert.IsType<SharpTS.Parsing.Stmt.ImportRequire>(statements[0]);
        Assert.Equal("fs", importReq.AliasName.Lexeme);
        Assert.Equal("fs", importReq.ModulePath);
        Assert.True(importReq.IsExported);
    }

    #endregion

    #region Compiled Module Tests

    /// <summary>
    /// Tests basic export = with a string literal.
    /// </summary>
    [Fact]
    public void ExportAssignment_StringLiteral()
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                export = "hello";
                """,
            ["./main.ts"] = """
                import MyLib = require('./lib');
                console.log(MyLib);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello\n", output);
    }

    /// <summary>
    /// Tests basic export = with an object.
    /// </summary>
    [Fact]
    public void ExportAssignment_Object()
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                const MyLibrary = {
                    version: "1.0.0"
                };
                export = MyLibrary;
                """,
            ["./main.ts"] = """
                import MyLib = require('./lib');
                console.log(MyLib.version);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("1.0.0\n", output);
    }

    /// <summary>
    /// Tests export = with a class.
    /// Note: Cross-module class static field access is a known limitation affecting both
    /// default export and export =. The class Type is exported correctly, but static
    /// field access on imported class Types doesn't work properly.
    /// </summary>
    [Fact]
    public void ExportAssignment_Class()
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                class MyLibrary {
                    static version: string = "1.0.0";
                    static greet(): string {
                        return "Hello from MyLibrary";
                    }
                }
                export = MyLibrary;
                """,
            ["./main.ts"] = """
                import MyLib = require('./lib');
                console.log(MyLib.version);
                console.log(MyLib.greet());
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("1.0.0\nHello from MyLibrary\n", output);
    }

    /// <summary>
    /// Tests export = with a function.
    /// </summary>
    [Fact]
    public void ExportAssignment_Function()
    {
        var files = new Dictionary<string, string>
        {
            ["./greet.ts"] = """
                function greet(name: string): string {
                    return "Hello, " + name + "!";
                }
                export = greet;
                """,
            ["./main.ts"] = """
                import greet = require('./greet');
                console.log(greet("World"));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Hello, World!\n", output);
    }

    /// <summary>
    /// Tests import = require() with a module using ES6 named exports.
    /// </summary>
    [Fact]
    public void ImportRequire_ES6Module()
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                export function square(n: number): number {
                    return n * n;
                }
                """,
            ["./main.ts"] = """
                import math = require('./math');
                console.log(math.PI);
                console.log(math.square(4));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("3.14159\n16\n", output);
    }

    #endregion
}
