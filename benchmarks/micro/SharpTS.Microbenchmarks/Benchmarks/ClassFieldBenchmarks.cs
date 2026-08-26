using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;

namespace SharpTS.Microbenchmarks.Benchmarks;

public abstract class ClassFieldBenchmarkBase : ComputationalBenchmarkBase
{
    protected Func<double, double> SharpTs = null!;

    [Params(100_000)]
    public int N { get; set; }

    protected void Load(string functionName) => SharpTs = LoadCompiled(functionName);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ClassFieldReuseBenchmarks : ClassFieldBenchmarkBase
{
    [GlobalSetup]
    public void Setup() => Load("classFieldReuse");

    [Benchmark]
    public double SharpTS() => SharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() => ClassFieldCSharp.FieldReuse(N);

    [Benchmark]
    public object? BoxedEquivalentCSharp() => ClassFieldCSharp.BoxedFieldReuse(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ClassMethodReuseBenchmarks : ClassFieldBenchmarkBase
{
    [GlobalSetup]
    public void Setup() => Load("classMethodReuse");

    [Benchmark]
    public double SharpTS() => SharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() => ClassFieldCSharp.MethodReuse(N);

    [Benchmark]
    public object? BoxedEquivalentCSharp() => ClassFieldCSharp.BoxedMethodReuse(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class InheritedClassMethodBenchmarks : ClassFieldBenchmarkBase
{
    private Func<double, double> _direct = null!;
    private Func<double, double> _inherited = null!;

    [GlobalSetup]
    public void Setup()
    {
        _direct = LoadCompiled("classMethodInheritanceBase");
        _inherited = LoadCompiled("classMethodInherited");
    }

    [Benchmark(Baseline = true)]
    public double DirectBaseMethod() => _direct(N);

    [Benchmark]
    public double InheritedBaseMethod() => _inherited(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SuperClassMethodBenchmarks : ClassFieldBenchmarkBase
{
    private Func<double, double> _directOverride = null!;
    private Func<double, double> _super = null!;

    [GlobalSetup]
    public void Setup()
    {
        _directOverride = LoadCompiled("classMethodOverride");
        _super = LoadCompiled("classMethodSuper");
    }

    [Benchmark(Baseline = true)]
    public double DirectOverride() => _directOverride(N);

    [Benchmark]
    public double OverrideCallingSuper() => _super(N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ClassConstructionBenchmarks : ClassFieldBenchmarkBase
{
    [GlobalSetup]
    public void Setup() => Load("classConstruction");

    [Benchmark]
    public double SharpTS() => SharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() => ClassFieldCSharp.Construction(N);

    [Benchmark]
    public object? BoxedEquivalentCSharp() => ClassFieldCSharp.BoxedConstruction(N);
}
