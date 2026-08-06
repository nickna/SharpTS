using System.Diagnostics;
using System.Text.RegularExpressions;
using SharpTS.Diagnostics;
using SharpTS.Execution;
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
    public IReadOnlyList<ExecutionPhaseTiming> Timings { get; init; } = [];

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

        var timings = new List<ExecutionPhaseTiming>();
        Lexer lexer;
        List<Token> tokens;
        var phaseStartedAt = Stopwatch.GetTimestamp();
        try
        {
            lexer = new Lexer(source) { JsxTolerant = isTsx };
            tokens = lexer.ScanTokens();
            timings.Add(ExecutionPhaseTiming.Completed(
                "tokenize", ElapsedMilliseconds(phaseStartedAt)));
        }
        catch (Exception ex)
        {
            timings.Add(ExecutionPhaseTiming.Failed(
                "tokenize", ElapsedMilliseconds(phaseStartedAt)));
            return new SingleSourceAnalysisResult(
                [],
                null,
                [LexErrorToDiagnostic(ex.Message, options.FileName)])
            {
                Timings = timings
            };
        }

        ParseDiagnosticResult parseResult;
        phaseStartedAt = Stopwatch.GetTimestamp();
        try
        {
            var parser = new Parser(tokens, options.DecoratorMode)
                .WithFilePath(options.FileName);
            if (isTsx)
                parser.WithJsx(source, (options.Jsx ?? JsxParseOptions.Default).ApplyPragmas(lexer.Pragmas));

            parseResult = parser.Parse();
        }
        catch (Exception ex)
        {
            timings.Add(ExecutionPhaseTiming.Failed(
                "parse", ElapsedMilliseconds(phaseStartedAt)));
            return new SingleSourceAnalysisResult(
                [],
                null,
                [Diagnostic.ParseError(ex.Message, new SourceLocation(options.FileName, 1))])
            {
                Timings = timings
            };
        }

        if (!parseResult.IsSuccess)
        {
            timings.Add(ExecutionPhaseTiming.Failed(
                "parse", ElapsedMilliseconds(phaseStartedAt)));
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                parseResult.Diagnostics,
                parseResult.HitErrorLimit)
            {
                Timings = timings
            };
        }
        timings.Add(ExecutionPhaseTiming.Completed(
            "parse", ElapsedMilliseconds(phaseStartedAt)));

        // These APIs deliberately have no ModuleResolver. Reject every module-loading form
        // explicitly instead of relying on a later checker/runtime failure. Besides producing a
        // stable diagnostic, this keeps the untrusted-source boundary intact if the checker gains
        // more permissive single-file module behavior in the future.
        phaseStartedAt = Stopwatch.GetTimestamp();
        var moduleDiagnostics = ModuleLoadingValidator.Validate(
            parseResult.Statements,
            options.FileName);
        if (moduleDiagnostics.Count > 0)
        {
            timings.Add(ExecutionPhaseTiming.Failed(
                "typeCheck", ElapsedMilliseconds(phaseStartedAt)));
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                moduleDiagnostics)
            {
                Timings = timings
            };
        }

        TypeCheckDiagnosticResult typeResult;
        IReadOnlyList<Diagnostic> diagnostics;
        try
        {
            var checker = new TypeChecker().WithFilePath(options.FileName);
            checker.SetDecoratorMode(options.DecoratorMode);
            typeResult = checker.CheckWithRecovery(parseResult.Statements);
            diagnostics = TypeCheckPolicy.ApplyLineDirectives(
                typeResult.Diagnostics,
                lexer.Pragmas);
        }
        catch (Exception ex)
        {
            timings.Add(ExecutionPhaseTiming.Failed(
                "typeCheck", ElapsedMilliseconds(phaseStartedAt)));
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                [Diagnostic.TypeError(ex.Message, new SourceLocation(options.FileName, 1))])
            {
                Timings = timings
            };
        }

        timings.Add(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? ExecutionPhaseTiming.Failed("typeCheck", ElapsedMilliseconds(phaseStartedAt))
            : ExecutionPhaseTiming.Completed("typeCheck", ElapsedMilliseconds(phaseStartedAt)));

        return new SingleSourceAnalysisResult(
            parseResult.Statements,
            typeResult.TypeMap,
            diagnostics,
            typeResult.HitErrorLimit)
        {
            Timings = timings
        };
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

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
