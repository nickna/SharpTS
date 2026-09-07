using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Typed-delegate measurements; compilation and delegate creation are untimed.</summary>
[MemoryDiagnoser]
public class ObjectDestructuringBenchmarks
{
    private Func<double, double> _dictionary = null!;
    private Func<double, double> _carrier = null!;
    private Func<double, double> _direct = null!;
    private Func<double, double> _hoisted = null!;
    private Func<double, double> _fractional = null!;
    private Func<double, double> _varying = null!;
    private Func<double, double> _mutated = null!;

    [Params(1_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(ObjectDestructuringBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ObjectDestructuring.ts")!;
        using var reader = new StreamReader(stream);
        var path = CompilationCache.GetOrCompile(reader.ReadToEnd(), "ObjectDestructuring");
        var assembly = BenchmarkHarness.LoadCompiledAssembly(path, "object-destructuring");
        _dictionary = BenchmarkHarness.GetCompiledNumberFunc(assembly, "destructureMaterialized");
        _carrier = BenchmarkHarness.GetCompiledNumberFunc(assembly, "destructureCarrierMaterialized");
        _direct = BenchmarkHarness.GetCompiledNumberFunc(assembly, "direct");
        _hoisted = BenchmarkHarness.GetCompiledNumberFunc(assembly, "hoisted");
        _fractional = BenchmarkHarness.GetCompiledNumberFunc(assembly, "fractional");
        _varying = BenchmarkHarness.GetCompiledNumberFunc(assembly, "varying");
        _mutated = BenchmarkHarness.GetCompiledNumberFunc(assembly, "mutated");
        if (_dictionary(N) != N * 3d || _carrier(N) != N * 3d || _direct(N) != N * 3d || _hoisted(N) != N * 3d ||
            _fractional(N) != N * 3.75 || _varying(N) != N * 5d ||
            _mutated(N) != N * (N - 1d) / 2 + N * 2d)
            throw new InvalidOperationException("Object destructuring benchmark checksum mismatch.");
    }

    [Benchmark] public double Dictionary() => _dictionary(N);
    [Benchmark] public double MaterializedCarrier() => _carrier(N);
    [Benchmark] public double Direct() => _direct(N);
    [Benchmark] public double ManuallyHoisted() => _hoisted(N);
    [Benchmark] public double Fractional() => _fractional(N);
    [Benchmark] public double Varying() => _varying(N);
    [Benchmark] public double Mutated() => _mutated(N);

    [Benchmark(Baseline = true)]
    public double CSharpNumericLoop()
    {
        double result = 0;
        for (int i = 0; i < N; i++) result = result + 1 + 2;
        return result;
    }
}
