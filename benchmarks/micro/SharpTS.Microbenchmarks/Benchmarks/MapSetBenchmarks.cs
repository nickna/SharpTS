using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MapOperationsBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _mapOperations = null!;

    [Params(10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _mapOperations = LoadCompiled("mapOperations");

    [Benchmark]
    public double SharpTS() => _mapOperations(N);

    [Benchmark]
    public double Idiomatic() => MapSetCSharp.IdiomaticMapOperations(N);

    [Benchmark]
    public object Equivalent() => MapSetCSharp.EquivalentMapOperations(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MapIterationBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _mapIteration = null!;

    [Params(10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _mapIteration = LoadCompiled("mapIteration");

    [Benchmark]
    public double SharpTS() => _mapIteration(N);

    [Benchmark]
    public double Idiomatic() => MapSetCSharp.IdiomaticMapIteration(N);

    [Benchmark]
    public object Equivalent() => MapSetCSharp.EquivalentMapIteration(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SetOperationsBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _setOperations = null!;

    [Params(10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _setOperations = LoadCompiled("setOperations");

    [Benchmark]
    public double SharpTS() => _setOperations(N);

    [Benchmark]
    public double Idiomatic() => MapSetCSharp.IdiomaticSetOperations(N);

    [Benchmark]
    public object Equivalent() => MapSetCSharp.EquivalentSetOperations(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SetIterationBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _setIteration = null!;

    [Params(10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _setIteration = LoadCompiled("setIteration");

    [Benchmark]
    public double SharpTS() => _setIteration(N);

    [Benchmark]
    public double Idiomatic() => MapSetCSharp.IdiomaticSetIteration(N);

    [Benchmark]
    public object Equivalent() => MapSetCSharp.EquivalentSetIteration(N);
}
