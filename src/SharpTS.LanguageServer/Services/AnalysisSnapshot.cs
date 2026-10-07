using System.Collections.Frozen;
using System.Collections.Immutable;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpDiagnostic = SharpTS.Diagnostics.Diagnostic;
using SharpDiagnosticSeverity = SharpTS.Diagnostics.DiagnosticSeverity;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// The parser and checker configuration used for one completed analysis.
/// </summary>
internal sealed record AnalysisOptions(
    DecoratorMode DecoratorMode,
    JsxParseOptions JsxOptions,
    TypeCheckerOptions CheckerOptions);

/// <summary>A captured edge in the checked module/reference graph.</summary>
internal sealed record AnalysisDependency(string SourcePath, string TargetPath);

/// <summary>
/// One source capture and its parser output. Tokens and AST objects belong to this analysis;
/// collection copies prevent parser/module list mutations from changing the published shape.
/// </summary>
/// <remarks>
/// AST nodes and source span tables preserve reference identity with the TypeMap and bindings.
/// They must never be parsed into, checked again, or otherwise mutated after publication.
/// Cursor-specific recovery must create separate artifacts instead of editing this document.
/// </remarks>
internal sealed class AnalysisDocument
{
    public AnalysisDocument(
        SourceDocument document,
        IReadOnlyList<Token> tokens,
        IReadOnlyList<Stmt> statements,
        IReadOnlyList<SharpDiagnostic> parseDiagnostics,
        bool hitParseErrorLimit = false)
    {
        Document = document;
        Tokens = tokens.ToImmutableArray();
        Statements = statements.ToImmutableArray();
        ParseDiagnostics = AnalysisSnapshot.CopyDiagnostics(parseDiagnostics);
        HitParseErrorLimit = hitParseErrorLimit;
        HasRecoveredSyntax = hitParseErrorLimit ||
            ParseDiagnostics.Any(diagnostic => diagnostic.Severity == SharpDiagnosticSeverity.Error);
    }

    public SourceDocument Document { get; }
    public IReadOnlyList<Token> Tokens { get; }
    public IReadOnlyList<Stmt> Statements { get; }
    public EditorSyntaxIndex? Syntax => Document.EditorSyntax;
    public IReadOnlyList<SharpDiagnostic> ParseDiagnostics { get; }
    public bool HitParseErrorLimit { get; }
    public bool HasRecoveredSyntax { get; }
}

/// <summary>
/// Completed semantic data for one connected source component. No checker or resolver is retained
/// or exposed, and callers can only query the build-owned TypeMap.
/// </summary>
/// <remarks>
/// Construction transfers ownership of the TypeMap, documents, and AST graph to the snapshot.
/// The builder must finish checking before construction and must not recheck or mutate those
/// objects afterward. Cache validation and caller cancellation belong to the analysis service,
/// allowing multiple requests to safely query the same completed capture.
/// </remarks>
internal sealed class AnalysisSnapshot
{
    private readonly TypeMap _typeMap;
    private readonly FrozenDictionary<string, AnalysisDocument> _documentsByPath;

    public AnalysisSnapshot(
        FrozenBindingIndex bindings,
        TypeMap typeMap,
        IReadOnlyList<AnalysisDocument> documents,
        IReadOnlyList<SharpDiagnostic> diagnostics,
        NavigationGraphScope scope,
        bool hasRecoveredSyntax = false,
        bool hasPartialSemantics = false,
        AnalysisOptions? options = null,
        IReadOnlyList<AnalysisDependency>? dependencies = null,
        FrozenMemberIndex? members = null)
    {
        Bindings = bindings;
        Members = members ?? FrozenMemberIndex.Empty;
        _typeMap = typeMap;
        Documents = documents.ToImmutableArray();
        _documentsByPath = documents.ToFrozenDictionary(
            document => NormalizeDocumentPath(document.Document.Path),
            StringComparer.OrdinalIgnoreCase);
        Diagnostics = CopyDiagnostics(diagnostics);
        Scope = scope with { RootFiles = scope.RootFiles.ToImmutableArray() };
        HasRecoveredSyntax = hasRecoveredSyntax ||
            documents.Any(document => document.HasRecoveredSyntax);
        HasPartialSemantics = hasPartialSemantics || HasRecoveredSyntax;
        Options = options;
        Dependencies = dependencies?.ToImmutableArray() ?? ImmutableArray<AnalysisDependency>.Empty;
        ClassTypes = typeMap.ClassTypes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public FrozenBindingIndex Bindings { get; }
    public FrozenMemberIndex Members { get; }
    public IReadOnlyList<AnalysisDocument> Documents { get; }
    public IReadOnlyList<SharpDiagnostic> Diagnostics { get; }
    public NavigationGraphScope Scope { get; }
    public bool HasRecoveredSyntax { get; }
    public bool HasPartialSemantics { get; }
    public AnalysisOptions? Options { get; }
    public IReadOnlyList<AnalysisDependency> Dependencies { get; }
    public IReadOnlyDictionary<string, TypeInfo.Class> ClassTypes { get; }
    public int TypeCount => _typeMap.Count;

    public bool TryGetDocument(string path, out AnalysisDocument? document) =>
        _documentsByPath.TryGetValue(NormalizeDocumentPath(path), out document);

    private static string NormalizeDocumentPath(string path) =>
        Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : path;

    public TypeInfo? GetType(Expr expression) => _typeMap.Get(expression);

    public bool TryGetType(Expr expression, out TypeInfo? type) =>
        _typeMap.TryGet(expression, out type);

    public TypeInfo.Class? GetClassType(Stmt.Class declaration) =>
        _typeMap.GetClassType(declaration);

    public TypeInfo.Class? GetClassType(string className) => _typeMap.GetClassType(className);

    public TypeInfo.Class? GetClassExprType(Expr.ClassExpr expression) =>
        _typeMap.GetClassExprType(expression);

    public TypeInfo.Function? GetFunctionType(string functionName) =>
        _typeMap.GetFunctionType(functionName);

    internal static IReadOnlyList<SharpDiagnostic> CopyDiagnostics(
        IReadOnlyList<SharpDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Properties is null
            ? diagnostic
            : diagnostic with
            {
                Properties = diagnostic.Properties.ToFrozenDictionary(StringComparer.Ordinal),
            }).ToImmutableArray();
}
