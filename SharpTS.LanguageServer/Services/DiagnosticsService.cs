using System.Collections.Concurrent;
using SharpTS.Diagnostics;
using SharpTS.LanguageServer.Conversions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using SharpDiagnostic = SharpTS.Diagnostics.Diagnostic;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Produces the LSP diagnostics for a document. Phase 1 = the @DotNetType interop analyzer
/// (the tsserver-impossible value). Parse errors are left to the built-in TypeScript server.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly InteropAnalyzer _interop;
    private readonly ConcurrentDictionary<string, CachedAnalysis> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedWorkspaceCheck> _workspaceChecks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="resolve">CLR type resolver. Null = in-process registry (BCL only);
    /// the server injects an AssemblyReferenceLoader when a project/references are configured
    /// so the user's own @DotNetType targets resolve too.</param>
    public DiagnosticsService(
        Func<string, Type?>? resolve = null,
        Func<IEnumerable<string>>? typeNames = null) =>
        _interop = new InteropAnalyzer(resolve, typeNames);

    public List<LspDiagnostic> Analyze(
        string text,
        DiagnosticPublishMode mode = DiagnosticPublishMode.SharpTsOnly,
        string? fileName = null)
    {
        var snapshot = new DocumentSnapshot(
            fileName is null
                ? "untitled:diagnostics"
                : new Uri(Path.GetFullPath(fileName)).AbsoluteUri,
            text,
            Version: 0,
            fileName is null ? null : Path.GetFullPath(fileName));
        return Analyze(snapshot, mode, CancellationToken.None);
    }

    /// <summary>
    /// Analyzes one immutable text version. Lexing, AST/source spans, interop diagnostics, and
    /// the optional full type-check result are retained together and reused only for that exact
    /// version and text.
    /// </summary>
    public List<LspDiagnostic> Analyze(
        DocumentSnapshot snapshot,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == DiagnosticPublishMode.Off)
            return [];

        CachedAnalysis analysis = GetOrBuild(snapshot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<SharpDiagnostic>(analysis.InteropDiagnostics);
        if (mode == DiagnosticPublishMode.All)
        {
            diagnostics.AddRange(analysis.ParseResult.Diagnostics);
            if (analysis.ParseResult.IsSuccess)
            {
                TypeCheckDiagnosticResult result =
                    analysis.TypeCheckResult ??
                    BuildTypeCheck(analysis, cancellationToken);
                analysis.TypeCheckResult = result;
                diagnostics.AddRange(result.Diagnostics);
            }
        }

        return LspConversions.ToLsp(diagnostics, snapshot.Text, mode);
    }

    /// <summary>
    /// Workspace-aware form used by diagnostic publication. In <c>all</c> mode it checks the
    /// module graph from the same overlay snapshot and caches that result by workspace version.
    /// </summary>
    public List<LspDiagnostic> Analyze(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot snapshot,
        DiagnosticPublishMode mode,
        CancellationToken cancellationToken)
    {
        if (mode != DiagnosticPublishMode.All ||
            snapshot.FilePath is null)
        {
            return Analyze(snapshot, mode, cancellationToken);
        }

        CachedAnalysis analysis = GetOrBuild(snapshot, cancellationToken);
        var diagnostics = new List<SharpDiagnostic>(
            analysis.InteropDiagnostics);
        diagnostics.AddRange(analysis.ParseResult.Diagnostics);
        if (analysis.ParseResult.IsSuccess)
        {
            string path = snapshot.FilePath;
            CachedWorkspaceCheck workspaceCheck = _workspaceChecks.AddOrUpdate(
                path,
                _ => BuildWorkspaceCheck(
                    workspace,
                    snapshot,
                    analysis,
                    cancellationToken),
                (_, current) =>
                    current.DocumentVersion == snapshot.Version &&
                    current.WorkspaceVersion == workspace.WorkspaceVersion
                        ? current
                        : BuildWorkspaceCheck(
                            workspace,
                            snapshot,
                            analysis,
                            cancellationToken));
            diagnostics.AddRange(workspaceCheck.Diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return LspConversions.ToLsp(
            diagnostics,
            snapshot.Text,
            DiagnosticPublishMode.All);
    }

    public void Invalidate(string uriOrPath)
    {
        _cache.TryRemove(uriOrPath, out _);
        _workspaceChecks.TryRemove(uriOrPath, out _);
    }

    internal IReadOnlyList<Stmt> GetStatements(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken) =>
        GetOrBuild(snapshot, cancellationToken).ParseResult.Statements;

    private CachedAnalysis GetOrBuild(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string key = snapshot.FilePath ?? snapshot.Uri;
        return _cache.AddOrUpdate(
            key,
            _ => Build(snapshot, cancellationToken),
            (_, current) =>
                current.Version == snapshot.Version &&
                string.Equals(current.Text, snapshot.Text, StringComparison.Ordinal)
                    ? current
                    : Build(snapshot, cancellationToken));
    }

    private CachedAnalysis Build(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fileName = snapshot.FilePath ?? snapshot.Uri;

        // Stage3 decorators are the run-mode default and are required for @DotNetType to parse.
        bool isTsx =
            (fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase));
        List<Token> tokens =
            new Lexer(snapshot.Text) { JsxTolerant = isTsx }.ScanTokens();
        cancellationToken.ThrowIfCancellationRequested();

        var document = new SourceDocument(
            fileName,
            snapshot.Text,
            isVirtual: snapshot.FilePath is null);
        var parser = new Parser(tokens, DecoratorMode.Stage3)
            .WithSourceDocument(document);
        if (isTsx)
        {
            parser.WithJsx(snapshot.Text, JsxParseOptions.Default);
        }
        ParseDiagnosticResult parsed = parser.Parse();
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SharpDiagnostic> interopDiagnostics = parsed.IsSuccess
            ? _interop.Analyze(
                parsed.Statements,
                new PositionMap(snapshot.Text),
                cancellationToken)
            : [];

        return new CachedAnalysis(
            snapshot.Version,
            snapshot.Text,
            document,
            tokens,
            parsed,
            interopDiagnostics);
    }

    private static TypeCheckDiagnosticResult BuildTypeCheck(
        CachedAnalysis analysis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checker = new TypeChecker(
            TypeCheckerOptions.Default with { MaxErrors = int.MaxValue })
            .WithFilePath(analysis.Document.Path)
            .WithCancellation(cancellationToken);
        return checker.CheckWithRecovery(
            analysis.ParseResult.Statements,
            analysis.Document);
    }

    private static CachedWorkspaceCheck BuildWorkspaceCheck(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot snapshot,
        CachedAnalysis analysis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckedNavigationModel? model = NavigationModelBuilder.TryBuild(
            snapshot.FilePath!,
            snapshot.Text,
            workspace.TextOverlay,
            cancellationToken);
        IReadOnlyList<SharpDiagnostic> diagnostics;
        if (model is null)
        {
            diagnostics = BuildTypeCheck(
                    analysis,
                    cancellationToken)
                .Diagnostics;
        }
        else
        {
            diagnostics = model.Checker.GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.FilePath is null ||
                    string.Equals(
                        Path.GetFullPath(diagnostic.FilePath),
                        snapshot.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return new CachedWorkspaceCheck(
            snapshot.Version,
            workspace.WorkspaceVersion,
            diagnostics);
    }

    internal sealed record CachedAnalysis(
        int Version,
        string Text,
        SourceDocument Document,
        IReadOnlyList<Token> Tokens,
        ParseDiagnosticResult ParseResult,
        IReadOnlyList<SharpDiagnostic> InteropDiagnostics)
    {
        public TypeCheckDiagnosticResult? TypeCheckResult { get; set; }
    }

    private sealed record CachedWorkspaceCheck(
        int DocumentVersion,
        long WorkspaceVersion,
        IReadOnlyList<SharpDiagnostic> Diagnostics);
}
