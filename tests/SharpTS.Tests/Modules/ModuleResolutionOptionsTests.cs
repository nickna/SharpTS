using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.Modules;

public class ModuleResolutionOptionsTests
{
    [Fact]
    public void PathsUseLongestPrefixAndTargetFallbacks()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string expected = dir.CreateFile("src/special/value.ts", "export const value = 1;");
        dir.CreateFile("src/general/value.ts", "export const value = 2;");
        string entry = dir.CreateFile("src/main.ts", "export {};");
        var options = new ModuleResolutionOptions(
            ModuleResolutionMode.Bundler,
            dir.GetPath("src"),
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["@app/*"] = [dir.GetPath("src/general/*")],
                ["@app/special/*"] = [dir.GetPath("missing/*"), dir.GetPath("src/special/*")],
            });

        var resolver = new ModuleResolver(entry, options);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveModulePath("@app/special/value", entry));
    }

    [Fact]
    public void BaseUrlResolvesBareSourceFiles()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string expected = dir.CreateFile("src/models/user.ts", "export interface User {}");
        string entry = dir.CreateFile("src/main.ts", "export {};");
        var options = new ModuleResolutionOptions(
            ModuleResolutionMode.Classic,
            dir.GetPath("src"),
            new Dictionary<string, IReadOnlyList<string>>());

        var resolver = new ModuleResolver(entry, options);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveModulePath("models/user", entry));
    }

    [Fact]
    public void Node10IgnoresExportsWhileNode16UsesThem()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string legacy = dir.CreateFile("node_modules/pkg/legacy.ts", "export const value = 1;");
        string modern = dir.CreateFile("node_modules/pkg/modern.ts", "export const value = 2;");
        dir.CreateFile("node_modules/pkg/package.json", """
            { "main": "./legacy.ts", "exports": { ".": "./modern.ts" } }
            """);
        string entry = dir.CreateFile("main.ts", "export {};");

        var node10 = new ModuleResolver(
            entry,
            new ModuleResolutionOptions(
                ModuleResolutionMode.Node10, null,
                new Dictionary<string, IReadOnlyList<string>>()));
        var node16 = new ModuleResolver(
            entry,
            new ModuleResolutionOptions(
                ModuleResolutionMode.Node16, null,
                new Dictionary<string, IReadOnlyList<string>>()));

        Assert.Equal(Path.GetFullPath(legacy), node10.ResolveModulePath("pkg", entry));
        Assert.Equal(Path.GetFullPath(modern), node16.ResolveModulePath("pkg", entry));
    }

    [Fact]
    public void RelativeJsSpecifierMapsToTypeScriptSource()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string expected = dir.CreateFile("dep.ts", "export const value = 1;");
        string entry = dir.CreateFile("main.ts", "export {};");

        var resolver = new ModuleResolver(entry);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveModulePath("./dep.js", entry));
    }
}
