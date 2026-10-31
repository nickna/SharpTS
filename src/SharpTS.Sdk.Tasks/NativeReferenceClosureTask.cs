using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpTS.Compilation;

namespace SharpTS.Sdk.Tasks;

/// <summary>Loads captured native references and verifies the SDK selected those exact bytes.</summary>
public sealed class NativeReferenceClosureTask : Task
{
    /// <summary>The compiler-owned exported project directory.</summary>
    [Required] public required string ProjectDirectory { get; set; }
    /// <summary>When supplied, validate the final SDK reference resolution.</summary>
    public ITaskItem[]? ResolvedReferences { get; set; }
    /// <summary>Require final resolution even when the SDK selected no references.</summary>
    public bool ValidateResolution { get; set; }
    /// <summary>Captured DLL references that replace the original HintPath inputs.</summary>
    [Output] public ITaskItem[] CapturedReferences { get; set; } = [];

    /// <inheritdoc/>
    public override bool Execute()
    {
        try
        {
            var entries = NativeReferenceManifest.ReadAndVerify(ProjectDirectory);
            CapturedReferences = entries.Select(entry =>
            {
                string captured = Path.GetFullPath(Path.Combine(ProjectDirectory, entry.Path));
                using var pe = new PEReader(File.OpenRead(captured));
                var metadata = pe.GetMetadataReader();
                var definition = metadata.GetAssemblyDefinition();
                var identity = new AssemblyName { Name = metadata.GetString(definition.Name), Version = definition.Version,
                    CultureName = metadata.GetString(definition.Culture) };
                identity.SetPublicKey(metadata.GetBlobBytes(definition.PublicKey));
                if (identity.Name != entry.Name) throw new InvalidDataException("Captured reference name no longer matches its manifest.");
                var item = new TaskItem(identity.FullName);
                item.SetMetadata("HintPath", captured);
                item.SetMetadata("Private", "true");
                item.SetMetadata("SpecificVersion", "true");
                item.SetMetadata("SharpTSNativeGuest", "true");
                return (ITaskItem)item;
            }).ToArray();
            if (!ValidateResolution) return true;
            var expected = entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ProjectDirectory, "native-project.json")));
            var framework = manifest.RootElement.GetProperty("frameworkReferences").EnumerateArray().ToDictionary(
                value => value.GetProperty("name").GetString()!, value => value.GetProperty("sha256").GetString()!, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool guest = false;
            foreach (var reference in ResolvedReferences ?? [])
            {
                using var pe = new PEReader(File.OpenRead(reference.ItemSpec));
                var metadata = pe.GetMetadataReader();
                string name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                if (expected.TryGetValue(name, out var entry))
                {
                    if (!seen.Add(name) || Hash(reference.ItemSpec) != entry.Sha256)
                        throw new InvalidDataException($"Resolved reference '{reference.ItemSpec}' does not match captured implementation '{entry.Identity}'.");
                }
                else if (name == "Guest")
                {
                    if (guest || Hash(reference.ItemSpec) != manifest.RootElement.GetProperty("guestSha256").GetString())
                        throw new InvalidDataException("Resolved Guest no longer matches the compiler-owned guest bytes.");
                    guest = true;
                }
                else if (framework.TryGetValue(name, out string? frameworkHash))
                {
                    if (Hash(reference.ItemSpec) != frameworkHash)
                        throw new InvalidDataException($"SDK framework reference '{reference.ItemSpec}' does not match the compiler's captured framework bytes.");
                }
                else if (entries.Count != 0 || reference.GetMetadata("FrameworkReferenceName") != "Microsoft.NETCore.App")
                    throw new InvalidDataException($"Resolved reference '{reference.ItemSpec}' is outside the captured native managed graph and SDK framework reference pack.");
            }
            if (!guest || seen.Count != expected.Count)
                throw new InvalidDataException("SDK resolution omitted a captured native managed reference or Guest.");
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or BadImageFormatException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
        {
            Log.LogError(null, "SHARPTS402", null, null, 0, 0, 0, 0, error.Message);
            return false;
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
