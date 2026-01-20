using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for triple-slash path references in script files.
/// </summary>
public class TripleSlashReferenceTests
{
    #region Basic Path References

    [Fact]
    public void PathReference_FunctionFromReferencedScript()
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                function greet(name: string): string {
                    return "Hello, " + name;
                }
                """,
            ["./main.ts"] = """
                /// <reference path="./helper.ts" />
                console.log(greet("World"));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void PathReference_VariableFromReferencedScript()
    {
        var files = new Dictionary<string, string>
        {
            ["./constants.ts"] = """
                const PI: number = 3.14159;
                const E: number = 2.71828;
                """,
            ["./main.ts"] = """
                /// <reference path="./constants.ts" />
                console.log(PI);
                console.log(E);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3.14159\n2.71828\n", output);
    }

    [Fact]
    public void PathReference_ClassFromReferencedScript()
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                class Person {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                    greet(): string {
                        return "Hi, I'm " + this.name;
                    }
                }
                """,
            ["./main.ts"] = """
                /// <reference path="./person.ts" />
                let p: Person = new Person("Alice");
                console.log(p.greet());
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Hi, I'm Alice\n", output);
    }

    #endregion

    #region Multiple References

    [Fact]
    public void PathReference_MultipleReferences()
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                const valueA: number = 10;
                """,
            ["./b.ts"] = """
                const valueB: number = 20;
                """,
            ["./c.ts"] = """
                const valueC: number = 30;
                """,
            ["./main.ts"] = """
                /// <reference path="./a.ts" />
                /// <reference path="./b.ts" />
                /// <reference path="./c.ts" />
                console.log(valueA + valueB + valueC);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("60\n", output);
    }

    #endregion

    #region Nested References

    [Fact]
    public void PathReference_NestedReferences()
    {
        // A references B, B references C
        var files = new Dictionary<string, string>
        {
            ["./c.ts"] = """
                const baseValue: number = 5;
                """,
            ["./b.ts"] = """
                /// <reference path="./c.ts" />
                const doubledValue: number = baseValue * 2;
                """,
            ["./a.ts"] = """
                /// <reference path="./b.ts" />
                console.log(baseValue);
                console.log(doubledValue);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./a.ts");
        Assert.Equal("5\n10\n", output);
    }

    [Fact]
    public void PathReference_DiamondReferences()
    {
        // A references B and C, both B and C reference D
        var files = new Dictionary<string, string>
        {
            ["./d.ts"] = """
                let counter: number = 0;
                counter = counter + 1;
                console.log("d executed");
                """,
            ["./b.ts"] = """
                /// <reference path="./d.ts" />
                console.log("b executed");
                """,
            ["./c.ts"] = """
                /// <reference path="./d.ts" />
                console.log("c executed");
                """,
            ["./a.ts"] = """
                /// <reference path="./b.ts" />
                /// <reference path="./c.ts" />
                console.log("a executed");
                console.log(counter);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./a.ts");
        // d should only execute once (script caching)
        Assert.Contains("d executed\n", output);
        Assert.Equal(1, output.Split("d executed").Length - 1);
    }

    #endregion

    #region Execution Order

    [Fact]
    public void PathReference_ExecutionOrder_ReferencesFirst()
    {
        var files = new Dictionary<string, string>
        {
            ["./first.ts"] = """
                console.log("first");
                """,
            ["./second.ts"] = """
                console.log("second");
                """,
            ["./main.ts"] = """
                /// <reference path="./first.ts" />
                /// <reference path="./second.ts" />
                console.log("main");
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("first\nsecond\nmain\n", output);
    }

    #endregion

    #region Path Resolution

    [Fact]
    public void PathReference_WithoutExtension()
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                const helperValue: number = 42;
                """,
            ["./main.ts"] = """
                /// <reference path="./helper" />
                console.log(helperValue);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void PathReference_NestedDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["./lib/utils.ts"] = """
                function square(x: number): number {
                    return x * x;
                }
                """,
            ["./main.ts"] = """
                /// <reference path="./lib/utils.ts" />
                console.log(square(5));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void PathReference_NotFoundFile_ThrowsError()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                /// <reference path="./nonexistent.ts" />
                console.log("test");
                """
        };

        var ex = Assert.Throws<Exception>(() =>
            TestHarness.RunModulesInterpreted(files, "./main.ts"));
        Assert.Contains("not found", ex.Message.ToLower());
    }

    [Fact]
    public void PathReference_InModuleFile_ThrowsError()
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                const x: number = 1;
                """,
            ["./main.ts"] = """
                /// <reference path="./helper.ts" />
                export const y: number = 2;
                """
        };

        var ex = Assert.Throws<Exception>(() =>
            TestHarness.RunModulesInterpreted(files, "./main.ts"));
        Assert.Contains("script", ex.Message.ToLower());
    }

    [Fact]
    public void PathReference_ToModuleFile_ThrowsError()
    {
        var files = new Dictionary<string, string>
        {
            ["./module.ts"] = """
                export const x: number = 1;
                """,
            ["./main.ts"] = """
                /// <reference path="./module.ts" />
                console.log("test");
                """
        };

        var ex = Assert.Throws<Exception>(() =>
            TestHarness.RunModulesInterpreted(files, "./main.ts"));
        Assert.Contains("module", ex.Message.ToLower());
    }

    [Fact]
    public void PathReference_Circular_ThrowsError()
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                /// <reference path="./b.ts" />
                const a: number = 1;
                """,
            ["./b.ts"] = """
                /// <reference path="./a.ts" />
                const b: number = 2;
                """
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            TestHarness.RunModulesInterpreted(files, "./a.ts"));
        Assert.Contains("circular", ex.Message.ToLower());
    }

    #endregion

    #region Script Detection

    [Fact]
    public void ScriptDetection_FileWithNoImportExport_IsScript()
    {
        var files = new Dictionary<string, string>
        {
            ["./script.ts"] = """
                const x: number = 1;
                console.log(x);
                """
        };

        // Should work as a script file
        var output = TestHarness.RunModulesInterpreted(files, "./script.ts");
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void ScriptDetection_FileWithImport_IsModule()
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                export const x: number = 1;
                """,
            ["./main.ts"] = """
                import { x } from './helper';
                console.log(x);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n", output);
    }

    #endregion
}
