using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Compiles a worker entry module into an in-memory assembly that can be loaded into an
/// isolated <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// Worker artifacts are cached by the content of every source module in the worker graph.
/// Each worker still loads the cached bytes into its own load context, so generated static
/// module state and the emitted event loop remain realm-local.
/// </remarks>
internal static class CompiledWorkerCompilationService
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> ArtifactCache = new();
    private static long _compilationCount;
    private static long _executionCount;

    internal static long CompilationCount => Interlocked.Read(ref _compilationCount);
    internal static long ExecutionCount => Interlocked.Read(ref _executionCount);

    internal static byte[] Compile(string entryPath)
    {
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "Compiled Worker execution requires a managed SharpTS runtime with dynamic code support.");
        }

        var absolutePath = Path.GetFullPath(entryPath);
        var resolver = new ModuleResolver(absolutePath);
        var entryModule = resolver.LoadModule(absolutePath);
        var modules = resolver.GetModulesInOrder(entryModule);
        var fingerprint = ComputeFingerprint(modules);

        var artifact = ArtifactCache.GetOrAdd(
            fingerprint,
            _ => new Lazy<byte[]>(
                () => CompileCore(modules, resolver, fingerprint),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return artifact.Value;
    }

    internal static void RecordExecution() => Interlocked.Increment(ref _executionCount);

    /// <summary>
    /// Returns whether the worker can run in an isolated compiled realm without losing an
    /// already-supported compatibility feature. Unsupported cross-realm values and blocking
    /// host bridges retain the previous interpreter-backed worker path until their emitted
    /// equivalents are implemented.
    /// </summary>
    internal static bool CanExecuteCompiled(string entryPath, object? workerData, bool hasStdin)
    {
        if (hasStdin || ContainsUnsupportedRealmValue(
                workerData, new(System.Collections.Generic.ReferenceEqualityComparer.Instance)))
            return false;

        var absolutePath = Path.GetFullPath(entryPath);
        var resolver = new ModuleResolver(absolutePath);
        var entryModule = resolver.LoadModule(absolutePath);
        foreach (var module in resolver.GetModulesInOrder(entryModule))
        {
            var source = module.Document?.Text;
            if (source is null)
                continue;

            // Blocking Atomics and the process.stdin host bridge do not yet have compiled
            // worker-realm implementations. This conservative source-graph check may choose
            // the compatibility path for a mention in unreachable code, but never changes
            // observable behavior.
            if (source.Contains("Atomics.wait", StringComparison.Ordinal) ||
                source.Contains("process.stdin", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] CompileCore(
        List<ParsedModule> modules,
        ModuleResolver resolver,
        string fingerprint)
    {
        var checker = new TypeChecker().AsWorkerContext();
        var typeMap = checker.CheckModules(modules, resolver);
        var firstError = checker.GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (firstError is not null)
            throw new InvalidOperationException($"Worker script type error: {firstError}");

        var statements = modules.SelectMany(m => m.Statements).ToList();
        var deadCode = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"SharpTS.Worker.{fingerprint[..16]}");
        compiler.CompileModules(modules, resolver, typeMap, deadCode);
        var bytes = compiler.SaveToBytes();
        Interlocked.Increment(ref _compilationCount);
        return bytes;
    }

    private static string ComputeFingerprint(IEnumerable<ParsedModule> modules)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var module in modules.Where(m => !m.IsBuiltIn).OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            var pathBytes = Encoding.UTF8.GetBytes(Path.GetFullPath(module.Path));
            hash.AppendData(pathBytes);
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(module.Path));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool ContainsUnsupportedRealmValue(object? value, HashSet<object> visited)
    {
        if (value is null || value is string or bool or double or System.Numerics.BigInteger)
            return false;
        if (!value.GetType().IsValueType && !visited.Add(value))
            return false;

        if (value is CompiledMessagePortBridge)
            return true;

        string typeName = value.GetType().Name;
        if (typeName is "$ArrayBuffer" or "$SharedArrayBuffer" ||
            (typeName.StartsWith('$') && typeName.EndsWith("Array", StringComparison.Ordinal)))
        {
            return true;
        }

        return value switch
        {
            IDictionary<string, object?> dictionary =>
                dictionary.Values.Any(item => ContainsUnsupportedRealmValue(item, visited)),
            IEnumerable<object?> sequence =>
                sequence.Any(item => ContainsUnsupportedRealmValue(item, visited)),
            SharpTSObject obj => obj.PropertyNames.Any(
                name => ContainsUnsupportedRealmValue(obj.GetProperty(name), visited)),
            _ => false,
        };
    }
}
