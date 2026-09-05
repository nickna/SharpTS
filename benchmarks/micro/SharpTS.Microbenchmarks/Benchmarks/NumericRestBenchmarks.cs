using System.Reflection;
using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Rest packing, dispatch and scalar-specialization attribution.</summary>
[MemoryDiagnoser]
public class NumericRestBenchmarks
{
    private Func<double, double> _run = null!;

    [Params("restFixedParameters", "restDirect", "restAlias", "restConstantIndex",
        "restVaryingIndex", "restPacking", "restEscaping", "restSpread", "restDynamicDispatch",
        "restSelectedTarget")]
    public string Case { get; set; } = null!;

    [Params(10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using Stream stream = typeof(NumericRestBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericRest.ts")!;
        using var reader = new StreamReader(stream);
        string path = CompilationCache.GetOrCompile(reader.ReadToEnd(), "NumericRest");
        Assembly assembly = BenchmarkHarness.LoadCompiledAssembly(path, "numeric-rest");
        BenchmarkHarness.InitializeCompiledModules(assembly);
        _run = BenchmarkHarness.GetCompiledNumberFunc(assembly, Case);
        double range = (double)N * (N - 1) / 2;
        double expected = Case switch
        {
            "restPacking" or "restEscaping" => 0.5 + 4 * N,
            "restVaryingIndex" => 0.5 + 4 * range,
            _ => 0.5 + range + 6 * N
        };
        if (_run(N) != expected)
            throw new InvalidOperationException($"Incorrect checksum for {Case}");
    }

    [Benchmark]
    public double Run() => _run(N);
}
