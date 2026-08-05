using System.Text.RegularExpressions;
using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Options shared by the single-source interpretation and compilation pipelines.
/// </summary>
internal sealed record SingleSourceAnalysisOptions(
    DecoratorMode DecoratorMode,
    string FileName,
    JsxParseOptions? Jsx = null);

/// <summary>
/// The reusable front-end result for a source string that has no module resolver.
/// </summary>
internal sealed record SingleSourceAnalysisResult(
    List<Stmt> Statements,
    TypeMap? TypeMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool HitErrorLimit = false)
{
    public bool Success =>
        TypeMap is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Runs the canonical lexer, parser, and type-checker front end for APIs that accept one
/// in-memory source string. Keeping this pipeline shared prevents interpreted and compiled
/// embedding behavior from drifting (notably pragma and diagnostic handling).
/// </summary>
internal static class SingleSourceAnalyzer
{
    public static SingleSourceAnalysisResult Analyze(
        string source,
        SingleSourceAnalysisOptions options)
    {
        bool isTsx = options.FileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || options.FileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
            || options.Jsx is not null;

        Lexer lexer;
        List<Token> tokens;
        try
        {
            lexer = new Lexer(source) { JsxTolerant = isTsx };
            tokens = lexer.ScanTokens();
        }
        catch (Exception ex)
        {
            return new SingleSourceAnalysisResult(
                [],
                null,
                [LexErrorToDiagnostic(ex.Message, options.FileName)]);
        }

        var parser = new Parser(tokens, options.DecoratorMode)
            .WithFilePath(options.FileName);
        if (isTsx)
            parser.WithJsx(source, (options.Jsx ?? JsxParseOptions.Default).ApplyPragmas(lexer.Pragmas));

        var parseResult = parser.Parse();
        if (!parseResult.IsSuccess)
        {
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                parseResult.Diagnostics,
                parseResult.HitErrorLimit);
        }

        // These APIs deliberately have no ModuleResolver. Reject every module-loading form
        // explicitly instead of relying on a later checker/runtime failure. Besides producing a
        // stable diagnostic, this keeps the untrusted-source boundary intact if the checker gains
        // more permissive single-file module behavior in the future.
        var moduleDiagnostics = ModuleLoadingValidator.Validate(
            parseResult.Statements,
            options.FileName);
        if (moduleDiagnostics.Count > 0)
        {
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                moduleDiagnostics);
        }

        var checker = new TypeChecker().WithFilePath(options.FileName);
        checker.SetDecoratorMode(options.DecoratorMode);
        var typeResult = checker.CheckWithRecovery(parseResult.Statements);
        var diagnostics = TypeCheckPolicy.ApplyLineDirectives(
            typeResult.Diagnostics,
            lexer.Pragmas);

        return new SingleSourceAnalysisResult(
            parseResult.Statements,
            typeResult.TypeMap,
            diagnostics,
            typeResult.HitErrorLimit);
    }

    private static Diagnostic LexErrorToDiagnostic(string message, string fileName)
    {
        var match = Regex.Match(message, @"\bat line (\d+)\b");
        SourceLocation? location = match.Success && int.TryParse(match.Groups[1].Value, out var line)
            ? new SourceLocation(fileName, line)
            : null;
        return Diagnostic.ParseError(message, location);
    }

    private sealed class ModuleLoadingValidator(string fileName) : AstVisitorBase
    {
        private const string Message =
            "Module loading is not available in the single-source embedding API.";

        private readonly List<Diagnostic> _diagnostics = [];

        public static IReadOnlyList<Diagnostic> Validate(
            IEnumerable<Stmt> statements,
            string fileName)
        {
            var validator = new ModuleLoadingValidator(fileName);
            foreach (var statement in statements)
                validator.Visit(statement);
            return validator._diagnostics;
        }

        protected override void VisitImport(Stmt.Import stmt)
        {
            // The automatic JSX-runtime import is compiler-generated and does not grant source
            // code access to the module loader.
            if (!stmt.IsSynthesizedJsxRuntime)
                Add(stmt.Keyword);
        }

        protected override void VisitImportRequire(Stmt.ImportRequire stmt) => Add(stmt.Keyword);

        protected override void VisitExport(Stmt.Export stmt)
        {
            if (stmt.FromModulePath is not null)
                Add(stmt.Keyword);
            base.VisitExport(stmt);
        }

        protected override void VisitDynamicImport(Expr.DynamicImport expr)
        {
            Add(expr.Keyword);
            base.VisitDynamicImport(expr);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            if (expr.Callee is Expr.Variable variable && variable.Name.Lexeme == "require")
                Add(variable.Name);
            base.VisitCall(expr);
        }

        private void Add(Token token) => _diagnostics.Add(
            Diagnostic.ModuleError(Message, new SourceLocation(fileName, token.Line)));
    }
}
