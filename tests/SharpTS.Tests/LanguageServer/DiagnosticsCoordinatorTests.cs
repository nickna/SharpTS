using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class DiagnosticsCoordinatorTests
{
    [Theory]
    [InlineData(DiagnosticPublishMode.SharpTsOnly)]
    [InlineData(DiagnosticPublishMode.All)]
    public async Task PersistentFailureDoesNotStarveAnotherQueuedDocument(DiagnosticPublishMode mode)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string brokenSource = "@DotNetType('Injected.Type') declare class Injected {}";
        string brokenUri = new Uri(directory.CreateFile("broken.ts", brokenSource)).AbsoluteUri;
        string validUri = new Uri(directory.CreateFile("valid.ts", "const value = 1;")).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(brokenUri, brokenSource, version: 1);
        store.Open(validUri, "const value = 1;", version: 1);
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(
            resolve: _ => throw new InvalidOperationException("persistent interop failure"), analysis: analysis);
        var failures = new ConcurrentQueue<Exception>();
        var published = new ConcurrentQueue<PublishDiagnosticsParams>();
        using var coordinator = new DiagnosticsCoordinator(store, diagnostics, new DocumentDependencyGraph(),
            new DiagnosticsSettings(mode), published.Enqueue, TimeSpan.FromMilliseconds(25), failures.Enqueue);

        coordinator.Queue(brokenUri);
        coordinator.Queue(validUri);
        await coordinator.DrainAsync();

        Assert.Contains("persistent interop failure", Assert.Single(failures).Message);
        PublishDiagnosticsParams valid = Assert.Single(published);
        Assert.Equal(validUri, valid.Uri.ToString(), ignoreCase: true);
        Assert.Empty(valid.Diagnostics);
        published.Clear();

        // The failed URI is retried on the next external event, while unrelated work still runs.
        coordinator.Queue(validUri);
        await coordinator.DrainAsync();
        Assert.Equal(2, failures.Count);
        Assert.Equal(validUri, Assert.Single(published).Uri.ToString(), ignoreCase: true);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(25, false)]
    [InlineData(0, true)]
    [InlineData(25, true)]
    public async Task AnalysisFailureIsReportedWithDetailsAndNextEditCanPublish(
        int debounceMilliseconds, bool useHostChannel)
    {
        AsyncLocalConsoleRedirector.Install();
        using var stderr = new StringWriter();
        using var captureError = AsyncLocalConsoleRedirector.WithErr(stderr);
        using var stdout = AsyncLocalConsoleRedirector.Capture();
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "@DotNetType('Injected.Type') declare class Injected {}";
        string path = directory.CreateFile("input.ts", source);
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(uri, source, version: 1);
        var failure = new InvalidOperationException("analysis defect", new Exception("original cause"));
        List<Exception> failures = [];
        List<PublishDiagnosticsParams> published = [];
        using var coordinator = new DiagnosticsCoordinator(
            store,
            new DiagnosticsService(resolve: _ => throw failure),
            new DocumentDependencyGraph(),
            new DiagnosticsSettings(DiagnosticPublishMode.All),
            published.Add,
            TimeSpan.FromMilliseconds(debounceMilliseconds),
            useHostChannel ? null : failures.Add);

        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        if (useHostChannel)
        {
            Assert.Empty(failures);
            Assert.Contains(failure.ToString(), stderr.ToString());
        }
        else
        {
            Assert.Same(failure, Assert.Single(failures));
            Assert.Equal("", stderr.ToString());
        }
        Assert.Equal("", stdout.GetOutput());
        Assert.Contains("original cause", failure.ToString());
        Assert.Contains("InteropAnalyzer", failure.StackTrace);
        Assert.Empty(published);
        string report = stderr.ToString();

        store.Open(uri, "const value = 1;", version: 2);
        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        Assert.Equal(useHostChannel ? 0 : 1, failures.Count);
        Assert.Equal(report, stderr.ToString());
        Assert.Equal(2, Assert.Single(published).Version);
    }

    [Fact]
    public async Task AnalysisCancellationIsObservedWithoutFailureOrPublication()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "@DotNetType('Injected.Type') declare class Injected {}";
        string path = directory.CreateFile("input.ts", source);
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(uri, source, version: 1);
        List<Exception> failures = [];
        List<PublishDiagnosticsParams> published = [];
        using var coordinator = new DiagnosticsCoordinator(
            store,
            new DiagnosticsService(resolve: _ => throw new OperationCanceledException()),
            new DocumentDependencyGraph(),
            new DiagnosticsSettings(),
            published.Add,
            TimeSpan.FromMilliseconds(25),
            failures.Add);

        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        Assert.Empty(failures);
        Assert.Empty(published);
    }

    [Fact]
    public async Task MalformedSourcePublishesDiagnosticsWithoutReportingInternalFailure()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "let broken = ;\nlet value: number = 'wrong';";
        string path = directory.CreateFile("input.ts", source);
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(uri, source, version: 1);
        List<Exception> failures = [];
        List<PublishDiagnosticsParams> published = [];
        using var coordinator = new DiagnosticsCoordinator(
            store,
            new DiagnosticsService(),
            new DocumentDependencyGraph(),
            new DiagnosticsSettings(DiagnosticPublishMode.All),
            published.Add,
            TimeSpan.Zero,
            failures.Add);

        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        Assert.Empty(failures);
        Assert.NotEmpty(Assert.Single(published).Diagnostics);
    }
}
