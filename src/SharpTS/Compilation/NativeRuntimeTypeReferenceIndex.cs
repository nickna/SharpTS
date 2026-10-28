using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PEPacker;

namespace SharpTS.Compilation;

/// <summary>The private RuntimeType literal is defined by CoreLib, never forwarded by System.Runtime.</summary>
internal sealed class NativeRuntimeTypeReferenceIndex : IReferenceAssemblyIndex
{
    private readonly IReferenceAssemblyIndex _inner;
    private readonly AssemblyIdentity _identity;

    internal NativeRuntimeTypeReferenceIndex(IReferenceAssemblyIndex inner, Type runtimeType, byte[] original)
    {
        if (runtimeType != typeof(object).GetType() || runtimeType.Assembly != typeof(object).Assembly ||
            runtimeType.FullName != "System.RuntimeType")
            throw new ArgumentException("An exact CoreLib RuntimeType is required.", nameof(runtimeType));
        var name = runtimeType.Assembly.GetName();
        byte[] token = name.GetPublicKeyToken()!;
        _identity = new(name.Name!, name.Version!, name.CultureName ?? "", ImmutableArray.Create(token), 0);
        _inner = inner;
        using var pe = new PEReader(new MemoryStream(original, writable: false));
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            if (reader.GetString(type.Name) != "RuntimeType" || reader.GetString(type.Namespace) != "System") continue;
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference)
                throw new InvalidOperationException("RuntimeType has an unproved assembly scope.");
            var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
            if (reader.GetString(assembly.Name) != name.Name || assembly.Version != name.Version ||
                reader.GetString(assembly.Culture) != (name.CultureName ?? "") ||
                !reader.GetBlobBytes(assembly.PublicKeyOrToken).AsSpan().SequenceEqual(
                    (assembly.Flags & AssemblyFlags.PublicKey) != 0 ? name.GetPublicKey()! : token))
                throw new InvalidOperationException("RuntimeType does not refer to the exact original CoreLib identity.");
        }
    }

    public bool TryResolveType(string name, [NotNullWhen(true)] out AssemblyIdentity? identity)
    {
        if (name == "System.RuntimeType") { identity = _identity; return true; }
        return _inner.TryResolveType(name, out identity);
    }

    public bool TryGetIdentity(string name, [NotNullWhen(true)] out AssemblyIdentity? identity)
    {
        if (name == _identity.Name) { identity = _identity; return true; }
        return _inner.TryGetIdentity(name, out identity);
    }
}
