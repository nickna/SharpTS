using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Ordinary dispatch with the callable wrapper created outside measurement.</summary>
[MemoryDiagnoser]
public class NumericRestDispatchBenchmarks
{
    private Func<double, object, double> _run = null!;
    private object _target = null!;

    [Params("Reads", "Length")]
    public string Case { get; set; } = null!;

    [Params(10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(NumericRestDispatchBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericRest.ts")!;
        using var reader = new StreamReader(stream);
        var assembly = BenchmarkHarness.LoadCompiledAssembly(
            CompilationCache.GetOrCompile(reader.ReadToEnd(), "NumericRest"), "numeric-rest");
        BenchmarkHarness.InitializeCompiledModules(assembly);
        _run = BenchmarkHarness.GetCompiledMethod(assembly, Case == "Reads" ? "restUnknownTarget" : "restUnknownLength")
            .CreateDelegate<Func<double, object, double>>();
        _target = BenchmarkHarness.GetCompiledMethod(assembly, Case == "Reads" ? "restReadTarget" : "restLengthTarget")
            .Invoke(null, null)!;
        double expected = Case == "Reads" ? 0.5 + (double)N * (N - 1) / 2 + 6 * N : 0.5 + 4 * N;
        if (_run(N, _target) != expected) throw new InvalidOperationException($"Incorrect checksum for {Case}");
    }

    [Benchmark]
    public double Run() => _run(N, _target);
}
