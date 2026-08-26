using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Sparse guest-throw timing and allocation comparisons. The branch-only and
/// no-throw protected-region controls separate loop/try overhead from the
/// primitive wrapper and JavaScript Error allocations.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ExceptionThrowBenchmarks
{
    private Func<double, double> _branchControl = null!;
    private Func<double, double> _tryCatchNoThrow = null!;
    private Func<double, double> _primitiveThrow = null!;
    private Func<double, double> _errorThrow = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(ExceptionThrowBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ExceptionThrows.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource ExceptionThrows.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "ExceptionThrows");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "exception-throws");
        _branchControl = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "throwBranchControl");
        _tryCatchNoThrow = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "throwTryCatchNoThrow");
        _primitiveThrow = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "throwPrimitiveSparse");
        _errorThrow = BenchmarkHarness.GetCompiledNumberFunc(
            compiled, "throwErrorSparse");
    }

    [Benchmark(Baseline = true)]
    public double BranchOnlyControl() => _branchControl(N);

    [Benchmark]
    public double TryCatchNoThrow() => _tryCatchNoThrow(N);

    [Benchmark]
    public double PrimitiveThrow() => _primitiveThrow(N);

    [Benchmark]
    public double ErrorThrow() => _errorThrow(N);
}
