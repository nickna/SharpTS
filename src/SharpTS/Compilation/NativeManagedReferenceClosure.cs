using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.References;

namespace SharpTS.Compilation;

/// <summary>One complete, byte-owned cohort of explicitly supplied net10 implementation DLLs.</summary>
internal sealed class NativeManagedReferenceClosure
{
    private sealed record Input(string Path, NativeAssemblyIdentity Identity, byte[] Image,
        IReadOnlyList<NativeAssemblyIdentity> Dependencies);
    private readonly IReadOnlyList<Input> _inputs;
    private readonly IReadOnlyList<Input> _framework;
    private NativeManagedReferenceClosure(IReadOnlyList<Input> inputs, IReadOnlyList<Input> framework, ReferenceSet references)
    {
        _inputs = inputs; _framework = framework;
        References = new ReferenceSet(references.ManifestPath, Array.AsReadOnly(references.References.ToArray()));
    }
    internal ReferenceSet References { get; }
    internal bool IsEmpty => _inputs.Count == 0;

    internal static NativeManagedReferenceClosure Capture(string directory, IReadOnlyList<string> paths, string? wrapperName, string? sdkPath = null)
    {
        try
        {
            var references = DotNetReferences.ResolveNativeExplicit(directory, paths);
            var inputs = new List<Input>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in references.References)
            {
                byte[] image = File.ReadAllBytes(reference.Path);
                using var pe = new PEReader(new MemoryStream(image, writable: false));
                var header = pe.PEHeaders;
                var cor = header.CorHeader;
                if (!pe.HasMetadata || cor is null || header.CoffHeader.Machine != Machine.I386 ||
                    (header.CoffHeader.Characteristics & Characteristics.Dll) == 0 ||
                    (cor.Flags & CorFlags.ILOnly) == 0 || (cor.Flags & (CorFlags.Requires32Bit | CorFlags.Prefers32Bit | CorFlags.NativeEntryPoint)) != 0 ||
                    cor.ManagedNativeHeaderDirectory.Size != 0)
                    throw Refuse(reference.Path, "requires a pure AnyCPU managed implementation DLL");
                var metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly) throw Refuse(reference.Path, "is not an assembly");
                var identity = NativeAssemblyIdentity.Definition(metadata);
                if (!names.Add(identity.Name)) throw Refuse(reference.Path, $"duplicates assembly name '{identity.Name}'");
                if (identity.Culture != "" || identity.Flags != 0 || !NativeReferenceFileName.IsSupported(identity.Name))
                    throw Refuse(reference.Path, "has an unsupported culture, content type, or file name");
                if (identity.Name.Equals("Guest", StringComparison.OrdinalIgnoreCase) ||
                    identity.Name.Equals("SharpTS", StringComparison.OrdinalIgnoreCase) ||
                    identity.Name.StartsWith("SharpTS.", StringComparison.OrdinalIgnoreCase) ||
                    identity.Name.Equals(wrapperName ?? "NativeApp", StringComparison.OrdinalIgnoreCase) ||
                    NativeGuestAssemblyClosure.IsFrameworkName(identity.Name))
                    throw Refuse(reference.Path, $"shadows reserved assembly '{identity.Name}'");
                bool target = false;
                foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(handle);
                    string? name = AttributeName(metadata, attribute.Constructor);
                    if (name == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute")
                        throw Refuse(reference.Path, "is a compile-only reference assembly; supply the executable implementation");
                    if (name == "System.Runtime.Versioning.TargetFrameworkAttribute")
                    {
                        var value = metadata.GetBlobReader(attribute.Value);
                        target = value.ReadUInt16() == 1 && value.ReadSerializedString() == ".NETCoreApp,Version=v10.0";
                    }
                }
                if (!target) throw Refuse(reference.Path, "requires a net10.0 implementation with its TargetFrameworkAttribute");
                if (metadata.AssemblyFiles.Count != 0 || metadata.ManifestResources.Count != 0 ||
                    metadata.GetTableRowCount(System.Reflection.Metadata.Ecma335.TableIndex.ModuleRef) != 0 ||
                    metadata.MethodDefinitions.Any(handle => (metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) != 0 ||
                        (metadata.GetMethodDefinition(handle).ImplAttributes & MethodImplAttributes.InternalCall) != 0))
                    throw Refuse(reference.Path, "contains unproved module, native, or resource assets");
                inputs.Add(new(reference.Path, identity, image, NativeAssemblyIdentity.References(metadata)));
            }
            var framework = new List<Input>();
            if (inputs.Count != 0)
            {
                string pack = sdkPath ?? SdkResolver.FindReferenceAssembliesPath()
                    ?? throw Refuse("framework", "a net10 reference pack is required for metadata-only native interop");
                foreach (string path in Directory.GetFiles(pack, "*.dll").Order(StringComparer.Ordinal))
                {
                    byte[] image = File.ReadAllBytes(path);
                    using var pe = new PEReader(new MemoryStream(image, writable: false));
                    var identity = NativeAssemblyIdentity.Definition(pe.GetMetadataReader());
                    if (!NativeGuestAssemblyClosure.IsFrameworkReference(identity))
                        throw Refuse(path, "is not an exact supported framework metadata identity");
                    framework.Add(new(path, identity, image, []));
                }
                if (framework.Count == 0) throw Refuse(pack, "contains no proved framework metadata images");
                if (inputs.Any(input => framework.Any(known => string.Equals(input.Identity.Name, known.Identity.Name, StringComparison.OrdinalIgnoreCase))))
                    throw Refuse("explicit references", "shadows a reserved framework assembly name");
            }
            var closure = new NativeManagedReferenceClosure(inputs.AsReadOnly(), framework.AsReadOnly(), references);
            foreach (var input in inputs) closure.Validate(input.Dependencies, input.Path);
            return closure;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or System.Security.SecurityException)
        { throw Refuse("explicit references", error.Message); }
    }

    internal void Validate(IEnumerable<NativeAssemblyIdentity> dependencies, string source)
    {
        foreach (var identity in dependencies)
            if (!NativeGuestAssemblyClosure.IsFrameworkReference(identity) && !_inputs.Any(input => input.Identity == identity))
                throw Refuse(source, $"dependency '{identity}' has no exact explicitly supplied implementation; supply the complete managed DLL graph");
    }

    internal AssemblyReferenceLoader CreateLoader()
    {
        // Virtual canonical paths let the byte resolver match actual assembly names
        // even when the caller's implementation DLL has a different file name.
        string Key(Input input) => Path.Combine(Path.GetTempPath(), "sharpts-native-metadata", input.Identity.Name + ".dll");
        var images = AssemblyReferenceLoader.CapturedAssemblySet.FromImages(_inputs.Concat(_framework).Select(input =>
            new KeyValuePair<string, byte[]>(Key(input), input.Image)));
        var loader = new AssemblyReferenceLoader(_inputs.Select(Key), images);
        if (loader.MetadataFailures.Count != 0) { loader.Dispose(); throw Refuse("metadata", string.Join("; ", loader.MetadataFailures)); }
        return loader;
    }

    internal IEnumerable<(string Name, string Identity, string Sha256)> FrameworkReferences => _framework.Select(input =>
        (input.Identity.Name, input.Identity.ToString(), Convert.ToHexStringLower(SHA256.HashData(input.Image))));

    internal ReferenceSet ExportedReferences(string directory) => new(null, _inputs.Select(input =>
        new ResolvedReference(Path.Combine(directory, "references", input.Identity.Name + ".dll"), ReferenceOrigin.Cli)).ToArray());

    internal IReadOnlyList<NativeReferenceManifest.Entry> Export(string directory)
    {
        var entries = new List<NativeReferenceManifest.Entry>();
        foreach (var input in _inputs.OrderBy(input => input.Identity.Name, StringComparer.Ordinal))
        {
            string relative = "references/" + input.Identity.Name + ".dll";
            string path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(input.Image);
            entries.Add(new(input.Identity.Name, relative, Convert.ToHexStringLower(SHA256.HashData(input.Image)), input.Path,
                input.Identity.ToString(), input.Dependencies.Select(dependency => dependency.ToString()).ToArray()));
        }
        return entries.AsReadOnly();
    }

    private static string? AttributeName(MetadataReader metadata, EntityHandle constructor)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        if (type.Kind == HandleKind.TypeReference)
        {
            var value = metadata.GetTypeReference((TypeReferenceHandle)type);
            if (value.ResolutionScope.Kind == HandleKind.AssemblyReference &&
                NativeGuestAssemblyClosure.IsFrameworkReference(NativeAssemblyIdentity.Reference(metadata, (AssemblyReferenceHandle)value.ResolutionScope)))
                return metadata.GetString(value.Namespace) + "." + metadata.GetString(value.Name);
        }
        return null;
    }
    internal static CompileException Refuse(string source, string reason) => new(DiagnosticCode.NativeGuestExternalReferences,
        $"Native managed reference '{source}' {reason}. Only a complete explicit net10.0 AnyCPU implementation-DLL closure is supported; use --target dll or --target exe for other reference cohorts.");
}

