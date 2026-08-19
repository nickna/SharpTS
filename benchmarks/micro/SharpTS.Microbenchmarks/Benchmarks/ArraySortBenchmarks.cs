using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Dense <c>Array.prototype.sort</c> benchmark. The TypeScript function copies
/// a reusable input before sorting so every invocation sees identical unsorted
/// data, while <see cref="MemoryDiagnoserAttribute"/> captures the sort path's
/// managed allocation cost.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ArraySortBenchmarks
{
    private MethodInfo _sortNumbers = null!;
    private List<object> _numbers = null!;

    [Params(100, 1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(ArraySortBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ArraySort.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource ArraySort.ts");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        string dllPath = CompilationCache.GetOrCompile(source, "ArraySort");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "arraysort");
        _sortNumbers = BenchmarkHarness.GetCompiledMethod(
            compiled, "sortNumbers");

        _numbers = new List<object>(N);
        long state = 123456789;
        for (int index = 0; index < N; index++)
        {
            state = state * 48271 % 2147483647;
            _numbers.Add((double)state);
        }
    }

    [Benchmark]
    public object? SharpTS_DenseNumericSort()
        => BenchmarkHarness.InvokeCompiled(_sortNumbers, _numbers);
}
