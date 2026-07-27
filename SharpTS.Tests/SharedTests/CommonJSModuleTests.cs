using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for CommonJS-style module interop: export = and import x = require('path').
/// Migrated from CompilerTests to run against both interpreter and compiler.
/// </summary>
public class CommonJSModuleTests
{
    #region Parsing Tests

    [Fact]
    public void ExportAssignment_ParsesCorrectly()
    {
        var source = """
            const x = 42;
            export = x;
            """;

        var statements = TestHarness.Parse(source);
        Assert.Equal(2, statements.Count);
        var exportStmt = Assert.IsType<SharpTS.Parsing.Stmt.Export>(statements[1]);
        Assert.NotNull(exportStmt.ExportAssignment);
    }

    [Fact]
    public void ImportRequire_ParsesCorrectly()
    {
        var source = """
            import fs = require('fs');
            """;

        var statements = TestHarness.Parse(source);
        Assert.Single(statements);
        var importReq = Assert.IsType<SharpTS.Parsing.Stmt.ImportRequire>(statements[0]);
        Assert.Equal("fs", importReq.AliasName.Lexeme);
        Assert.Equal("fs", importReq.ModulePath);
        Assert.False(importReq.IsExported);
    }

    [Fact]
    public void ExportImportRequire_ParsesCorrectly()
    {
        var source = """
            export import fs = require('fs');
            """;

        var statements = TestHarness.Parse(source);
        Assert.Single(statements);
        var importReq = Assert.IsType<SharpTS.Parsing.Stmt.ImportRequire>(statements[0]);
        Assert.Equal("fs", importReq.AliasName.Lexeme);
        Assert.Equal("fs", importReq.ModulePath);
        Assert.True(importReq.IsExported);
    }

    #endregion

    #region Module Execution Tests

    [Theory, ModeData]
    public void ExportAssignment_StringLiteral(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void ExportAssignment_Object(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1.0.0\n", output);
    }

    [Theory, ModeData]
    public void ExportAssignment_Class(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1.0.0\nHello from MyLibrary\n", output);
    }

    [Theory, ModeData]
    public void ExportAssignment_Function(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory, ModeData]
    public void ImportRequire_ES6Module(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3.14159\n16\n", output);
    }

    [Theory, ModeData]
    public void ImportRequire_AliasAccessInsideFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.ts"] = """
                export const value: string = "ok";
                """,
            ["./main.ts"] = """
                import lib = require('./lib');
                function printValue() {
                    console.log(lib.value);
                }
                printValue();
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("ok\n", output);
    }

    #endregion
}
