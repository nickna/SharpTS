using SharpTS.Modules;
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

    [Theory, ModeData]
    public void PathReferenceKeepsMarkerFreeModuleBindingsIsolated(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                /// <reference path="./module.mts" />
                const local = 2;
                console.log(local);
                """,
            ["module.mts"] = "const local = 1; const moduleOnly = 3;",
        };

        Assert.Equal("2\n", TestHarness.RunModules(files, "main.ts", mode));

        files["main.ts"] += "\nconsole.log(moduleOnly);";
        var error = Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.RunModules(files, "main.ts", mode));
        Assert.Contains("Undefined variable 'moduleOnly'", error.Message);
    }

    [Fact]
    public void PathReferenceToMarkerFreeModuleIsTypeOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string entryPath = Path.Combine(root, "main.ts");
        string referencePath = Path.Combine(root, "module.mts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = """
                /// <reference path="./module.mts" />
                console.log("main");
                """,
            [referencePath] = "console.log(\"reference\");",
        });

        ParsedModule entry = resolver.LoadModule(entryPath);
        ParsedModule reference = Assert.Single(resolver.GetModulesInOrder(entry), module => module.Path == referencePath);
        Assert.False(reference.IsScript);
        Assert.DoesNotContain(reference, resolver.GetRuntimeModulesInOrder(entry));
    }
}
