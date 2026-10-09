using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class SharedDiagnosticsTests
{
    [Fact]
    public async Task StatementsDiagnosticsAndDefinitionReuseOneCheckedCapture()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "import { value } from './dep';\nconst result: number = value;";
        string path = CreateProject(directory, source);
        DocumentRequestSnapshot capture = Capture(path, source);
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(analysis: analysis);
        using var definitions = new DefinitionService(analysis);

        var statements = await diagnostics.GetStatementsAsync(capture, DiagnosticPublishMode.All, CancellationToken.None);
        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture);
        Assert.NotNull(lease);
        Assert.True(lease.Model.Snapshot.TryGetDocument(path, out AnalysisDocument? document));
        Assert.Same(document!.Statements, statements);

        DiagnosticsAnalysisResult result = await diagnostics.AnalyzeResultAsync(
            capture, capture.Document, DiagnosticPublishMode.All, CancellationToken.None);
        NavigationDefinitionResult definition = await definitions.FindDefinitionsAsync(
            capture, new Position(1, "const result: number = ".Length + 1), CancellationToken.None);

        Assert.Empty(result.Diagnostics);
        Assert.Single(definition.Locations);
        Assert.True(result.IsCurrent(CancellationToken.None));
        Assert.Equal(1, analysis.Statistics.Builds);
        Assert.Equal(1, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task SharpTsOnlyStatementsAndDiagnosticsDoNotBuildGeneralSemantics()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "const value: string = 1;";
        string path = directory.CreateFile("main.ts", source);
        DocumentRequestSnapshot capture = Capture(path, source);
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(analysis: analysis);

        Assert.NotEmpty(await diagnostics.GetStatementsAsync(capture, DiagnosticPublishMode.SharpTsOnly, CancellationToken.None));
        DiagnosticsAnalysisResult result = await diagnostics.AnalyzeResultAsync(
            capture, capture.Document, DiagnosticPublishMode.SharpTsOnly, CancellationToken.None);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, analysis.Statistics.Builds);
        Assert.Equal(0, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task ClosedDependencyEditRefreshesFullDiagnosticsWithoutAnOpenBufferEdit()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "import { value } from './dep';\nconst result: number = value;";
        string path = CreateProject(directory, source);
        DocumentRequestSnapshot capture = Capture(path, source);
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(analysis: analysis);

        DiagnosticsAnalysisResult first = await diagnostics.AnalyzeResultAsync(
            capture, capture.Document, DiagnosticPublishMode.All, CancellationToken.None);
        Assert.Empty(first.Diagnostics);
        File.WriteAllText(directory.GetPath("dep.ts"), "export const value: string = 'changed';");

        // The shared lease has already been disposed; its publication proof remains usable.
        Assert.False(first.IsCurrent(CancellationToken.None));
        DiagnosticsAnalysisResult second = await diagnostics.AnalyzeResultAsync(
            capture, capture.Document, DiagnosticPublishMode.All, CancellationToken.None);

        Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
        Assert.True(second.IsCurrent(CancellationToken.None));
        Assert.Equal(2, analysis.Statistics.Builds);
    }

    [Fact]
    public async Task CoordinatorRefusesADependencyMutationAfterCheckingAndCanPublishTheNextCapture()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "import { value } from './dep';\n@DotNetType('System.String') declare class NetString {}\nconst result: number = value;";
        string path = CreateProject(directory, source);
        string uri = new Uri(path).AbsoluteUri;
        var store = new DocumentStore();
        store.Open(uri, source, version: 1);
        using var analysis = new SemanticAnalysisService();
        bool mutate = true;
        using var diagnostics = new DiagnosticsService(resolve: _ =>
        {
            if (mutate)
            {
                mutate = false;
                File.WriteAllText(directory.GetPath("dep.ts"), "export const value: string = 'changed';");
            }
            return typeof(string);
        }, analysis: analysis);
        List<PublishDiagnosticsParams> published = [];
        List<Exception> failures = [];
        using var coordinator = new DiagnosticsCoordinator(store, diagnostics, new DocumentDependencyGraph(),
            new DiagnosticsSettings(DiagnosticPublishMode.All), published.Add, TimeSpan.Zero, failures.Add);

        coordinator.Queue(uri);
        await coordinator.DrainAsync();
        Assert.Empty(published);
        Assert.Empty(failures);

        coordinator.Queue(uri);
        await coordinator.DrainAsync();

        Assert.Empty(failures);
        Assert.Contains(Assert.Single(published).Diagnostics,
            diagnostic => diagnostic.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AlternateSeparatorSnapshotReusesSharedAstAndKeepsModuleDiagnostics()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "import { value } from './dep';\nconst result: string = value;";
        string path = CreateProject(directory, source);
        string alternatePath = path.Replace('\\', '/');
        var document = new DocumentSnapshot(new Uri(path).AbsoluteUri, source, 1, alternatePath);
        var capture = new DocumentRequestSnapshot(document, 1,
            new Dictionary<string, DocumentSnapshot>(StringComparer.OrdinalIgnoreCase) { [alternatePath] = document });
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(analysis: analysis);

        var statements = await diagnostics.GetStatementsAsync(capture, DiagnosticPublishMode.All, CancellationToken.None);
        DiagnosticsAnalysisResult result = await diagnostics.AnalyzeResultAsync(
            capture, document, DiagnosticPublishMode.All, CancellationToken.None);
        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture);

        Assert.NotNull(lease);
        Assert.True(lease.Model.Snapshot.TryGetDocument(alternatePath, out AnalysisDocument? shared));
        Assert.Same(shared!.Statements, statements);
        Assert.NotNull(result.Validation);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("not assignable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains("module mode", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task RecoveredSourceUsesTheSharedParserDiagnosticsAndAst()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "let broken = ;\nlet value: number = 'wrong';";
        string path = CreateProject(directory, source);
        DocumentRequestSnapshot capture = Capture(path, source);
        using var analysis = new SemanticAnalysisService();
        using var diagnostics = new DiagnosticsService(analysis: analysis);

        var statements = await diagnostics.GetStatementsAsync(capture, DiagnosticPublishMode.All, CancellationToken.None);
        DiagnosticsAnalysisResult result = await diagnostics.AnalyzeResultAsync(
            capture, capture.Document, DiagnosticPublishMode.All, CancellationToken.None);
        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture);

        Assert.NotNull(lease);
        Assert.True(lease.Model.Snapshot.TryGetDocument(path, out AnalysisDocument? document));
        Assert.Same(document!.Statements, statements);
        Assert.True(document.HasRecoveredSyntax);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal(1, analysis.Statistics.Builds);
    }

    private static string CreateProject(TempTestDirectory directory, string source)
    {
        directory.CreateFile("tsconfig.json", """
            { "compilerOptions": { "noLib": true, "types": [] }, "files": ["main.ts", "dep.ts"] }
            """);
        directory.CreateFile("dep.ts", "export const value: number = 1;");
        return directory.CreateFile("main.ts", source);
    }

    private static DocumentRequestSnapshot Capture(string path, string source)
    {
        var store = new DocumentStore();
        string uri = new Uri(path).AbsoluteUri;
        store.Open(uri, source, version: 1);
        Assert.True(store.TryCapture(uri, out DocumentRequestSnapshot capture));
        return capture;
    }
}
