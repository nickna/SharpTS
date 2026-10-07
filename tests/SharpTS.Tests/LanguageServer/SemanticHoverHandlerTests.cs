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

public sealed class SemanticHoverHandlerTests
{
    [Fact]
    public async Task ConcurrentAndCachedHoversReuseOneCheckedSnapshot()
    {
        using var project = new HoverProject();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => project.HoverAsync("local")));
        Assert.All(results, hover => project.AssertHover(hover, "local", "number"));
        project.AssertHover(await project.HoverAsync("alias"), "alias", "number");
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits >= 1);
    }

    [Fact]
    public async Task DefaultSemanticMarkupIsPlainText()
    {
        using var project = new HoverProject();
        Hover hover = project.AssertHover(await project.HoverAsync("local"), "local", "number");
        Assert.Equal(MarkupKind.PlainText, hover.Contents.MarkupContent!.Kind);
        Assert.DoesNotContain("```", hover.Contents.MarkupContent.Value);
    }

    [Fact]
    public async Task ImportedAliasUsesExactUtf16CrLfSourceRangeAndDirtyDependencyType()
    {
        using var project = new HoverProject();
        project.AssertHover(await project.HoverAsync("alias"), "alias", "number");
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), "export const value: string = 'dirty';\n", 1));
        project.AssertHover(await project.HoverAsync("alias"), "alias", "string");
        Assert.Equal(2, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task SourceVersionChangedDuringCheckingRefusesOldContentAndRange()
    {
        using var project = new HoverProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.HoverAsync("local");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(project.Store.Open(project.Uri.ToString(), "// moved source\r\n" + HoverProject.Source, 2));
            // Bypass service invalidation so the handler must enforce its captured version itself.
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertHover(await project.HoverAsync("local"), "local", "number");
            Assert.Equal(2, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task DirtyDependencyVersionChangedDuringCheckingRefusesOldType()
    {
        using var project = new HoverProject();
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), HoverProject.Dependency, 1));
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.HoverAsync("local");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(project.Store.Open(project.DependencyUri.ToString(), "export const value: string = 'dirty';\n", 2));
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertHover(await project.HoverAsync("local"), "local", "string");
            Assert.Equal(2, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task ClosedDependencySameLengthAndTimestampMutationRefusesOldType()
    {
        const string changed = "export const value: string = 'x';\n";
        Assert.Equal(HoverProject.Dependency.Length, changed.Length);
        using var project = new HoverProject();
        DateTime stamp = File.GetLastWriteTimeUtc(project.DependencyPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.HoverAsync("local");
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.DependencyPath, changed);
            File.SetLastWriteTimeUtc(project.DependencyPath, stamp);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertHover(await project.HoverAsync("local"), "local", "string");
            Assert.True(project.Analysis.Statistics.Checks >= 2);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOneHoverDoesNotCancelPeerOrCachedResult()
    {
        using var project = new HoverProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.HoverAsync("local", cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.HoverAsync("alias");
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            project.AssertHover(await peer.WaitAsync(TimeSpan.FromSeconds(20)), "alias", "number");
            project.AssertHover(await project.HoverAsync("local"), "local", "number");
            Assert.Equal(1, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task PreCancelledHoverDoesNotStartGeneralAnalysis()
    {
        using var project = new HoverProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await project.HoverAsync("local", cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task InteropOnlyOrdinaryHoverKeepsGeneralAnalysisLazy()
    {
        using var project = new HoverProject(fullMode: false);
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Interop hover started general checking.");
        Assert.Null(await project.HoverAsync("local"));
        Assert.Null(await project.HoverAsync("alias"));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteClrBufferReturnsNoHoverInsteadOfThrowing(bool fullMode)
    {
        const string source = "// DotNetType\nconst stable = 1;\n/*";
        using var project = new HoverProject(source, fullMode);
        Assert.Null(await project.HoverAtAsync(source.IndexOf("stable", StringComparison.Ordinal) + 2));
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecoratorHoverKeepsPriorityWithoutGeneralAnalysis(bool fullMode)
    {
        const string source = "@DotNetType(\"System.String\")\ndeclare class NetString {}\n";
        using var project = new HoverProject(source, fullMode);
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Decorator hover started general checking.");
        Hover hover = Assert.IsType<Hover>(await project.HoverAtAsync(source.IndexOf("DotNetType", StringComparison.Ordinal) + 2));
        Assert.Contains("System.String", hover.Contents.MarkupContent!.Value);
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task MixedClrAndOrdinaryUsageHoversShareGeneralAnalysis()
    {
        const string source = "import { StringBuilder as SB } from 'dotnet:System.Text.StringBuilder';\n" +
            "const builder = new SB();\nbuilder./*clr*/append('x');\nconst count: number = 1;\n/*local*/count;\n";
        using var project = new HoverProject(source);
        Hover clr = Assert.IsType<Hover>(await project.HoverAsync("clr"));
        Assert.Contains("Append", clr.Contents.MarkupContent!.Value);
        project.AssertHover(await project.HoverAsync("local"), "local", "number");
        Assert.Equal(1, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task GuiHoverKeepsPriorityWithoutGeneralAnalysis()
    {
        const string source = "const view = <Button onClick={() => {}}>ok</Button>;";
        string path = System.IO.Path.Combine(FindRoot(), "tests", "fixtures", "SharpTS.Gui.Sdk.Consumer", "main.tsx");
        var store = new DocumentStore();
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        Assert.True(store.Open(uri.ToString(), source, 1));
        using var analysis = new SemanticAnalysisService();
        analysis.BeforeCheck = () => throw new InvalidOperationException("GUI hover started general checking.");
        var members = new MemberHoverService();
        var handler = new HoverHandler(store, new DecoratorService(), members,
            semantic: new SemanticHoverService(analysis, members));
        Hover hover = Assert.IsType<Hover>(await handler.Handle(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, source.IndexOf("Button", StringComparison.Ordinal) + 2),
        }, CancellationToken.None));
        Assert.Contains("Clickable", hover.Contents.MarkupContent!.Value);
        Assert.Equal(0, analysis.Statistics.Builds);
        Assert.Equal(0, analysis.Statistics.Checks);
    }

    private static string FindRoot()
    {
        for (string? path = AppContext.BaseDirectory; path is not null; path = System.IO.Path.GetDirectoryName(path))
            if (File.Exists(System.IO.Path.Combine(path, "SharpTS.sln"))) return path;
        throw new InvalidOperationException("Could not locate SharpTS.sln.");
    }

    private sealed class HoverProject : IDisposable
    {
        public const string Source = "import { value as renamed } from './dependency';\r\n" +
            "const local = /*alias*/renamed;\r\n/* 😀 */ /*local*/local;\r\n";
        public const string Dependency = "export const value: number = 123;\n";
        public TempTestDirectory Directory { get; } = CliTestHelper.CreateTempDirectory();
        public DocumentStore Store { get; } = new();
        public NavigationWorkspaceContext Workspace { get; } = new();
        public SemanticAnalysisService Analysis { get; }
        public string Path { get; }
        public string DependencyPath { get; }
        public DocumentUri Uri => DocumentUri.FromFileSystemPath(Path);
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);
        private readonly HoverHandler _handler;

        public HoverProject(string? source = null, bool fullMode = true)
        {
            Directory.CreateFile("tsconfig.json", """{ "include": ["*.ts"], "compilerOptions": { "noLib": true, "types": [], "experimentalDecorators": true } }""");
            DependencyPath = Directory.CreateFile("dependency.ts", Dependency);
            Path = Directory.CreateFile("entry.ts", source ?? Source);
            Assert.True(Store.Open(Uri.ToString(), source ?? Source, 1));
            Workspace.Initialize(new InitializeParams { RootUri = DocumentUri.FromFileSystemPath(Directory.Path) });
            Analysis = new SemanticAnalysisService(Workspace);
            var members = new MemberHoverService();
            _handler = new HoverHandler(Store, new DecoratorService(), members,
                semantic: fullMode ? new SemanticHoverService(Analysis, members) : null);
        }

        public Task<Hover?> HoverAsync(string marker, CancellationToken cancellationToken = default)
        {
            string text = CurrentText();
            string comment = "/*" + marker + "*/";
            int offset = text.IndexOf(comment, StringComparison.Ordinal);
            Assert.True(offset >= 0, "Missing hover marker " + marker);
            return HoverAtAsync(offset + comment.Length + 1, cancellationToken);
        }

        public Task<Hover?> HoverAtAsync(int offset, CancellationToken cancellationToken = default)
        {
            var (line, column) = new LineIndex(CurrentText()).ToPosition(offset);
            return _handler.Handle(new HoverParams
            {
                TextDocument = new TextDocumentIdentifier(Uri),
                Position = new Position(line - 1, column - 1),
            }, cancellationToken);
        }

        public Hover AssertHover(Hover? result, string marker, string expectedType)
        {
            Hover hover = Assert.IsType<Hover>(result);
            Assert.Contains(expectedType, hover.Contents.MarkupContent!.Value);
            string text = CurrentText();
            string comment = "/*" + marker + "*/";
            int start = text.IndexOf(comment, StringComparison.Ordinal) + comment.Length;
            int end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            var lines = new LineIndex(text);
            var (startLine, startColumn) = lines.ToPosition(start);
            var (endLine, endColumn) = lines.ToPosition(end);
            Assert.Equal(new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1), hover.Range);
            return hover;
        }

        private string CurrentText()
        {
            Assert.True(Store.TryGet(Uri.ToString(), out string text));
            return text;
        }

        public void Dispose()
        {
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
