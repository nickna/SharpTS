using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILVerify;

namespace SharpTS.Compilation;

/// <summary>
/// Verifies IL in compiled assemblies using Microsoft.ILVerification.
/// </summary>
public class ILVerifier : IResolver, IDisposable
{
    private readonly string _sdkPath;
    private readonly Dictionary<string, PEReader> _assemblyCache = new();
    private bool _disposed;

    public ILVerifier(string sdkPath)
    {
        _sdkPath = sdkPath;
    }

    /// <summary>
    /// Verifies the IL in an assembly and returns any verification errors.
    /// </summary>
    /// <param name="assemblyStream">Stream containing the assembly to verify</param>
    /// <returns>List of verification error messages</returns>
    public List<string> Verify(Stream assemblyStream)
    {
        var errors = new List<string>();

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
                    errors.Add($"[IL Error] {typeName}.{methodName}: {result.Message}");
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

    public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName)
    {
        var name = assemblyName.Name ?? "";

        // Check cache first
        if (_assemblyCache.TryGetValue(name, out var cached))
            return cached;

        // Try to find in SDK reference assemblies
        var dllPath = Path.Combine(_sdkPath, $"{name}.dll");
        if (File.Exists(dllPath))
        {
            var reader = new PEReader(File.OpenRead(dllPath));
            _assemblyCache[name] = reader;
            return reader;
        }

        // Try runtime assemblies as fallback
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimePath != null)
        {
            var runtimeDll = Path.Combine(runtimePath, $"{name}.dll");
            if (File.Exists(runtimeDll))
            {
                var reader = new PEReader(File.OpenRead(runtimeDll));
                _assemblyCache[name] = reader;
                return reader;
            }
        }

        return null;
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
