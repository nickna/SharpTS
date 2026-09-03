using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Infrastructure;

/// <summary>
/// Harness for compiling TypeScript to .NET assemblies and invoking compiled methods
/// for BenchmarkDotNet measurements. Handles pre-compilation and reflection-based invocation.
/// </summary>
public static class BenchmarkHarness
{
    private static readonly Dictionary<string, Assembly> _compiledAssemblies = new();
    private static readonly Dictionary<string, MethodInfo> _methodCache = new();
    private static readonly HashSet<Assembly> _initializedModuleAssemblies = [];
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
        var statements = parser.ParseOrThrow();

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
    /// Compiles an in-memory module graph through the same
    /// <see cref="ILCompiler.CompileModules"/> pipeline used by imported CLI
    /// programs. Module paths are resolved under a virtual absolute root; no
    /// temporary source tree or filesystem module discovery enters the timed
    /// benchmark process.
    /// </summary>
    public static string CompileTypeScriptModules(
        IReadOnlyDictionary<string, string> sources,
        string entryPoint,
        string assemblyName)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "CompiledTS");
        Directory.CreateDirectory(outputDir);
        var dllPath = Path.Combine(outputDir, $"{assemblyName}.dll");

        string virtualRoot = Path.Combine(
            Path.GetTempPath(), $"sharpts_benchmark_{assemblyName}");
        var virtualFiles = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, source) in sources)
        {
            string fullPath = Path.GetFullPath(Path.Combine(
                virtualRoot, path.TrimStart('.', '/', '\\')));
            virtualFiles[fullPath] = source;
        }

        string entryPath = Path.GetFullPath(Path.Combine(
            virtualRoot, entryPoint.TrimStart('.', '/', '\\')));
        var resolver = new ModuleResolver(entryPath, virtualFiles);
        var entryModule = resolver.LoadModule(entryPath);
        var modules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        var typeMap = checker.CheckModules(modules, resolver);
        var errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "Module benchmark type-check failed:" + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        var statements = modules.SelectMany(module => module.Statements).ToList();
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler(assemblyName);
        compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

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

                // Plain top-level functions compile to `$Program.<name>`. When the
                // source uses `export function <name>` (so the same file can be
                // imported by the shell harness), the compiler mangles the static
                // method to `$Program.$M_<module>_<name>`. Accept either form.
                var statics = programType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                method = Array.Find(statics, m => m.Name == functionName)
                    ?? Array.Find(statics, m => m.Name.EndsWith("_" + functionName, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"Could not find method '{functionName}' (or a '*_{functionName}' export) in $Program type");

                _methodCache[cacheKey] = method;
            }
            return method;
        }
    }

    /// <summary>
    /// Returns a strongly-typed delegate for a compiled `number -> number`
    /// function, avoiding per-call reflection (<see cref="MethodInfo.Invoke"/>)
    /// and argument boxing inside the measured region. Top-level TypeScript
    /// functions annotated <c>(n: number): number</c> compile to
    /// <c>static double f(double)</c>.
    /// </summary>
    public static Func<double, double> GetCompiledNumberFunc(Assembly assembly, string functionName)
    {
        var method = GetCompiledMethod(assembly, functionName);
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(double) ||
            parameters.Length != 1 || parameters[0].ParameterType != typeof(double))
        {
            throw new InvalidOperationException(
                $"Compiled benchmark '{functionName}' has signature " +
                $"{method.ReturnType.Name}({string.Join(", ", parameters.Select(p => p.ParameterType.Name))}); " +
                "expected Double(Double)");
        }
        return method.CreateDelegate<Func<double, double>>();
    }

    /// <summary>
    /// Returns a strongly-typed delegate for a compiled two-number function so
    /// parameterized benchmarks do not box either input in the measured region.
    /// </summary>
    public static Func<double, double, double> GetCompiledNumber2Func(
        Assembly assembly, string functionName)
    {
        var method = GetCompiledMethod(assembly, functionName);
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(double) ||
            parameters.Length != 2 ||
            parameters[0].ParameterType != typeof(double) ||
            parameters[1].ParameterType != typeof(double))
        {
            throw new InvalidOperationException(
                $"Compiled benchmark '{functionName}' has signature " +
                $"{method.ReturnType.Name}({string.Join(", ", parameters.Select(p => p.ParameterType.Name))}); " +
                "expected Double(Double, Double)");
        }
        return method.CreateDelegate<Func<double, double, double>>();
    }

    /// <summary>
    /// Returns a strongly-typed delegate for a compiled async TypeScript
    /// <c>number -&gt; Promise&lt;number&gt;</c> function. Async stubs intentionally use
    /// the runtime's dynamic ABI: <c>Task&lt;object&gt; f(object)</c>.
    /// </summary>
    public static Func<object?, Task<object?>> GetCompiledAsyncNumberFunc(
        Assembly assembly, string functionName)
    {
        var method = GetCompiledMethod(assembly, functionName);
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(Task<object>) ||
            parameters.Length != 1 || parameters[0].ParameterType != typeof(object))
        {
            throw new InvalidOperationException(
                $"Compiled async benchmark '{functionName}' has signature " +
                $"{method.ReturnType.Name}({string.Join(", ", parameters.Select(p => p.ParameterType.Name))}); " +
                "expected Task<Object>(Object)");
        }
        var invoke = method.CreateDelegate<Func<object?, Task<object?>>>();
        var eventLoopType = assembly.GetType("$EventLoop")
            ?? throw new InvalidOperationException(
                "Compiled async benchmark assembly has no $EventLoop type");
        var getInstance = eventLoopType.GetMethod(
            "GetInstance", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Compiled $EventLoop has no GetInstance method");
        var run = eventLoopType.GetMethod(
            "Run", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Compiled $EventLoop has no Run method");
        object eventLoop = getInstance.Invoke(null, null)
            ?? throw new InvalidOperationException("Compiled $EventLoop.GetInstance returned null");

        // Direct reflection calls bypass the generated program entry point,
        // which normally performs the JavaScript job checkpoint. Pump once per
        // invocation so queued Promise reactions run under the same scheduler
        // measured by real standalone output (#1440). Run drains an entire
        // nested microtask chain; the reflection call itself is constant setup
        // overhead rather than per-link dynamic dispatch.
        return argument =>
        {
            Task<object?> task = invoke(argument);
            try
            {
                run.Invoke(eventLoop, null);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
            return task;
        };
    }

    /// <summary>
    /// Runs a compiled module graph's entry-point initialization once so import
    /// cells and live bindings are populated before an exported method is
    /// invoked directly by a benchmark delegate.
    /// </summary>
    public static void InitializeCompiledModules(Assembly assembly)
    {
        lock (_lock)
        {
            if (!_initializedModuleAssemblies.Add(assembly))
                return;

            var programType = assembly.GetType("$Program")
                ?? throw new InvalidOperationException(
                    "Could not find $Program type in compiled module assembly");
            var main = programType.GetMethod(
                "Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "Could not find $Program.Main in compiled module assembly");
            try
            {
                main.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
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
