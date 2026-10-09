using SharpTS.Diagnostics;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class AnalysisSnapshotTests
{
    [Fact]
    public void SnapshotCopiesPublishedCollectionsAndQueriesCapturedExpressionIdentity()
    {
        var source = new SourceDocument("snapshot.ts", "1");
        var tokens = new Lexer(source.Text).ScanTokens();
        var statements = new Parser(tokens).ParseOrThrow();
        var properties = new Dictionary<string, object> { ["detail"] = "captured" };
        var diagnostics = new List<Diagnostic>
        {
            new(DiagnosticSeverity.Warning, DiagnosticCode.General, "warning", Properties: properties),
        };
        var document = new AnalysisDocument(source, tokens, statements, diagnostics);
        var documents = new List<AnalysisDocument> { document };
        var roots = new List<string> { source.Path };
        var dependencies = new List<AnalysisDependency> { new(source.Path, "dependency.ts") };
        var expression = new Expr.Literal(1d);
        var typeMap = new TypeMap();
        typeMap.Set(expression, TypeInfo.Primitive.Number);
        var snapshot = new AnalysisSnapshot(
            new BindingIndex().Freeze(), typeMap, documents, diagnostics,
            new NavigationGraphScope("tsconfig.json", roots, IsComplete: true),
            dependencies: dependencies);

        tokens.Clear();
        statements.Clear();
        diagnostics.Clear();
        documents.Clear();
        roots.Clear();
        dependencies.Clear();
        properties["detail"] = "changed";

        Assert.NotEmpty(document.Tokens);
        Assert.NotEmpty(document.Statements);
        Assert.Single(snapshot.Documents);
        Assert.Single(snapshot.Scope.RootFiles);
        Assert.Single(snapshot.Dependencies);
        Assert.Equal("captured", Assert.Single(snapshot.Diagnostics).Properties!["detail"]);
        Assert.Equal("captured", Assert.Single(document.ParseDiagnostics).Properties!["detail"]);
        Assert.Same(document, snapshot.Documents[0]);
        Assert.True(snapshot.TryGetDocument("SNAPSHOT.TS", out AnalysisDocument? found));
        Assert.Same(document, found);
        Assert.Same(TypeInfo.Primitive.Number, snapshot.GetType(expression));
        Assert.Null(snapshot.GetType(new Expr.Literal(1d)));
        Assert.False(snapshot.HasRecoveredSyntax);
        Assert.False(snapshot.HasPartialSemantics);
    }

    [Fact]
    public void ParseRecoveryAndExplicitPartialCheckingRemainVisible()
    {
        var source = new SourceDocument("recovered.ts", "const value = ;");
        var parseError = new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.ParseError, "missing expression");
        var document = new AnalysisDocument(source, [], [], [parseError], hitParseErrorLimit: true);
        var snapshot = new AnalysisSnapshot(
            new BindingIndex().Freeze(), new TypeMap(), [document], [],
            new NavigationGraphScope(null, [], IsComplete: false));

        Assert.True(document.HitParseErrorLimit);
        Assert.True(snapshot.HasRecoveredSyntax);
        Assert.True(snapshot.HasPartialSemantics);

        var partial = new AnalysisSnapshot(
            new BindingIndex().Freeze(), new TypeMap(), [], [],
            new NavigationGraphScope(null, [], IsComplete: false), hasPartialSemantics: true);
        Assert.False(partial.HasRecoveredSyntax);
        Assert.True(partial.HasPartialSemantics);
    }
}
