using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class MemberReferenceHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclarationFilteringIncludesExactClosedReverseImporterRanges(bool includeDeclaration)
    {
        using var project = new MemberProject();
        project.AssertField(await project.ReferencesAsync(includeDeclaration: includeDeclaration), includeDeclaration);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedMemberReferencesReuseTheCompletedWorkspaceChecks()
    {
        using var project = new MemberProject();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => project.ReferencesAsync()));
        Assert.All(results, result => project.AssertField(result));
        long completedChecks = project.Analysis.Statistics.Checks;
        Assert.True(completedChecks > 0);
        project.AssertField(await project.ReferencesAsync());
        project.AssertField(await project.ReferencesAsync(includeDeclaration: false), includeDeclaration: false);
        Assert.Equal(completedChecks, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits > 0);
    }

    [Fact]
    public async Task DirtyOwnerChangesTheCanonicalAnchorAndAllDeclarationRanges()
    {
        using var project = new MemberProject();
        project.AssertField(await project.ReferencesAsync());
        string dirty = "// shifted dirty owner\r\n" + MemberProject.Owner;
        Assert.True(project.Store.Open(project.OwnerUri.ToString(), dirty, 1));
        project.AssertField(await project.ReferencesAsync(), owner: dirty);
        string changed = "// shifted again\r\n" + dirty;
        Assert.True(project.Store.Open(project.OwnerUri.ToString(), changed, 2));
        project.AssertField(await project.ReferencesAsync(), owner: changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceOrDirtyOwnerVersionChangedDuringCheckingRefusesTheCapturedMemberSet(bool owner)
    {
        using var project = new MemberProject();
        if (owner) Assert.True(project.Store.Open(project.OwnerUri.ToString(), MemberProject.Owner, 1));
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.ReferencesAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            if (owner)
                Assert.True(project.Store.Open(project.OwnerUri.ToString(), "// new owner\r\n" + MemberProject.Owner, 2));
            else
                Assert.True(project.Store.Open(project.Uri.ToString(), "// new source\r\n" + MemberProject.Source, 2));
            // Bypass notification invalidation to exercise the final captured-version guard.
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.ReferencesAsync(),
                owner: owner ? "// new owner\r\n" + MemberProject.Owner : null,
                source: owner ? null : "// new source\r\n" + MemberProject.Source);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task SameLengthAndTimestampClosedOwnerMutationCannotPublishOldDeclarationRanges()
    {
        string original = "// padding\r\n" + MemberProject.Owner;
        string changed = MemberProject.Owner + "// padding\r\n";
        Assert.Equal(original.Length, changed.Length);
        using var project = new MemberProject(original);
        DateTime timestamp = File.GetLastWriteTimeUtc(project.OwnerPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.ReferencesAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.OwnerPath, changed);
            File.SetLastWriteTimeUtc(project.OwnerPath, timestamp);
            gate.Release();
            AssertNoLocations(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.ReferencesAsync(), owner: changed);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task ClosedReverseImporterMutationCannotPublishAnObsoleteOccurrenceRange()
    {
        using var project = new MemberProject();
        string changed = "// closed importer moved\r\n" + MemberProject.Reverse;
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.ReferencesAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.ReversePath, changed);
            gate.Release();
            AssertNoLocations(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.ReferencesAsync(), reverse: changed);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task ConfigMembershipChangedDuringCheckingCannotPublishTheRemovedRootOccurrence()
    {
        using var project = new MemberProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.ReferencesAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.AppConfigPath,
                """{"compilerOptions":{"noLib":true,"types":[]},"files":["main.ts"],"references":[{"path":"../lib"}]}""");
            gate.Release();
            AssertNoLocations(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.ReferencesAsync(), includeReverse: false);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task WorkspaceFolderChangedDuringCheckingRefusesTheOldMemberDomain()
    {
        using var project = new MemberProject();
        using var added = CliTestHelper.CreateTempDirectory();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.ReferencesAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            project.Workspace.Change([added.Path], []);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOneMemberRequestPreservesItsWorkspacePeerAndWarmResult()
    {
        using var project = new MemberProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.ReferencesAsync(cancellationToken: cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.ReferencesAsync();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            project.AssertField(await peer.WaitAsync(TimeSpan.FromSeconds(20)));
            long completedChecks = project.Analysis.Statistics.Checks;
            project.AssertField(await project.ReferencesAsync());
            Assert.Equal(completedChecks, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task PreCancelledMemberRequestStartsNoSharedChecking()
    {
        using var project = new MemberProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await project.ReferencesAsync(cancellationToken: cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
    }

    [Fact]
    public async Task IncompleteWorkspaceRefusesNewMemberReferencesAndPreservesLexicalReferences()
    {
        using var project = new MemberProject();
        project.Directory.CreateFile("broken/tsconfig.json", "{ invalid config");
        AssertNoLocations(await project.ReferencesAsync());
        var lexical = Assert.IsAssignableFrom<LocationContainer>(await project.ReferencesAsync("lexicalUse"));
        Assert.Equal(2, lexical.Count());
        Assert.All(lexical, location => Assert.Equal(project.Uri, location.Uri));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("token")]
    public async Task CompleteMemberReferencesNeverAuthorizePublicOrParameterPropertyRename(string member)
    {
        using var project = new MemberProject();
        string marker = member == "field" ? "read" : "token";
        Assert.NotEmpty(Assert.IsAssignableFrom<LocationContainer>(await project.ReferencesAsync(marker)));
        Assert.True(project.Store.TryCapture(project.Uri.ToString(), out var capture));
        var domain = await project.References.FindReferenceResultAsync(capture, project.Position(marker), true,
            project.Workspace.SnapshotRoots());
        Assert.True(domain.IsComplete);
        Assert.False(domain.IsRenameEligible);
        var rename = new RenameService(project.References);
        Assert.Null(await new PrepareRenameHandler(project.Store, rename, project.Workspace).Handle(new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier(project.Uri), Position = project.Position(marker),
        }, CancellationToken.None));
        Assert.Null(await new RenameHandler(project.Store, rename, project.Workspace).Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(project.Uri), Position = project.Position(marker), NewName = "nextMember",
        }, CancellationToken.None));
    }

    private static void AssertNoLocations(LocationContainer? result)
    {
        if (result is not null) Assert.Empty(result);
    }

    private sealed class MemberProject : IDisposable
    {
        public const string Owner = "export class Base {\r\n  /* 😀 */ /*decl*/field: number = 1;\r\n" +
            "  constructor(public /*parameter*/token: number = 1) {}\r\n" +
            "  read(): number { return this./*self*/field; }\r\n}\r\nexport class Derived extends Base {}\r\n";
        public const string Source = "import { Derived } from '../lib/model';\r\nconst item = new Derived(1);\r\n" +
            "const /*lexicalDeclaration*/lexical = 1;\r\n/* 😀 */ item./*read*/field;\r\n" +
            "item./*token*/token;\r\n/*lexicalUse*/lexical;\r\n";
        public const string Reverse = "import { Base } from '../lib/model';\r\nconst closed = new Base(1);\r\n" +
            "/* 😀 */ closed./*reverse*/field;\r\n";
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public NavigationWorkspaceContext Workspace { get; } = new();
        public SemanticAnalysisService Analysis { get; }
        public ReferenceService References { get; }
        public string AppConfigPath { get; }
        public string OwnerPath { get; }
        public string ReversePath { get; }
        public DocumentUri OwnerUri => DocumentUri.FromFileSystemPath(OwnerPath);
        public DocumentUri Uri { get; }
        private readonly ReferencesHandler _handler;
        private readonly string _owner;

        public MemberProject(string? owner = null)
        {
            _owner = owner ?? Owner;
            Directory.CreateFile("lib/tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"files":["model.ts"]}""");
            AppConfigPath = Directory.CreateFile("app/tsconfig.json",
                """{"compilerOptions":{"noLib":true,"types":[]},"files":["main.ts","reverse.ts"],"references":[{"path":"../lib"}]}""");
            OwnerPath = Directory.CreateFile("lib/model.ts", _owner);
            ReversePath = Directory.CreateFile("app/reverse.ts", Reverse);
            Uri = DocumentUri.FromFileSystemPath(Directory.CreateFile("app/main.ts", Source));
            Assert.True(Store.Open(Uri.ToString(), Source, 1));
            Workspace.Initialize(new InitializeParams { RootUri = DocumentUri.FromFileSystemPath(Directory.Path) });
            Analysis = new SemanticAnalysisService(Workspace);
            References = new ReferenceService(Analysis);
            _handler = new ReferencesHandler(Store, References, Workspace);
        }

        public Position Position(string marker = "read")
        {
            Assert.True(Store.TryGet(Uri.ToString(), out var text));
            string comment = "/*" + marker + "*/";
            int start = text.IndexOf(comment, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing marker " + marker);
            var (line, column) = new LineIndex(text).ToPosition(start + comment.Length + 1);
            return new(line - 1, column - 1);
        }

        public Task<LocationContainer?> ReferencesAsync(string marker = "read", bool includeDeclaration = true,
            CancellationToken cancellationToken = default) => _handler.Handle(new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = Position(marker),
                Context = new ReferenceContext { IncludeDeclaration = includeDeclaration },
            }, cancellationToken);

        public void AssertField(LocationContainer? result, bool includeDeclaration = true, string? owner = null,
            string? source = null, string? reverse = null, bool includeReverse = true)
        {
            var actual = Assert.IsAssignableFrom<LocationContainer>(result).ToArray();
            var expected = new List<Location>
            {
                At(OwnerUri, owner ?? _owner, "self", "field"),
                At(Uri, source ?? Source, "read", "field"),
            };
            if (includeDeclaration) expected.Add(At(OwnerUri, owner ?? _owner, "decl", "field"));
            if (includeReverse) expected.Add(At(DocumentUri.FromFileSystemPath(ReversePath), reverse ?? Reverse, "reverse", "field"));
            static IEnumerable<Location> Order(IEnumerable<Location> values) => values.OrderBy(value => value.Uri.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Range.Start.Line).ThenBy(value => value.Range.Start.Character)
                .ThenBy(value => value.Range.End.Line).ThenBy(value => value.Range.End.Character);
            static string Key(Location value) => $"{value.Uri}|{value.Range.Start.Line}:{value.Range.Start.Character}-" +
                $"{value.Range.End.Line}:{value.Range.End.Character}";
            Assert.Equal(Order(expected).Select(Key).ToArray(), actual.Select(Key).ToArray());
            Assert.Equal(actual.Length, actual.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        private static Location At(DocumentUri uri, string text, string marker, string spelling)
        {
            string comment = "/*" + marker + "*/";
            int offset = text.IndexOf(comment, StringComparison.Ordinal) + comment.Length;
            Assert.True(offset >= comment.Length);
            Assert.Equal(spelling, text.Substring(offset, spelling.Length));
            var lines = new LineIndex(text);
            var (startLine, startColumn) = lines.ToPosition(offset);
            var (endLine, endColumn) = lines.ToPosition(offset + spelling.Length);
            return new Location { Uri = uri, Range = new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1) };
        }

        public void Dispose()
        {
            References.Dispose();
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
            _released.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        public void Release() => _released.TrySetResult();
    }
}
