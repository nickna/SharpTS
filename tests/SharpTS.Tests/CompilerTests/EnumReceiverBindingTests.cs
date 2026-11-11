using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EnumReceiverBindingTests
{
    [Fact]
    public void LocalConstEnumsInlineMembersFromTheLexicalDeclaration()
    {
        const string source = """
            enum E { A = 40 }
            function read() {
                const enum E { A = 5 }
                console.log(E.A, E["A"]);
                {
                    const enum E { A = "nested" }
                    console.log(E.A);
                }
                return () => E.A;
            }
            console.log(read()(), E.A);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("5 5\nnested\n5 40\n", output);
    }

    [Fact]
    public void AsyncFunctionInlinesLocalConstEnumMembersAcrossAwait()
    {
        const string source = """
            async function read() {
                const enum E { A = 5 }
                await Promise.resolve();
                console.log(E.A, E["A"]);
            }
            read();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("5 5\n", output);
    }

    [Fact]
    public void TypedParameterShadowsConstEnumForDotAndIndexReads()
    {
        const string source = """
            const enum E { A = 3 }
            function read(E: { A: number; 3: string }) { return [E.A, E["A"], E[3]].join(","); }
            console.log(read({ A: 9, 3: "local" }), E.A, E["A"]);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("9,9,local 3 3\n", output);
    }

    [Fact]
    public void UncheckedLocalAndClosureShadowConstEnumForDynamicIndices()
    {
        const string source = """
            const enum E { A = 3 }
            function read() {
                const E = { A: 8, 3: "local" };
                const key = 3;
                return () => console.log(E["A"], E[key]);
            }
            read()();
            console.log(E.A);
            """;

        Assert.Equal("8 local\n3\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void CheckedConstEnumImportRemainsAvailableOutsideShadowingScope()
    {
        var files = new Dictionary<string, string>
        {
            ["defs.ts"] = "export const enum E { A = 3 }",
            ["main.ts"] = """
                import { E } from './defs';
                function read(E: { A: number }) { return E["A"]; }
                console.log(read({ A: 9 }), E["A"], E.A);
                """,
        };

        Assert.Equal("9 3 3\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Fact]
    public void ConstEnumImportsUseTheirDeclaringBindings()
    {
        var files = new Dictionary<string, string>
        {
            ["def.ts"] = "export const enum E { A = 3 }",
            ["def-other.ts"] = "const enum E { A = 7 } export const other = E.A;",
            ["barrel.ts"] = "export { E as Public } from './def';",
            ["main.ts"] = """
                import { E, E as Alias } from './def';
                import { other } from './def-other';
                import { Public } from './barrel';
                console.log(E.A, other, Alias["A"], Public.A);
                """,
        };

        Assert.Equal("3 7 3 3\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
