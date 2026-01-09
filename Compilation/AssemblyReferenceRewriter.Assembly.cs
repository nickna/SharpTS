using System.Reflection;
using System.Reflection.Metadata;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    private void CopyAssemblyDefinition()
    {
        var assemblyDef = _reader.GetAssemblyDefinition();

        _metadata.AddAssembly(
            GetOrAddString(_reader.GetString(assemblyDef.Name)),
            assemblyDef.Version,
            GetOrAddString(_reader.GetString(assemblyDef.Culture)),
            GetOrAddBlob(_reader.GetBlobBytes(assemblyDef.PublicKey)),
            assemblyDef.Flags,
            assemblyDef.HashAlgorithm);
    }

    private void CopyModuleDefinition()
    {
        var moduleDef = _reader.GetModuleDefinition();

        _metadata.AddModule(
            moduleDef.Generation,
            GetOrAddString(_reader.GetString(moduleDef.Name)),
            GetOrAddGuid(_reader.GetGuid(moduleDef.Mvid)),
            GetOrAddGuid(moduleDef.GenerationId.IsNil ? default : _reader.GetGuid(moduleDef.GenerationId)),
            GetOrAddGuid(moduleDef.BaseGenerationId.IsNil ? default : _reader.GetGuid(moduleDef.BaseGenerationId)));
    }

    private void CreateAssemblyReferences()
    {
        // Determine which target assemblies we need based on types used
        HashSet<string> neededAssemblies = [];

        foreach (var typeRefHandle in _reader.TypeReferences)
        {
            var typeRef = _reader.GetTypeReference(typeRefHandle);
            var scope = typeRef.ResolutionScope;

            // Only rewrite references from System.Private.CoreLib
            if (scope.Kind == HandleKind.AssemblyReference)
            {
                var asmRef = _reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
                var asmName = _reader.GetString(asmRef.Name);

                if (asmName == "System.Private.CoreLib")
                {
                    var typeName = GetFullTypeName(typeRef);
                    if (_typeToAssembly.TryGetValue(typeName, out var targetAsm))
                    {
                        neededAssemblies.Add(targetAsm);
                    }
                    else
                    {
                        // Default to System.Runtime for unknown types
                        neededAssemblies.Add("System.Runtime");
                    }
                }
            }
        }

        // Always include System.Runtime as the core runtime assembly.
        // This ensures deterministic behavior across platforms since Directory.GetFiles()
        // returns files in different orders on Windows (alphabetical) vs Linux (inode order),
        // which affects type-to-assembly mapping when types exist in multiple assemblies.
        neededAssemblies.Add("System.Runtime");

        // Create references for all needed SDK assemblies
        foreach (var asmName in neededAssemblies)
        {
            if (_assemblyInfoCache.TryGetValue(asmName, out var asmInfo))
            {
                // Use public key token (not full public key) and clear the PublicKey flag.
                // The SDK reference assemblies have the PublicKey flag set with full public keys,
                // but GetPublicKeyToken() returns just the 8-byte token which is more compatible.
                var flags = (AssemblyFlags)asmInfo.Flags & ~AssemblyFlags.PublicKey;

                var handle = _metadata.AddAssemblyReference(
                    GetOrAddString(asmInfo.Name!),
                    asmInfo.Version!,
                    GetOrAddString(asmInfo.CultureName ?? string.Empty),
                    GetOrAddBlob(asmInfo.GetPublicKeyToken() ?? []),
                    flags,
                    default);

                _newAssemblyRefs[asmName] = handle;
            }
        }

        // Copy existing assembly references (except System.Private.CoreLib)
        foreach (var asmRefHandle in _reader.AssemblyReferences)
        {
            var asmRef = _reader.GetAssemblyReference(asmRefHandle);
            var name = _reader.GetString(asmRef.Name);

            if (name == "System.Private.CoreLib")
                continue;

            var newHandle = _metadata.AddAssemblyReference(
                GetOrAddString(name),
                asmRef.Version,
                GetOrAddString(_reader.GetString(asmRef.Culture)),
                GetOrAddBlob(_reader.GetBlobBytes(asmRef.PublicKeyOrToken)),
                asmRef.Flags,
                GetOrAddBlob(_reader.GetBlobBytes(asmRef.HashValue)));

            _assemblyRefMap[asmRefHandle] = newHandle;
        }
    }
}
