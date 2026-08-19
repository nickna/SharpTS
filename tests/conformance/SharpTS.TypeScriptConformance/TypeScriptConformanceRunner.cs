using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using System.Text.RegularExpressions;

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

        // Build an in-memory program so virtual @filename files, imports,
        // triple-slash references, declaration files, lib.*.d.ts and @types all
        // flow through the same resolver used by the product CLI.
        string virtualRoot = Path.Combine(
            Path.GetTempPath(),
            "SharpTS.TypeScriptConformance",
            Path.GetFileNameWithoutExtension(testFilePath));
        var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootFiles = new List<string>();
        foreach (var file in metadata.Files)
        {
            string relativeName = file.Name
                .Replace(':', '_')
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(
                virtualRoot,
                relativeName.Replace('/', Path.DirectorySeparatorChar)));
            virtualFiles[path] = file.Body;
            rootFiles.Add(path);

            // The TypeScript test harness exposes tests/lib fixtures through the
            // virtual /.lib/ directory. Preserve that convention in our in-memory
            // resolver so JSX cases can reference react.d.ts/react16.d.ts without
            // depending on files outside the vendored corpus.
            foreach (Match match in Regex.Matches(
                         file.Body,
                         """///\s*<reference\s+path\s*=\s*["']/(?<path>\.lib/[^"']+)["']""",
                         RegexOptions.IgnoreCase))
            {
                string fixturePath = match.Groups["path"].Value;
                string fixtureSourcePath = Path.Combine(
                    _typescriptRoot,
                    "tests",
                    "lib",
                    fixturePath[".lib/".Length..].Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fixtureSourcePath))
                {
                    string virtualFixturePath = Path.GetFullPath(
                        "/" + fixturePath.Replace('/', Path.DirectorySeparatorChar));
                    virtualFiles[virtualFixturePath] = File.ReadAllText(fixtureSourcePath);
                }
            }
        }

        if (rootFiles.Count == 0)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.HarnessError,
                "Test did not contain any source files.",
                null);
        }

        TypeScriptProgramOptions programOptions = new()
        {
            LoadDefaultLib = true,
            NoLib = DirectiveBool(metadata, "nolib") ?? false,
            Lib = metadata.Lib.Count > 0
                ? metadata.Lib
                : [DefaultLibraryForTarget(metadata.Target)],
            Types = DirectiveList(metadata, "types"),
            TypeRoots = null,
            PreferDeclarationFiles = true,
        };

        ModuleResolver resolver;
        List<ParsedModule> modules;
        try
        {
            resolver = new ModuleResolver(rootFiles[0], virtualFiles, programOptions)
            {
                JsxOptions = ResolveJsxOptions(metadata),
                RecoverParseErrors = true,
            };

            // TypeScript resolves ambient modules declared by any declaration root before
            // following imports from the program's other roots. Register those declarations
            // up front so an in-test `declare module "react"` wins over SharpTS's executable
            // React fallback just as an installed declaration package would.
            var declarationRoots = rootFiles
                .Where(IsDeclarationFile)
                .Select(rootFile => resolver.LoadModule(rootFile))
                .ToList();
            resolver.RegisterAmbientModuleDeclarations(declarationRoots);

            var entry = resolver.LoadProgram(rootFiles[0]);
            modules = resolver.GetModulesInOrder(entry);
            resolver.RegisterAmbientModuleDeclarations(modules);

            // TypeScript treats every @filename section as a root file, even if
            // it is not reachable through an import from the first section.
            var seen = modules.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string rootFile in rootFiles.Skip(1))
            {
                var root = resolver.LoadModule(rootFile);
                var rootModules = resolver.GetModulesInOrder(root);
                resolver.RegisterAmbientModuleDeclarations(rootModules);
                foreach (var module in rootModules)
                {
                    if (seen.Add(module.Path))
                        modules.Add(module);
                }
            }
        }
        catch (Exception ex)
        {
            return new TypeScriptConformanceResult(
                TypeScriptConformanceOutcome.ParseError,
                ex.Message,
                null);
        }

        // Type-check with recovery so we collect every diagnostic, not just
        // the first one.
        IReadOnlyList<Diagnostic> diagnostics;
        try
        {
            // Each strictness knob follows the test's directives, with the specific directive
            // overriding @strict. TypeScript 6's compiler defaults @strict to true; the metadata
            // parser supplies that default while preserving an explicit @strict: false.
            bool strictNullChecks = metadata.StrictNullChecks ?? metadata.Strict;
            bool noImplicitAny = metadata.NoImplicitAny ?? metadata.Strict;
            // Raise the error cap well above the product default (10) so we collect every diagnostic
            // a test expects — *.errors.txt baselines can list many errors in one file.
            var checker = new TypeChecker(new TypeCheckerOptions
            {
                StrictNullChecks = strictNullChecks,
                StrictFunctionTypes = DirectiveBool(metadata, "strictfunctiontypes") ?? metadata.Strict,
                NoImplicitAny = noImplicitAny,
                NoImplicitThis = DirectiveBool(metadata, "noimplicitthis") ?? metadata.Strict,
                StrictPropertyInitialization =
                    DirectiveBool(metadata, "strictpropertyinitialization") ?? metadata.Strict,
                ExactOptionalPropertyTypes =
                    DirectiveBool(metadata, "exactoptionalpropertytypes") ?? false,
                NoUncheckedIndexedAccess =
                    DirectiveBool(metadata, "nouncheckedindexedaccess") ?? false,
                MaxErrors = 1000,
            });
            checker.CheckModules(modules, resolver);
            diagnostics = modules
                .SelectMany(module => module.ParseDiagnostics)
                .Concat(checker.GetDiagnostics())
                .ToList();
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

        var actual = ToBaselineDiagnostics(diagnostics);

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

        var matches = DiagnosticSetsMatch(expected, actual);
        return new TypeScriptConformanceResult(
            matches ? TypeScriptConformanceOutcome.Pass : TypeScriptConformanceOutcome.Fail,
            matches ? null : FormatMismatch(expected, actual),
            null,
            expected,
            actual);
    }

    private static bool IsDeclarationFile(string path) =>
        path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase);

    private static bool? DirectiveBool(
        TypeScriptConformanceMetadata metadata,
        string name)
    {
        if (!metadata.RawDirectives.TryGetValue(name, out string? value))
            return null;
        if (bool.TryParse(value, out bool parsed))
            return parsed;
        return null;
    }

    /// <summary>
    /// Maps the test's <c>@jsx:</c> family of directives onto parser jsx options. tsc's
    /// harness default for an unset <c>@jsx</c> is None (JSX in .tsx is TS17004);
    /// <c>preserve</c>/<c>react-native</c> are checker-equivalent to the classic transform
    /// here since the conformance runner never emits.
    /// </summary>
    private static JsxParseOptions ResolveJsxOptions(TypeScriptConformanceMetadata metadata)
    {
        JsxMode mode = metadata.Jsx?.Trim().ToLowerInvariant() switch
        {
            "react" or "preserve" or "react-native" => JsxMode.React,
            "react-jsx" => JsxMode.ReactJsx,
            "react-jsxdev" => JsxMode.ReactJsxDev,
            _ => JsxMode.None,
        };
        var options = new JsxParseOptions(mode);
        if (metadata.RawDirectives.TryGetValue("jsxfactory", out string? factory) &&
            !string.IsNullOrWhiteSpace(factory))
            options = options with { Factory = factory.Trim() };
        if (metadata.RawDirectives.TryGetValue("jsxfragmentfactory", out string? fragment) &&
            !string.IsNullOrWhiteSpace(fragment))
            options = options with { FragmentFactory = fragment.Trim() };
        if (metadata.RawDirectives.TryGetValue("jsximportsource", out string? importSource) &&
            !string.IsNullOrWhiteSpace(importSource))
            options = options with { ImportSource = importSource.Trim() };
        return options;
    }

    private static IReadOnlyList<string>? DirectiveList(
        TypeScriptConformanceMetadata metadata,
        string name)
    {
        if (!metadata.RawDirectives.TryGetValue(name, out string? value))
            return null;
        return value.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string DefaultLibraryForTarget(string? target)
    {
        string selected = target?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .ToLowerInvariant() ?? "es5";

        return selected switch
        {
            "es3" or "es5" => "lib.d.ts",
            "es6" or "es2015" => "lib.es6.d.ts",
            "es2016" or "es2017" or "es2018" or "es2019" or
            "es2020" or "es2021" or "es2022" or "es2023" or
            "es2024" or "es2025" =>
                $"lib.{selected}.full.d.ts",
            "esnext" or "latest" => "lib.esnext.full.d.ts",
            _ => "lib.d.ts",
        };
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
        "es2024" => 2024,
        "es2025" => 2025,
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
