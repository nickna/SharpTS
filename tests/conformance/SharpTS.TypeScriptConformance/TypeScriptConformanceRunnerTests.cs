using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Acceptance tests for #84: the runner classifies a hand-picked test into
/// one of the buckets and the result is actionable. We don't assert <c>Pass</c>
/// — SharpTS isn't 100% conformant and the point is to surface where it isn't.
/// The bar is that the pipeline runs end-to-end without throwing.
/// </summary>
public class TypeScriptConformanceRunnerTests
{
    private readonly ITestOutputHelper _output;

    public TypeScriptConformanceRunnerTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The canonical hand-picked test from #84's acceptance criteria.
    /// Exercises conditional types — dense type-system mechanics, no lib
    /// dependency. Whatever bucket it lands in is fine; we just need
    /// classification to succeed.
    /// </summary>
    [Fact]
    public void RunOne_ConditionalTypes1_ClassifiesIntoABucket()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/typescript not initialized — skipping");
            return;
        }

        var testPath = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "types", "conditional", "conditionalTypes1.ts");
        Assert.True(File.Exists(testPath), $"Expected hand-picked test at {testPath}");

        var runner = new TypeScriptConformanceRunner(root);
        var result = runner.RunOne(testPath);

        _output.WriteLine($"outcome: {result.Outcome}");
        if (result.Message is not null) _output.WriteLine($"  message: {result.Message}");
        if (result.SkipReason is not null) _output.WriteLine($"  skip:    {result.SkipReason}");
        if (result.ExpectedDiagnostics is { Count: > 0 } expected)
            _output.WriteLine($"  expected ({expected.Count}): {string.Join(", ", expected.Take(5).Select(d => $"{d.TsCode}@L{d.Line}"))}{(expected.Count > 5 ? "..." : "")}");
        if (result.ActualDiagnostics is { Count: > 0 } actual)
            _output.WriteLine($"  actual   ({actual.Count}): {string.Join(", ", actual.Take(5).Select(d => $"{d.TsCode}@L{d.Line}"))}{(actual.Count > 5 ? "..." : "")}");

        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
    }

    [Fact]
    public void RunOne_Es2018IntlApis_MatchesTargetLibraryBaseline()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "es2018",
            "es2018IntlAPIs.ts");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(
            result.Outcome == TypeScriptConformanceOutcome.Pass,
            result.Message ?? result.Outcome.ToString());
    }

    [Fact]
    public void RunOne_TsxElementResolution19_MatchesEmptyDiagnosticBaseline()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "jsx",
            "tsxElementResolution19.tsx");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(
            result.Outcome == TypeScriptConformanceOutcome.Pass,
            result.Message ?? result.Outcome.ToString());
    }

    [Fact]
    public void RunOne_TsxReferenceToHarnessLib_ResolvesFixture()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "jsx",
            "checkJsxChildrenProperty1.tsx");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
    }

    [Theory]
    [InlineData("es2022/arbitraryModuleNamespaceIdentifiers/arbitraryModuleNamespaceIdentifiers_syntax.ts")]
    [InlineData("jsx/jsxParsingError2.tsx")]
    [InlineData("jsx/jsxAttributeInitializer.ts")]
    [InlineData("jsx/jsxInvalidEsprimaTestSuite.tsx")]
    [InlineData("jsx/tsxElementResolution17.tsx")]
    [InlineData("jsx/tsxNamespacedTagName1.tsx")]
    [InlineData("jsx/tsxReactEmitEntities.tsx")]
    public void RunOne_IntentionalSyntaxErrorsBecomeComparableDiagnostics(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
    }

    [Theory]
    [InlineData("es2019/importMeta/importMeta.ts")]
    [InlineData("es2020/modules/exportAsNamespace_nonExistent.ts")]
    [InlineData("es2022/es2024SharedMemory.ts")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarations.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarationsLocalTypes.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryLocalTypeGlobalFallback.tsx")]
    public void RunOne_ExpandedSubset_DoesNotCrashParserOrChecker(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);
        _output.WriteLine(result.Message ?? result.Outcome.ToString());

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
    }

    [Fact]
    public void RunOne_NonexistentFile_ReturnsHarnessError()
    {
        var runner = new TypeScriptConformanceRunner("/nonexistent");
        var result = runner.RunOne("/nonexistent/missing.ts");
        Assert.Equal(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
        Assert.Contains("Failed to read", result.Message);
    }

    [Fact]
    public void RunOne_DirectiveSkip_ShortCircuits()
    {
        // Build a tiny test with @experimentalDecorators set, configure the
        // runner to skip it. Verifies the skip-list short-circuit before any
        // parse/check work happens.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "// @experimentalDecorators: true\nconst x = 1;\n");
            var runner = new TypeScriptConformanceRunner(
                "/fake-root",
                skipDirectives: new HashSet<string>(StringComparer.Ordinal) { "experimentaldecorators" });
            var result = runner.RunOne(tmp);
            Assert.Equal(TypeScriptConformanceOutcome.Skipped, result.Outcome);
            Assert.Equal("directive:experimentaldecorators", result.SkipReason);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_MultiFile_UsesProgramResolver()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "// @filename: a.ts\nexport const x = 1;\n// @filename: b.ts\nimport { x } from './a';\n");
            var runner = new TypeScriptConformanceRunner("/fake-root");
            var result = runner.RunOne(tmp);
            Assert.NotEqual(TypeScriptConformanceOutcome.Skipped, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_MultiFile_ReportsDiagnosticsFromEveryRoot()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "// @filename: a.ts\nexport const x: string = 1;\n" +
                "// @filename: b.ts\nimport { x } from './a';\nconst y: number = x;\n");
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            Assert.Contains(result.ActualDiagnostics ?? [], d => d.TsCode == "TS2322");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_TsxWithGlobalIntrinsicElements_UsesProgramResolver()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"sharpts-{Guid.NewGuid():N}.tsx");
        try
        {
            // Automatic runtime: classic `@jsx: react` without a React import would be a
            // legitimate TS2304 under the JSX checking pipeline (as in tsc).
            File.WriteAllText(tmp, """
                // @jsx: react-jsx
                // @filename: renderer.d.ts
                declare global {
                    namespace JSX {
                        interface IntrinsicElements { button: { disabled?: boolean }; }
                    }
                }
                export {};
                // @filename: view.tsx
                <button disabled={false}></button>;
                """);

            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            Assert.DoesNotContain(
                result.ActualDiagnostics ?? [],
                diagnostic => diagnostic.TsCode is "TS2304" or "TS2339" or "TS1360");
        }
        finally { File.Delete(tmp); }
    }

    #region Strictness directives reach the checker

    /// <summary>
    /// Runs a synthetic test through the real runner and returns the TS codes it produced.
    /// A <c>/fake-root</c> means no <c>*.errors.txt</c> baseline is found, so the outcome is
    /// uninteresting — what is pinned here is the directive → TypeChecker option wiring.
    /// </summary>
    private static IReadOnlyList<string> ActualCodes(string source)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, source);
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);
            Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            return result.ActualDiagnostics?.Select(d => d.TsCode).ToList() ?? [];
        }
        finally { File.Delete(tmp); }
    }

    // An unannotated parameter on a DECLARED function — the one shape SharpTS reports
    // noImplicitAny for. Arrows are deliberately exempt, so they cannot be used here.
    private const string ImplicitAnyParam = "function f(x) { return x; }\n";

    [Fact]
    public void NoDirectives_UseTypeScript6StrictDefault()
    {
        Assert.Contains("TS7006", ActualCodes(ImplicitAnyParam));
    }

    [Fact]
    public void StrictDirective_EnablesNoImplicitAny()
    {
        Assert.Contains("TS7006", ActualCodes("// @strict: true\n" + ImplicitAnyParam));
    }

    [Fact]
    public void NoImplicitAnyDirective_EnablesItWithoutStrict()
    {
        Assert.Contains("TS7006", ActualCodes("// @noImplicitAny: true\n" + ImplicitAnyParam));
    }

    [Fact]
    public void NoImplicitAnyDirective_OverridesStrict()
    {
        // The specific directive beats the umbrella, matching how @strictNullChecks behaves.
        Assert.DoesNotContain("TS7006",
            ActualCodes("// @strict: true\n// @noImplicitAny: false\n" + ImplicitAnyParam));
    }

    [Fact]
    public void StrictDirectiveFalse_LeavesNoImplicitAnyOff()
    {
        Assert.DoesNotContain("TS7006", ActualCodes("// @strict: false\n" + ImplicitAnyParam));
    }

    #endregion
}

