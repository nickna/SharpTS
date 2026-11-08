using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExplicitEsModuleExtensionTests
{
    [Theory]
    [InlineData("main.mjs", ExecutionMode.Interpreted)]
    [InlineData("main.mjs", ExecutionMode.Compiled)]
    [InlineData("main.mts", ExecutionMode.Interpreted)]
    [InlineData("main.mts", ExecutionMode.Compiled)]
    public void ExplicitModuleExtensionsDoNotRequireImportOrExport(string file, ExecutionMode mode)
    {
        const string source = """
            function remove(value) {
                try { console.log("result", delete value.length); }
                catch (error) { console.log("caught", error instanceof TypeError); }
            }
            remove("x");
            """;

        Assert.Equal("caught true\n", TestHarness.RunModules(
            new Dictionary<string, string> { [file] = source }, file, mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void MarkerFreeDeclarationFilesKeepTheirGlobalScope(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                /// <reference path="./globals.d.mts" />
                const item: ReferencedGlobal = { count: 1 };
                console.log(item.count);
                """,
            ["globals.d.mts"] = "interface ReferencedGlobal { count: number; }",
        };

        Assert.Equal("1\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
