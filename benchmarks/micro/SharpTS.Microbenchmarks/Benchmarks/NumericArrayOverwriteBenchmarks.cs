using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NumericArrayOverwriteBenchmarks
{
    private Func<double, double> _sharpTs = null!;
    private double[] _baseline = null!;

    [Params(1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly compiled = NumericArrayWriteBenchmarkAssembly.Load();
        BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "setupNumericArrays")(N);
        _sharpTs = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "numericArrayOverwrite");
        _baseline = new double[N];
    }

    [Benchmark]
    public double SharpTS() => _sharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() =>
        NumericArrayWriteBaselines.Overwrite(_baseline);
}
