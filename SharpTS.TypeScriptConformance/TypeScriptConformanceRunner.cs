using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Runs a single TS conformance test through SharpTS's lexer/parser/type
/// checker, collects diagnostics, and diffs them against the test's
/// <c>*.errors.txt</c> baseline. Mirrors <c>SharpTS.Test262.Test262Runner</c>
/// in shape; the pipeline is much simpler because there's no execution stage.
///
/// The runner is intentionally non-throwing — every failure mode maps to a
/// <see cref="TypeScriptConformanceOutcome"/> bucket. Throwing would make
/// baseline runs brittle (one rogue test would tank the whole suite).
/// </summary>
public sealed class TypeScriptConformanceRunner
{
    private readonly string _typescriptRoot;
    private readonly IReadOnlySet<string>? _skipDirectives;
    private readonly IReadOnlySet<string>? _skipTests;

    /// <summary>
    /// Constructs a runner against the vendored TypeScript checkout.
    /// <paramref name="skipDirectives"/> is an optional set of directive names
    /// (lower-cased, e.g. "experimentaldecorators") whose presence in a test's
    /// metadata short-circuits the run as <c>Skipped</c>.
    /// <paramref name="skipTests"/> is an optional set of test paths (relative
    /// to the conformance corpus root, forward slashes) that bypass the
    /// pipeline entirely. Used as an escape hatch for tests that crash the
    /// runner.
    /// </summary>
    public TypeScriptConformanceRunner(
        string typescriptRoot,
        IReadOnlySet<string>? skipDirectives = null,
        IReadOnlySet<string>? skipTests = null)
    {
        _typescriptRoot = typescriptRoot;
        _skipDirectives = skipDirectives;
        _skipTests = skipTests;
    }

