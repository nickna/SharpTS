using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class EditorSyntaxAnalysisTests
{
    [Fact]
    public async Task CheckedDocumentsCaptureSyntaxAndRecoveryCannotMutateTheirPublishedGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true},"include":["*.ts"]}""");
        const string text = "class C { value: number = 1; } const c = new C(); c.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);
        Assert.True(analysis.Model.Snapshot.TryGetDocument(path, out var document));
        Assert.NotNull(document!.Syntax);
        var index = document.Syntax;
        int spanCount = document.Document.Spans.Count;
        var statements = document.Statements.ToArray();
        long checks = service.Statistics.Checks;

        var recovered = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion);
        var repeated = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion);

        Assert.NotNull(recovered);
        Assert.True(recovered.IsRecovered);
        Assert.Same(recovered, repeated);
        Assert.NotSame(document.Document, recovered.Document);
        Assert.Equal(text, recovered.Document.Text);
        Assert.Same(index, document.Syntax);
        Assert.Equal(spanCount, document.Document.Spans.Count);
        Assert.Equal(statements, document.Statements);
        Assert.Equal(checks, service.Statistics.Checks);
        Assert.Equal(1, service.EditorSyntaxStatistics.Parses);
        Assert.Equal(1, service.EditorSyntaxStatistics.CacheHits);
    }

    [Fact]
    public async Task CursorQueryAndPolicyEachIdentifyASeparateArtifact()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "function f(a: number, b: number): void {} f(1, ";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);

        var first = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.SignatureHelp);
        var otherCursor = service.GetEditorSyntax(analysis, text.Length - 1, EditorQueryKind.SignatureHelp);
        var otherQuery = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Syntax);
        var otherPolicy = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.SignatureHelp,
            policy: EditorRecoveryPolicy.Default with { Version = EditorRecoveryPolicy.Default.Version + 1 });

        Assert.NotNull(first);
        Assert.NotNull(otherCursor);
        Assert.NotNull(otherQuery);
        Assert.NotNull(otherPolicy);
        Assert.NotSame(first, otherCursor);
        Assert.NotSame(first, otherQuery);
        Assert.NotSame(first, otherPolicy);
        Assert.False(otherQuery.IsRecovered);
        Assert.Equal(4, service.EditorSyntaxStatistics.RetainedArtifacts);
        Assert.Equal(1, service.Statistics.Checks);
    }

    [Fact]
    public async Task IdenticalTextInDifferentDocumentsCannotShareCursorOwnership()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true},"include":["*.ts"]}""");
        const string text = "export const value = 1; value.";
        string firstPath = directory.CreateFile("first.ts", text);
        string secondPath = directory.CreateFile("second.ts", text);
        using var service = new SemanticAnalysisService();
        using var firstAnalysis = await service.GetDocumentAsync(firstPath, text);
        using var secondAnalysis = await service.GetDocumentAsync(secondPath, text);
        Assert.NotNull(firstAnalysis);
        Assert.NotNull(secondAnalysis);

        var first = service.GetEditorSyntax(firstAnalysis, text.Length, EditorQueryKind.Completion);
        var second = service.GetEditorSyntax(secondAnalysis, text.Length, EditorQueryKind.Completion);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(firstPath, first.Document.Path);
        Assert.Equal(secondPath, second.Document.Path);
        Assert.Same(first.Document, first.Syntax.Document);
        Assert.Same(second.Document, second.Syntax.Document);
    }

    [Fact]
    public async Task InvalidatingTheBaseAlsoClearsAndRefusesItsCursorArtifacts()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);
        Assert.NotNull(service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion));

        service.InvalidateAll();

        Assert.Equal(0, service.EditorSyntaxStatistics.RetainedArtifacts);
        Assert.Null(service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion));
    }

    [Fact]
    public async Task ClosedConfigurationChangesRefuseCachedRecoveryWithoutAFileNotification()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string config = directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true},"include":["*.ts"]}""");
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);
        Assert.NotNull(service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion));

        File.WriteAllText(config, """{"compilerOptions":{"noLib":true,"strict":true},"include":["*.ts"]}""");

        Assert.Null(service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion));
    }

    [Fact]
    public async Task CursorArtifactsHaveAnLruBoundAndNeverCheckAgain()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "function f(a: number): number { return a; } f(1);";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService(maxSnapshots: 1);
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);
        var first = service.GetEditorSyntax(analysis, 0, EditorQueryKind.Syntax);
        Assert.NotNull(first);
        for (int cursor = 1; cursor < 10; cursor++)
            Assert.NotNull(service.GetEditorSyntax(analysis, cursor, EditorQueryKind.Syntax));

        Assert.Equal(4, service.EditorSyntaxStatistics.RetainedArtifacts);
        Assert.NotSame(first, service.GetEditorSyntax(analysis, 0, EditorQueryKind.Syntax));
        Assert.Equal(1, service.Statistics.Checks);
    }

    [Fact]
    public async Task OversizedArtifactsAreUsableWithoutBeingRetained()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService(maxRetainedBytes: 1);
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);

        var first = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion);
        var second = service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(0, service.EditorSyntaxStatistics.RetainedArtifacts);
        Assert.Equal(0, service.EditorSyntaxStatistics.EstimatedRetainedBytes);
    }

    [Fact]
    public async Task CancelledCursorRequestDoesNotPublishAnArtifact()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, text);
        Assert.NotNull(analysis);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Throws<OperationCanceledException>(() =>
            service.GetEditorSyntax(analysis, text.Length, EditorQueryKind.Completion, cancellation.Token));

        Assert.Equal(0, service.EditorSyntaxStatistics.Parses);
        Assert.Equal(0, service.EditorSyntaxStatistics.RetainedArtifacts);
    }

    [Fact]
    public void ModuleEditorCaptureIsOptInAndPreservesWrittenDocumentOwnership()
    {
        string path = Path.GetFullPath("editor-syntax-capture.ts");
        const string text = "const value: number = 1; value;";
        var files = new Dictionary<string, string> { [path] = text };
        var ordinary = new ModuleResolver(path, files).LoadModule(path);
        var editor = new ModuleResolver(path, files) { CaptureEditorSyntax = true }.LoadModule(path);

        Assert.Null(ordinary.Document!.EditorSyntax);
        Assert.NotNull(editor.Document!.EditorSyntax);
        Assert.True(editor.Document.EditorSyntax.Count > 0);
        Assert.Equal(ordinary.Statements.Select(statement => statement.GetType()),
            editor.Statements.Select(statement => statement.GetType()));
        var ordinaryDeclaration = Assert.IsType<Stmt.Const>(ordinary.Statements[0]);
        var editorDeclaration = Assert.IsType<Stmt.Const>(editor.Statements[0]);
        Assert.Equal((ordinaryDeclaration.Name.Type, ordinaryDeclaration.Name.Lexeme,
                ordinaryDeclaration.Name.Start, ordinaryDeclaration.Name.End, ordinaryDeclaration.Name.Line),
            (editorDeclaration.Name.Type, editorDeclaration.Name.Lexeme,
                editorDeclaration.Name.Start, editorDeclaration.Name.End, editorDeclaration.Name.Line));
        Assert.Equal(ordinaryDeclaration.TypeAnnotation, editorDeclaration.TypeAnnotation);
        Assert.Equal(Assert.IsType<Expr.Literal>(ordinaryDeclaration.Initializer).Value,
            Assert.IsType<Expr.Literal>(editorDeclaration.Initializer).Value);
        Assert.Equal(Assert.IsType<NamedTypeNode>(ordinaryDeclaration.TypeAnnotationNode).Name,
            Assert.IsType<NamedTypeNode>(editorDeclaration.TypeAnnotationNode).Name);
        Assert.Equal(text, editor.Document.Text);
    }
}
