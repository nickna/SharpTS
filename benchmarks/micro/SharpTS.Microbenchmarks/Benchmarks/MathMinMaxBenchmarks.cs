using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Fixed-arity typed Math.min/max workloads, plus a dynamic control that must
/// retain JavaScript argument coercion.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MathMinMaxBenchmarks
{
    private Func<double, double> _zero = null!;
    private Func<double, double, double> _one = null!;
    private Func<double, double, double, double> _two = null!;
    private Func<double, double, double, double, double, double> _several = null!;
    private Func<object, object, double, double> _dynamic = null!;

    [Params(100, 10_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(MathMinMaxBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.MathMinMax.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource MathMinMax.ts");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        string dllPath = CompilationCache.GetOrCompile(source, "MathMinMax");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(dllPath, "math-min-max");
        _zero = BenchmarkHarness.GetCompiledMethod(compiled, "mathMinZeroLoop")
            .CreateDelegate<Func<double, double>>();
        _one = BenchmarkHarness.GetCompiledMethod(compiled, "mathMaxOneLoop")
            .CreateDelegate<Func<double, double, double>>();
        _two = BenchmarkHarness.GetCompiledMethod(compiled, "mathMinTwoLoop")
            .CreateDelegate<Func<double, double, double, double>>();
        _several = BenchmarkHarness.GetCompiledMethod(compiled, "mathMaxSeveralLoop")
            .CreateDelegate<Func<double, double, double, double, double, double>>();
        _dynamic = BenchmarkHarness.GetCompiledMethod(compiled, "mathMinDynamicLoop")
            .CreateDelegate<Func<object, object, double, double>>();
    }

    [Benchmark]
    public double SharpTS_Zero() => _zero(N);

    [Benchmark]
    public double SharpTS_One() => _one(3, N);

    [Benchmark]
    public double SharpTS_Two() => _two(3, 4, N);

    [Benchmark]
    public double NativeCSharp_Two()
    {
        double total = 0;
        for (int i = 0; i < N; i++)
            total += Math.Min(3, 4);
        return total;
    }

    [Benchmark]
    public double SharpTS_Several() => _several(1, 7, 3, 5, N);

    [Benchmark]
    public double SharpTS_DynamicControl() => _dynamic(3d, 4d, N);
}
