using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SharpTS.Diagnostics;
using SharpTS.IO;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Project;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using SharpDiagnostic = SharpTS.Diagnostics.Diagnostic;

namespace SharpTS.LanguageServer.Services;

/// <summary>Diagnostics and the input validation required before they can be published.</summary>
internal sealed record DiagnosticsAnalysisResult(
    List<LspDiagnostic> Diagnostics,
    AnalysisValidation? Validation = null,
    AnalysisMetadataProvider? Metadata = null,
    int? MetadataGeneration = null)
{
    public bool IsCurrent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Validation is not null && !Validation.IsCurrent(cancellationToken))
            return false;
        if (Metadata is null || MetadataGeneration is null)
            return true;
        // Validation must see physical inputs, even when called inside an older build scope.
        using var scope = CompilerFileSystem.Use(CompilerFileSystem.Physical, cancellationToken);
        return Metadata.RefreshGeneration() == MetadataGeneration;
    }
}

internal sealed record DiagnosticsDocumentInputs(
    IReadOnlyList<Stmt> Statements,
    IReadOnlyList<AnalysisDependency>? Dependencies = null);

/// <summary>
/// Uses shared checked editor snapshots for full diagnostics and keeps SharpTS-only analysis
/// lazy so an interop-only editor never starts the general TypeScript checker.
/// </summary>
public sealed class DiagnosticsService : IDisposable
{
    private readonly InteropAnalyzer _interop;
    private readonly SemanticAnalysisService _analysis;
    private readonly bool _ownsAnalysis;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly ConcurrentDictionary<string, CachedAnalysis> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="resolve">CLR resolver, or the in-process registry when none is supplied.</param>
    /// <param name="typeNames">Public CLR type names used for interop suggestions.</param>
    /// <param name="analysis">The server's shared checked analysis service.</param>
    /// <param name="metadata">Optional stable CLR metadata generations for interop diagnostics.</param>
    public DiagnosticsService(
        Func<string, Type?>? resolve = null,
        Func<IEnumerable<string>>? typeNames = null,
        SemanticAnalysisService? analysis = null,
        AnalysisMetadataProvider? metadata = null)
    {
        _analysis = analysis ?? new SemanticAnalysisService();
        _ownsAnalysis = analysis is null;
        _metadata = metadata;
        if (metadata is not null)
        {
            resolve ??= metadata.Resolve;
            typeNames ??= metadata.GetTypeNames;
        }
        _interop = new InteropAnalyzer(resolve, typeNames);
    }

    public List<LspDiagnostic> Analyze(
        string text,
        DiagnosticPublishMode mode = DiagnosticPublishMode.SharpTsOnly,
        string? fileName = null)
    {
        var snapshot = new DocumentSnapshot(
            fileName is null ? "untitled:diagnostics" : new Uri(Path.GetFullPath(fileName)).AbsoluteUri,
            text,
            Version: 0,
            fileName is null ? null : Path.GetFullPath(fileName));
        return Analyze(snapshot, mode, CancellationToken.None);
    }

