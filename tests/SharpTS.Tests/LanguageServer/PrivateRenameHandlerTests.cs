using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Project;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class PrivateRenameHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WholePrivateTokenPrepareAndVersionedEditsUseExactUtf16Ranges(bool crlf)
    {
        using var project = new HandlerProject(crlf: crlf);
        project.AssertPrepare(await project.PrepareAsync());
        WorkspaceEdit plain = project.AssertEdit(await project.RenameAsync("new"));
        WorkspaceEdit prefixed = project.AssertEdit(await project.RenameAsync("#new"));
        Assert.Equal(EditKeys(plain), EditKeys(prefixed));
        WorkspaceEdit keyword = project.AssertEdit(await project.RenameAsync("class"), replacement: "#class");
        WorkspaceEdit prefixedKeyword = project.AssertEdit(await project.RenameAsync("#class"), replacement: "#class");
        Assert.Equal(EditKeys(keyword), EditKeys(prefixedKeyword));
        project.AssertEdit(await project.RenameAsync("longerName"), replacement: "#longerName");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingNestedOrFalseDocumentChangesCapabilityRefusesPrivateAndPreservesLexicalRename(int shape)
    {
        ClientCapabilities capabilities = shape switch
        {
            0 => new ClientCapabilities(),
            1 => new ClientCapabilities { Workspace = new() },
            _ => Capabilities(false),
        };
        using var project = new HandlerProject(clientCapabilities: capabilities);
        Assert.Null(await project.PrepareAsync());
        Assert.Null(await project.RenameAsync("new"));

        const string lexical = "const /*declaration*/local = 1;\n/*read*/local;\n";
        project.Open(lexical, 2);
        Assert.NotNull(await project.PrepareAsync());
        var edit = Assert.IsType<WorkspaceEdit>(await project.RenameAsync("nextLocal"));
        Assert.Null(edit.DocumentChanges);
        Assert.Equal(2, Assert.Single(edit.Changes!).Value.Count());
    }

    [Fact]
    public async Task VersionedEditCapabilityWithPrivateServicePreservesExistingLexicalRename()
    {
        using var project = new HandlerProject();
        const string lexical = "const /*declaration*/local = 1;\n/*read*/local;\n";
        project.Open(lexical, 2);
        Assert.NotNull(await project.PrepareAsync());
        var edit = Assert.IsType<WorkspaceEdit>(await project.RenameAsync("nextLocal"));
        Assert.Null(edit.DocumentChanges);
        var changed = Assert.Single(edit.Changes!);
        Assert.Equal(project.Uri, changed.Key);
        var edits = changed.Value.ToArray();
        Assert.Equal(2, edits.Length);
        Assert.All(edits, value => Assert.Equal("nextLocal", value.NewText));
    }

    [Fact]
    public async Task HandlerWithoutPrivateServiceCannotAdmitThePrivateFeature()
    {
        using var project = new HandlerProject(privateService: false);
        Assert.Null(await project.PrepareAsync());
        Assert.Null(await project.RenameAsync("new"));
    }

    [Fact]
    public async Task ConcurrentPrepareAndRenameRequestsShareOneCheckAndKeepTheirOwnProjection()
    {
        using var project = new HandlerProject();
        var prepare = project.PrepareAsync();
        var rename = project.RenameAsync("new");
        var longer = project.RenameAsync("longerName");
        await Task.WhenAll(prepare, rename, longer);
        project.AssertPrepare(await prepare);
        project.AssertEdit(await rename);
        project.AssertEdit(await longer, replacement: "#longerName");
        project.AssertEdit(await project.RenameAsync("#new"));
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceVersionChangedDuringCheckingRefusesCapturedPrepareOrEdits(bool prepare)
    {
        using var project = new HandlerProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            Task<object?> pending = project.QueryAsync(prepare);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            project.Open("// changed open version\r\n" + project.Source, 2);
            // No notification invalidation: exercise the handler's final captured-version guard.
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertPrepare(await project.PrepareAsync());
            project.AssertEdit(await project.RenameAsync("new"));
        }
        finally { gate.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirtyDependencyVersionChangedDuringCheckingRefusesCapturedPrivateResult(bool prepare)
    {
        using var project = new HandlerProject();
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), HandlerProject.Dependency, 1));
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            Task<object?> pending = project.QueryAsync(prepare);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(project.Store.Open(project.DependencyUri.ToString(), HandlerProject.ChangedDependency, 2));
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertEdit(await project.RenameAsync("new"));
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task SameLengthAndTimestampClosedDependencyMutationRefusesOldEdits()
    {
        Assert.Equal(HandlerProject.Dependency.Length, HandlerProject.ChangedDependency.Length);
        using var project = new HandlerProject();
        DateTime timestamp = File.GetLastWriteTimeUtc(project.DependencyPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.RenameAsync("new");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.DependencyPath, HandlerProject.ChangedDependency);
            File.SetLastWriteTimeUtc(project.DependencyPath, timestamp);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertEdit(await project.RenameAsync("new"));
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task ConfigChangedDuringCheckingRefusesTheCapturedPrivateEdits()
    {
        const string original = """{"compilerOptions":{"noLib":true,"types":[],"strictNullChecks":false},"include":["*.ts"]}""";
        const string changed = """{"compilerOptions":{"noLib":true,"types":[],"strictNullChecks":true },"include":["*.ts"]}""";
        Assert.Equal(original.Length, changed.Length);
        using var project = new HandlerProject(config: original);
        DateTime timestamp = File.GetLastWriteTimeUtc(project.ConfigPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.RenameAsync("new");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.ConfigPath, changed);
            File.SetLastWriteTimeUtc(project.ConfigPath, timestamp);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertEdit(await project.RenameAsync("new"));
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task MetadataGenerationChangedDuringCheckingRefusesTheCapturedPrivateEdits()
    {
        using var project = new HandlerProject(metadata: true);
        string assembly = typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location;
        File.Copy(assembly, Path.Combine(project.Directory.Path, "first.dll"));
        File.Copy(assembly, Path.Combine(project.Directory.Path, "other.dll"));
        const string original = """{"references":["first.dll"]}""";
        const string changed = """{"references":["other.dll"]}""";
        Assert.Equal(original.Length, changed.Length);
        string manifest = project.Directory.CreateFile("sharpts.json", original);
        DateTime timestamp = File.GetLastWriteTimeUtc(manifest);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.RenameAsync("new");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(manifest, changed);
            File.SetLastWriteTimeUtc(manifest, timestamp);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertEdit(await project.RenameAsync("new"));
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task WorkspaceFolderChangedDuringCheckingRefusesTheCapturedPrivateResult()
    {
        using var project = new HandlerProject();
        using var added = CliTestHelper.CreateTempDirectory();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.PrepareAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            project.Workspace.Change([added.Path], []);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertPrepare(await project.PrepareAsync());
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOnePrivateRenamePreservesItsPeerAndCachedPrepare()
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.RenameAsync("new", cancellationToken: cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.RenameAsync("longerName");
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            project.AssertEdit(await peer.WaitAsync(TimeSpan.FromSeconds(20)), replacement: "#longerName");
            project.AssertPrepare(await project.PrepareAsync());
            project.AssertEdit(await project.RenameAsync("new"));
            Assert.Equal(1, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledPrivateRequestsStartNoSharedChecking(bool prepare)
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await project.QueryAsync(prepare, cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingOrMismatchedOpenCaptureCannotProduceVersionedEdits(int mismatch)
    {
        using var project = new HandlerProject();
        Assert.True(project.Store.TryCapture(project.Uri.ToString(), out var capture));
        var documents = capture.FileSystemDocuments.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        string path = Path.GetFullPath(capture.Document.FilePath!);
        if (mismatch == 0) documents.Remove(path);
        else documents[path] = mismatch == 1
            ? capture.Document with { Version = capture.Document.Version + 1 }
            : capture.Document with { Text = "// other text\n" + capture.Document.Text };
        var unavailable = capture with { FileSystemDocuments = documents };
        Assert.Null((await project.Private.PrepareAsync(unavailable, project.Position(), CancellationToken.None)).Value);
        Assert.Null((await project.Private.RenameAsync(unavailable, project.Position(), "new", CancellationToken.None)).Value);
        Assert.Equal(0, project.Analysis.Statistics.Builds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ExactCapturedIntegerVersionIsPreservedWithoutInventingASentinel(int version)
    {
        using var project = new HandlerProject(version: version);
        project.AssertPrepare(await project.PrepareAsync());
        project.AssertEdit(await project.RenameAsync("new"));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(99, 99)]
    public async Task InvalidPositionsCannotClampToARealPrivateDomain(int line, int character)
    {
        using var project = new HandlerProject();
        var position = new Position(line, character);
        Assert.True(project.Store.TryCapture(project.Uri.ToString(), out var capture));
        Assert.Null((await project.Private.PrepareAsync(capture, position, CancellationToken.None)).Value);
        Assert.Null((await project.Private.RenameAsync(capture, position, "new", CancellationToken.None)).Value);
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Null(await project.PrepareAtAsync(position));
        Assert.Null(await project.RenameAtAsync(position, "new"));
    }

    private static ClientCapabilities Capabilities(bool documentChanges) => new()
    {
        Workspace = new() { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = documentChanges } },
    };

    private static string RangeKey(Range range) =>
        $"{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}";

    private static string[] EditKeys(WorkspaceEdit edit) => Assert.IsType<TextDocumentEdit>(
            Assert.Single(edit.DocumentChanges!).TextDocumentEdit).Edits
        .Select(value => RangeKey(value.Range) + "=" + value.NewText).ToArray();

    private sealed class HandlerProject : IDisposable
    {
        public const string Dependency = "export const seed: number = 1;\n";
        public const string ChangedDependency = "export const seed: number = 2;\n";
        private const string OriginalSource = "import { seed } from './dependency';\nclass Box {\n" +
            "  /* 😀 */ /*declaration*/#old: number = seed;\n" +
            "  read(): number { return this./*read*/#old; }\n" +
            "  write(value: number): void { this./*write*/#old = value; }\n}\n" +
            "class Other { #old: number = 9; read(): number { return this.#old; } }\n" +
            "const untouched = '#old'; // #old is ordinary text\n";
        private readonly PrepareRenameHandler _prepare;
        private readonly RenameHandler _rename;
        private readonly ReferenceService _references;
        private readonly AnalysisMetadataProvider? _metadata;
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public NavigationWorkspaceContext Workspace { get; } = new();
        public SemanticAnalysisService Analysis { get; }
        public PrivateRenameService Private { get; }
        public DocumentUri Uri { get; }
        public string DependencyPath { get; }
        public string ConfigPath { get; }
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);
        public string Source
        {
            get
            {
                Assert.True(Store.TryGet(Uri.ToString(), out var source));
                return source;
            }
        }

        public HandlerProject(bool crlf = false, bool privateService = true, bool metadata = false, int version = 1,
            string? config = null, ClientCapabilities? clientCapabilities = null)
        {
            ConfigPath = Directory.CreateFile("tsconfig.json", config ??
                """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts"]}""");
            DependencyPath = Directory.CreateFile("dependency.ts", Dependency);
            string source = crlf ? OriginalSource.Replace("\n", "\r\n", StringComparison.Ordinal) : OriginalSource;
            Uri = DocumentUri.FromFileSystemPath(Directory.CreateFile("main.ts", source));
            Assert.True(Store.Open(Uri.ToString(), source, version));
            Workspace.Initialize(new InitializeParams { RootUri = DocumentUri.FromFileSystemPath(Directory.Path) });
            if (metadata) _metadata = new AnalysisMetadataProvider(startDirectory: Directory.Path);
            Analysis = new SemanticAnalysisService(Workspace, _metadata);
            Private = new PrivateRenameService(Analysis);
            _references = new ReferenceService(Analysis);
            var rename = new RenameService(_references);
            _prepare = new PrepareRenameHandler(Store, rename, Workspace, privateService ? Private : null);
            _rename = new RenameHandler(Store, rename, Workspace, privateService ? Private : null);
            Register(clientCapabilities ?? Capabilities(true));
        }

        public void Register(ClientCapabilities client)
        {
            var prepare = ((IRegistration<RenameRegistrationOptions, RenameCapability>)_prepare)
                .GetRegistrationOptions(new RenameCapability(), client);
            var rename = ((IRegistration<RenameRegistrationOptions, RenameCapability>)_rename)
                .GetRegistrationOptions(new RenameCapability(), client);
            Assert.True(prepare.PrepareProvider);
            Assert.True(rename.PrepareProvider);
        }

        public void Open(string source, int version) => Assert.True(Store.Open(Uri.ToString(), source, version));

        public Position Position(string marker = "read")
        {
            string comment = "/*" + marker + "*/";
            int offset = Source.IndexOf(comment, StringComparison.Ordinal);
            Assert.True(offset >= 0, "Missing marker " + marker);
            var (line, column) = new LineIndex(Source).ToPosition(offset + comment.Length + 1);
            return new Position(line - 1, column - 1);
        }

        public Task<RangeOrPlaceholderRange?> PrepareAsync(CancellationToken cancellationToken = default) =>
            PrepareAtAsync(Position(), cancellationToken);

        public Task<RangeOrPlaceholderRange?> PrepareAtAsync(Position position, CancellationToken cancellationToken = default) =>
            _prepare.Handle(new PrepareRenameParams { TextDocument = new TextDocumentIdentifier(Uri), Position = position },
                cancellationToken);

        public Task<WorkspaceEdit?> RenameAsync(string name, CancellationToken cancellationToken = default) =>
            RenameAtAsync(Position(), name, cancellationToken);

        public Task<WorkspaceEdit?> RenameAtAsync(Position position, string name, CancellationToken cancellationToken = default) =>
            _rename.Handle(new RenameParams { TextDocument = new TextDocumentIdentifier(Uri), Position = position, NewName = name },
                cancellationToken);

        public async Task<object?> QueryAsync(bool prepare, CancellationToken cancellationToken = default) =>
            prepare ? await PrepareAsync(cancellationToken) : await RenameAsync("new", cancellationToken);

        public void AssertPrepare(RangeOrPlaceholderRange? result)
        {
            var value = Assert.IsType<RangeOrPlaceholderRange>(result);
            Assert.True(value.IsPlaceholderRange);
            var placeholder = Assert.IsType<PlaceholderRange>(value.PlaceholderRange);
            Assert.Equal("#old", placeholder.Placeholder);
            Assert.Equal(RangeKey(At("read")), RangeKey(placeholder.Range));
        }

        public WorkspaceEdit AssertEdit(WorkspaceEdit? result, string replacement = "#new")
        {
            var value = Assert.IsType<WorkspaceEdit>(result);
            Assert.Null(value.Changes);
            var change = Assert.Single(value.DocumentChanges!);
            Assert.True(change.IsTextDocumentEdit);
            var target = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
            Assert.Equal(Uri, target.TextDocument.Uri);
            Assert.True(Store.TryCapture(Uri.ToString(), out var capture));
            Assert.Equal(capture.Document.Version, target.TextDocument.Version);
            var edits = target.Edits.ToArray();
            Assert.Equal(new[] { "declaration", "read", "write" }.Select(marker => RangeKey(At(marker))),
                edits.Select(edit => RangeKey(edit.Range)));
            Assert.All(edits, edit => Assert.Equal(replacement, edit.NewText));
            Assert.Equal(edits.Length, edits.Select(edit => RangeKey(edit.Range)).Distinct().Count());
            return value;
        }

        private Range At(string marker)
        {
            string comment = "/*" + marker + "*/";
            int offset = Source.IndexOf(comment, StringComparison.Ordinal) + comment.Length;
            Assert.True(offset >= comment.Length);
            Assert.Equal("#old", Source.Substring(offset, 4));
            var lines = new LineIndex(Source);
            var (line, column) = lines.ToPosition(offset);
            var (endLine, endColumn) = lines.ToPosition(offset + 4);
            return new Range(line - 1, column - 1, endLine - 1, endColumn - 1);
        }

        public void Dispose()
        {
            _references.Dispose();
            Analysis.Dispose();
            _metadata?.Dispose();
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
            _released.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        public void Release() => _released.TrySetResult();
    }
}
