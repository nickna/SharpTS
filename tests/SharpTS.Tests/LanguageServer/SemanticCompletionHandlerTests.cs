using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class SemanticCompletionHandlerTests
{
    [Fact]
    public void UnspecifiedHoverAndCompletionCapabilitiesCanRegisterWithoutThrowing()
    {
        var store = new DocumentStore();
        var decorators = new DecoratorService();
        var members = new MemberHoverService();
        var hover = new HoverHandler(store, decorators, members);
        var completion = new CompletionHandler(store, decorators);
        var client = new ClientCapabilities();

        var hoverOptions = ((IRegistration<HoverRegistrationOptions, HoverCapability>)hover)
            .GetRegistrationOptions(null!, client);
        var completionOptions = ((IRegistration<CompletionRegistrationOptions, CompletionCapability>)completion)
            .GetRegistrationOptions(null!, client);

        Assert.NotNull(hoverOptions.DocumentSelector);
        Assert.Contains("@", completionOptions.TriggerCharacters!);
        Assert.DoesNotContain(".", completionOptions.TriggerCharacters!);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedCompletionsShareOneCompletedCheck()
    {
        using var project = new HandlerProject();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => project.CompleteAsync()));
        Assert.All(results, result => Assert.Contains("number", Assert.Single(result.Items).Detail));
        Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits > 0);
    }

    [Fact]
    public async Task DirtyDependencyChangesTheNewCompletionDetail()
    {
        using var project = new HandlerProject();
        Assert.Contains("number", Assert.Single((await project.CompleteAsync()).Items).Detail);
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), "export const value: string = 'dirty';\n", 1));
        Assert.Contains("string", Assert.Single((await project.CompleteAsync()).Items).Detail);
        Assert.Equal(2, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSourceOrDependencyDuringCheckRefusesTheOldCompletion(bool dependency)
    {
        using var project = new HandlerProject();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.CompleteAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            if (dependency)
                Assert.True(project.Store.Open(project.DependencyUri.ToString(), "export const value: string = 'dirty';\n", 2));
            else
                Assert.True(project.Store.Open(project.Uri.ToString(), "// moved\n" + HandlerProject.Source, 2));
            gate.Release();
            Assert.Empty((await pending.WaitAsync(TimeSpan.FromSeconds(20))).Items);
            Assert.Single((await project.CompleteAsync()).Items);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task SameLengthClosedDependencyMutationRefusesOldCompletionDetail()
    {
        const string changed = "export const value: string = 'x';\n";
        Assert.Equal(HandlerProject.Dependency.Length, changed.Length);
        using var project = new HandlerProject();
        DateTime timestamp = File.GetLastWriteTimeUtc(project.DependencyPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.CompleteAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.DependencyPath, changed);
            File.SetLastWriteTimeUtc(project.DependencyPath, timestamp);
            gate.Release();
            Assert.Empty((await pending.WaitAsync(TimeSpan.FromSeconds(20))).Items);
            Assert.Contains("string", Assert.Single((await project.CompleteAsync()).Items).Detail);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOneCompletionPreservesTheSharedBuildForItsPeer()
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.CompleteAsync(cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.CompleteAsync();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            Assert.Single((await peer.WaitAsync(TimeSpan.FromSeconds(20))).Items);
            Assert.Single((await project.CompleteAsync()).Items);
            Assert.Equal(1, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task PreCancelledCompletionDoesNotStartChecking()
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await project.CompleteAsync(cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
    }

    [Fact]
    public async Task InteropOnlyOrdinaryCompletionDoesNotStartGeneralAnalysis()
    {
        using var project = new HandlerProject(full: false);
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Interop completion started checking.");
        Assert.Empty((await project.CompleteAsync()).Items);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecoratorCompletionKeepsPriorityWithoutChecking(bool full)
    {
        using var project = new HandlerProject(full);
        const string source = "@Dot";
        Assert.True(project.Store.Open(project.Uri.ToString(), source, 2));
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Decorator completion started checking.");
        var result = await project.CompleteAtAsync(source.Length);
        Assert.Contains(result.Items, item => item.Label == "DotNetType");
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task GuiCompletionKeepsPriorityWithoutChecking()
    {
        const string source = "const view = <But";
        string path = System.IO.Path.Combine(FindRoot(), "tests", "fixtures", "SharpTS.Gui.Sdk.Consumer", "main.tsx");
        var store = new DocumentStore();
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        Assert.True(store.Open(uri.ToString(), source, 1));
        using var analysis = new SemanticAnalysisService();
        analysis.BeforeCheck = () => throw new InvalidOperationException("GUI completion started checking.");
        var handler = new CompletionHandler(store, new DecoratorService(), semantic: new SemanticCompletionService(analysis));
        var result = await handler.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, source.Length),
        }, CancellationToken.None);
        Assert.Contains(result.Items, item => item.Label == "Button");
        Assert.Equal(0, analysis.Statistics.Checks);
    }

    private static string FindRoot()
    {
        for (string? path = AppContext.BaseDirectory; path is not null; path = System.IO.Path.GetDirectoryName(path))
            if (File.Exists(System.IO.Path.Combine(path, "SharpTS.sln"))) return path;
        throw new InvalidOperationException("Could not locate SharpTS.sln.");
    }

    private sealed class HandlerProject : IDisposable
    {
        public const string Source = "import { value as renamed } from './dependency';\nconst visible = renamed;\nvi;\n";
        public const string Dependency = "export const value: number = 123;\n";
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly CompletionHandler _handler;
        public DocumentStore Store { get; } = new();
        public SemanticAnalysisService Analysis { get; } = new();
        public DocumentUri Uri { get; }
        public string DependencyPath { get; }
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);

        public HandlerProject(bool full = true)
        {
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts"]}""");
            DependencyPath = _directory.CreateFile("dependency.ts", Dependency);
            Uri = DocumentUri.FromFileSystemPath(_directory.CreateFile("main.ts", Source));
            Assert.True(Store.Open(Uri.ToString(), Source, 1));
            _handler = new(Store, new DecoratorService(), semantic: full ? new SemanticCompletionService(Analysis) : null);
        }

        public Task<CompletionList> CompleteAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(Store.TryGet(Uri.ToString(), out var source));
            int offset = source.LastIndexOf("vi;", StringComparison.Ordinal) + 2;
            return CompleteAtAsync(offset, cancellationToken);
        }

        public Task<CompletionList> CompleteAtAsync(int offset, CancellationToken cancellationToken = default)
        {
            Assert.True(Store.TryGet(Uri.ToString(), out var source));
            var (line, column) = new LineIndex(source).ToPosition(offset);
            return _handler.Handle(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = new Position(line - 1, column - 1),
            }, cancellationToken);
        }

        public void Dispose()
        {
            Analysis.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class CheckGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        private int _checks;
        public Task Entered => _entered.Task;
        public void HoldFirstCheck()
        {
            if (Interlocked.Increment(ref _checks) != 1) return;
            _entered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Completion test gate timed out.");
        }
        public void Release() => _release.Set();
    }
}
