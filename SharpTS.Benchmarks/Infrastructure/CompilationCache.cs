using System.Collections.Concurrent;

namespace SharpTS.Benchmarks.Infrastructure;

/// <summary>
/// Manages pre-compilation of TypeScript sources during benchmark initialization.
/// Ensures compilation happens once in GlobalSetup, not during measurement.
/// Thread-safe for parallel benchmark execution.
/// </summary>
public static class CompilationCache
{
    private static readonly ConcurrentDictionary<string, string> _dllPaths = new();

    /// <summary>
    /// Gets or compiles a TypeScript source to a DLL.
    /// If already compiled, returns the cached DLL path.
    /// Thread-safe.
    /// </summary>
    /// <param name="tsSource">TypeScript source code</param>
    /// <param name="assemblyName">Name for the compiled assembly</param>
    /// <returns>Path to the compiled DLL</returns>
    public static string GetOrCompile(string tsSource, string assemblyName)
    {
        return _dllPaths.GetOrAdd(assemblyName, _ =>
            BenchmarkHarness.CompileTypeScript(tsSource, assemblyName));
    }

    /// <summary>
    /// Clears the compilation cache. Useful for testing or rebuilding benchmarks.
    /// </summary>
    public static void Clear()
    {
        _dllPaths.Clear();
    }
}
