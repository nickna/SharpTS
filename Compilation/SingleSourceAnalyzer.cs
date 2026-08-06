using System.Diagnostics;
using System.Text.RegularExpressions;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
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
        SingleSourceAnalysisOptions options,
        ExecutionTimingCollector? timingCollector = null)
    {
        timingCollector ??= new ExecutionTimingCollector();
        bool isTsx = options.FileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || options.FileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
            || options.Jsx is not null;

        Lexer lexer;
        List<Token> tokens;
        var phaseStartedAt = timingCollector.Start();
        try
        {
            lexer = new Lexer(source) { JsxTolerant = isTsx };
            tokens = lexer.ScanTokens();
            timingCollector.Complete(ExecutionPhaseTiming.Tokenize, phaseStartedAt);
        }
        catch (Exception ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.Tokenize, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                [],
                null,
                [ex is SharpTSException sharpTsException
                    ? sharpTsException.Diagnostic
                    : LexErrorToDiagnostic(ex.Message, options.FileName)])
            {
                Timings = timingCollector.Snapshot()
            };
        }

        ParseDiagnosticResult parseResult;
        phaseStartedAt = timingCollector.Start();
        try
        {
            var parser = new Parser(tokens, options.DecoratorMode)
                .WithFilePath(options.FileName);
            if (isTsx)
                parser.WithJsx(source, (options.Jsx ?? JsxParseOptions.Default).ApplyPragmas(lexer.Pragmas));

            parseResult = parser.Parse();
        }
        catch (SharpTSException ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.Parse, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                [],
                null,
                [ex.Diagnostic])
            {
                Timings = timingCollector.Snapshot()
            };
        }
        catch (Exception ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.Parse, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                [],
                null,
                [Diagnostic.CompileError(ex.Message, new SourceLocation(options.FileName, 1))])
            {
                Timings = timingCollector.Snapshot()
            };
        }

        if (!parseResult.IsSuccess)
        {
            timingCollector.Fail(ExecutionPhaseTiming.Parse, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                parseResult.Diagnostics,
                parseResult.HitErrorLimit)
            {
                Timings = timingCollector.Snapshot()
            };
        }
        timingCollector.Complete(ExecutionPhaseTiming.Parse, phaseStartedAt);

        // These APIs deliberately have no ModuleResolver. Reject every module-loading form
        // explicitly instead of relying on a later checker/runtime failure. Besides producing a
        // stable diagnostic, this keeps the untrusted-source boundary intact if the checker gains
        // more permissive single-file module behavior in the future.
        phaseStartedAt = timingCollector.Start();
        IReadOnlyList<Diagnostic> moduleDiagnostics;
        try
        {
            moduleDiagnostics = ModuleLoadingValidator.Validate(
                parseResult.Statements,
                options.FileName);
        }
        catch (SharpTSException ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.ValidateModules, phaseStartedAt);
            return new SingleSourceAnalysisResult(parseResult.Statements, null, [ex.Diagnostic])
            {
                Timings = timingCollector.Snapshot()
            };
        }
        catch (Exception ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.ValidateModules, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                [Diagnostic.CompileError(ex.Message, new SourceLocation(options.FileName, 1))])
            {
                Timings = timingCollector.Snapshot()
            };
        }
        if (moduleDiagnostics.Count > 0)
        {
            timingCollector.Fail(ExecutionPhaseTiming.ValidateModules, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                moduleDiagnostics)
            {
                Timings = timingCollector.Snapshot()
            };
        }
        timingCollector.Complete(ExecutionPhaseTiming.ValidateModules, phaseStartedAt);

        TypeCheckDiagnosticResult typeResult;
        IReadOnlyList<Diagnostic> diagnostics;
        phaseStartedAt = timingCollector.Start();
        try
        {
            var checker = new TypeChecker().WithFilePath(options.FileName);
            checker.SetDecoratorMode(options.DecoratorMode);
            typeResult = checker.CheckWithRecovery(parseResult.Statements);
            diagnostics = TypeCheckPolicy.ApplyLineDirectives(
                typeResult.Diagnostics,
                lexer.Pragmas);
        }
        catch (SharpTSException ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.TypeCheck, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                [ex.Diagnostic])
            {
                Timings = timingCollector.Snapshot()
            };
        }
        catch (Exception ex)
        {
            timingCollector.Fail(ExecutionPhaseTiming.TypeCheck, phaseStartedAt);
            return new SingleSourceAnalysisResult(
                parseResult.Statements,
                null,
                [Diagnostic.CompileError(ex.Message, new SourceLocation(options.FileName, 1))])
            {
                Timings = timingCollector.Snapshot()
            };
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            timingCollector.Fail(ExecutionPhaseTiming.TypeCheck, phaseStartedAt);
        else
            timingCollector.Complete(ExecutionPhaseTiming.TypeCheck, phaseStartedAt);

        return new SingleSourceAnalysisResult(
            parseResult.Statements,
            typeResult.TypeMap,
            diagnostics,
            typeResult.HitErrorLimit)
        {
            Timings = timingCollector.Snapshot()
        };
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
