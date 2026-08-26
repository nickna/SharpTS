using System.Reflection;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Measures regex literal compilation overhead. Each TS regex literal
/// (<c>/foo/g</c>) currently constructs a fresh
/// <c>System.Text.RegularExpressions.Regex</c> on every evaluation —
/// in a loop that's per-iter compilation. A cache keyed by
/// (pattern, options) should collapse repeated calls to a single
/// compilation per process lifetime.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class RegexBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _tsLiteral = null!;
    private MethodInfo _tsValidator = null!;
    private MethodInfo _tsExtract = null!;
    // Match the options used by SharpTS's emitted $RegExp for a plain literal,
    // so this is an engine-equivalent ceiling rather than an interpreted-regex
    // comparison.
    private readonly Regex _nativeValidator = new(
        "^[a-z]+$", RegexOptions.ECMAScript | RegexOptions.Compiled);
    private readonly Regex _nativeReplace = new(
        "foo", RegexOptions.ECMAScript | RegexOptions.Compiled);

    [Params(100, 10_000, 100_000)]
    public int N { get; set; }

    private const string ReplaceInput = "foo bar foo baz foo qux";
    private const string ValidatorInput = "abcdefghij";
    private const string ExtractInput = "contact: alice@example and bob@elsewhere";

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(RegexBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.Regex.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource Regex.ts");
        using var reader = new StreamReader(stream);
        var tsSource = reader.ReadToEnd();

        var dllPath = CompilationCache.GetOrCompile(tsSource, "Regex");
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "regex");

        _tsLiteral = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexLiteralLoop");
        _tsValidator = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexValidatorLoop");
        _tsExtract = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexExtractLoop");
    }

    [Benchmark]
    [BenchmarkCategory("RegexLiteral")]
    public object? SharpTS_RegexLiteralLoop()
        => BenchmarkHarness.InvokeCompiled(_tsLiteral, ReplaceInput, (double)N);

    [Benchmark]
    [BenchmarkCategory("RegexLiteral")]
    public int NativeCSharp_RegexReplaceLoop()
    {
        int total = 0;
        for (int i = 0; i < N; i++)
            total += _nativeReplace.Replace(ReplaceInput, "bar").Length;
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("RegexValidator")]
    public object? SharpTS_RegexValidatorLoop()
        => BenchmarkHarness.InvokeCompiled(_tsValidator, ValidatorInput, (double)N);

    [Benchmark]
    [BenchmarkCategory("RegexValidator")]
    public int NativeCSharp_RegexValidatorLoop()
    {
        int valid = 0;
        for (int i = 0; i < N; i++)
            if (_nativeValidator.IsMatch(ValidatorInput))
                valid++;
        return valid;
    }

    [Benchmark]
    [BenchmarkCategory("RegexExtract")]
    public object? SharpTS_RegexExtractLoop()
        => BenchmarkHarness.InvokeCompiled(_tsExtract, ExtractInput, (double)N);
}
