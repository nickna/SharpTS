using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Object-literal allocation benchmarks. Each <c>{ ... }</c> in TS source
/// emits <c>new Dictionary&lt;string, object&gt;()</c> + N <c>set_Item</c>
/// calls + a no-op <c>CreateObject</c> helper call. Measures the per-iter
/// cost in tight loops — return values, options bags, tree builders.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ObjectLiteralsBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _tsSmall = null!;
    private MethodInfo _tsMedium = null!;
    private MethodInfo _tsNested = null!;

    [Params(100, 10_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(ObjectLiteralsBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ObjectLiterals.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource ObjectLiterals.ts");
        using var reader = new StreamReader(stream);
        var tsSource = reader.ReadToEnd();

        var dllPath = CompilationCache.GetOrCompile(tsSource, "ObjectLiterals");
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "objectliterals");

        _tsSmall = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "smallLiteralLoop");
        _tsMedium = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "mediumLiteralLoop");
        _tsNested = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "nestedLiteralLoop");
    }

    [Benchmark]
    [BenchmarkCategory("SmallLiteral")]
    public object? SharpTS_SmallLiteralLoop()
        => BenchmarkHarness.InvokeCompiled(_tsSmall, (double)N);

    [Benchmark]
    [BenchmarkCategory("MediumLiteral")]
    public object? SharpTS_MediumLiteralLoop()
        => BenchmarkHarness.InvokeCompiled(_tsMedium, (double)N);

    [Benchmark]
    [BenchmarkCategory("NestedLiteral")]
    public object? SharpTS_NestedLiteralLoop()
        => BenchmarkHarness.InvokeCompiled(_tsNested, (double)N);
}
