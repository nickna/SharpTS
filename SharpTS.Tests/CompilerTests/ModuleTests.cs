using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for module compilation to .NET IL.
/// </summary>
public class ModuleTests
{
    #region Side-Effect Imports (Basic Module Initialization)

    /// <summary>
    /// Tests that modules execute in the correct order - this is the most basic
    /// module functionality that should work.
    /// </summary>
    [Fact]
    public void SideEffectImport_ExecutesModule()
    {
        var files = new Dictionary<string, string>
        {
            ["./sideeffect.ts"] = """
                console.log("Side effect executed");
                """,
            ["./main.ts"] = """
                import './sideeffect';
                console.log("Main executed");
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Side effect executed\nMain executed\n", output);
    }

    [Fact]
    public void ExecutionOrder_MultipleModules()
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                console.log("a");
                """,
            ["./b.ts"] = """
                import './a';
                console.log("b");
                """,
            ["./main.ts"] = """
                import './b';
                console.log("main");
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("a\nb\nmain\n", output);
    }

    #endregion

    #region Named Exports and Imports

    [Fact]
    public void NamedExport_SingleVariable()
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                """,
            ["./main.ts"] = """
                import { PI } from './math';
                console.log(PI);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("3.14159\n", output);
    }

    [Fact]
    public void NamedExport_MultipleVariables()
    {
        var files = new Dictionary<string, string>
        {
            ["./constants.ts"] = """
                export const X: number = 10;
                export const Y: number = 20;
                export const Z: number = 30;
                """,
            ["./main.ts"] = """
                import { X, Y, Z } from './constants';
                console.log(X + Y + Z);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void NamedExport_Function()
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function add(a: number, b: number): number {
                    return a + b;
                }
                """,
            ["./main.ts"] = """
                import { add } from './utils';
                console.log(add(3, 4));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void NamedImport_WithAlias()
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const value: number = 42;
                """,
            ["./main.ts"] = """
                import { value as answer } from './math';
                console.log(answer);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void NamedExport_List()
    {
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                const a: number = 1;
                const b: number = 2;
                const c: number = 3;
                export { a, b, c };
                """,
            ["./main.ts"] = """
                import { a, b, c } from './values';
                console.log(a + b + c);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("6\n", output);
    }

    #endregion

    #region Default Exports and Imports

    [Fact]
    public void DefaultExport_Expression()
    {
        var files = new Dictionary<string, string>
        {
            ["./config.ts"] = """
                export default 42;
                """,
            ["./main.ts"] = """
                import config from './config';
                console.log(config);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void DefaultExport_Function()
    {
        var files = new Dictionary<string, string>
        {
            ["./greet.ts"] = """
                export default function greet(name: string): string {
                    return "Hello, " + name;
                }
                """,
            ["./main.ts"] = """
                import greet from './greet';
                console.log(greet("World"));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Hello, World\n", output);
    }

    #endregion

    #region Combined Imports

    [Fact]
    public void CombinedImport_DefaultAndNamed()
    {
        var files = new Dictionary<string, string>
        {
            ["./module.ts"] = """
                export const helper: string = "Helper";
                export default "Default";
                """,
            ["./main.ts"] = """
                import def, { helper } from './module';
                console.log(def);
                console.log(helper);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Default\nHelper\n", output);
    }

    #endregion

    #region Multi-Level Dependencies

    [Fact]
    public void MultiLevel_ThreeModules()
    {
        var files = new Dictionary<string, string>
        {
            ["./c.ts"] = """
                export const value: number = 10;
                """,
            ["./b.ts"] = """
                import { value } from './c';
                export const doubled: number = value * 2;
                """,
            ["./a.ts"] = """
                import { doubled } from './b';
                console.log(doubled);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./a.ts");
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Module Execution Order

    [Fact]
    public void ExecutionOrder_DependenciesFirst()
    {
        var files = new Dictionary<string, string>
        {
            ["./leaf.ts"] = """
                console.log("leaf");
                export const x: number = 1;
                """,
            ["./middle.ts"] = """
                import { x } from './leaf';
                console.log("middle");
                export const y: number = x + 1;
                """,
            ["./root.ts"] = """
                import { y } from './middle';
                console.log("root");
                console.log(y);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./root.ts");
        Assert.Equal("leaf\nmiddle\nroot\n2\n", output);
    }

    #endregion

    #region Re-exports

    [Fact]
    public void ReExport_Named()
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const value: number = 42;
                """,
            ["./reexporter.ts"] = """
                export { value } from './original';
                """,
            ["./main.ts"] = """
                import { value } from './reexporter';
                console.log(value);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ReExport_All()
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const A: number = 1;
                export const B: number = 2;
                """,
            ["./reexporter.ts"] = """
                export * from './original';
                """,
            ["./main.ts"] = """
                import { A, B } from './reexporter';
                console.log(A + B);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Export Enum

    [Fact]
    public void Export_Enum()
    {
        var files = new Dictionary<string, string>
        {
            ["./status.ts"] = """
                export enum Status {
                    Active,
                    Inactive,
                    Pending
                }
                """,
            ["./main.ts"] = """
                import { Status } from './status';
                console.log(Status.Active);
                console.log(Status.Pending);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("0\n2\n", output);
    }

    #endregion
}
