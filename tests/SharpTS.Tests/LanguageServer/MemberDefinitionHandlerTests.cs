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

public sealed class MemberDefinitionHandlerTests
{
    [Fact]
    public async Task UnchangedMemberRequestsShareTheCheckedSnapshot()
    {
        using var project = new MemberProject();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => project.DefinitionAsync()));
        Assert.All(results, result => project.AssertField(result));
        project.AssertField(await project.DefinitionAsync());
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits >= 1);
    }

    [Fact]
    public async Task NewSourceVersionDuringCheckingRefusesTheCapturedMemberLocation()
    {
        using var project = new MemberProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.DefinitionAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(project.Store.Open(project.Uri.ToString(), "// new source version\n" + MemberProject.Source, 2));
            // No service invalidation: the handler must independently reject its captured version.
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.DefinitionAsync());
            Assert.Equal(2, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task NewDirtyDependencyVersionDuringCheckingRefusesTheOldMemberRange()
    {
        using var project = new MemberProject();
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), project.Dependency, 1));
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        string dirty = "// new dependency version\r\n" + project.Dependency;
        try
        {
            var pending = project.DefinitionAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(project.Store.Open(project.DependencyUri.ToString(), dirty, 2));
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.DefinitionAsync(), dirty);
            Assert.Equal(2, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task SameLengthAndTimestampClosedDependencyEditRefusesTheOldMemberRange()
    {
        string original = "// padding\r\n" + MemberProject.DefaultDependency;
        string changed = MemberProject.DefaultDependency + "// padding\r\n";
        Assert.Equal(original.Length, changed.Length);
        using var project = new MemberProject(original);
        DateTime stamp = File.GetLastWriteTimeUtc(project.DependencyPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.DefinitionAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.DependencyPath, changed);
            File.SetLastWriteTimeUtc(project.DependencyPath, stamp);
            gate.Release();
            AssertNoLocations(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.DefinitionAsync(), changed);
            Assert.True(project.Analysis.Statistics.Checks >= 2);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOneMemberRequestDoesNotCancelItsPeerOrCachedResult()
    {
        using var project = new MemberProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.DefinitionAsync(cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.DefinitionAsync();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            project.AssertField(await peer.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertField(await project.DefinitionAsync());
            Assert.Equal(1, project.Analysis.Statistics.Checks);
            Assert.True(project.Analysis.Statistics.CacheHits >= 1);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task PreCancelledMemberRequestDoesNotStartChecking()
    {
        using var project = new MemberProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await project.DefinitionAsync(cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    private static void AssertNoLocations(LocationOrLocationLinks? result)
    {
        if (result is not null) Assert.Empty(result);
    }

    private sealed class MemberProject : IDisposable
    {
        public const string Source = "import { Derived as Renamed } from './dependency';\nconst item = new Renamed();\nitem./*field*/field;\n";
        public const string DefaultDependency = "export class Base {\r\n  /* 😀 */ field: number = 1;\r\n}\r\nexport class Derived extends Base {}\r\n";
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public NavigationWorkspaceContext Workspace { get; } = new();
        public SemanticAnalysisService Analysis { get; }
        public string Dependency { get; }
        public string DependencyPath { get; }
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);
        public string Path { get; }
        public DocumentUri Uri => DocumentUri.FromFileSystemPath(Path);
        private readonly DefinitionService _definitions;
        private readonly DefinitionHandler _handler;

        public MemberProject(string? dependency = null)
        {
            Dependency = dependency ?? DefaultDependency;
            Directory.CreateFile("tsconfig.json", """{ "include": ["*.ts"], "compilerOptions": { "noLib": true, "types": [] } }""");
            DependencyPath = Directory.CreateFile("dependency.ts", Dependency);
            Path = Directory.CreateFile("entry.ts", Source);
            Assert.True(Store.Open(Uri.ToString(), Source, 1));
            Workspace.Initialize(new InitializeParams { RootUri = DocumentUri.FromFileSystemPath(Directory.Path) });
            Analysis = new SemanticAnalysisService(Workspace);
            _definitions = new DefinitionService(Analysis);
            _handler = new DefinitionHandler(Store, _definitions);
        }

        public Task<LocationOrLocationLinks?> DefinitionAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(Store.TryGet(Uri.ToString(), out string text));
            int offset = text.IndexOf("/*field*/", StringComparison.Ordinal) + "/*field*/".Length + 1;
            var (line, column) = new LineIndex(text).ToPosition(offset);
            return _handler.Handle(new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(Uri),
                Position = new Position(line - 1, column - 1),
            }, cancellationToken);
        }

        public void AssertField(LocationOrLocationLinks? result, string? dependency = null)
        {
            var target = Assert.Single(Assert.IsAssignableFrom<LocationOrLocationLinks>(result));
            Location location = Assert.IsType<Location>(target.Location);
            Assert.Equal(DependencyUri, location.Uri);
            string text = dependency ?? Dependency;
            int offset = text.IndexOf("field:", StringComparison.Ordinal);
            var lines = new LineIndex(text);
            var (startLine, startColumn) = lines.ToPosition(offset);
            var (endLine, endColumn) = lines.ToPosition(offset + "field".Length);
            Assert.Equal(new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1), location.Range);
        }

        public void Dispose()
        {
            _definitions.Dispose();
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