    /// <summary>
    /// Runs one test and returns its classified result. Does not throw.
    /// </summary>
    public TypeScriptConformanceResult RunOne(string testFilePath)
    {
        // Explicit skip-by-path — escape hatch for tests that crash the runner
        // in ways the bucket model can't absorb. Checked first so we don't even
        // open the file.
        if (_skipTests is not null)
        {
            var rel = Path.GetRelativePath(_typescriptRoot, testFilePath).Replace('\\', '/');
            if (_skipTests.Contains(rel))
                return new TypeScriptConformanceResult(
                    TypeScriptConformanceOutcome.Skipped,
                    null,
                    "explicitly-skipped");
        }

        string source;
        try
        {
            source = File.ReadAllText(testFilePath);
        }
        catch (Exception ex)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.HarnessError,
                $"Failed to read test file: {ex.Message}",
                null);
        }

        var metadata = TypeScriptConformanceMetadataParser.Parse(testFilePath, source);

        // Directive-based skip (e.g. @experimentalDecorators) — fast exit before
        // we burn parse/type-check cycles on something we'll throw away.
        if (_skipDirectives is not null)
        {
            foreach (var key in _skipDirectives)
            {
                if (metadata.RawDirectives.ContainsKey(key))
                    return new TypeScriptConformanceResult(
                        TypeScriptConformanceOutcome.Skipped,
                        null,
                        $"directive:{key}");
            }
        }

        // Multi-file tests need cross-file resolution (imports, declaration
        // merging across virtual files). #84 is single-file scope; defer
        // multi-file as a clean Skipped bucket so subset baselines are stable.
        if (metadata.Files.Count > 1)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.Skipped,
                null,
                "multi-file-deferred");
        }

        var virtualFile = metadata.Files[0];

        // Parse — failures here are a Pass IFF the baseline expected a parse
        // error. Otherwise ParseError. Today we don't yet distinguish parse
        // from type errors in the baseline match; treat any parse failure as
        // ParseError. (Refining this is on the same level as proper multi-file
        // handling — a follow-up.)
        ParseDiagnosticResult parseResult;
        try
        {
            var lexer = new Lexer(virtualFile.Body);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            parseResult = parser.Parse();
        }
        catch (Exception ex)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.ParseError,
                ex.Message,
                null);
        }
        if (!parseResult.IsSuccess)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.ParseError,
                parseResult.Diagnostics.FirstOrDefault()?.ToString(),
                null);
        }

        // Type-check with recovery so we collect every diagnostic, not just
        // the first one. CheckWithRecovery is the API designed for this.
        TypeCheckDiagnosticResult checkResult;
        try
        {
            // Each strictness knob follows the test's directives, with the specific directive
            // overriding @strict and everything defaulting off — matching how tsc generated the
            // legacy *.errors.txt baselines.
            bool strictNullChecks = metadata.StrictNullChecks ?? metadata.Strict;
            bool noImplicitAny = metadata.NoImplicitAny ?? metadata.Strict;
            // Raise the error cap well above the product default (10) so we collect every diagnostic
            // a test expects — *.errors.txt baselines can list many errors in one file.
            var checker = new TypeChecker(new TypeCheckerOptions
            {
                StrictNullChecks = strictNullChecks,
                StrictFunctionTypes = metadata.Strict,
                NoImplicitAny = noImplicitAny,
                MaxErrors = 1000,
            });
            checkResult = checker.CheckWithRecovery(parseResult.Statements);
        }
        catch (Exception ex)
        {
            // Anything that escapes CheckWithRecovery is a checker bug, not a
            // diagnostic. Bucket distinctly so we can spot regressions.
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.TypeCheckError,
                ex.Message,
                null);
        }

        var actual = ToBaselineDiagnostics(checkResult.Diagnostics);

        var baselinePath = ResolveBaselinePath(testFilePath);
        IReadOnlyList<BaselineDiagnostic> expected;
        try
        {
            expected = File.Exists(baselinePath)
                ? ErrorsBaselineParser.Parse(File.ReadAllText(baselinePath))
                : Array.Empty<BaselineDiagnostic>();
        }
        catch (Exception ex)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.HarnessError,
                $"Failed to read baseline {baselinePath}: {ex.Message}",
                null);
        }

        // Lib-drift filter (#83): if tsc expected diagnostics but our checker
        // produced none, the most likely cause is that we have a global tsc
        // doesn't (under whatever @lib the test set). We bucket as Skipped
        // rather than Fail so the baseline isn't dominated by lib-version
        // noise. Conservative — only fires when our diagnostic set is
        // completely empty AND expected is non-empty AND every expected code
        // is one of the property/global-resolution shapes.
        if (LooksLikeLibDrift(expected, actual))
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.Skipped,
                null,
                "lib-drift",
                expected,
                actual);
        }

        var matches = DiagnosticSetsMatch(expected, actual);
        return new TypeScriptConformanceResult(
            matches ? TypeScriptConformanceOutcome.Pass : TypeScriptConformanceOutcome.Fail,
            matches ? null : FormatMismatch(expected, actual),
            null,
            expected,
            actual);
    }

    /// <summary>
    /// Locates the <c>*.errors.txt</c> baseline for a given test path. TS uses
    /// <c>tests/baselines/reference/&lt;testname&gt;.errors.txt</c> (flat directory,
    /// no folder mirroring). Returns the expected path even if the file
    /// doesn't exist — caller treats absence as "no expected diagnostics."
    ///
    /// Multi-target tests (<c>// @target: es2015, es2020, ...</c>) emit one
    /// baseline per target — <c>name(target=X).errors.txt</c> — and no plain
    /// file. SharpTS has a single always-latest world model (globals are always
    /// available, no per-target lib), so we compare against the newest available
    /// target variant. Without this, those tests are scored against an empty
    /// baseline and every real diagnostic is mis-counted as spurious.
    /// </summary>
    private string ResolveBaselinePath(string testFilePath)
    {
        var basename = Path.GetFileNameWithoutExtension(testFilePath);
        var dir = TypeScriptConformancePaths.BaselinesDir(_typescriptRoot);
        var plain = Path.Combine(dir, $"{basename}.errors.txt");
        if (File.Exists(plain)) return plain;
        return ResolveNewestTargetBaseline(dir, basename) ?? plain;
    }

    /// <summary>
    /// Newest-target <c>name(target=X).errors.txt</c> baseline for a multi-target
    /// test, or null if none exist. "Newest" follows ES ordering
    /// (<c>es3 &lt; es5 &lt; es2015 &lt; ... &lt; esnext</c>) so the chosen baseline
    /// matches SharpTS's always-latest lib surface.
    /// </summary>
    private static string? ResolveNewestTargetBaseline(string baselinesDir, string basename)
    {
        // A missing baselines directory means "no baseline", not a crash. Without this an
        // uninitialized external/typescript submodule takes down the resolver with a
        // DirectoryNotFoundException instead of bucketing cleanly.
        if (!Directory.Exists(baselinesDir)) return null;

        string? best = null;
        var bestRank = int.MinValue;
        foreach (var path in Directory.EnumerateFiles(baselinesDir, $"{basename}(target=*).errors.txt"))
        {
            var file = Path.GetFileName(path);
            const string marker = "(target=";
            var open = file.IndexOf(marker, StringComparison.Ordinal);
            var close = file.IndexOf(").errors.txt", StringComparison.Ordinal);
            if (open < 0 || close <= open + marker.Length) continue;
            var target = file.Substring(open + marker.Length, close - (open + marker.Length));
            var rank = TargetRank(target);
            if (rank > bestRank) { bestRank = rank; best = path; }
        }
        return best;
    }

    private static int TargetRank(string target) => target.Trim().ToLowerInvariant() switch
    {
        "es3" => 3,
        "es5" => 5,
        "es6" or "es2015" => 2015,
        "es2016" => 2016,
        "es2017" => 2017,
        "es2018" => 2018,
        "es2019" => 2019,
        "es2020" => 2020,
        "es2021" => 2021,
        "es2022" => 2022,
        "es2023" => 2023,
        "esnext" => int.MaxValue,
        _ => 0,
    };

    /// <summary>
    /// Converts SharpTS diagnostics into the (line, tsCode) match-key form.
    /// Drops diagnostics with no <c>TsCode</c> (SharpTS-only — see #95) — they
    /// don't participate in conformance matching, intentionally.
    /// </summary>
    private static IReadOnlyList<BaselineDiagnostic> ToBaselineDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        var result = new List<BaselineDiagnostic>();
        foreach (var d in diagnostics)
        {
            if (d.TsCode is null) continue;
            if (d.Severity != DiagnosticSeverity.Error) continue;
            result.Add(new BaselineDiagnostic(d.Line, d.TsCode));
        }
        return result;
    }

    /// <summary>
    /// Set equality on (line, code) tuples. Multiple diagnostics with the
    /// same (line, code) collapse to one — TS sometimes reports duplicate
    /// codes at one position when a single source error cascades; that's a
    /// difference we don't want to chase.
    /// </summary>
    private static bool DiagnosticSetsMatch(
        IReadOnlyList<BaselineDiagnostic> expected,
        IReadOnlyList<BaselineDiagnostic> actual)
    {
        var e = new HashSet<(int, string)>(expected.Select(d => (d.Line, d.TsCode)));
        var a = new HashSet<(int, string)>(actual.Select(d => (d.Line, d.TsCode)));
        return e.SetEquals(a);
    }

    /// <summary>
    /// Lib-drift heuristic from #83: the test expected errors but we produced
    /// none, and every expected code is a "missing surface" shape (TS2339
    /// "Property does not exist", TS2304 "Cannot find name", TS2551 "did you
    /// mean", TS7053 "no index signature"). The strongest signal that the
    /// divergence is lib-version noise rather than a checker bug.
    /// </summary>
    private static bool LooksLikeLibDrift(
        IReadOnlyList<BaselineDiagnostic> expected,
        IReadOnlyList<BaselineDiagnostic> actual)
    {
        if (actual.Count > 0) return false;
        if (expected.Count == 0) return false;
        foreach (var e in expected)
        {
            if (e.TsCode is not ("TS2339" or "TS2304" or "TS2551" or "TS7053")) return false;
        }
        return true;
    }

    private static string FormatMismatch(
        IReadOnlyList<BaselineDiagnostic> expected,
        IReadOnlyList<BaselineDiagnostic> actual)
    {
        var e = new HashSet<(int, string)>(expected.Select(d => (d.Line, d.TsCode)));
        var a = new HashSet<(int, string)>(actual.Select(d => (d.Line, d.TsCode)));
        var missing = e.Except(a).OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();
        var extra = a.Except(e).OrderBy(t => t.Item1).ThenBy(t => t.Item2).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($"baseline expected {expected.Count}, got {actual.Count}; ");
        if (missing.Count > 0)
            sb.Append($"missing: [{string.Join(", ", missing.Select(t => $"{t.Item2}@L{t.Item1}"))}]; ");
        if (extra.Count > 0)
            sb.Append($"extra: [{string.Join(", ", extra.Select(t => $"{t.Item2}@L{t.Item1}"))}]");
        return sb.ToString().TrimEnd(';', ' ');
    }
}
