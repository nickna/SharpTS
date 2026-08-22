using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;

namespace SharpTS.Microbenchmarks.Benchmarks;

public abstract class AsyncPromiseBenchmarkBase : ComputationalBenchmarkBase
{
    protected static Func<object?, Task<object?>> LoadCompiledAsync(string functionName)
    {
        var tsSource = LoadTypeScriptSource();
        var dllPath = Infrastructure.CompilationCache.GetOrCompile(tsSource, "Algorithms");
        var assembly = Infrastructure.BenchmarkHarness.LoadCompiledAssembly(dllPath, "algorithms");
        return Infrastructure.BenchmarkHarness.GetCompiledAsyncNumberFunc(assembly, functionName);
    }
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AsyncSequentialAwaitBenchmarks : AsyncPromiseBenchmarkBase
{
    private Func<object?, Task<object?>> _compiled = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _compiled = LoadCompiledAsync("asyncSequentialAwait");

    [Benchmark]
    public Task<object?> SharpTS() => _compiled((double)N);

    [Benchmark]
    public Task<double> Idiomatic() => AsyncPromiseCSharp.IdiomaticSequentialAwait(N);

    [Benchmark]
    public Task<object?> Equivalent() => AsyncPromiseCSharp.EquivalentSequentialAwait((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AsyncFunctionCallsBenchmarks : AsyncPromiseBenchmarkBase
{
    private Func<object?, Task<object?>> _compiled = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _compiled = LoadCompiledAsync("asyncFunctionCalls");

    [Benchmark]
    public Task<object?> SharpTS() => _compiled((double)N);

    [Benchmark]
    public Task<double> Idiomatic() => AsyncPromiseCSharp.IdiomaticFunctionCalls(N);

    [Benchmark]
    public Task<object?> Equivalent() => AsyncPromiseCSharp.EquivalentFunctionCalls((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PromiseThenChainBenchmarks : AsyncPromiseBenchmarkBase
{
    private Func<object?, Task<object?>> _compiled = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _compiled = LoadCompiledAsync("promiseThenChain");

    [Benchmark]
    public Task<object?> SharpTS() => _compiled((double)N);

    [Benchmark]
    public Task<double> Idiomatic() => AsyncPromiseCSharp.IdiomaticThenChain(N);

    [Benchmark]
    public Task<object?> Equivalent() => AsyncPromiseCSharp.EquivalentThenChain((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PromiseAllBenchmarks : AsyncPromiseBenchmarkBase
{
    private Func<object?, Task<object?>> _compiled = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _compiled = LoadCompiledAsync("promiseAllFanOut");

    [Benchmark]
    public Task<object?> SharpTS() => _compiled((double)N);

    [Benchmark]
    public Task<double> Idiomatic() => AsyncPromiseCSharp.IdiomaticAll(N);

    [Benchmark]
    public Task<object?> Equivalent() => AsyncPromiseCSharp.EquivalentAll((double)N);
}
