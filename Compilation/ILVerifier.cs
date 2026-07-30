using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using ILVerify;

namespace SharpTS.Compilation;

/// <summary>
/// Verifies IL in compiled assemblies using Microsoft.ILVerification.
/// </summary>
public class ILVerifier : IResolver, IDisposable
{
    private readonly string? _sdkPath;
    private readonly IReadOnlyList<string> _extraProbeDirectories;
    private readonly Dictionary<string, PEReader> _assemblyCache = new();
    private bool _disposed;

    /// <param name="sdkPath">Optional extra probe directory (e.g. an explicit --sdk-path).
    /// Probed after the shared-framework runtime directory.</param>
    /// <param name="extraProbeDirectories">Additional probe directories, e.g. the locations
    /// of third-party reference assemblies (sharpts.json / -r) the emitted IL calls into.
    /// Probed last.</param>
    public ILVerifier(string? sdkPath = null, IEnumerable<string>? extraProbeDirectories = null)
    {
        _sdkPath = sdkPath;
        _extraProbeDirectories = extraProbeDirectories?.Distinct().ToArray() ?? [];
    }

    /// <summary>
    /// Verifies the IL in an assembly and returns any verification errors.
    /// </summary>
    /// <param name="assemblyStream">Stream containing the assembly to verify</param>
    /// <returns>List of verification error messages</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "IL verification is a managed-host-only feature and rejects Native AOT before reflecting over ILVerify diagnostic objects.")]
    public List<string> Verify(Stream assemblyStream)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "IL verification is not available in a native SharpTS build — use a managed host.");
        }

        List<string> errors = [];

        assemblyStream.Position = 0;
        using var peReader = new PEReader(assemblyStream, PEStreamOptions.LeaveOpen);
        var metadataReader = peReader.GetMetadataReader();

        var verifier = new Verifier(this, new VerifierOptions
        {
            IncludeMetadataTokensInErrorMessages = true,
            SanityChecks = true
        });

        // Set the system module (System.Runtime or mscorlib)
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Runtime"));

        // Verify all methods in the assembly
        foreach (var methodHandle in metadataReader.MethodDefinitions)
        {
            var method = metadataReader.GetMethodDefinition(methodHandle);

            // Skip methods without IL body (abstract, extern, etc.)
            if (method.RelativeVirtualAddress == 0)
                continue;

            try
            {
                var results = verifier.Verify(peReader, methodHandle);
                foreach (var result in results)
                {
                    var typeName = GetTypeName(metadataReader, method.GetDeclaringType());
                    var methodName = metadataReader.GetString(method.Name);
                    // Include error code and any additional args for debugging
                    var argsStr = result.Args != null && result.Args.Length > 0
                        ? $" [{string.Join(", ", result.Args)}]"
                        : "";
                    var diag = "";
                    try
                    {
                        var ea = result.GetType().GetProperty("ErrorArguments")?.GetValue(result) as System.Collections.IEnumerable;
                        if (ea != null)
                        {
                            var parts = new List<string>();
                            foreach (var a in ea)
                            {
                                var n = a.GetType().GetProperty("Name")?.GetValue(a);
                                var val = a.GetType().GetProperty("Value")?.GetValue(a);
                                parts.Add($"{n}={val}");
                            }
                            if (parts.Count > 0) diag = $" {{{string.Join(", ", parts)}}}";
                        }
                    }
                    catch { }
                    errors.Add($"[IL Error] {typeName}.{methodName}: {result.Code}{argsStr}{diag} - {result.Message}");
                }
            }
            catch (Exception ex)
            {
                // Some methods may fail to verify due to missing dependencies
                var typeName = GetTypeName(metadataReader, method.GetDeclaringType());
                var methodName = metadataReader.GetString(method.Name);
                errors.Add($"[IL Error] {typeName}.{methodName}: Verification failed - {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Verifies the IL and prints errors to console.
    /// </summary>
    /// <param name="assemblyStream">Stream containing the assembly to verify</param>
    /// <returns>True if verification passed with no errors</returns>
    public bool VerifyAndReport(Stream assemblyStream)
    {
        var errors = Verify(assemblyStream);

        if (errors.Count == 0)
        {
            Console.WriteLine("IL verification passed.");
            return true;
        }

        Console.WriteLine($"IL verification found {errors.Count} error(s):");
        foreach (var error in errors)
        {
            Console.WriteLine($"  {error}");
        }

        return false;
    }

    private static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        if (handle.IsNil)
            return "<unknown>";

        var typeDef = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(typeDef.Namespace);
        var name = reader.GetString(typeDef.Name);

        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    #region IResolver Implementation

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "An empty bundled core-library location skips the runtime-directory probe and falls through to the explicit SDK and additional probe directories.")]
    public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName)
    {
        var name = assemblyName.Name ?? "";

        // Check cache first
        if (_assemblyCache.TryGetValue(name, out var cached))
            return cached;

        // Resolve from the shared-framework runtime directory first. Emitted assemblies
        // reference System.Private.CoreLib (not the ref-assembly contract surface), and
        // the runtime directory contains both CoreLib and the type-forwarding facades,
        // so every reference resolves in one consistent universe. Mixing ref assemblies
        // with CoreLib from the runtime dir makes core type identities diverge and
        // ILVerify flags nearly every stack interaction (issue #189).
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimePath != null)
        {
            var reader = TryLoad(name, runtimePath);
            if (reader != null)
                return reader;
        }

        // Fall back to the explicit SDK path, if one was given
        if (_sdkPath != null)
        {
            var reader = TryLoad(name, _sdkPath);
            if (reader != null)
                return reader;
        }

        // Finally, third-party reference directories (sharpts.json / -r assemblies).
        foreach (var dir in _extraProbeDirectories)
        {
            var reader = TryLoad(name, dir);
            if (reader != null)
                return reader;
        }

        return null;
    }

    private PEReader? TryLoad(string assemblyName, string directory)
    {
        var dllPath = Path.Combine(directory, $"{assemblyName}.dll");
        if (!File.Exists(dllPath))
            return null;

        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(dllPath);
            var reader = new PEReader(stream);
            _assemblyCache[assemblyName] = reader;
            return reader;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    public PEReader? ResolveModule(AssemblyNameInfo referencingAssembly, string fileName)
    {
        // Module resolution - look for the file in the same directory as the referencing assembly
        // For our purposes, we primarily deal with single-module assemblies
        return null;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var reader in _assemblyCache.Values)
        {
            reader.Dispose();
        }
        _assemblyCache.Clear();

        _disposed = true;
    }

    #endregion
}
