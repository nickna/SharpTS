using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PEPacker;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>Checks the actual saved guest's dependency identities against the exported framework graph.</summary>
internal static class NativeGuestAssemblyClosure
{
    internal static void ValidateImage(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var metadata = pe.GetMetadataReader();
        var framework = EmbeddedReferenceAssemblyIndex.Default;
        // Uri's implementation assembly is part of the runtime framework but
        // its public facade identity lives in System.Runtime. These literal
        // anchors prove the private identities used by the emitted runtime.
        var implementations = new[] { typeof(object).Assembly.GetName(), typeof(Uri).Assembly.GetName() };
        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            string name = metadata.GetString(reference.Name);
            string culture = metadata.GetString(reference.Culture);
            byte[] token = metadata.GetBlobBytes(reference.PublicKeyOrToken);
            bool validKey = true;
            if ((reference.Flags & AssemblyFlags.PublicKey) != 0)
            {
                // Reflection.Emit can retain a full framework public key. Compare
                // the CLR's canonical token, as the published reference index does.
                try
                {
                    var keyName = new AssemblyName(); keyName.SetPublicKey(token);
                    token = keyName.GetPublicKeyToken() ?? [];
                }
                catch (Exception error) when (error is ArgumentException or System.Security.SecurityException)
                { validKey = false; }
            }
            bool knownImplementation = validKey && implementations.Any(implementation =>
                name == implementation.Name && reference.Version == implementation.Version &&
                culture == (implementation.CultureName ?? "") && token.AsSpan().SequenceEqual(implementation.GetPublicKeyToken())) &&
                (reference.Flags & ~AssemblyFlags.PublicKey) == 0;
            bool knownFramework = validKey && framework.TryGetIdentity(name, out var identity) &&
                reference.Version == identity.Version && culture == identity.CultureName &&
                token.AsSpan().SequenceEqual(identity.PublicKeyToken.AsSpan()) &&
                (reference.Flags & ~AssemblyFlags.PublicKey) == (identity.Flags & ~AssemblyFlags.PublicKey);
            if (knownImplementation || knownFramework) continue;
            throw new CompileException(DiagnosticCode.NativeGuestExternalReferences,
                $"Experimental native guest output references '{name}, Version={reference.Version}', which is not in the exported framework dependency closure. " +
                "Third-party and implicit runtime library dependencies are not included by this profile. Use --target dll or --target exe.");
        }
    }
}
