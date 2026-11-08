using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EnumReceiverBindingTests
{
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
}
