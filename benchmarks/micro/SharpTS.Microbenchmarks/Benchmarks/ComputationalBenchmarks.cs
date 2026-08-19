using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

// Computational algorithm benchmarks: SharpTS-compiled TypeScript vs idiomatic
// C# (native types — the performance ceiling) vs "equivalent" C# (object?/boxing
// — an approximation of the dynamic-typing tax).
//
// One class per algorithm so each carries a single [Params] axis. (A single
// class with three independent [Params] would run BenchmarkDotNet's full
// Cartesian product — every method across all 27 combinations — even though
// each method reads only one param, ~9x of which is wasted/duplicated work.)
//
// The TypeScript bodies come from benchmarks/cross-runtime/scripts/lib/algorithms.ts — the
// same file the cross-runtime shell harness runs — so both systems measure
// identical code. The functions are reached through a cached
// Func<double,double> delegate, keeping reflection out of the timed region.

/// <summary>Shared embedded-resource loading + compiled-delegate resolution.</summary>
public abstract class ComputationalBenchmarkBase
{
    private const string ResourceName = "SharpTS.Microbenchmarks.algorithms.ts";

    protected static string LoadTypeScriptSource()
    {
        var assembly = typeof(ComputationalBenchmarkBase).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Could not find embedded resource {ResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    protected static Func<double, double> LoadCompiled(string functionName)
    {
        var tsSource = LoadTypeScriptSource();

        // Compiled once and cached across all three algorithm classes.
        var dllPath = CompilationCache.GetOrCompile(tsSource, "Algorithms");
        var tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "algorithms");
        return BenchmarkHarness.GetCompiledNumberFunc(tsAssembly, functionName);
    }
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FibonacciBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _fibonacci = null!;

    [Params(10, 20, 30)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _fibonacci = LoadCompiled("fibonacci");

    [Benchmark]
    public double SharpTS() => _fibonacci(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.Fibonacci(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.Fibonacci((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FactorialBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _factorial = null!;

    [Params(20, 50, 100)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _factorial = LoadCompiled("factorial");

    [Benchmark]
    public double SharpTS() => _factorial(N);

    [Benchmark]
    public long Idiomatic() => IdiomaticCSharp.Factorial(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.Factorial((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class CountPrimesBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _countPrimes = null!;

    [Params(1000, 10000, 100000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _countPrimes = LoadCompiled("countPrimes");

    [Benchmark]
    public double SharpTS() => _countPrimes(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.CountPrimes(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.CountPrimes((double)N);
}
