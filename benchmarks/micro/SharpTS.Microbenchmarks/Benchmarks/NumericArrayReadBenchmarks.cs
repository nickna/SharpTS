using System.Reflection;
using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Read-only attribution: all receiver construction is outside measurement.</summary>
[MemoryDiagnoser]
public class NumericArrayReadBenchmarks
{
    private Func<object, double, double> _run = null!;
    private object _values = null!;

    [Params("BoxedArray", "NumericArray", "PlainList")]
    public string Storage { get; set; } = null!;

    [Params("readFixed", "readVarying")]
    public string Case { get; set; } = null!;

    [Params(10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(NumericArrayReadBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericArrayRead.ts")!;
        using var reader = new StreamReader(stream);
        var assembly = BenchmarkHarness.LoadCompiledAssembly(
            CompilationCache.GetOrCompile(reader.ReadToEnd(), "NumericArrayRead"), "numeric-array-read");
        BenchmarkHarness.InitializeCompiledModules(assembly);
        _run = BenchmarkHarness.GetCompiledMethod(assembly, Case)
            .CreateDelegate<Func<object, double, double>>();
        var boxed = new List<object> { 1d, 1d, 1d, 1d, 1d };
        _values = Storage switch
        {
            "PlainList" => boxed,
            "BoxedArray" => Activator.CreateInstance(assembly.GetType("$Array")!, [boxed])!,
            "NumericArray" => BenchmarkHarness.GetCompiledMethod(assembly, "numericReadInput").Invoke(null, null)!,
            _ => throw new InvalidOperationException(Storage)
        };
        if (_run(_values, N) != 0.5 + 4 * N)
            throw new InvalidOperationException($"Incorrect checksum for {Storage}/{Case}");
    }

    [Benchmark]
    public double Run() => _run(_values, N);
}
