using System.Reflection;
using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Allocation and throughput attribution for scalar and ordinary rest calls.</summary>
[MemoryDiagnoser]
public class NumericRestBenchmarks
{
    private Func<double, double> _run = null!;

    [Params("stableNumericRest", "indirectNumericRest", "dynamicIndexNumericRest", "spreadNumericRest",
        "selectedNumericRest", "varyingIndexNumericRest")]
    public string Case { get; set; } = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(NumericRestBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericRest.ts")!;
        using var reader = new StreamReader(stream);
        string path = CompilationCache.GetOrCompile(reader.ReadToEnd(), "NumericRest");
        Assembly assembly = BenchmarkHarness.LoadCompiledAssembly(path, "numeric-rest");
        BenchmarkHarness.InitializeCompiledModules(assembly);
        _run = BenchmarkHarness.GetCompiledNumberFunc(assembly, Case);
        double pairs = N / 2;
        double expected = Case == "varyingIndexNumericRest"
            ? 0.5 + 3 * pairs * (pairs - 1) + 10 * pairs
            : 0.5 + (double)N * (N - 1) / 2 + 6 * N;
        if (_run(N) != expected)
            throw new InvalidOperationException("Numeric rest checksum mismatch");
    }

    [Benchmark]
    public double Run() => _run(N);
}
