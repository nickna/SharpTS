using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Parser-only attribution for stable parseInt(string, 10). Delegates target
/// the emitted runtime helpers directly so generated wrapper and TypeScript
/// loop costs do not obscure the decimal scanner.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ParseIntDecimalBenchmarks
{
    private Func<string, double> _decimal = null!;
    private Func<string, int, double> _general = null!;

    [Params(
        "123456789012345",
        "  -12345suffix",
        "\u00A0+9007199254740991tail")]
    public string Input { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Assembly assembly = typeof(ParseIntDecimalBenchmarks).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.ParseIntDecimal.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource ParseIntDecimal.ts");
        using var reader = new StreamReader(stream);
        string dllPath = CompilationCache.GetOrCompile(
            reader.ReadToEnd(), "ParseIntDecimal");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "parse-int-decimal");
        BenchmarkHarness.InitializeCompiledModules(compiled);

        Type runtime = compiled.GetType("$Runtime")
            ?? throw new InvalidOperationException(
                "Compiled parseInt benchmark has no $Runtime type");
        _decimal = runtime.GetMethod(
                "NumberParseIntDecimalString",
                BindingFlags.Public | BindingFlags.Static)
            ?.CreateDelegate<Func<string, double>>()
            ?? throw new InvalidOperationException(
                "Compiled runtime has no decimal parseInt helper");
        _general = runtime.GetMethod(
                "NumberParseIntString",
                BindingFlags.Public | BindingFlags.Static)
            ?.CreateDelegate<Func<string, int, double>>()
            ?? throw new InvalidOperationException(
                "Compiled runtime has no general typed parseInt helper");
    }

    [Benchmark]
    public double DecimalScanner() => _decimal(Input);

    [Benchmark(Baseline = true)]
    public double GeneralRadixParser() => _general(Input, 10);
}
