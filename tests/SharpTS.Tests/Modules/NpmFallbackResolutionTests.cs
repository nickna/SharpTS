using SharpTS.Modules;
using SharpTS.Modules.Stdlib.Providers;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.Modules;

/// <summary>
/// Discovery and resolution-precedence tests for the embedded npm-fallback shim packages
/// (react family): a real node_modules package must always win; the shim answers only when
/// nothing else resolves.
/// </summary>
public class NpmFallbackResolutionTests
{
    [Fact]
    public void Provider_DiscoversReactFamilyModules()
    {
        var provider = new EmbeddedNpmFallbackProvider();

        Assert.Contains("react", provider.ProvidedModules);
        Assert.Contains("react/jsx-runtime", provider.ProvidedModules);
        Assert.Contains("react/jsx-dev-runtime", provider.ProvidedModules);
        Assert.Contains("react-dom/server", provider.ProvidedModules);
    }

    [Theory]
    [InlineData("react", "stdlib:npm/react/index.ts")]
    [InlineData("react/jsx-runtime", "stdlib:npm/react/jsx-runtime.ts")]
    [InlineData("react/jsx-dev-runtime", "stdlib:npm/react/jsx-dev-runtime.ts")]
    [InlineData("react-dom/server", "stdlib:npm/react-dom/server.ts")]
    public void Provider_ResolvesToNpmVirtualPaths(string specifier, string expectedVirtualPath)
    {
        var provider = new EmbeddedNpmFallbackProvider();

        Assert.True(provider.TryResolve(specifier, out var module));
        Assert.Equal(expectedVirtualPath, module!.VirtualPath);
        // Round trip: the virtual path extracts back to the same specifier.
        Assert.Equal(specifier, EmbeddedNpmFallbackProvider.TryExtractSpecifier(expectedVirtualPath));
    }

    [Fact]
    public void TryExtractSpecifier_IgnoresNodeStdlibPaths()
    {
        Assert.Null(EmbeddedNpmFallbackProvider.TryExtractSpecifier("stdlib:node/fs.ts"));
        Assert.Null(EmbeddedNpmFallbackProvider.TryExtractSpecifier("C:/code/react.ts"));
    }

    [Fact]
    public void BareReactResolvesToShimWhenNoNodeModules()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var entry = dir.CreateFile("main.ts", "export {};");
        var resolver = new ModuleResolver(entry);

        string resolved = resolver.ResolveModulePath("react", entry);

        Assert.Equal("stdlib:npm/react/index.ts", resolved);
    }

    [Fact]
    public void RealNodeModulesReactWinsOverShim()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("node_modules/react/package.json", """{ "name": "react", "main": "index.js" }""");
        dir.CreateFile("node_modules/react/index.js", "module.exports = { real: true };");
        var entry = dir.CreateFile("main.ts", "export {};");
        var resolver = new ModuleResolver(entry);

        string resolved = resolver.ResolveModulePath("react", entry);

        Assert.Contains("node_modules", resolved);
        Assert.DoesNotContain("stdlib:", resolved);
    }

    [Fact]
    public void NodeBuiltinResolutionIsUnchanged()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var entry = dir.CreateFile("main.ts", "export {};");
        var resolver = new ModuleResolver(entry);

        // Node builtins keep stdlib-first behavior (never consult node_modules).
        string resolved = resolver.ResolveModulePath("path", entry);

        Assert.StartsWith("stdlib:", resolved);
        Assert.DoesNotContain("npm", resolved);
    }

    [Fact]
    public void UnknownBareSpecifierStillThrows()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var entry = dir.CreateFile("main.ts", "export {};");
        var resolver = new ModuleResolver(entry);

        var ex = Assert.Throws<Exception>(() => resolver.ResolveModulePath("left-pad", entry));
        Assert.Contains("Cannot resolve bare specifier", ex.Message);
    }
}
