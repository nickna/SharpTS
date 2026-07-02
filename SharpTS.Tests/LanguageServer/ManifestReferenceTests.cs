using SharpTS.Compilation;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.References;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

/// <summary>
/// The language-server side of sharpts.json (issue #1197): manifest-resolved paths feed
/// the existing MetadataLoadContext <see cref="AssemblyReferenceLoader"/> (Resolve, never
/// Load — the editor process must not execute workspace code), and the shared resolver
/// then validates dotnet: imports against manifest types exactly like the runtime does.
/// Mirrors the wiring in SharpTS.LanguageServer/Program.cs.
/// </summary>
[Collection("ExternalAssembly")]
public class ManifestReferenceTests(ExternalAssemblyFixture fixture)
{
    [Fact]
    public void ManifestPaths_FeedLoader_ResolveTypeAndValidateImport()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var dllName = $"{ExternalAssemblyFixture.GreeterAssemblyName}.dll";
        File.Copy(fixture.GreeterDllPath, dir.GetPath(dllName));
        dir.CreateFile("sharpts.json", $$"""{ "references": ["./{{dllName}}"] }""");

        var refSet = DotNetReferences.Resolve(dir.Path, []);
        using var loader = new AssemblyReferenceLoader(refSet.References.Select(r => r.Path));

        // The MLC loader resolves the manifest type without loading it for execution.
        Assert.NotNull(loader.TryResolve(ExternalAssemblyFixture.GreeterTypeName));

        // And the shared import analyzer produces zero diagnostics for a valid import
        // against it — squiggles can't disagree with the actual loader.
        var source = $"import {{ Greeter }} from \"dotnet:{ExternalAssemblyFixture.GreeterTypeName}\";";
        var tokens = new Lexer(source).ScanTokens();
        var parsed = new Parser(tokens, DecoratorMode.Stage3).Parse();
        Assert.True(parsed.IsSuccess);
        var diagnostics = new InteropAnalyzer(loader.TryResolve).Analyze(parsed.Statements);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BrokenManifest_ResolveThrows_ServerWiringTreatsItAsNonFatal()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", "{ broken ");

        // The LSP Program.cs wraps Resolve in try/catch and continues BCL-only; here we
        // pin that Resolve throws (rather than silently returning empty) so the server
        // logs the reason.
        Assert.ThrowsAny<Exception>(() => DotNetReferences.Resolve(dir.Path, []));
    }
}
