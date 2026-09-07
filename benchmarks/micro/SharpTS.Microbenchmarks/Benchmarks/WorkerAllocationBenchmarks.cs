using System.Reflection;
using BenchmarkDotNet.Attributes;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks.Benchmarks;

/// <summary>Canonical worker kernel, with separately measured construction and traversal.</summary>
[MemoryDiagnoser]
public class WorkerAllocationBenchmarks
{
    [Params(5000, 8192, 8193, 20000)]
    public int N { get; set; }

    [Params(false, true)]
    public bool RecordAlias { get; set; }

    private Func<double, double, double> _full = null!;
    private Func<double, double, object> _build = null!;
    private Func<object, double> _traverse = null!;
    private object _records = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(WorkerAllocationBenchmarks).Assembly.GetManifestResourceStream(
            "SharpTS.Microbenchmarks.allocation-kernel.ts")!;
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd();
        if (RecordAlias)
            source = source.Replace("interface AllocationRecord {", "type AllocationRecord = {");

        // Derive phase bodies from the canonical source, so diagnostic loops cannot
        // silently drift away from the full workload. Fail if its structure changes.
        int buildStart = source.IndexOf("    const records:", StringComparison.Ordinal);
        int traversalStart = source.IndexOf("    let checksum:", StringComparison.Ordinal);
        int end = source.LastIndexOf('}');
        if (buildStart < 0 || traversalStart <= buildStart || end <= traversalStart)
            throw new InvalidOperationException("Allocation kernel phase markers changed");
        string diagnostic = source + "\nexport function buildAllocationRecords(start: number, end: number): AllocationRecord[] {\n"
            + source[buildStart..traversalStart] + "return records;\n}\n"
            + "export function traverseAllocationRecords(records: AllocationRecord[]): number {\n"
            + source[traversalStart..end] + "}\n";
        string key = "WorkerAllocation" + (RecordAlias ? "Alias" : "Interface");
        string dll = CompilationCache.GetOrCompileModules(
            new Dictionary<string, string> { ["kernel.ts"] = diagnostic }, "kernel.ts", key);
        Assembly assembly = BenchmarkHarness.LoadCompiledAssembly(dll, key);
        BenchmarkHarness.InitializeCompiledModules(assembly);
        _full = BenchmarkHarness.GetCompiledMethod(assembly, "allocationChecksum")
            .CreateDelegate<Func<double, double, double>>();
        _build = BenchmarkHarness.GetCompiledMethod(assembly, "buildAllocationRecords")
            .CreateDelegate<Func<double, double, object>>();
        _traverse = BenchmarkHarness.GetCompiledMethod(assembly, "traverseAllocationRecords")
            .CreateDelegate<Func<object, double>>();
        _records = _build(0, N);
        double expected = 2.0 * N * N + 9.0 * N - (N / 100 * 10 + Math.Min(N % 100, 10));
        if (_full(0, N) != expected || _traverse(_records) != expected)
            throw new InvalidOperationException("Allocation benchmark checksum mismatch");
    }

    [Benchmark] public double Full() => _full(0, N);
    // Returning records deliberately crosses the escape boundary, so this is a
    // boxed-storage control. Full() is allowed to retain private numeric arrays;
    // Construction + Traversal is therefore not an additive decomposition of it.
    [Benchmark] public object Construction() => _build(0, N);
    [Benchmark] public double Traversal() => _traverse(_records);
}