/// <summary>
/// Tests for the committed-baseline read/write/diff harness. Mirrors the
/// shape of <c>SharpTS.Test262.Test262BaselineDiffer</c>'s tests.
/// </summary>
public class TypeScriptConformanceBaselineTests
{
    [Fact]
    public void Header_IsVersionedAndPinsCorpus()
    {
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        Assert.StartsWith(
            "# SharpTS baseline-format=1 suite=TypeScript corpus=" + revision + " — ",
            TypeScriptConformanceBaseline.Header(revision));
    }

    [Fact]
    public void EncodeBucket_PassWithNoSkipReason_JustOutcomeName()
    {
        var r = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Pass, null, null);
        Assert.Equal("Pass", TypeScriptConformanceBaseline.EncodeBucket(r));
    }

    [Fact]
    public void EncodeBucket_SkippedWithReason_AppendsReason()
    {
        var r = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Skipped, null, "lib-drift");
        Assert.Equal("Skipped:lib-drift", TypeScriptConformanceBaseline.EncodeBucket(r));
    }

    [Fact]
    public void Diff_EmptyToEmpty_NoChanges()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        Assert.False(diff.HasHardFailures);
        Assert.Empty(diff.NewRegressions);
        Assert.Empty(diff.NewEntries);
    }

    [Fact]
    public void Diff_PassToFail_IsRegression()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Pass" },
            new Dictionary<string, string> { ["a.ts"] = "Fail" });
        Assert.True(diff.HasHardFailures);
        Assert.Single(diff.NewRegressions);
        Assert.Equal("a.ts", diff.NewRegressions[0].RelPath);
    }

    [Fact]
    public void Diff_FailToPass_IsNewPassHardFailure()
    {
        // Forces baseline updates through review so improvements are noticed.
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Fail" },
            new Dictionary<string, string> { ["a.ts"] = "Pass" });
        Assert.True(diff.HasHardFailures);
        Assert.Single(diff.NewPasses);
    }

    [Fact]
    public void Diff_SkippedReasonChange_IsBucketChangeNotHardFailure()
    {
        // Both Skipped — same "good" bucket, just different reason. Worth
        // surfacing but not worth failing the build.
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Skipped:directive:foo" },
            new Dictionary<string, string> { ["a.ts"] = "Skipped:lib-drift" });
        Assert.False(diff.HasHardFailures);
        Assert.Single(diff.BucketChanges);
    }

    [Fact]
    public void Diff_RemovedEntry_IsTracked()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Pass", ["b.ts"] = "Fail" },
            new Dictionary<string, string> { ["a.ts"] = "Pass" });
        Assert.Single(diff.RemovedEntries);
        Assert.Equal("b.ts", diff.RemovedEntries[0].RelPath);
    }
}
