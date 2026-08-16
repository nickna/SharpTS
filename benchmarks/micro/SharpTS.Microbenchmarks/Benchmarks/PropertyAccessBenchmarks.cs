using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Property-access benchmarks. Measures cost of <c>obj.foo</c> lookup in
/// tight loops — pervasive in real code (AST traversal, options reads,
/// chained accessors). Each iteration touches the same property on a
/// reused object, so monomorphic call-site assumptions hold.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PropertyAccessBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _tsSingle = null!;
    private MethodInfo _tsChain = null!;
    private MethodInfo _tsMethodCall = null!;
    private MethodInfo _tsClassProp = null!;
    private MethodInfo _tsPropWrite = null!;

    [Params(100, 10_000, 1_000_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(PropertyAccessBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.PropertyAccess.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource PropertyAccess.ts");
        using var reader = new StreamReader(stream);
        var tsSource = reader.ReadToEnd();

        var dllPath = CompilationCache.GetOrCompile(tsSource, "PropertyAccess");
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "propertyaccess");

        _tsSingle = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "singlePropLoop");
        _tsChain = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "chainPropLoop");
        _tsMethodCall = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "methodCallLoop");
        _tsClassProp = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "classPropLoop");
        _tsPropWrite = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "singlePropWriteLoop");
    }

    [Benchmark]
    [BenchmarkCategory("SinglePropLoop")]
    public object? SharpTS_SinglePropLoop()
        => BenchmarkHarness.InvokeCompiled(_tsSingle, (double)N);

    [Benchmark]
    [BenchmarkCategory("ChainPropLoop")]
    public object? SharpTS_ChainPropLoop()
        => BenchmarkHarness.InvokeCompiled(_tsChain, (double)N);

    [Benchmark]
    [BenchmarkCategory("MethodCallLoop")]
    public object? SharpTS_MethodCallLoop()
        => BenchmarkHarness.InvokeCompiled(_tsMethodCall, (double)N);

    [Benchmark]
    [BenchmarkCategory("ClassPropLoop")]
    public object? SharpTS_ClassPropLoop()
        => BenchmarkHarness.InvokeCompiled(_tsClassProp, (double)N);

    [Benchmark]
    [BenchmarkCategory("SinglePropWriteLoop")]
    public object? SharpTS_SinglePropWriteLoop()
        => BenchmarkHarness.InvokeCompiled(_tsPropWrite, (double)N);
}
