using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Execution;
using SharpTS.Microbenchmarks.Infrastructure;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Proves the #1387 intrinsic global-match path does not allocate the detailed
/// exec result objects that <c>String.matchAll</c> must expose. Both workloads
/// scan the same input and matches; only the observable result shape differs.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class RegexGlobalMatchAllocationBenchmarks
{
    private Assembly _tsAssembly = null!;
    private MethodInfo _globalMatch = null!;
    private MethodInfo _detailedMatchAll = null!;

    [Params(100, 10_000)]
    public int N { get; set; }

    private const string Input = "alpha beta gamma delta epsilon";

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(RegexGlobalMatchAllocationBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.Regex.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource Regex.ts");
        using var reader = new StreamReader(stream);
        var dllPath = CompilationCache.GetOrCompile(reader.ReadToEnd(), "RegexGlobalMatchAllocation");
        _tsAssembly = BenchmarkHarness.LoadCompiledAssembly(dllPath, "regex-global-match-allocation");
        _globalMatch = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexGlobalMatchLoop");
        _detailedMatchAll = BenchmarkHarness.GetCompiledMethod(_tsAssembly, "regexDetailedMatchAllLoop");
    }

    [Benchmark(Baseline = true)]
    public object? IntrinsicGlobalStringMatch()
        => BenchmarkHarness.InvokeCompiled(_globalMatch, Input, (double)N);

    [Benchmark]
    public object? DetailedStringMatchAll()
        => BenchmarkHarness.InvokeCompiled(_detailedMatchAll, Input, (double)N);
}

/// <summary>
/// Interpreter counterpart to <see cref="RegexGlobalMatchAllocationBenchmarks"/>.
/// Parsing, checking, declaration setup, and regex-template compilation happen
/// outside the measured operation; each benchmark executes the same hot loops
/// as the compiled allocation comparison.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class RegexGlobalMatchInterpreterAllocationBenchmarks : IDisposable
{
    private Interpreter _interpreter = null!;
    private TypeMap _typeMap = null!;
    private List<Stmt> _globalMatch = null!;
    private List<Stmt> _detailedMatchAll = null!;

    [Params(100, 10_000)]
    public int N { get; set; }

    private const string Input = "alpha beta gamma delta epsilon";

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(RegexGlobalMatchInterpreterAllocationBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.Regex.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource Regex.ts");
        using var reader = new StreamReader(stream);
        List<Stmt> declarations = Parse(reader.ReadToEnd());
        _typeMap = new TypeChecker().Check(declarations);
        _interpreter = new Interpreter(
            stdout: TextWriter.Null,
            stderr: TextWriter.Null);
        _interpreter.Interpret(declarations, _typeMap);

        _globalMatch = Parse($"regexGlobalMatchLoop({Quote(Input)}, {N});");
        _detailedMatchAll = Parse($"regexDetailedMatchAllLoop({Quote(Input)}, {N});");
    }

    [Benchmark(Baseline = true)]
    public object? IntrinsicGlobalStringMatch()
        => _interpreter.InterpretRepl(_globalMatch, _typeMap);

    [Benchmark]
    public object? DetailedStringMatchAll()
        => _interpreter.InterpretRepl(_detailedMatchAll, _typeMap);

    [GlobalCleanup]
    public void Dispose() => _interpreter?.Dispose();

    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();

    private static string Quote(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
