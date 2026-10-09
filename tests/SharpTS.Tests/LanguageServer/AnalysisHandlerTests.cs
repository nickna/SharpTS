using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.IO;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EditorAnalysisGateCollection
{
    public const string Name = "Editor analysis checker gates";
}

// These tests deliberately block a checker worker. Keep their release continuations
// independent of the aggressive runner's queue of unrelated synchronous collections.
[Collection(EditorAnalysisGateCollection.Name)]
public sealed class AnalysisHandlerTests
{
    [Fact]
    public async Task NavigationAndRenameHandlersReuseOneCheckIncludingTheWorkspaceSeed()
    {
        using var project = new HandlerProject();

        var definition = Assert.IsAssignableFrom<LocationOrLocationLinks>(
            await project.RequestAsync("definition"));
        Assert.Equal(project.DependencyUri, Assert.Single(definition).Location!.Uri);
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        var references = Assert.IsAssignableFrom<LocationContainer>(
            await project.RequestAsync("references"));
        Assert.Equal(3, references.Count());
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.NotNull(await project.RequestAsync("prepare"));
        var rename = Assert.IsType<WorkspaceEdit>(await project.RequestAsync("rename"));
        Assert.NotNull(rename.Changes);
        Assert.Equal(2, rename.Changes.Count);
        Assert.Equal(3, rename.Changes.Values.Sum(edits => edits.Count()));
        Assert.All(rename.Changes.Values.SelectMany(edits => edits), edit =>
            Assert.Equal("renamedValue", edit.NewText));
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits >= 3);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("references")]
    [InlineData("prepare")]
    [InlineData("rename")]
    public async Task NewDocumentVersionDuringAnalysisRefusesTheOldHandlerResponse(string feature)
    {
        using var project = new HandlerProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            Task<object?> pending = project.RequestAsync(feature);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(project.Store.Open(project.Uri.ToString(),
                project.Source.Replace("const result", "const changed", StringComparison.Ordinal), version: 2));
            // Deliberately omit service invalidation: this exercises the handler's
            // final document-version guard after a successful old-capture check.
            gate.Release();

            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, project.Analysis.Statistics.Checks);
        }
        finally
        {
            gate.Release();
        }
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("references")]
    [InlineData("prepare")]
    [InlineData("rename")]
    public async Task ClosedDependencyMutationDuringCheckingRefusesOldLocationsAndEdits(string feature)
    {
        using var project = new HandlerProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            Task<object?> pending = project.RequestAsync(feature);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllText(project.DependencyPath, "// declaration moved\nexport const value = 2;\n");
            gate.Release();

            AssertNoNavigationOrEdit(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            object? fresh = await project.RequestAsync(feature);
            Assert.NotNull(fresh);
            if (feature == "definition")
            {
                var locations = Assert.IsAssignableFrom<LocationOrLocationLinks>(fresh);
                Assert.Equal(1, Assert.Single(locations).Location!.Range.Start.Line);
            }
            Assert.True(project.Analysis.Statistics.Checks >= 2);
        }
        finally
        {
            gate.Release();
        }
    }

    [Theory]
    [InlineData("references")]
    [InlineData("prepare")]
    [InlineData("rename")]
    public async Task WorkspaceVersionChangeDuringAnalysisRefusesTheOldDomain(string feature)
    {
        using var project = new HandlerProject();
        using var added = CliTestHelper.CreateTempDirectory();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            Task<object?> pending = project.RequestAsync(feature);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            // Bypass notification invalidation to isolate the handler's workspace guard.
            project.Workspace.Change([added.Path], []);
            gate.Release();

            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task WatchedFileNotificationInvalidatesAndRepublishesOpenDiagnostics()
    {
        using var project = new HandlerProject();
        using var diagnostics = new DiagnosticsService(analysis: project.Analysis);
        var published = new List<PublishDiagnosticsParams>();
        var failures = new List<Exception>();
        using var coordinator = Coordinator(project.Store, diagnostics, published, failures);
        using var previous = await project.Analysis.GetDocumentAsync(project.Capture());
        Assert.NotNull(previous);
        File.WriteAllText(project.DependencyPath, "export const value: string = 'changed';\n");
        var handler = new AnalysisWatchedFilesHandler(project.Analysis, coordinator);

        await handler.Handle(new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(new FileEvent
            {
                Uri = project.DependencyUri,
                Type = FileChangeType.Changed,
            }),
        }, CancellationToken.None);
        await coordinator.DrainAsync();

        Assert.Empty(failures);
        Assert.False(previous.IsCurrent());
        PublishDiagnosticsParams result = Assert.Single(published);
        Assert.Equal(project.Uri, result.Uri);
        Assert.Equal(1, result.Version);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task WorkspaceFolderNotificationChangesRootsInvalidatesAndRepublishes()
    {
        using var project = new HandlerProject();
        using var added = CliTestHelper.CreateTempDirectory();
        using var diagnostics = new DiagnosticsService(analysis: project.Analysis);
        var published = new List<PublishDiagnosticsParams>();
        var failures = new List<Exception>();
        using var coordinator = Coordinator(project.Store, diagnostics, published, failures);
        using var previous = await project.Analysis.GetDocumentAsync(project.Capture());
        Assert.NotNull(previous);
        long version = project.Workspace.Version;
        var handler = new AnalysisWorkspaceFoldersHandler(project.Workspace, project.Analysis, coordinator);

        await handler.Handle(new DidChangeWorkspaceFoldersParams
        {
            Event = new WorkspaceFoldersChangeEvent
            {
                Added = new Container<WorkspaceFolder>(new WorkspaceFolder
                {
                    Name = "added",
                    Uri = DocumentUri.FromFileSystemPath(added.Path),
                }),
                Removed = new Container<WorkspaceFolder>(new WorkspaceFolder
                {
                    Name = "old",
                    Uri = DocumentUri.FromFileSystemPath(project.Directory.Path),
                }),
            },
        }, CancellationToken.None);
        await coordinator.DrainAsync();

        Assert.Empty(failures);
        Assert.True(project.Workspace.Version > version);
        Assert.True(StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(added.Path), Assert.Single(project.Workspace.SnapshotRoots())));
        Assert.False(previous.IsCurrent());
        PublishDiagnosticsParams result = Assert.Single(published);
        Assert.Equal(project.Uri, result.Uri);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task InteropOnlySyncAndEditorHandlersKeepGeneralAnalysisLazy()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value: string = 1;\n");
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        var store = new DocumentStore();
        using var analysis = new SemanticAnalysisService();
        analysis.BeforeCheck = () => throw new InvalidOperationException("Interop-only started general checking.");
        using var diagnostics = new DiagnosticsService(analysis: analysis);
        var published = new List<PublishDiagnosticsParams>();
        var failures = new List<Exception>();
        using var coordinator = Coordinator(store, diagnostics, published, failures, DiagnosticPublishMode.SharpTsOnly);
        var sync = new TextDocumentSyncHandler(store, coordinator, analysis);
        var decorators = new DecoratorService();
        var hover = new HoverHandler(store, decorators, new MemberHoverService());
        var completion = new CompletionHandler(store, decorators);
        var signatures = new SignatureHelpHandler(store, decorators);
        await sync.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "typescript", Version = 1, Text = File.ReadAllText(path),
            },
        }, CancellationToken.None);
        await coordinator.DrainAsync();

        Assert.Empty(Assert.Single(published).Diagnostics);
        Assert.Null(await hover.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 7),
        }, CancellationToken.None));
        Assert.Empty((await completion.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 7),
        }, CancellationToken.None)).Items);
        Assert.Null(await signatures.Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 7),
        }, CancellationToken.None));

        const string interop = "@DotNetType(\"System.String\")\ndeclare class NetString {}\n";
        await sync.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = interop }),
        }, CancellationToken.None);
        await coordinator.DrainAsync();
        Assert.NotNull(await hover.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 5),
        }, CancellationToken.None));
        Assert.Contains((await completion.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 1),
        }, CancellationToken.None)).Items, item => item.Label == "DotNetType");
        Assert.NotNull(await signatures.Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, interop.IndexOf('(') + 1),
        }, CancellationToken.None));
        Assert.Equal(0, analysis.Statistics.Builds);
        Assert.Equal(0, analysis.Statistics.Checks);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("temp")]
    public void GuiDiscoveryStopsBeforeAmbientDirectories(string boundary)
    {
        string ambient = boundary == "home"
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetTempPath();
        Assert.False(string.IsNullOrWhiteSpace(ambient));
        string workspace = Path.Combine(ambient, "sharpts-gui-probe-" + Guid.NewGuid());
        string document = Path.Combine(workspace, "child", "main.tsx");
        var fileSystem = new GuiProbeFileSystem();
        using var scope = CompilerFileSystem.Use(fileSystem);

        Assert.Null(new GuiContractService().Completion(document, "<Bu", 0, 3));

        Assert.NotEmpty(fileSystem.EnumeratedDirectories);
        Assert.All(fileSystem.EnumeratedDirectories, path =>
            Assert.True(IsWithin(path, workspace), $"GUI discovery escaped the project into {path}."));
    }

    [Fact]
    public void GuiDiscoveryWithoutAFilePathDoesNotProbeTheCurrentDirectory()
    {
        var fileSystem = new GuiProbeFileSystem();
        using var scope = CompilerFileSystem.Use(fileSystem);
        var gui = new GuiContractService();

        Assert.Null(gui.Completion(null, "<Bu", 0, 3));
        Assert.Null(gui.Hover(null, "<Button />", 0, 3));
        Assert.Null(gui.Definition(null, "<Button />", 0, 3));

        Assert.Empty(fileSystem.EnumeratedDirectories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InaccessibleGuiDiscoveryReturnsNoResult(bool failWhileReading)
    {
        var fileSystem = new GuiProbeFileSystem(failWhileReading ? "read" : "enumerate");
        using var scope = CompilerFileSystem.Use(fileSystem);
        string document = Path.Combine(Path.GetTempPath(), "gui-inaccessible", "main.tsx");
        var gui = new GuiContractService();

        Assert.Null(gui.Completion(document, "<Bu", 0, 3));
        Assert.Null(gui.Hover(document, "<Button />", 0, 3));
        Assert.Null(gui.Definition(document, "<Button />", 0, 3));
        Assert.NotEmpty(fileSystem.EnumeratedDirectories);
    }

    [Fact]
    public void InaccessibleGeneratedGuiDeclarationReturnsNoDefinition()
    {
        var fileSystem = new GuiProbeFileSystem("declaration");
        using var scope = CompilerFileSystem.Use(fileSystem);
        string document = Path.Combine(Path.GetTempPath(), "gui-declaration-inaccessible", "main.tsx");

        Assert.Null(new GuiContractService().Definition(document, "<Button />", 0, 3));
    }

    private static DiagnosticsCoordinator Coordinator(DocumentStore store, DiagnosticsService diagnostics,
        List<PublishDiagnosticsParams> published, List<Exception> failures,
        DiagnosticPublishMode mode = DiagnosticPublishMode.All) =>
        new(store, diagnostics, new DocumentDependencyGraph(), new DiagnosticsSettings(mode),
            published.Add, TimeSpan.Zero, failures.Add);

    private static void AssertNoNavigationOrEdit(object? result)
    {
        switch (result)
        {
            case null: return;
            case LocationOrLocationLinks definitions: Assert.Empty(definitions); return;
            case LocationContainer references: Assert.Empty(references); return;
            default: Assert.Fail("A stale handler returned a rename range or edit."); return;
        }
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed class HandlerProject : IDisposable
    {
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public NavigationWorkspaceContext Workspace { get; } = new();
        public SemanticAnalysisService Analysis { get; }
        public string Path { get; }
        public string DependencyPath { get; }
        public string Source { get; } = "import { value } from './dependency';\nconst result: number = value;\n";
        public DocumentUri Uri => DocumentUri.FromFileSystemPath(Path);
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);
        private readonly DefinitionService _definitions;
        private readonly ReferenceService _references;
        private readonly RenameService _rename;
        private readonly Position _position = new(1, "const result: number = ".Length + 1);

        public HandlerProject()
        {
            Directory.CreateFile("tsconfig.json",
                """{ "include": ["*.ts"], "compilerOptions": { "noLib": true } }""");
            DependencyPath = Directory.CreateFile("dependency.ts", "export const value = 1;\n");
            Path = Directory.CreateFile("entry.ts", Source);
            Store.Open(Uri.ToString(), Source, version: 1);
            Workspace.Initialize(new InitializeParams { RootUri = DocumentUri.FromFileSystemPath(Directory.Path) });
            Analysis = new SemanticAnalysisService(Workspace);
            _definitions = new DefinitionService(Analysis);
            _references = new ReferenceService(Analysis);
            _rename = new RenameService(_references);
        }

        public DocumentRequestSnapshot Capture()
        {
            Assert.True(Store.TryCapture(Uri.ToString(), out DocumentRequestSnapshot? capture));
            return capture;
        }

        public async Task<object?> RequestAsync(string feature) => feature switch
        {
            "definition" => await new DefinitionHandler(Store, _definitions).Handle(new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = _position,
            }, CancellationToken.None),
            "references" => await new ReferencesHandler(Store, _references, Workspace).Handle(new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = _position,
                Context = new ReferenceContext { IncludeDeclaration = true },
            }, CancellationToken.None),
            "prepare" => await new PrepareRenameHandler(Store, _rename, Workspace).Handle(new PrepareRenameParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = _position,
            }, CancellationToken.None),
            "rename" => await new RenameHandler(Store, _rename, Workspace).Handle(new RenameParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = _position, NewName = "renamedValue",
            }, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };

        public void Dispose()
        {
            _definitions.Dispose();
            _references.Dispose();
            Analysis.Dispose();
            Directory.Dispose();
        }
    }

    private sealed class CheckGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _checks;
        public Task Entered => _entered.Task;
        public void HoldFirstCheck()
        {
            if (Interlocked.Increment(ref _checks) != 1) return;
            _entered.TrySetResult();
            _released.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        public void Release() => _released.TrySetResult();
    }

    private sealed class GuiProbeFileSystem(string? failure = null) : ICompilerFileSystem
    {
        public List<string> EnumeratedDirectories { get; } = [];
        public bool FileExists(string path) => failure == "declaration";
        public bool DirectoryExists(string path) => true;
        public FileAttributes GetAttributes(string path) => FileAttributes.Directory;
        public string ReadAllText(string path)
        {
            if (failure == "declaration")
            {
                if (System.IO.Path.GetFileName(path) == "App.csproj")
                    return "<Project Sdk=\"SharpTS.Gui.Sdk/1.0\" />";
                if (System.IO.Path.GetFileName(path) == "control-docs.generated.json")
                    return """{ "controls": [{ "kind": "Button", "documentation": "button", "props": [] }] }""";
                throw new UnauthorizedAccessException("Injected inaccessible generated declaration.");
            }
            throw new IOException("Injected unreadable GUI project.");
        }
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options)
        {
            EnumeratedDirectories.Add(System.IO.Path.GetFullPath(path));
            if (failure == "enumerate") throw new UnauthorizedAccessException("Injected inaccessible directory.");
            if (failure == "declaration")
                return searchPattern == "*.csproj"
                    ? [System.IO.Path.Combine(path, "App.csproj")]
                    : [System.IO.Path.Combine(path, "@sharpts", "control-docs.generated.json")];
            return failure == "read" ? [System.IO.Path.Combine(path, "App.csproj")] : [];
        }
        public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options) => [];
    }
}