internal sealed record NativeAssemblyIdentity(string Name, Version Version, string Culture, string Token, AssemblyFlags Flags)
{
    internal static NativeAssemblyIdentity Definition(MetadataReader metadata)
    {
        var value = metadata.GetAssemblyDefinition();
        // Reference-pack definitions use the definition-only processor architecture
        // bits (0x70). CLR AssemblyName identity excludes them; AssemblyRef flags do not.
        return Read(metadata.GetString(value.Name), value.Version, metadata.GetString(value.Culture), metadata.GetBlobBytes(value.PublicKey),
            (value.Flags & ~(AssemblyFlags)0x70) | AssemblyFlags.PublicKey);
    }
    internal static IReadOnlyList<NativeAssemblyIdentity> References(MetadataReader metadata) =>
        metadata.AssemblyReferences.Select(handle => Reference(metadata, handle)).ToArray();
    internal static NativeAssemblyIdentity Reference(MetadataReader metadata, AssemblyReferenceHandle handle)
    {
        var value = metadata.GetAssemblyReference(handle);
        return Read(metadata.GetString(value.Name), value.Version, metadata.GetString(value.Culture), metadata.GetBlobBytes(value.PublicKeyOrToken), value.Flags);
    }
    private static NativeAssemblyIdentity Read(string name, Version version, string culture, byte[] key, AssemblyFlags flags)
    {
        if ((flags & AssemblyFlags.PublicKey) != 0 && key.Length != 0)
        { var assembly = new AssemblyName(); assembly.SetPublicKey(key); key = assembly.GetPublicKeyToken() ?? []; }
        return new(name, version, culture, Convert.ToHexStringLower(key), flags & ~AssemblyFlags.PublicKey);
    }
    public override string ToString() => $"{Name}, Version={Version}, Culture={(Culture.Length == 0 ? "neutral" : Culture)}, PublicKeyToken={(Token.Length == 0 ? "null" : Token)}, Flags={(int)Flags}";
}
