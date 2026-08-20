using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Execution;
using SharpTS.Microbenchmarks.Infrastructure;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Faithful compiled counterpart to the cross-runtime sort workload. Numeric
/// and record arrays use the same seeded inputs and callback shapes; cumulative
/// slice-only probes isolate copying from comparator/sort/write-back costs.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ArraySortBenchmarks
{
    private Func<List<object>, double> _copyNumbers = null!;
    private Func<List<object>, double> _copyRecords = null!;
    private Func<List<object>, double> _sortNumbers = null!;
    private Func<List<object>, double> _sortRecords = null!;
    private Func<List<object>, List<object>, double> _sortCombined = null!;
    private List<object> _numbers = null!;
    private List<object> _records = null!;

    [Params(1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(ArraySortBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ArraySort.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource ArraySort.ts");
        using var reader = new StreamReader(stream);

        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "ArraySort");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "arraysort");
        _copyNumbers = GetUnary(compiled, "copyNumbers");
        _copyRecords = GetUnary(compiled, "copyRecords");
        _sortNumbers = GetUnary(compiled, "sortNumbers");
        _sortRecords = GetUnary(compiled, "sortRecords");
        _sortCombined = BenchmarkHarness.GetCompiledMethod(compiled, "sortCombined")
            .CreateDelegate<Func<List<object>, List<object>, double>>();

        MethodInfo makeNumbers = BenchmarkHarness.GetCompiledMethod(
            compiled, "makeNumbers");
        MethodInfo makeRecords = BenchmarkHarness.GetCompiledMethod(
            compiled, "makeRecords");
        _numbers = (List<object>?)BenchmarkHarness.InvokeCompiled(
            makeNumbers, (double)N)
            ?? throw new InvalidOperationException(
                "makeNumbers did not return an array backing list");
        _records = (List<object>?)BenchmarkHarness.InvokeCompiled(
            makeRecords, (double)N)
            ?? throw new InvalidOperationException(
                "makeRecords did not return an array backing list");
    }

    [Benchmark(Baseline = true)]
    public double SliceNumbers() => _copyNumbers(_numbers);

    [Benchmark]
    public double SliceRecords() => _copyRecords(_records);

    [Benchmark]
    public double SortNumbers() => _sortNumbers(_numbers);

    [Benchmark]
    public double SortRecords() => _sortRecords(_records);

    [Benchmark]
    public double CombinedAcceptanceShape() => _sortCombined(_numbers, _records);

    private static Func<List<object>, double> GetUnary(
        Assembly assembly, string name)
        => BenchmarkHarness.GetCompiledMethod(assembly, name)
            .CreateDelegate<Func<List<object>, double>>();
}

/// <summary>
/// Interpreter counterpart to <see cref="ArraySortBenchmarks"/>. Parsing,
/// checking, declaration setup, and seeded input generation are outside the
/// measured operations; callbacks enter through the interpreter's callable
/// interface without REPL parsing overhead.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ArraySortInterpreterBenchmarks : IDisposable
{
    private Interpreter _interpreter = null!;
    private ISharpTSCallable _copyNumbers = null!;
    private ISharpTSCallable _copyRecords = null!;
    private ISharpTSCallable _sortNumbers = null!;
    private ISharpTSCallable _sortRecords = null!;
    private ISharpTSCallable _sortCombined = null!;
    private RuntimeValue _numbers;
    private RuntimeValue _records;
    private readonly RuntimeValue[] _unaryArgs = new RuntimeValue[1];
    private readonly RuntimeValue[] _binaryArgs = new RuntimeValue[2];

    [Params(1_000, 10_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string source = LoadSource();
        List<Stmt> declarations = Parse(source);
        TypeMap typeMap = new TypeChecker().Check(declarations);
        _interpreter = new Interpreter(
            stdout: TextWriter.Null,
            stderr: TextWriter.Null);
        _interpreter.Interpret(declarations, typeMap);

        RuntimeEnvironment environment = GetEnvironment(_interpreter);
        _copyNumbers = GetCallable(environment, "copyNumbers");
        _copyRecords = GetCallable(environment, "copyRecords");
        _sortNumbers = GetCallable(environment, "sortNumbers");
        _sortRecords = GetCallable(environment, "sortRecords");
        _sortCombined = GetCallable(environment, "sortCombined");

        RuntimeValue[] size = [RuntimeValue.FromNumber(N)];
        _numbers = GetCallable(environment, "makeNumbers")
            .CallV2(_interpreter, size);
        _records = GetCallable(environment, "makeRecords")
            .CallV2(_interpreter, size);
    }

    [Benchmark(Baseline = true)]
    public double SliceNumbers() => InvokeUnary(_copyNumbers, _numbers);

    [Benchmark]
    public double SliceRecords() => InvokeUnary(_copyRecords, _records);

    [Benchmark]
    public double SortNumbers() => InvokeUnary(_sortNumbers, _numbers);

    [Benchmark]
    public double SortRecords() => InvokeUnary(_sortRecords, _records);

    [Benchmark]
    public double CombinedAcceptanceShape()
    {
        _binaryArgs[0] = _numbers;
        _binaryArgs[1] = _records;
        return _sortCombined.CallV2(_interpreter, _binaryArgs).AsNumber();
    }

    [GlobalCleanup]
    public void Dispose() => _interpreter?.Dispose();

    private double InvokeUnary(ISharpTSCallable callable, RuntimeValue argument)
    {
        _unaryArgs[0] = argument;
        return callable.CallV2(_interpreter, _unaryArgs).AsNumber();
    }

    private static string LoadSource()
    {
        Assembly assembly = typeof(ArraySortInterpreterBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ArraySort.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource ArraySort.ts");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();

    private static RuntimeEnvironment GetEnvironment(Interpreter interpreter)
        => (RuntimeEnvironment?)(typeof(Interpreter).GetProperty(
                "Environment", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(interpreter))
            ?? throw new InvalidOperationException(
                "The interpreter did not create a runtime environment");

    private static ISharpTSCallable GetCallable(
        RuntimeEnvironment environment, string name)
        => environment.TryGet(name, out RuntimeValue value)
            ? value.ToObject() as ISharpTSCallable
                ?? throw new InvalidOperationException(
                    $"Interpreter binding '{name}' is not callable")
            : throw new InvalidOperationException(
                $"Interpreter benchmark function '{name}' was not found");
}
