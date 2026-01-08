using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Benchmarks.Baselines;
using SharpTS.Benchmarks.Infrastructure;

namespace SharpTS.Benchmarks.Benchmarks;

/// <summary>
/// Computational algorithm benchmarks comparing SharpTS-compiled TypeScript
/// against idiomatic C# and equivalent C# (dynamic types).
/// Tests function call overhead, loop performance, and array operations.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ComputationalBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _tsFibonacci = null!;
    private MethodInfo _tsFactorial = null!;
    private MethodInfo _tsCountPrimes = null!;

    [Params(10, 20, 30)]
    public int FibN { get; set; }

    [Params(20, 50, 100)]
    public int FactN { get; set; }

    [Params(1000, 10000, 100000)]
    public int PrimeN { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Load embedded TypeScript source
        var assembly = typeof(ComputationalBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Benchmarks.TypeScriptSources.Computational.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource Computational.ts");
        using var reader = new StreamReader(stream);
        var tsSource = reader.ReadToEnd();

        // Pre-compile TypeScript (cached across iterations)
        var dllPath = CompilationCache.GetOrCompile(tsSource, "Computational");

        // Load assembly into process
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "computational");

        // Cache method references (avoid reflection overhead in benchmark loop)
        _tsFibonacci = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "fibonacci");
        _tsFactorial = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "factorial");
        _tsCountPrimes = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "countPrimes");
    }

    // ==================== Fibonacci Benchmarks ====================

    [Benchmark]
    [BenchmarkCategory("Fibonacci")]
    public object? SharpTS_Fibonacci()
    {
        return BenchmarkHarness.InvokeCompiled(_tsFibonacci, (double)FibN);
    }

    [Benchmark]
    [BenchmarkCategory("Fibonacci")]
    public int Idiomatic_Fibonacci()
    {
        return IdiomaticCSharp.Fibonacci(FibN);
    }

    [Benchmark]
    [BenchmarkCategory("Fibonacci")]
    public object? Equivalent_Fibonacci()
    {
        return EquivalentCSharp.Fibonacci(FibN);
    }

    // ==================== Factorial Benchmarks ====================

    [Benchmark]
    [BenchmarkCategory("Factorial")]
    public object? SharpTS_Factorial()
    {
        return BenchmarkHarness.InvokeCompiled(_tsFactorial, (double)FactN);
    }

    [Benchmark]
    [BenchmarkCategory("Factorial")]
    public long Idiomatic_Factorial()
    {
        return IdiomaticCSharp.Factorial(FactN);
    }

    [Benchmark]
    [BenchmarkCategory("Factorial")]
    public object? Equivalent_Factorial()
    {
        return EquivalentCSharp.Factorial(FactN);
    }

    // ==================== Count Primes Benchmarks ====================

    [Benchmark]
    [BenchmarkCategory("CountPrimes")]
    public object? SharpTS_CountPrimes()
    {
        return BenchmarkHarness.InvokeCompiled(_tsCountPrimes, (double)PrimeN);
    }

    [Benchmark]
    [BenchmarkCategory("CountPrimes")]
    public int Idiomatic_CountPrimes()
    {
        return IdiomaticCSharp.CountPrimes(PrimeN);
    }

    [Benchmark]
    [BenchmarkCategory("CountPrimes")]
    public object? Equivalent_CountPrimes()
    {
        return EquivalentCSharp.CountPrimes(PrimeN);
    }
}
