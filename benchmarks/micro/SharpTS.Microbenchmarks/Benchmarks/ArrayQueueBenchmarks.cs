using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ArrayQueueBenchmarks
{
    private Func<double, double> _shift = null!;
    private Func<double, double> _unshift = null!;

    [Params(1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(ArrayQueueBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ArrayQueue.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource ArrayQueue.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(reader.ReadToEnd(), "ArrayQueue");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(dllPath, "array-queue");
        BenchmarkHarness.InitializeCompiledModules(compiled);
        _shift = BenchmarkHarness.GetCompiledNumberFunc(compiled, "arrayShiftDrain");
        _unshift = BenchmarkHarness.GetCompiledNumberFunc(compiled, "arrayUnshiftBuild");
    }

    [Benchmark]
    [BenchmarkCategory("Shift")]
    public double SharpTS_Shift() => _shift(N);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Shift")]
    public double CSharp_Shift() => ArrayQueueBaselines.ShiftDrain(N);

    [Benchmark]
    [BenchmarkCategory("Unshift")]
    public double SharpTS_Unshift() => _unshift(N);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Unshift")]
    public double CSharp_Unshift() => ArrayQueueBaselines.UnshiftBuild(N);
}
