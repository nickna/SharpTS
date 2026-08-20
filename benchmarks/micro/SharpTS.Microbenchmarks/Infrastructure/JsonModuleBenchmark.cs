using System.Reflection;

namespace SharpTS.Microbenchmarks.Infrastructure;

/// <summary>
/// Builds the imported-module JSON benchmark graph from embedded sources.
/// The driver mirrors json.ts's imported algorithm plus capturing callback
/// handoff, while executing exactly one callback per BenchmarkDotNet operation.
/// </summary>
internal static class JsonModuleBenchmark
{
    private const string AlgorithmsResource =
        "SharpTS.Microbenchmarks.algorithms.ts";
    private const string CallbackResource =
        "SharpTS.Microbenchmarks.TypeScriptSources.BenchmarkCallback.ts";
    private const string DriverResource =
        "SharpTS.Microbenchmarks.TypeScriptSources.JsonBenchmarkDriver.ts";
    private const string InterpreterBridgeResource =
        "SharpTS.Microbenchmarks.TypeScriptSources.JsonInterpreterBridge.ts";

    internal static readonly HashSet<string> ModuleOnlyResources =
        [CallbackResource, DriverResource, InterpreterBridgeResource];

    internal static string Compile(string assemblyName)
        => CompilationCache.GetOrCompileModules(
            LoadSources(), "json-driver.ts", assemblyName);

    internal static Dictionary<string, string> LoadSources(
        bool includeInterpreterBridge = false)
    {
        var sources = new Dictionary<string, string>
        {
            ["algorithms.ts"] = ReadResource(AlgorithmsResource),
            ["benchmark-callback.ts"] = ReadResource(CallbackResource),
            ["json-driver.ts"] = ReadResource(DriverResource),
        };
        if (includeInterpreterBridge)
        {
            sources["json-interpreter-bridge.ts"] =
                ReadResource(InterpreterBridgeResource);
        }
        return sources;
    }

    private static string ReadResource(string name)
    {
        Assembly assembly = typeof(JsonModuleBenchmark).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Could not find embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
