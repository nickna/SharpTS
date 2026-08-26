using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Execution;
using SharpTS.Microbenchmarks.Baselines;
using SharpTS.Microbenchmarks.Infrastructure;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Benchmarks;

// Starter-set workloads that broaden coverage beyond the original arithmetic
// kernels: a builtin-heavy JSON round-trip, a data-parallel typed-array kernel,
// and allocation/GC-heavy binary-trees. The TypeScript bodies live in
// benchmarks/cross-runtime/scripts/lib/algorithms.ts (shared byte-identical with the
// cross-runtime shell harness) and are reached through a cached
// Func<double,double> delegate via ComputationalBenchmarkBase.LoadCompiled.
//
// As elsewhere: SharpTS-compiled vs idiomatic C# (native types — the ceiling)
// vs "equivalent" C# (object?/boxing — the dynamic-typing tax).

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonRoundTripBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _jsonRoundTrip = null!;

    [Params(100, 1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _jsonRoundTrip = LoadCompiled("jsonRoundTrip");

    [Benchmark]
    public double SharpTS() => _jsonRoundTrip(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.JsonRoundTrip(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.JsonRoundTrip((double)N);
}

/// <summary>
/// Cumulative JSON phase probes.  Subtract adjacent timings/allocations to
/// isolate build, stringify, parse, and post-parse traversal costs without
/// introducing cross-benchmark state or changing the guarded round trip.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonPhaseBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _build = null!;
    private Func<double, double> _stringify = null!;
    private Func<double, double> _parse = null!;
    private Func<double, double> _roundTrip = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _build = LoadCompiled("jsonBuildPhase");
        _stringify = LoadCompiled("jsonStringifyPhase");
        _parse = LoadCompiled("jsonParsePhase");
        _roundTrip = LoadCompiled("jsonRoundTrip");
    }

    [Benchmark(Baseline = true)]
    public double Build() => _build(N);

    [Benchmark]
    public double BuildAndStringify() => _stringify(N);

    [Benchmark]
    public double BuildStringifyAndParse() => _parse(N);

    [Benchmark]
    public double FullRoundTrip() => _roundTrip(N);
}

/// <summary>
/// Cumulative phase probes through the faithful imported-module and capturing
/// callback path used by cross-runtime json.ts. BenchmarkDotNet owns the outer
/// timing loop; each delegate performs exactly one callback invocation.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonImportedModulePhaseBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _build = null!;
    private Func<double, double> _stringify = null!;
    private Func<double, double> _parse = null!;
    private Func<double, double> _roundTrip = null!;

    [Params(1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _build = LoadImportedJsonCompiled("importedJsonBuildPhase");
        _stringify = LoadImportedJsonCompiled("importedJsonStringifyPhase");
        _parse = LoadImportedJsonCompiled("importedJsonParsePhase");
        _roundTrip = LoadImportedJsonCompiled("importedJsonRoundTrip");
    }

    [Benchmark(Baseline = true)]
    public double Build() => _build(N);

    [Benchmark]
    public double BuildAndStringify() => _stringify(N);

    [Benchmark]
    public double BuildStringifyAndParse() => _parse(N);

    [Benchmark]
    public double FullRoundTrip() => _roundTrip(N);
}

/// <summary>
/// Hard-gate sizes for the exact imported-module round trip. MemoryDiagnoser
/// reports allocated bytes and Gen0/Gen1/Gen2 collections for both sizes.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonImportedModuleRoundTripBenchmarks
    : ComputationalBenchmarkBase
{
    private Func<double, double> _roundTrip = null!;

    [Params(1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() =>
        _roundTrip = LoadImportedJsonCompiled("importedJsonRoundTrip");

    [Benchmark]
    public double SharpTS() => _roundTrip(N);
}

/// <summary>
/// Interpreter counterpart to <see cref="JsonPhaseBenchmarks"/>. Parsing,
/// type-checking, declaration binding, and realm construction happen in setup;
/// the measured methods execute only the same cumulative phase functions used
/// by the compiled and cross-runtime benchmarks.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonInterpreterPhaseBenchmarks
    : ComputationalBenchmarkBase, IDisposable
{
    private Interpreter _interpreter = null!;
    private TypeMap _typeMap = null!;
    private List<Stmt> _build = null!;
    private List<Stmt> _stringify = null!;
    private List<Stmt> _parse = null!;
    private List<Stmt> _roundTrip = null!;

    [Params(1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var source = LoadTypeScriptSource();
        var statements = Parse(source);
        _typeMap = new TypeChecker().Check(statements);
        _interpreter = new Interpreter(
            stdout: TextWriter.Null,
            stderr: TextWriter.Null);
        _interpreter.Interpret(statements, _typeMap);

        _build = Parse($"jsonBuildPhase({N});");
        _stringify = Parse($"jsonStringifyPhase({N});");
        _parse = Parse($"jsonParsePhase({N});");
        _roundTrip = Parse($"jsonRoundTrip({N});");
    }

    [Benchmark(Baseline = true)]
    public object? Build() => _interpreter.InterpretRepl(_build, _typeMap);

    [Benchmark]
    public object? BuildAndStringify() =>
        _interpreter.InterpretRepl(_stringify, _typeMap);

    [Benchmark]
    public object? BuildStringifyAndParse() =>
        _interpreter.InterpretRepl(_parse, _typeMap);

    [Benchmark]
    public object? FullRoundTrip() =>
        _interpreter.InterpretRepl(_roundTrip, _typeMap);

    [GlobalCleanup]
    public void Dispose() => _interpreter?.Dispose();

    private static List<Stmt> Parse(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        return new Parser(tokens).ParseOrThrow();
    }
}

/// <summary>
/// Interpreter counterpart to <see cref="JsonImportedModulePhaseBenchmarks"/>.
/// Module resolution, type checking, realm creation, and module execution occur
/// in setup; measured calls retain the imported live binding and capturing arrow
/// callback used by the exact cross-runtime workload.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JsonImportedModuleInterpreterPhaseBenchmarks : IDisposable
{
    private InterpretedJsonModuleBenchmark _benchmark = null!;

    [Params(1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _benchmark = InterpretedJsonModuleBenchmark.Create();

    [Benchmark(Baseline = true)]
    public double Build() => _benchmark.Build(N);

    [Benchmark]
    public double BuildAndStringify() => _benchmark.Stringify(N);

    [Benchmark]
    public double BuildStringifyAndParse() => _benchmark.Parse(N);

    [Benchmark]
    public double FullRoundTrip() => _benchmark.RoundTrip(N);

    [GlobalCleanup]
    public void Dispose() => _benchmark?.Dispose();
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TypedArrayBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _typedArrayKernel = null!;

    [Params(1000, 100000, 1000000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _typedArrayKernel = LoadCompiled("typedArrayKernel");

    [Benchmark]
    public double SharpTS() => _typedArrayKernel(N);

    [Benchmark]
    public double Idiomatic() => IdiomaticCSharp.TypedArrayKernel(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.TypedArrayKernel((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class Int32TypedArrayBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _int32Kernel = null!;

    [Params(1000, 100000, 1000000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _int32Kernel = LoadCompiled("int32Kernel");

    [Benchmark]
    public double SharpTS() => _int32Kernel(N);

    [Benchmark]
    public double Idiomatic() => IdiomaticCSharp.Int32Kernel(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.Int32Kernel((double)N);
}

[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BinaryTreesBenchmarks : ComputationalBenchmarkBase
{
    private Func<double, double> _binaryTrees = null!;

    [Params(8, 12, 16)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup() => _binaryTrees = LoadCompiled("binaryTrees");

    [Benchmark]
    public double SharpTS() => _binaryTrees(N);

    [Benchmark]
    public int Idiomatic() => IdiomaticCSharp.BinaryTrees(N);

    [Benchmark]
    public object? Equivalent() => EquivalentCSharp.BinaryTrees((double)N);
}
