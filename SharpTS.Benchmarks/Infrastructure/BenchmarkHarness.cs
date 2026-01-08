using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Benchmarks.Infrastructure;

/// <summary>
/// Harness for compiling TypeScript to .NET assemblies and invoking compiled methods
/// for BenchmarkDotNet measurements. Handles pre-compilation and reflection-based invocation.
/// </summary>
public static class BenchmarkHarness
{
    private static readonly Dictionary<string, Assembly> _compiledAssemblies = new();
    private static readonly Dictionary<string, MethodInfo> _methodCache = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Pre-compiles TypeScript source to a DLL at GlobalSetup.
    /// Returns the path to the compiled DLL for loading.
    /// </summary>
    /// <param name="source">TypeScript source code containing function definitions</param>
    /// <param name="assemblyName">Name for the compiled assembly</param>
    /// <returns>Full path to the compiled DLL</returns>
    public static string CompileTypeScript(string source, string assemblyName)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "CompiledTS");
        Directory.CreateDirectory(outputDir);

        var dllPath = Path.Combine(outputDir, $"{assemblyName}.dll");

        // Compile using ILCompiler (same pattern as TestHarness)
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);

        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        var compiler = new ILCompiler(assemblyName);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        // Copy SharpTS.dll runtime dependency (needed for runtime support)
        var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
        if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
        {
            File.Copy(sharpTsDll, Path.Combine(outputDir, "SharpTS.dll"), overwrite: true);
        }

        return dllPath;
    }

    /// <summary>
    /// Loads a compiled assembly and caches it for invocation.
    /// Thread-safe for parallel benchmark execution.
    /// </summary>
    /// <param name="dllPath">Path to the compiled DLL</param>
    /// <param name="key">Cache key for this assembly</param>
    /// <returns>Loaded Assembly</returns>
    public static Assembly LoadCompiledAssembly(string dllPath, string key)
    {
        lock (_lock)
        {
            if (!_compiledAssemblies.TryGetValue(key, out var assembly))
            {
                assembly = Assembly.LoadFrom(dllPath);
                _compiledAssemblies[key] = assembly;
            }
            return assembly;
        }
    }

    /// <summary>
    /// Gets a cached MethodInfo for a function in the compiled assembly.
    /// Top-level TypeScript functions are compiled as static methods on the $Program class.
    /// </summary>
    /// <param name="assembly">The compiled assembly</param>
    /// <param name="functionName">Name of the TypeScript function</param>
    /// <returns>MethodInfo for the function</returns>
    /// <exception cref="InvalidOperationException">If $Program type or method not found</exception>
    public static MethodInfo GetCompiledMethod(Assembly assembly, string functionName)
    {
        var cacheKey = $"{assembly.GetName().Name}::{functionName}";

        lock (_lock)
        {
            if (!_methodCache.TryGetValue(cacheKey, out var method))
            {
                var programType = assembly.GetType("$Program")
                    ?? throw new InvalidOperationException("Could not find $Program type in compiled assembly");

                method = programType.GetMethod(functionName, BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException($"Could not find method '{functionName}' in $Program type");

                _methodCache[cacheKey] = method;
            }
            return method;
        }
    }

    /// <summary>
    /// Invokes a compiled TypeScript function with arguments via reflection.
    /// Handles TypeScript's dynamic typing by converting arguments to appropriate types.
    /// </summary>
    /// <param name="method">The MethodInfo to invoke</param>
    /// <param name="args">Arguments to pass (will be converted to object[])</param>
    /// <returns>Return value from the method</returns>
    /// <exception cref="Exception">Unwrapped exception from the invoked method</exception>
    public static object? InvokeCompiled(MethodInfo method, params object?[] args)
    {
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap and rethrow the inner exception for clearer error messages
            throw ex.InnerException ?? ex;
        }
    }
}
