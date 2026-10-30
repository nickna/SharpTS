using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PEPacker;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>Checks actual saved AssemblyRefs against the exact framework and captured DLL graph.</summary>
internal static class NativeGuestAssemblyClosure
{
    internal static void ValidateImage(byte[] image, NativeManagedReferenceClosure? closure = null)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var references = NativeAssemblyIdentity.References(pe.GetMetadataReader());
        if (closure is not null) { closure.Validate(references, "saved Guest.dll"); return; }
        foreach (var reference in references)
            if (!IsFrameworkReference(reference))
                throw new CompileException(DiagnosticCode.NativeGuestExternalReferences,
                    $"Experimental native guest output references '{reference}', which is not in the exported framework dependency closure. " +
                    "Supply the complete explicit managed implementation DLL graph or use --target dll or --target exe.");
    }

    internal static bool IsFrameworkName(string name) =>
        EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(name, out _) ||
        new[] { typeof(object).Assembly.GetName().Name, typeof(Uri).Assembly.GetName().Name }
            .Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

    internal static bool IsFrameworkReference(NativeAssemblyIdentity reference)
    {
        if (EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(reference.Name, out var identity) &&
            reference == new NativeAssemblyIdentity(identity.Name, identity.Version, identity.CultureName,
                Convert.ToHexStringLower(identity.PublicKeyToken.AsSpan()), identity.Flags & ~AssemblyFlags.PublicKey)) return true;
        return new[] { typeof(object).Assembly.GetName(), typeof(Uri).Assembly.GetName() }.Any(identity =>
            reference == new NativeAssemblyIdentity(identity.Name!, identity.Version!, identity.CultureName ?? "",
                Convert.ToHexStringLower(identity.GetPublicKeyToken() ?? []), 0));
    }
}
