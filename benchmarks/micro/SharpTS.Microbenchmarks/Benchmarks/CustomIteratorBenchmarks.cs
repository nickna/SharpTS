using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
public class CustomIteratorBenchmarks
{
    private Func<double, double> _dynamic = null!;
    private Func<double, double> _stable = null!;

    [Params(1_000, 10_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dynamic = CustomIteratorModuleBenchmark.Bind(true);
        _stable = CustomIteratorModuleBenchmark.Bind(false);
        double expected = (double)N * (N - 1) / 2;
        if (_dynamic(N) != expected || _stable(N) != expected)
            throw new InvalidOperationException("Custom iterator checksum mismatch");
    }

    [Benchmark]
    public double DynamicProtocol() => _dynamic(N);

    [Benchmark(Baseline = true)]
    public double StableProtocol() => _stable(N);
}

/// <summary>
/// Attribution only: invokes the original emitted next body without protocol
/// dispatch or result-property reads. Not equivalent to the full for-of workload.
/// </summary>
[MemoryDiagnoser]
public class CustomIteratorNextBodyBenchmarks
{
    private Func<object, object> _next = null!;
    private Action _reset = null!;
    private readonly object _receiver = new();

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = CustomIteratorModuleBenchmark.Load(true);
        Type carrierType = assembly.GetTypes().Single(type =>
            type.Name.Contains("mutatedCustomIterator", StringComparison.Ordinal) &&
            type.GetField("current") is not null);
        Type nextType = assembly.GetTypes().Single(type =>
            type.GetField("$functionDC")?.FieldType == carrierType);
        object carrier = Activator.CreateInstance(carrierType)!;
        object target = Activator.CreateInstance(nextType)!;
        nextType.GetField("$functionDC")!.SetValue(target, carrier);
        carrierType.GetField("n")!.SetValue(carrier, (double)N);
        var receiver = Expression.Parameter(typeof(object));
        var call = Expression.Call(Expression.Constant(target), nextType.GetMethod("Invoke")!, receiver);
        _next = Expression.Lambda<Func<object, object>>(
            Expression.Convert(call, typeof(object)), receiver).Compile();
        FieldInfo current = carrierType.GetField("current")!;
        _reset = Expression.Lambda<Action>(Expression.Block(
            Expression.Assign(Expression.Field(Expression.Constant(carrier), current),
                Expression.Convert(Expression.Constant(0d), current.FieldType)),
            Expression.Empty())).Compile();
        object result = NextBody();
        var getProperty = assembly.GetType("$Runtime")!.GetMethod("GetProperty")!;
        if (!Equals(getProperty.Invoke(null, [result, "value"]), (double)N - 1) ||
            !Equals(getProperty.Invoke(null, [result, "done"]), false))
            throw new InvalidOperationException("Custom iterator next-body checksum mismatch");
    }

    [Benchmark]
    public object NextBody()
    {
        _reset();
        object result = null!;
        for (int i = 0; i < N; i++) result = _next(_receiver);
        return result;
    }
}

[MemoryDiagnoser]
public class CustomIteratorCallBenchmarks
{
    private Func<double, double> _call = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const string source = """
            function callLoop(n: number): number {
                const receiver: any = { method() { return 1; } };
                const alias: any = receiver;
                alias.method = alias.method;
                let sum = 0;
                for (let i = 0; i < n; i++) sum += receiver.method();
                return sum;
            }
            """;
        string path = CompilationCache.GetOrCompile(source, "CustomIteratorMinimalCall");
        _call = BenchmarkHarness.GetCompiledNumberFunc(
            BenchmarkHarness.LoadCompiledAssembly(path, "CustomIteratorMinimalCall"), "callLoop");
        if (_call(N) != N) throw new InvalidOperationException("Call checksum mismatch");
    }

    [Benchmark]
    public double GenericZeroArgumentCall() => _call(N);
}

/// <summary>Interpreter allocation attribution using the original function ASTs.</summary>
[MemoryDiagnoser]
public class CustomIteratorInterpreterBenchmarks : IDisposable
{
    private Interpreter _interpreter = null!;
    private TypeMap _types = null!;
    private List<Stmt> _dynamic = null!;
    private List<Stmt> _stable = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Interpret declarations only, excluding the imported timing driver.
        // AST nodes retain the exact original workload bodies.
        var declarations = new[] { "dynamic", "stable" }.SelectMany(name =>
            Parse(CustomIteratorModuleBenchmark.Read(name)).OfType<Stmt.Function>())
            .Cast<Stmt>().ToList();
        _types = new TypeChecker().Check(declarations);
        _interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        _interpreter.Interpret(declarations, _types);
        _dynamic = Parse($"mutatedCustomIterator({N});");
        _stable = Parse($"stableCustomIterator({N});");
        double expected = (double)N * (N - 1) / 2;
        if (!Equals(DynamicProtocol(), expected) || !Equals(StableProtocol(), expected))
            throw new InvalidOperationException("Interpreter iterator checksum mismatch");
    }

    [Benchmark]
    public object? DynamicProtocol() => _interpreter.InterpretRepl(_dynamic, _types);

    [Benchmark(Baseline = true)]
    public object? StableProtocol() => _interpreter.InterpretRepl(_stable, _types);

    [GlobalCleanup]
    public void Dispose() => _interpreter?.Dispose();

    private static List<Stmt> Parse(string source) =>
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
}
