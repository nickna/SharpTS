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

public sealed class SemanticSignatureHelpHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingNestedCapabilitiesRegisterAndUseConservativeDefaults(int shape)
    {
        using var project = new HandlerProject();
        SignatureHelpCapability? capability = shape switch
        {
            0 => null,
            1 => new(),
            _ => new() { SignatureInformation = new() },
        };
        var registration = project.Register(capability);
        Assert.NotNull(registration.DocumentSelector);
        Assert.Contains("(", registration.TriggerCharacters!);
        Assert.Contains(",", registration.TriggerCharacters!);
        SignatureHelp result = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Null(Assert.Single(result.Signatures).ActiveParameter);
    }

    [Fact]
    public async Task NegotiatedActiveParameterIsProjectedAtBothProtocolLevels()
    {
        using var project = new HandlerProject();
        project.Register(new SignatureHelpCapability
        {
            SignatureInformation = new()
            {
                ActiveParameterSupport = true,
                ParameterInformation = new() { LabelOffsetSupport = true },
            },
        });
        SignatureHelp result = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Equal(0, result.ActiveParameter);
        Assert.Equal(0, Assert.Single(result.Signatures).ActiveParameter);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedSignaturesReuseOneCompletedCheck()
    {
        using var project = new HandlerProject();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => project.HelpAsync()));
        Assert.All(results, help => project.AssertSignature(help, "number"));
        project.AssertSignature(await project.HelpAsync(), "number");
        Assert.Equal(1, project.Analysis.Statistics.Checks);
        Assert.True(project.Analysis.Statistics.CacheHits >= 1);
    }

    [Fact]
    public async Task DirtyImportedAliasChangesCandidatesAndRemovesAnInvalidWinner()
    {
        using var project = new HandlerProject();
        Assert.Equal(0, project.AssertSignature(await project.HelpAsync(), "number").ActiveSignature);
        Assert.True(project.Store.Open(project.DependencyUri.ToString(), HandlerProject.ChangedDependency, 1));
        SignatureHelp changed = project.AssertSignature(await project.HelpAsync(), "string");
        Assert.Null(changed.ActiveSignature);
        Assert.Equal(2, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceOrDirtyDependencyChangedDuringCheckingRefusesCapturedSignature(bool dependency)
    {
        using var project = new HandlerProject();
        if (dependency) Assert.True(project.Store.Open(project.DependencyUri.ToString(), HandlerProject.Dependency, 1));
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.HelpAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            if (dependency)
                Assert.True(project.Store.Open(project.DependencyUri.ToString(), HandlerProject.ChangedDependency, 2));
            else
                Assert.True(project.Store.Open(project.Uri.ToString(), "// moved source\r\n" + HandlerProject.Source, 2));
            // No explicit service invalidation: the handler must validate its capture itself.
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertSignature(await project.HelpAsync(), dependency ? "string" : "number");
            Assert.Equal(2, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task SameLengthAndTimestampClosedDependencyMutationRefusesOldSignature()
    {
        Assert.Equal(HandlerProject.Dependency.Length, HandlerProject.ChangedDependency.Length);
        using var project = new HandlerProject();
        DateTime timestamp = File.GetLastWriteTimeUtc(project.DependencyPath);
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var pending = project.HelpAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            File.WriteAllText(project.DependencyPath, HandlerProject.ChangedDependency);
            File.SetLastWriteTimeUtc(project.DependencyPath, timestamp);
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            project.AssertSignature(await project.HelpAsync(), "string");
            Assert.True(project.Analysis.Statistics.Checks >= 2);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CancellingOneSignatureRequestPreservesPeerAndCachedResult()
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        project.Analysis.BeforeCheck = gate.HoldFirstCheck;
        try
        {
            var cancelled = project.HelpAsync(cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = project.HelpAsync();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            project.AssertSignature(await peer.WaitAsync(TimeSpan.FromSeconds(20)), "number");
            project.AssertSignature(await project.HelpAsync(), "number");
            Assert.Equal(1, project.Analysis.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task PreCancelledSignatureRequestDoesNotStartGeneralAnalysis()
    {
        using var project = new HandlerProject();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await project.HelpAsync(cancellation.Token));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task InteropOnlyOrdinarySignaturesKeepGeneralAnalysisLazy()
    {
        using var project = new HandlerProject(full: false);
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Interop signature help started checking.");
        Assert.Null(await project.HelpAsync());
        Assert.Equal(0, project.Analysis.Statistics.Builds);
        Assert.Equal(0, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecoratorSignatureKeepsPriorityWithoutGeneralAnalysis(bool full)
    {
        using var project = new HandlerProject(full);
        const string source = "@DotNetType(\"System.String\")\r\ndeclare class NetString {}\r\n";
        Assert.True(project.Store.Open(project.Uri.ToString(), source, 2));
        project.Analysis.BeforeCheck = () => throw new InvalidOperationException("Decorator signature started checking.");
        SignatureHelp help = Assert.IsType<SignatureHelp>(await project.HelpAtAsync(source.IndexOf('(') + 1));
        Assert.Equal("DotNetType(typeName: string)", Assert.Single(help.Signatures).Label);
        Assert.Equal(0, help.ActiveParameter);
        Assert.Equal(0, project.Analysis.Statistics.Builds);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(99, 99)]
    public async Task InvalidPositionsNeverClampToARealSignatureOrStartAnalysis(int line, int character)
    {
        using var project = new HandlerProject();
        Assert.Null(await project.HandleAsync(new Position(line, character)));
        Assert.Equal(0, project.Analysis.Statistics.Builds);
    }

    private sealed class HandlerProject : IDisposable
    {
        public const string Source = "import { pick as local } from './dependency';\r\n/* 😀 */ local(/*caret*/1);\r\n";
        public const string Dependency = "export function pick(value: number): number { return value; }\n";
        public const string ChangedDependency = "export function pick(value: string): string { return value; }\n";
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly SignatureHelpHandler _handler;
        public DocumentStore Store { get; } = new();
        public SemanticAnalysisService Analysis { get; } = new();
        public DocumentUri Uri { get; }
        public string DependencyPath { get; }
        public DocumentUri DependencyUri => DocumentUri.FromFileSystemPath(DependencyPath);

        public HandlerProject(bool full = true)
        {
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[],"experimentalDecorators":true},"include":["*.ts"]}""");
            DependencyPath = _directory.CreateFile("dependency.ts", Dependency);
            Uri = DocumentUri.FromFileSystemPath(_directory.CreateFile("main.ts", Source));
            Assert.True(Store.Open(Uri.ToString(), Source, 1));
            _handler = new(Store, new DecoratorService(), semantic: full ? new SemanticSignatureHelpService(Analysis) : null);
        }

        public SignatureHelpRegistrationOptions Register(SignatureHelpCapability? capability) =>
            ((IRegistration<SignatureHelpRegistrationOptions, SignatureHelpCapability>)_handler)
                .GetRegistrationOptions(capability!, new ClientCapabilities());

        public Task<SignatureHelp?> HelpAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(Store.TryGet(Uri.ToString(), out var source));
            int offset = source.IndexOf("/*caret*/", StringComparison.Ordinal);
            Assert.True(offset >= 0);
            return HelpAtAsync(offset + "/*caret*/".Length, cancellationToken);
        }

        public Task<SignatureHelp?> HelpAtAsync(int offset, CancellationToken cancellationToken = default)
        {
            Assert.True(Store.TryGet(Uri.ToString(), out var source));
            var (line, column) = new LineIndex(source).ToPosition(offset);
            return HandleAsync(new Position(line - 1, column - 1), cancellationToken);
        }

        public Task<SignatureHelp?> HandleAsync(Position position, CancellationToken cancellationToken = default) =>
            _handler.Handle(new SignatureHelpParams
            {
                TextDocument = new TextDocumentIdentifier(Uri), Position = position,
            }, cancellationToken);

        public SignatureHelp AssertSignature(SignatureHelp? help, string type)
        {
            var result = Assert.IsType<SignatureHelp>(help);
            Assert.Contains("value: " + type, Assert.Single(result.Signatures).Label);
            return result;
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
            if (!_release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Signature help test gate timed out.");
        }
        public void Release() => _release.Set();
    }
}
