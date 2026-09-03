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
    private Func<double, double, double, double, double, double> _severalInvariant = null!;
    private Func<double, double, double> _severalControl = null!;
    private Func<object, object, double, double> _dynamic = null!;
    private double _a;
    private double _b;
    private double _c;
    private double _d;

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
        _severalInvariant = BenchmarkHarness.GetCompiledMethod(compiled, "mathMaxSeveralInvariantLoop")
            .CreateDelegate<Func<double, double, double, double, double, double>>();
        _severalControl = BenchmarkHarness.GetCompiledMethod(compiled, "mathMaxSeveralControlLoop")
            .CreateDelegate<Func<double, double, double>>();
        _dynamic = BenchmarkHarness.GetCompiledMethod(compiled, "mathMinDynamicLoop")
            .CreateDelegate<Func<object, object, double, double>>();

        // Instance fields keep the native controls unknown to the C# compiler
        // while matching the values passed to the generated TypeScript methods.
        _a = 7;
        _b = 4;
        _c = 2;
        _d = 3;
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
    public double SharpTS_Several() => _several(_a, _b, _c, _d, N);

    [Benchmark]
    public double NativeCSharp_Several()
    {
        double a = _a;
        double b = _b;
        double c = _c;
        double d = _d;
        double total = 0;
        for (int i = 0; i < N; i++)
            total += Math.Max(Math.Max(Math.Max(a + (i % 2), b), c), d);
        return total;
    }

    [Benchmark]
    public double SharpTS_SeveralInvariant() => _severalInvariant(_a, _b, _c, _d, N);

    [Benchmark]
    public double NativeCSharp_SeveralInvariant()
    {
        double a = _a;
        double b = _b;
        double c = _c;
        double d = _d;
        double total = 0;
        for (int i = 0; i < N; i++)
            total += Math.Max(Math.Max(Math.Max(a, b), c), d);
        return total;
    }

    [Benchmark]
    public double SharpTS_SeveralControl() => _severalControl(_a, N);

    [Benchmark]
    public double NativeCSharp_SeveralControl()
    {
        double a = _a;
        double total = 0;
        for (int i = 0; i < N; i++)
            total += a + (i % 2);
        return total;
    }

    [Benchmark]
    public double SharpTS_DynamicControl() => _dynamic(3d, "4", N);
}
