using System.Reflection;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Guest-throw timing and allocation comparisons. Frequency is explicit, and
/// local control-flow throws are separated from callee and finally paths that
/// require CLR exception unwinding in compiled output.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ExceptionThrowBenchmarks
{
    private Func<double, double, double> _branchControl = null!;
    private Func<double, double, double> _tryCatchNoThrow = null!;
    private Func<double, double, double> _primitiveLocal = null!;
    private Func<double, double, double> _calleeNoThrow = null!;
    private Func<double, double, double> _primitiveCallee = null!;
    private Func<double, double, double> _finallyNoThrow = null!;
    private Func<double, double, double> _primitiveFinally = null!;
    private Func<double, double, double> _errorThrow = null!;
    private Func<double, double, double> _errorConstruction = null!;
    private Func<double, double, double> _firstStackRead = null!;
    private Func<double, double, double> _repeatedStackRead = null!;

    [Params(100_000)]
    public int N { get; set; }

    [Params(16, 1024)]
    public int ThrowEvery { get; set; }

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
        _branchControl = Get(compiled, "throwBranchControl");
        _tryCatchNoThrow = Get(compiled, "throwTryCatchNoThrow");
        _primitiveLocal = Get(compiled, "throwPrimitiveLocal");
        _calleeNoThrow = Get(compiled, "throwCalleeNoThrow");
        _primitiveCallee = Get(compiled, "throwPrimitiveCallee");
        _finallyNoThrow = Get(compiled, "throwFinallyNoThrow");
        _primitiveFinally = Get(compiled, "throwPrimitiveThroughFinally");
        _errorThrow = Get(compiled, "throwErrorSparse");
        _errorConstruction = Get(compiled, "constructErrorSparse");
        _firstStackRead = Get(compiled, "firstErrorStackRead");
        _repeatedStackRead = Get(compiled, "repeatedErrorStackRead");
    }

    private static Func<double, double, double> Get(Assembly assembly, string name) =>
        BenchmarkHarness.GetCompiledNumber2Func(assembly, name);

    [Benchmark(Baseline = true)]
    public double CompiledBranchOnlyControl() => _branchControl(N, ThrowEvery);

    [Benchmark]
    public double CompiledTryCatchNoThrow() => _tryCatchNoThrow(N, ThrowEvery);

    [Benchmark]
    public double CompiledPrimitiveLocal() => _primitiveLocal(N, ThrowEvery);

    [Benchmark]
    public double CompiledCalleeNoThrow() => _calleeNoThrow(N, ThrowEvery);

    [Benchmark]
    public double CompiledPrimitiveCallee() => _primitiveCallee(N, ThrowEvery);

    [Benchmark]
    public double CompiledFinallyNoThrow() => _finallyNoThrow(N, ThrowEvery);

    [Benchmark]
    public double CompiledPrimitiveThroughFinally() => _primitiveFinally(N, ThrowEvery);

    [Benchmark]
    public double CompiledErrorThrow() => _errorThrow(N, ThrowEvery);

    [Benchmark]
    public double CompiledErrorConstructionNoStackRead() => _errorConstruction(N, ThrowEvery);

    [Benchmark]
    public double CompiledErrorFirstStackRead() => _firstStackRead(N, ThrowEvery);

    [Benchmark]
    public double CompiledErrorRepeatedStackRead() => _repeatedStackRead(N, ThrowEvery);

    [Benchmark]
    public double CSharpBranchOnlyControl()
    {
        double sum = 0;
        for (int i = 0; i < N; i++)
            sum += ShouldThrow(i) ? i : 1;
        return sum;
    }

    [Benchmark]
    public double CSharpBoxedPrimitiveEquivalent()
    {
        double sum = 0;
        for (int i = 0; i < N; i++)
        {
            if (ShouldThrow(i))
            {
                object caught = (double)i;
                sum += ReadCaughtPrimitive(caught, i);
            }
            else
            {
                sum += 1;
            }
        }
        return sum;
    }

    [Benchmark]
    public double CSharpClrExceptionUnwind()
    {
        double sum = 0;
        for (int i = 0; i < N; i++)
        {
            try
            {
                if (ShouldThrow(i)) ThrowPrimitive(i);
                sum += 1;
            }
            catch (PrimitiveBenchmarkException error)
            {
                sum += error.Value == i ? error.Value : -1;
            }
        }
        return sum;
    }

    private bool ShouldThrow(int i) => (i & (ThrowEvery - 1)) == 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double ReadCaughtPrimitive(object caught, double expected) =>
        caught is double value && value == expected ? value : -1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowPrimitive(double value) =>
        throw new PrimitiveBenchmarkException(value);

    private sealed class PrimitiveBenchmarkException(double value) : Exception
    {
        public double Value { get; } = value;
    }
}
