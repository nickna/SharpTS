using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Tracks the stable typed Number.prototype.toFixed path, including the exact
/// binary64-to-decimal rounding and its per-result allocation cost.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NumberToFixedBenchmarks
{
    private Func<double, double> _sharpTs = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(NumberToFixedBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumberToFixed.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource NumberToFixed.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "NumberToFixed");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "number-to-fixed");
        BenchmarkHarness.InitializeCompiledModules(compiled);
        _sharpTs = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "numberToFixedLoop");
    }

    [Benchmark]
    public double SharpTSExact() => _sharpTs(N);

    [Benchmark(Baseline = true)]
    public int BclFixedPoint()
    {
        int totalLength = 0;
        for (int i = 0; i < N; i++)
            totalLength += (i * 0.125).ToString("F2", CultureInfo.InvariantCulture).Length;
        return totalLength;
    }
}
