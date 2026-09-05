using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

[MemoryDiagnoser]
public class AllocationKernelBenchmarks
{
    private Func<double, double, double> _kernel = null!;
    private Func<double, object> _build = null!;
    private Func<object, double> _read = null!;
    private object _records = null!;

    [Params(2000, 20000)]
    public int N { get; set; }

    [Params(false, true)]
    public bool UseTypeAlias { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var source = ReadResource("SharpTS.Microbenchmarks.allocation-kernel.ts") + "\n" +
            ReadResource("SharpTS.Microbenchmarks.TypeScriptSources.AllocationPhases.ts");
        if (UseTypeAlias)
            source = source.Replace("interface AllocationRecord {", "type AllocationRecord = {")
                .Replace("interface PhaseAllocationRecord {", "type PhaseAllocationRecord = {");

        string name = UseTypeAlias ? "AllocationAlias" : "AllocationInterface";
        var path = CompilationCache.GetOrCompile(source, name);
        var assembly = BenchmarkHarness.LoadCompiledAssembly(path, name);
        _kernel = BenchmarkHarness.GetCompiledNumber2Func(assembly, "allocationChecksum");
        _build = BenchmarkHarness.GetCompiledMethod(assembly, "buildAllocationRecords")
            .CreateDelegate<Func<double, object>>();
        _read = BenchmarkHarness.GetCompiledMethod(assembly, "readAllocationRecords")
            .CreateDelegate<Func<object, double>>();
        _records = _build(N);

        double expected = 0;
        for (int i = 0; i < N; i++) expected += 4.0 * i + 4 + (i % 100 < 10 ? 6 : 7);
        if (_kernel(0, N) != expected || _read(_records) != expected)
            throw new InvalidOperationException("Allocation benchmark checksum mismatch.");
    }

    [Benchmark]
    public double FullKernel() => _kernel(0, N);

    [Benchmark]
    public object BuildRecords() => _build(N);

    [Benchmark]
    public double ReadRecords() => _read(_records);

    private static string ReadResource(string name)
    {
        using var stream = typeof(AllocationKernelBenchmarks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
