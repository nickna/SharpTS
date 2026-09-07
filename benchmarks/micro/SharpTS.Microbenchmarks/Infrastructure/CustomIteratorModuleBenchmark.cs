using System.Reflection;

namespace SharpTS.Microbenchmarks.Infrastructure;

/// <summary>Compiles the original cross-runtime sources, including their real bench import.</summary>
internal static class CustomIteratorModuleBenchmark
{
    internal static readonly HashSet<string> ModuleOnlyResources =
    [
        "SharpTS.Microbenchmarks.CustomIterator.stable.ts",
        "SharpTS.Microbenchmarks.CustomIterator.dynamic.ts",
        "SharpTS.Microbenchmarks.CustomIterator.bench.ts"
    ];

    internal static string Read(string name)
    {
        using var stream = typeof(CustomIteratorModuleBenchmark).Assembly.GetManifestResourceStream(
            $"SharpTS.Microbenchmarks.CustomIterator.{name}.ts")
            ?? throw new InvalidOperationException($"Missing custom iterator resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string Compile(bool dynamic, string assemblyName) =>
        CompilationCache.GetOrCompileModules(new Dictionary<string, string>
        {
            ["iterator.ts"] = Read(dynamic ? "dynamic" : "stable"),
            ["lib/bench.ts"] = Read("bench")
        }, "iterator.ts", assemblyName);

    internal static Assembly Load(bool dynamic)
    {
        string name = dynamic ? "DynamicCustomIterator" : "StableCustomIterator";
        // The measured functions own all their state. No module initializer is
        // needed to call them, and running Main would execute bench's timed driver.
        return BenchmarkHarness.LoadCompiledAssembly(Compile(dynamic, name), name);
    }

    internal static Func<double, double> Bind(bool dynamic)
    {
        MethodInfo method = BenchmarkHarness.GetCompiledMethod(Load(dynamic),
            dynamic ? "mutatedCustomIterator" : "stableCustomIterator");
        if (method.ReturnType == typeof(double))
            return method.CreateDelegate<Func<double, double>>();
        // Generic for-of can widen the accumulator and return ABI to object.
        // Keep that emitted boxing in the measurement, without reflection calls.
        var invoke = method.CreateDelegate<Func<double, object>>();
        return n => (double)invoke(n);
    }
}
