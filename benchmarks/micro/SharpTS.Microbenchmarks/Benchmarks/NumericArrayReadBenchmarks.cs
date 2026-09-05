using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NumericArrayReadBenchmarks
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
            compiled, "numericArrayRead");
        _baseline = new double[N];
        for (int i = 0; i < N; i++)
            _baseline[i] = i * 3.0;
    }

    [Benchmark]
    public double SharpTS() => _sharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() =>
        NumericArrayWriteBaselines.Read(_baseline);
}

internal static class NumericArrayWriteBenchmarkAssembly
{
    private static Assembly? _compiled;

    internal static Assembly Load()
    {
        if (_compiled is not null) return _compiled;
        Assembly assembly = typeof(NumericArrayWriteBenchmarkAssembly).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericArrayWrite.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource NumericArrayWrite.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "NumericArrayWriteSplit");
        _compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "numeric-array-write-split");
        BenchmarkHarness.InitializeCompiledModules(_compiled);
        return _compiled;
    }
}
