using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #1245: in compiled mode, a <c>namespace</c> declaration combined with a
/// top-level <c>import</c> (which turns the file into an ES module) crashed at startup with a
/// <c>NullReferenceException</c> in <c>$TSNamespace.Set</c>. The multi-module entry point never
/// called <c>InitializeNamespaceFields</c>, so the namespace's backing static field was still null
/// when the module's <c>$Initialize</c> body tried to populate it. A namespace in a plain script
/// (no import) was unaffected — becoming an ES module is what broke it. The interpreter always ran
/// these correctly; each test runs both modes so the compiled path is pinned against that oracle.
/// </summary>
public class NamespaceInModuleTests
{
    [Theory, ModeData]
    public void Namespace_WithImportAfter_ExportedConst(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                namespace NS {
                  export const x = 1;
                }
                console.log(NS.x);
                import * as fs from "fs";
                console.log(typeof fs.statSync);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1\nfunction\n", output);
    }

    [Theory, ModeData]
    public void Namespace_WithImportBefore_ExportedFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from "path";
                namespace NS {
                  export function greet(): string { return "hi"; }
                }
                console.log(NS.greet());
                console.log(typeof path.join);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hi\nfunction\n", output);
    }

    [Theory, ModeData]
    public void NestedNamespace_WithImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from "path";
                namespace Outer {
                  export const a = 10;
                  export namespace Inner {
                    export const b = 42;
                  }
                }
                console.log(Outer.a);
                console.log(Outer.Inner.b);
                console.log(typeof path.join);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10\n42\nfunction\n", output);
    }

    [Theory, ModeData]
    public void Namespace_WithSiblingModuleImport_ClassAndEnumMembers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export function add(a: number, b: number): number { return a + b; }
                """,
            ["main.ts"] = """
                import { add } from "./helper";
                namespace Calc {
                  export const base = 10;
                  export function compute(): number { return add(base, 5); }
                  export enum Op { Add, Sub }
                  export class Adder { run(): number { return add(1, 2); } }
                }
                console.log(Calc.base);
                console.log(Calc.compute());
                console.log(Calc.Op.Sub);
                console.log(new Calc.Adder().run());
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10\n15\n1\n3\n", output);
    }
}