    public List<LspDiagnostic> Analyze(
        DocumentSnapshot snapshot,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, DocumentSnapshot> documents = snapshot.FilePath is null
            ? new Dictionary<string, DocumentSnapshot>()
            : new Dictionary<string, DocumentSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [snapshot.FilePath] = snapshot,
            };
        return Analyze(new DocumentRequestSnapshot(snapshot, 0, documents), snapshot, mode, cancellationToken);
    }

    [SuppressMessage("Usage", "VSTHRD002", Justification = "Compatibility wrapper; production publication awaits analysis.")]
    public List<LspDiagnostic> Analyze(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot snapshot,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        DiagnosticsAnalysisResult result = AnalyzeResultAsync(workspace, snapshot, mode, cancellationToken)
            .GetAwaiter().GetResult();
        return result.IsCurrent(cancellationToken) ? result.Diagnostics : [];
    }

    internal async Task<DiagnosticsAnalysisResult> AnalyzeResultAsync(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot snapshot,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == DiagnosticPublishMode.Off)
            return new DiagnosticsAnalysisResult([]);

        if (mode == DiagnosticPublishMode.All && snapshot.FilePath is not null)
        {
            using AnalysisLease? lease = await _analysis.GetDocumentAsync(
                workspace with { Document = snapshot }, cancellationToken).ConfigureAwait(false);
            if (lease is not null && TryGetSharedDocument(lease, snapshot, out AnalysisDocument? document))
            {
                using var metadataScope = lease.EnterMetadataScope();
                var diagnostics = new List<SharpDiagnostic>();
                if (!document!.HasRecoveredSyntax)
                {
                    diagnostics.AddRange(_interop.Analyze(
                        document.Statements, new PositionMap(snapshot.Text), cancellationToken));
                }
                diagnostics.AddRange(document.ParseDiagnostics);
                diagnostics.AddRange(lease.Model.Snapshot.Diagnostics.Where(diagnostic =>
                    BelongsToDocument(diagnostic, snapshot.FilePath)));
                cancellationToken.ThrowIfCancellationRequested();
                return new DiagnosticsAnalysisResult(
                    LspConversions.ToLsp(diagnostics, snapshot.Text, mode), lease.Validation);
            }
        }

        // Untitled or unavailable semantic models retain standalone diagnostics. These ASTs
        // are private to this cheap cache and are never a published semantic snapshot's AST.
        using AnalysisMetadataView? metadata = _metadata?.Capture();
        using var scope = metadata?.EnterScope();
        CachedAnalysis analysis = GetOrBuild(snapshot, metadata?.Generation ?? 0, cancellationToken);
        var standaloneDiagnostics = new List<SharpDiagnostic>(analysis.InteropDiagnostics);
        if (mode == DiagnosticPublishMode.All)
        {
            standaloneDiagnostics.AddRange(analysis.ParseResult.Diagnostics);
            if (analysis.ParseResult.IsSuccess)
            {
                lock (analysis.TypeCheckGate)
                {
                    analysis.TypeCheckResult ??= BuildTypeCheck(analysis, cancellationToken);
                    standaloneDiagnostics.AddRange(analysis.TypeCheckResult.Diagnostics);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new DiagnosticsAnalysisResult(
            LspConversions.ToLsp(standaloneDiagnostics, snapshot.Text, mode),
            Metadata: _metadata, MetadataGeneration: metadata?.Generation);
    }

    public void Invalidate(string uriOrPath) => _cache.TryRemove(uriOrPath, out _);

    internal IReadOnlyList<Stmt> GetStatements(DocumentSnapshot snapshot, CancellationToken cancellationToken)
    {
        using AnalysisMetadataView? metadata = _metadata?.Capture();
        using var scope = metadata?.EnterScope();
        return GetOrBuild(snapshot, metadata?.Generation ?? 0, cancellationToken).ParseResult.Statements;
    }

    internal async Task<IReadOnlyList<Stmt>> GetStatementsAsync(
        DocumentRequestSnapshot workspace,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken) =>
        (await GetDocumentInputsAsync(workspace, mode, cancellationToken).ConfigureAwait(false)).Statements;

    internal async Task<DiagnosticsDocumentInputs> GetDocumentInputsAsync(
        DocumentRequestSnapshot workspace,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == DiagnosticPublishMode.All && workspace.Document.FilePath is not null)
        {
            using AnalysisLease? lease = await _analysis.GetDocumentAsync(workspace, cancellationToken)
                .ConfigureAwait(false);
            if (lease is not null && TryGetSharedDocument(lease, workspace.Document, out AnalysisDocument? document))
                return new DiagnosticsDocumentInputs(document!.Statements, lease.Model.Snapshot.Dependencies);
        }
        return new DiagnosticsDocumentInputs(GetStatements(workspace.Document, cancellationToken));
    }

    private static bool TryGetSharedDocument(
        AnalysisLease lease, DocumentSnapshot snapshot, out AnalysisDocument? document)
    {
        document = null;
        return snapshot.FilePath is not null &&
            lease.Model.Snapshot.TryGetDocument(snapshot.FilePath, out document) &&
            document is not null && string.Equals(document.Document.Text, snapshot.Text, StringComparison.Ordinal);
    }

    private static bool BelongsToDocument(SharpDiagnostic diagnostic, string path) =>
        diagnostic.FilePath is null || string.Equals(
            Path.GetFullPath(diagnostic.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    private CachedAnalysis GetOrBuild(DocumentSnapshot snapshot, int metadataGeneration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = snapshot.FilePath ?? snapshot.Uri;
        return _cache.AddOrUpdate(key,
            _ => Build(snapshot, metadataGeneration, cancellationToken),
            (_, current) => current.Version == snapshot.Version &&
                current.MetadataGeneration == metadataGeneration &&
                string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal)
                    ? current : Build(snapshot, metadataGeneration, cancellationToken));
    }

    private CachedAnalysis Build(DocumentSnapshot snapshot, int metadataGeneration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fileName = snapshot.FilePath ?? snapshot.Uri;
        bool isTsx = fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
        List<Token> tokens = new Lexer(snapshot.Text) { JsxTolerant = isTsx }
            .WithCancellation(cancellationToken).ScanTokens();
        var document = new SourceDocument(fileName, snapshot.Text, isVirtual: snapshot.FilePath is null);
        var parser = new Parser(tokens, DecoratorMode.Stage3)
            .WithCancellation(cancellationToken).WithSourceDocument(document);
        if (isTsx) parser.WithJsx(snapshot.Text, JsxParseOptions.Default);
        ParseDiagnosticResult parsed = parser.Parse();
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SharpDiagnostic> interopDiagnostics = parsed.IsSuccess
            ? _interop.Analyze(parsed.Statements, new PositionMap(snapshot.Text), cancellationToken) : [];
        return new CachedAnalysis(snapshot.Version, metadataGeneration, snapshot.Text, document, parsed, interopDiagnostics);
    }

    private static TypeCheckDiagnosticResult BuildTypeCheck(CachedAnalysis analysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checker = new TypeChecker(TypeCheckerOptions.Default with { MaxErrors = int.MaxValue })
            .WithFilePath(analysis.Document.Path).WithCancellation(cancellationToken);
        return checker.CheckWithRecovery(analysis.ParseResult.Statements, analysis.Document);
    }

    public void Dispose()
    {
        _cache.Clear();
        if (_ownsAnalysis) _analysis.Dispose();
    }

    private sealed record CachedAnalysis(
        int Version,
        int MetadataGeneration,
        string Text,
        SourceDocument Document,
        ParseDiagnosticResult ParseResult,
        IReadOnlyList<SharpDiagnostic> InteropDiagnostics)
    {
        public object TypeCheckGate { get; } = new();
        public TypeCheckDiagnosticResult? TypeCheckResult { get; set; }
    }
}
