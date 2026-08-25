using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Allocation coverage for stable numeric Date getter/setter calls. The TypeScript
/// body creates one Date per benchmark operation, while the measured loop must not
/// allocate boxed results per iteration.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DateNumericBenchmarks
{
    private Func<double, double> _dateNumericLoop = null!;

    [Params(100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(DateNumericBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.DateNumeric.ts")
            ?? throw new InvalidOperationException("Could not find embedded resource DateNumeric.ts");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        string dllPath = CompilationCache.GetOrCompile(source, "DateNumeric");
        var compiled = BenchmarkHarness.LoadCompiledAssembly(dllPath, "date-numeric");
        _dateNumericLoop = BenchmarkHarness.GetCompiledNumberFunc(compiled, "dateNumericLoop");
    }

    [Benchmark]
    public double SharpTS_GetTimeSetTime() => _dateNumericLoop(N);
}
