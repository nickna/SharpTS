using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;

namespace SharpTS.Microbenchmarks.Benchmarks;

// Starter-set workloads that broaden coverage beyond the original arithmetic
// kernels: a builtin-heavy JSON round-trip, a data-parallel typed-array kernel,
// and allocation/GC-heavy binary-trees. The TypeScript bodies live in
// benchmarks/cross-runtime/scripts/lib/algorithms.ts (shared byte-identical with the
// cross-runtime shell harness) and are reached through a cached
// Func<double,double> delegate via ComputationalBenchmarkBase.LoadCompiled.
//
// As elsewhere: SharpTS-compiled vs idiomatic C# (native types — the ceiling)
// vs "equivalent" C# (object?/boxing — the dynamic-typing tax).

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonRoundTripBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _jsonRoundTrip = null!;

    [Params(100, 1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _jsonRoundTrip = LoadCompiled("jsonRoundTrip");

    [Benchmark]
    public double SharpTS() => _jsonRoundTrip(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.JsonRoundTrip(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.JsonRoundTrip((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TypedArrayBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _typedArrayKernel = null!;

    [Params(1000, 100000, 1000000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _typedArrayKernel = LoadCompiled("typedArrayKernel");

    [Benchmark]
    public double SharpTS() => _typedArrayKernel(N);

    [Benchmark]
    public double Idiomatic() => IdiomaticCSharp.TypedArrayKernel(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.TypedArrayKernel((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BinaryTreesBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _binaryTrees = null!;

    [Params(8, 12, 16)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _binaryTrees = LoadCompiled("binaryTrees");

    [Benchmark]
    public double SharpTS() => _binaryTrees(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.BinaryTrees(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.BinaryTrees((double)N);
}
