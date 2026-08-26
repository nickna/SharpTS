using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>
/// Dynamic-input coverage for the fixed-arity primitive string intrinsics.
/// Search operations should allocate nothing in the measured loop; slicing
/// should allocate only the observable result strings.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PrimitiveStringIntrinsicBenchmarks
{
    private const string Input = "alpha-beta-gamma-delta";
    private const string Needle = "gamma";
    private const int Position = 2;
    private const int Start = 3;
    private const int End = 18;

    private Func<string, string, double, double, double> _indexOf = null!;
    private Func<string, string, double, double, double> _includes = null!;
    private Func<string, double, double, double, double> _slice = null!;
    private Func<string, double, double, double, double> _substring = null!;

    [Params(100, 10_000, 100_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(PrimitiveStringIntrinsicBenchmarks).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.TypeScriptSources.PrimitiveStringIntrinsics.ts")
            ?? throw new InvalidOperationException(
                "Could not find embedded resource PrimitiveStringIntrinsics.ts");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();

        string dllPath = CompilationCache.GetOrCompile(source, "PrimitiveStringIntrinsics");
        Assembly compiled = BenchmarkHarness.LoadCompiledAssembly(
            dllPath, "primitive-string-intrinsics");
        _indexOf = BenchmarkHarness.GetCompiledMethod(compiled, "stringIndexOfLoop")
            .CreateDelegate<Func<string, string, double, double, double>>();
        _includes = BenchmarkHarness.GetCompiledMethod(compiled, "stringIncludesLoop")
            .CreateDelegate<Func<string, string, double, double, double>>();
        _slice = BenchmarkHarness.GetCompiledMethod(compiled, "stringSliceLoop")
            .CreateDelegate<Func<string, double, double, double, double>>();
        _substring = BenchmarkHarness.GetCompiledMethod(compiled, "stringSubstringLoop")
            .CreateDelegate<Func<string, double, double, double, double>>();
    }

    [Benchmark]
    [BenchmarkCategory("StringIndexOf")]
    public double SharpTS_IndexOf() => _indexOf(Input, Needle, Position, N);

    [Benchmark]
    [BenchmarkCategory("StringIndexOf")]
    public int NativeCSharp_IndexOf()
    {
        int total = 0;
        for (int i = 0; i < N; i++)
            total += Input.IndexOf(Needle, Position, StringComparison.Ordinal);
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("StringIncludes")]
    public double SharpTS_Includes() => _includes(Input, Needle, Position, N);

    [Benchmark]
    [BenchmarkCategory("StringIncludes")]
    public int NativeCSharp_Includes()
    {
        int total = 0;
        for (int i = 0; i < N; i++)
            if (Input.IndexOf(Needle, Position, StringComparison.Ordinal) >= 0)
                total++;
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("StringSlice")]
    public double SharpTS_Slice() => _slice(Input, Start, End, N);

    [Benchmark]
    [BenchmarkCategory("StringSlice")]
    public int NativeCSharp_Slice()
    {
        int total = 0;
        for (int i = 0; i < N; i++)
            total += Input.Substring(Start, End - Start).Length;
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("StringSubstring")]
    public double SharpTS_Substring() => _substring(Input, Start, End, N);

    [Benchmark]
    [BenchmarkCategory("StringSubstring")]
    public int NativeCSharp_Substring()
    {
        int total = 0;
        for (int i = 0; i < N; i++)
            total += Input.Substring(Start, End - Start).Length;
        return total;
    }
}
