using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Proves the #1387 intrinsic global-match path does not allocate the detailed
/// exec result objects that <c>String.matchAll</c> must expose. Both workloads
/// scan the same input and matches; only the observable result shape differs.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class RegexGlobalMatchAllocationBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _globalMatch = null!;
    private MethodInfo _detailedMatchAll = null!;

    [Params(100, 10_000)]
    public int N { get; set; }

    private const string Input = "alpha beta gamma delta epsilon";

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(RegexGlobalMatchAllocationBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.Regex.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource Regex.ts");
        using var reader = new StreamReader(stream);
        var dllPath = CompilationCache.GetOrCompile(reader.ReadToEnd(), "RegexGlobalMatchAllocation");
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "regex-global-match-allocation");
        _globalMatch = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexGlobalMatchLoop");
        _detailedMatchAll = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexDetailedMatchAllLoop");
    }

    [Benchmark(Baseline = true)]
    public object? IntrinsicGlobalStringMatch()
        => BenchmarkHarness.InvokeCompiled(_globalMatch, Input, (double)N);

    [Benchmark]
    public object? DetailedStringMatchAll()
        => BenchmarkHarness.InvokeCompiled(_detailedMatchAll, Input, (double)N);
}
