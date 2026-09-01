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
    private static readonly ConcurrentDictionary<string, PreparedCompilation> PreparedCache = new();
    private static readonly ConcurrentDictionary<string, object> PreparationLocks = new();
    private static long _executionCount;

    internal static long ExecutionCount => Interlocked.Read(ref _executionCount);

    internal static byte[] Compile(string entryPath)
    {
        EnsureDynamicCodeSupported();
        return Compile(PrepareCompilation(entryPath));
    }

    internal static byte[] Compile(PreparedCompilation compilation)
    {
        EnsureDynamicCodeSupported();

        var artifact = ArtifactCache.GetOrAdd(
            compilation.Fingerprint,
            _ => new Lazy<byte[]>(
                () => CompileCore(
                    compilation.Modules,
                    compilation.Resolver,
                    compilation.Fingerprint),
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
    internal static bool TryPrepareCompiled(
        string entryPath,
        object? workerData,
        bool hasStdin,
        out PreparedCompilation compilation)
    {
        compilation = null!;
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ||
            hasStdin || ContainsUnsupportedRealmValue(
                workerData, new(System.Collections.Generic.ReferenceEqualityComparer.Instance)))
            return false;

        var prepared = PrepareCompilation(entryPath);
        foreach (var module in prepared.Modules)
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

        compilation = prepared;
        return true;
    }

    private static PreparedCompilation PrepareCompilation(string entryPath)
    {
        var absolutePath = Path.GetFullPath(entryPath);
        var preparationLock = PreparationLocks.GetOrAdd(absolutePath, static _ => new object());

        lock (preparationLock)
        {
            // ArtifactCache avoids repeated type-checking and IL emission, but worker startup
            // previously reparsed and re-resolved the entire module graph before discovering the
            // same fingerprint. Re-hash the cached graph first: this keeps content-based
            // invalidation exact while avoiding redundant lexer/parser/resolver work.
            if (PreparedCache.TryGetValue(absolutePath, out var cached))
            {
                var currentFingerprint = ComputeFingerprint(cached.Modules);
                if (string.Equals(currentFingerprint, cached.Fingerprint, StringComparison.Ordinal))
                    return cached;
            }

            var resolver = new ModuleResolver(absolutePath);
            var entryModule = resolver.LoadModule(absolutePath);
            var modules = resolver.GetModulesInOrder(entryModule);
            var prepared = new PreparedCompilation(modules, resolver, ComputeFingerprint(modules));
            PreparedCache[absolutePath] = prepared;
            return prepared;
        }
    }

    private static void EnsureDynamicCodeSupported()
    {
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "Compiled Worker execution requires a managed SharpTS runtime with dynamic code support.");
        }
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
        return compiler.SaveToBytes();
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

        string typeName = value.GetType().Name;
        if (typeName is "$ArrayBuffer" ||
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

    internal sealed record PreparedCompilation(
        List<ParsedModule> Modules,
        ModuleResolver Resolver,
        string Fingerprint);
}
