using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Compilation;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class WorkspaceLifecycleTests
{
    [Fact]
    public void DocumentStoreAppliesIncrementalChangesAndRejectsStaleVersions()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("input.ts", "const value = 1;\n");
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();

        Assert.True(store.Open(uri, "const value = 1;\n", version: 3));
        Assert.True(store.ApplyChanges(
            uri,
            version: 4,
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new Range(
                        new Position(0, 6),
                        new Position(0, 11)),
                    Text = "answer",
                },
                new TextDocumentContentChangeEvent
                {
                    Range = new Range(
                        new Position(0, 15),
                        new Position(0, 16)),
                    Text = "2",
                },
            ]));
        Assert.False(store.ApplyChanges(
            uri,
            version: 4,
            [new TextDocumentContentChangeEvent { Text = "stale" }]));

        Assert.True(store.TryCapture(uri, out DocumentRequestSnapshot? snapshot));
        Assert.Equal(4, snapshot.Document.Version);
        Assert.Equal("const answer = 2;\n", snapshot.Document.Text);
        Assert.Same(
            snapshot.Document,
            Assert.Single(snapshot.FileSystemDocuments).Value);
    }

    [Fact]
    public async Task DependencyChangeRepublishesOpenImporters()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string dependencyPath = directory.CreateFile(
            "dependency.ts",
            "export const value = 1;\n");
        string importerPath = directory.CreateFile(
            "importer.ts",
            "import { value } from \"./dependency\";\nconst text: string = value;\n");
        string dependencyUri = new Uri(dependencyPath).AbsoluteUri;
        string importerUri = new Uri(importerPath).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(
            dependencyUri,
            File.ReadAllText(dependencyPath),
            version: 1);
        store.Open(
            importerUri,
            File.ReadAllText(importerPath),
            version: 1);

        List<PublishDiagnosticsParams> published = [];
        using var coordinator = new DiagnosticsCoordinator(
            store,
            new DiagnosticsService(),
            new DocumentDependencyGraph(),
            new DiagnosticsSettings(DiagnosticPublishMode.All),
            published.Add,
            TimeSpan.Zero);

        coordinator.Queue(importerUri);
        await coordinator.DrainAsync();
        Assert.NotEmpty(Assert.Single(published).Diagnostics);
        published.Clear();

        store.Open(
            dependencyUri,
            "export const value = \"ready\";\n",
            version: 2);
        coordinator.Queue(dependencyUri);
        await coordinator.DrainAsync();

        Assert.True(
            new HashSet<string>(
                published.Select(item => item.Uri.ToString()),
                StringComparer.OrdinalIgnoreCase)
                .SetEquals([dependencyUri, importerUri]));
        Assert.Contains(published, item =>
            string.Equals(
                item.Uri.ToString(),
                dependencyUri,
                StringComparison.OrdinalIgnoreCase) &&
            item.Version == 2);
        Assert.Contains(published, item =>
            string.Equals(
                item.Uri.ToString(),
                importerUri,
                StringComparison.OrdinalIgnoreCase) &&
            item.Version == 1);
        Assert.Empty(Assert.Single(published, item =>
            string.Equals(
                item.Uri.ToString(),
                importerUri,
                StringComparison.OrdinalIgnoreCase)).Diagnostics);
    }

    [Fact]
    public async Task NewVersionCancelsStaleDebouncedPublication()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("input.ts", "const first = 1;\n");
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(uri, "const first = 1;\n", version: 1);
        List<PublishDiagnosticsParams> published = [];
        using var coordinator = new DiagnosticsCoordinator(
            store,
            new DiagnosticsService(),
            new DocumentDependencyGraph(),
            new DiagnosticsSettings(),
            published.Add,
            TimeSpan.FromMilliseconds(25));

        coordinator.Queue(uri);
        store.Open(uri, "const second = 2;\n", version: 2);
        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        PublishDiagnosticsParams result = Assert.Single(published);
        Assert.Equal(2, result.Version);
    }

    [Fact]
    public void AllDiagnosticsUsesTheCachedFullCheckerResult()
    {
        const string source = "const value: string = 1;";
        var snapshot = new DocumentSnapshot(
            "untitled:check",
            source,
            Version: 1,
            FilePath: null);
        var service = new DiagnosticsService();

        Assert.Empty(service.Analyze(
            snapshot,
            DiagnosticPublishMode.SharpTsOnly,
            CancellationToken.None));
        Assert.Contains(
            service.Analyze(
                snapshot,
                DiagnosticPublishMode.All,
                CancellationToken.None),
            diagnostic => diagnostic.Message.Contains(
                "not assignable",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConfigurationAcceptsBothClientShapes()
    {
        Assert.Equal(
            "off",
            ConfigurationHandler.FindDiagnosticsValue(
                Newtonsoft.Json.Linq.JObject.Parse(
                    """{ "sharpts": { "diagnostics": "off" } }""")));
        Assert.Equal(
            "all",
            ConfigurationHandler.FindDiagnosticsValue(
                Newtonsoft.Json.Linq.JObject.Parse(
                    """{ "diagnostics": "all" }""")));
    }

    [Fact]
    public void CheckerObservesEditorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var checker = new TypeChecker()
            .WithCancellation(cancellation.Token);

        Assert.Throws<OperationCanceledException>(() =>
            checker.CheckWithRecovery([]));
    }

    [Fact]
    public void ReferenceLoaderReloadKeepsTypesFromRetiredContextUsable()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string assemblyPath = directory.CreateFile("changing.dll", "not an assembly");
        using var loader = new ReloadingAssemblyReferenceLoader([assemblyPath]);
        Type? original = loader.TryResolve("System.String");
        Assert.NotNull(original);

        File.WriteAllText(assemblyPath, "changed assembly placeholder");
        Assert.NotNull(loader.TryResolve("System.String"));

        Assert.Equal(1, loader.Generation);
        Assert.Equal("System.String", original!.FullName);
    }
}
