using System.Collections.Concurrent;
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

    [Theory]
    [InlineData("queue")]
    [InlineData("close")]
    [InlineData("configuration")]
    public async Task WorkspaceCancellationPreservesOtherPendingDocuments(string operation)
    {
        using var workspace = new DiagnosticsWorkspace(TimeSpan.FromMilliseconds(50));
        string first = workspace.Open("first.ts", "const text: string = 1;\n");
        string second = workspace.Open("second.ts", "const text: string = 1;\n");
        workspace.Coordinator.Queue(first);
        await workspace.Coordinator.DrainAsync();
        workspace.Coordinator.Queue(second);
        await workspace.Coordinator.DrainAsync();
        Assert.All(workspace.Published, item => Assert.NotEmpty(item.Diagnostics));
        workspace.Published.Clear();

        workspace.Change(first, "const text: string = 'ready';\n");
        workspace.Coordinator.Queue(first);
        switch (operation)
        {
            case "queue":
                workspace.Change(second, "const text: string = 'ready';\n");
                workspace.Coordinator.Queue(second);
                break;
            case "close":
                workspace.Coordinator.Close(workspace.Store.Remove(second)!);
                break;
            case "configuration":
                workspace.Settings.Mode = DiagnosticPublishMode.Off;
                workspace.Coordinator.RepublishAll();
                // A subsequent document event must retain the other files queued by settings.
                workspace.Coordinator.Queue(second);
                break;
        }
        await workspace.Coordinator.DrainAsync();

        PublishDiagnosticsParams firstResult = Assert.Single(workspace.Results(first));
        Assert.Equal(2, firstResult.Version);
        Assert.Empty(firstResult.Diagnostics);
        Assert.Empty(Assert.Single(workspace.Results(second)).Diagnostics);
        Assert.Empty(workspace.Failures);
    }

    [Theory]
    [InlineData(DiagnosticPublishMode.All, false)]
    [InlineData(DiagnosticPublishMode.All, true)]
    [InlineData(DiagnosticPublishMode.SharpTsOnly, false)]
    public async Task ConfiguredDependencyChangesRepublishImportersAndCloseRestoresDisk(
        DiagnosticPublishMode mode, bool closedIntermediate)
    {
        using var workspace = new DiagnosticsWorkspace(TimeSpan.Zero, mode);
        workspace.Directory.CreateFile("tsconfig.json", """
            {
              "compilerOptions": {
                "noLib": true, "types": [], "baseUrl": ".",
                "paths": { "@lib/*": ["src/lib/*"] }
              },
              "include": ["src/**/*.ts"]
            }
            """);
        string dependency = workspace.Open("src/lib/dependency.ts", "export const value = 1;\n");
        if (closedIntermediate)
            workspace.Directory.CreateFile("src/lib/bridge.ts", "export { value } from './dependency';\n");
        string specifier = closedIntermediate ? "@lib/bridge" : "@lib/dependency";
        string importer = workspace.Open("src/importer.ts",
            $"import {{ value }} from '{specifier}';\nconst text: string = value;\n");
        workspace.Coordinator.Queue(importer);
        await workspace.Coordinator.DrainAsync();
        if (mode == DiagnosticPublishMode.All)
            Assert.NotEmpty(Assert.Single(workspace.Results(importer)).Diagnostics);
        workspace.Published.Clear();

        workspace.Change(dependency, "export const value = 'ready';\n");
        workspace.Coordinator.Queue(dependency);
        await workspace.Coordinator.DrainAsync();

        Assert.Equal(2, Assert.Single(workspace.Results(dependency)).Version);
        Assert.Empty(Assert.Single(workspace.Results(importer)).Diagnostics);
        workspace.Published.Clear();

        workspace.Analysis.InvalidateAll();
        workspace.Coordinator.Close(workspace.Store.Remove(dependency)!);
        await workspace.Coordinator.DrainAsync();
        PublishDiagnosticsParams restored = Assert.Single(workspace.Results(importer));
        if (mode == DiagnosticPublishMode.All)
            Assert.NotEmpty(restored.Diagnostics);
        else
            Assert.Equal(0, workspace.Analysis.Statistics.Checks);
        Assert.Empty(workspace.Failures);
    }

    [Fact]
    public async Task CancellationAfterPublishingDependencyPreservesItsPendingImporters()
    {
        using var workspace = new DiagnosticsWorkspace(TimeSpan.FromMilliseconds(25));
        string dependency = workspace.Open("dependency.ts", "export const value = 1;\n");
        string importer = workspace.Open("importer.ts",
            "import { value } from './dependency';\nconst text: string = value;\n");
        string unrelated = workspace.Open("unrelated.ts", "const ready = false;\n");
        workspace.Coordinator.Queue(importer);
        await workspace.Coordinator.DrainAsync();
        Assert.NotEmpty(Assert.Single(workspace.Results(importer)).Diagnostics);
        workspace.Published.Clear();
        workspace.OnPublish = item =>
        {
            if (!SameUri(item.Uri.ToString(), dependency) || item.Version != 2)
                return;
            workspace.OnPublish = null;
            workspace.Change(unrelated, "const ready = true;\n");
            workspace.Coordinator.Queue(unrelated);
        };

        workspace.Change(dependency, "export const value = 'ready';\n");
        workspace.Coordinator.Queue(dependency);
        await workspace.Coordinator.DrainAsync();

        Assert.Empty(workspace.Results(importer).Last().Diagnostics);
        Assert.Equal(2, Assert.Single(workspace.Results(unrelated)).Version);
        Assert.Empty(workspace.Failures);
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

    private static bool SameUri(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private sealed class DiagnosticsWorkspace : IDisposable
    {
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public SemanticAnalysisService Analysis { get; } = new();
        public DiagnosticsSettings Settings { get; }
        public ConcurrentQueue<PublishDiagnosticsParams> Published { get; } = new();
        public ConcurrentQueue<Exception> Failures { get; } = new();
        public DiagnosticsCoordinator Coordinator { get; }
        public Action<PublishDiagnosticsParams>? OnPublish { get; set; }
        private readonly DiagnosticsService _diagnostics;

        public DiagnosticsWorkspace(TimeSpan debounce, DiagnosticPublishMode mode = DiagnosticPublishMode.All)
        {
            Settings = new DiagnosticsSettings(mode);
            _diagnostics = new DiagnosticsService(analysis: Analysis);
            Coordinator = new DiagnosticsCoordinator(Store, _diagnostics, new DocumentDependencyGraph(),
                Settings, item =>
                {
                    Published.Enqueue(item);
                    OnPublish?.Invoke(item);
                }, debounce, Failures.Enqueue);
        }

        public string Open(string relativePath, string source)
        {
            string uri = new Uri(Directory.CreateFile(relativePath, source)).AbsoluteUri;
            Assert.True(Store.Open(uri, source, 1));
            return uri;
        }

        public void Change(string uri, string source)
        {
            Assert.True(Store.TryGetSnapshot(uri, out DocumentSnapshot? current));
            Assert.True(Store.ApplyChanges(uri, current.Version + 1,
                [new TextDocumentContentChangeEvent { Text = source }]));
            Analysis.InvalidateAll();
        }

        public IEnumerable<PublishDiagnosticsParams> Results(string uri) =>
            Published.Where(item => SameUri(item.Uri.ToString(), uri));

        public void Dispose()
        {
            Coordinator.Dispose();
            _diagnostics.Dispose();
            Analysis.Dispose();
            Directory.Dispose();
        }
    }
}
