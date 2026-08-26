using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Object-literal allocation benchmarks. Measures fixed records, nested/dynamic
/// objects, stable exact spread, overwrite order, and an escaping spread control
/// in tight loops such as options-bag and tree-building workloads.
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
    private MethodInfo _tsSpreadOne = null!;
    private MethodInfo _tsSpreadMultiple = null!;
    private MethodInfo _tsSpreadEscape = null!;
    private MethodInfo _tsObjectKeysExact = null!;
    private MethodInfo _tsObjectKeysMutation = null!;

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
        _tsSpreadOne = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "spreadOneSourceLoop");
        _tsSpreadMultiple = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "spreadMultipleOverwriteLoop");
        _tsSpreadEscape = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "spreadMutationEscapeLoop");
        _tsObjectKeysExact = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "objectKeysExactLoop");
        _tsObjectKeysMutation = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "objectKeysMutationLoop");
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

    [Benchmark]
    [BenchmarkCategory("SpreadOneSource")]
    public object? SharpTS_SpreadOneSourceLoop()
        => BenchmarkHarness.InvokeCompiled(_tsSpreadOne, (double)N);

    [Benchmark]
    [BenchmarkCategory("SpreadMultipleOverwrite")]
    public object? SharpTS_SpreadMultipleOverwriteLoop()
        => BenchmarkHarness.InvokeCompiled(_tsSpreadMultiple, (double)N);

    [Benchmark]
    [BenchmarkCategory("SpreadMutationEscape")]
    public object? SharpTS_SpreadMutationEscapeLoop()
        => BenchmarkHarness.InvokeCompiled(_tsSpreadEscape, (double)N);

    [Benchmark]
    [BenchmarkCategory("ObjectKeysExact")]
    public object? SharpTS_ObjectKeysExactLoop()
        => BenchmarkHarness.InvokeCompiled(_tsObjectKeysExact, (double)N);

    [Benchmark]
    [BenchmarkCategory("ObjectKeysMutation")]
    public object? SharpTS_ObjectKeysMutationLoop()
        => BenchmarkHarness.InvokeCompiled(_tsObjectKeysMutation, (double)N);
}
