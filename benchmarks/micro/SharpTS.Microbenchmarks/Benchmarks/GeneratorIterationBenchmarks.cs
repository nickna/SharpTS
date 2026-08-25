using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Numeric generator producer/consumer benchmark. The public-next control
/// isolates the standard iterator-result ABI from the direct stable-binding
/// for...of bridge.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class GeneratorIterationBenchmarks
{
    private Func<double, double> _sharpTsDirect = null!;
    private Func<double, double> _sharpTsPublicNext = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(GeneratorIterationBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.GeneratorIteration.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource GeneratorIteration.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "GeneratorIteration");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "generator-iteration");
        BenchmarkHarness.InitializeCompiledModules(compiled);
        _sharpTsDirect = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "generatorRangeIteration");
        _sharpTsPublicNext = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "generatorRangeManualNext");
    }

    [Benchmark]
    public double SharpTSDirectForOf() => _sharpTsDirect(N);

    [Benchmark]
    public double SharpTSPublicNext() => _sharpTsPublicNext(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() => GeneratorIterationBaselines.Idiomatic(N);

    [Benchmark]
    public double BoxedEquivalentCSharp() =>
        GeneratorIterationBaselines.BoxedEquivalent(N);
}
