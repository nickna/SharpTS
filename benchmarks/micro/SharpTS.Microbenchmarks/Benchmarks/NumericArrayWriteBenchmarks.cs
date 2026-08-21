using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Faithful in-process counterpart to cross-runtime <c>num-write</c>: an
/// escaping <c>number[]</c> is grown by sequential index assignment through a
/// helper, then read for a checksum.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class NumericArrayWriteBenchmarks
{
    private Func<double, double> _sharpTs = null!;

    [Params(1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(NumericArrayWriteBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.NumericArrayWrite.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource NumericArrayWrite.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "NumericArrayWrite");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "numeric-array-write");
        BenchmarkHarness.InitializeCompiledModules(compiled);
        _sharpTs = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "numericArrayWrite");
    }

    [Benchmark]
    public double SharpTS() => _sharpTs(N);

    [Benchmark(Baseline = true)]
    public double IdiomaticCSharp() => NumericArrayWriteBaselines.Idiomatic(N);

    [Benchmark]
    public double BoxedEquivalentCSharp() =>
        NumericArrayWriteBaselines.BoxedEquivalent(N);
}
