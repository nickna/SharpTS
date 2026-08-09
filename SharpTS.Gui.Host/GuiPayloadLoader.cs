#pragma warning disable SHARPTS_HOSTING001

using SharpTS.Hosting;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Gui.Host;

internal sealed record GuiAppManifest(
    string EntryPath,
    string CompiledAssembly,
    int HostedAbiVersion,
    int GuiApiVersion,
    int? DescriptorSchemaVersion,
    string? DescriptorSchemaHash);

internal static class GuiPayloadLoader
{
    public static GuiAppManifest LoadFile(string baseDirectory)
    {
        string path = Path.Combine(baseDirectory, ".sharpts", "app.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"SharpTS GUI application manifest is missing: {path}");
        GuiAppManifest manifest = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            GuiHostJsonContext.Default.GuiAppManifest)
            ?? throw new InvalidOperationException($"SharpTS GUI application manifest is invalid: {path}");
        ValidateAbi(manifest);
        return manifest;
    }

    public static GuiAppManifest LoadEmbedded(Assembly payloadAssembly)
    {
        using Stream stream = payloadAssembly.GetManifestResourceStream("SharpTS.Gui.App.json")
            ?? throw new InvalidOperationException("The embedded SharpTS GUI application manifest is missing.");
        GuiAppManifest manifest = JsonSerializer.Deserialize(
            stream,
            GuiHostJsonContext.Default.GuiAppManifest)
            ?? throw new InvalidOperationException("The embedded SharpTS GUI application manifest is invalid.");
        ValidateAbi(manifest);
        return manifest;
    }

    public static byte[] ReadEmbeddedResource(Assembly payloadAssembly, string resourceName)
    {
        using Stream stream = payloadAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded SharpTS GUI payload '{resourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static string ResolvePath(string baseDirectory, string relativePath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory))
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SharpTS GUI manifest path escapes the application: {relativePath}");
        return candidate;
    }

    private static void ValidateAbi(GuiAppManifest manifest)
    {
        if (manifest.HostedAbiVersion != SharpTSHostedAbi.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"SharpTS GUI host supports hosted ABI {SharpTSHostedAbi.CurrentVersion}; " +
                $"application requires ABI {manifest.HostedAbiVersion}.");
        }
        if (manifest.GuiApiVersion != DesktopBridge.GuiApiVersion)
        {
            string migration = manifest.GuiApiVersion == 1
                ? " GUI API 1 applications must migrate to API 2; see docs/gui/migrating-api-1-to-2.md."
                : string.Empty;
            throw new InvalidOperationException(
                $"SharpTS GUI host supports GUI API {DesktopBridge.GuiApiVersion}; " +
                $"application requires GUI API {manifest.GuiApiVersion}." + migration);
        }
        if (manifest.DescriptorSchemaVersion is null || string.IsNullOrWhiteSpace(manifest.DescriptorSchemaHash))
        {
            throw new InvalidOperationException(
                "SharpTS GUI API 2 application manifest is missing descriptor schema metadata; " +
                "rebuild the application with the current SharpTS.Gui.Sdk.");
        }
        if (manifest.DescriptorSchemaHash.Length != 64 ||
            manifest.DescriptorSchemaHash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                $"SharpTS GUI application descriptor schema hash is malformed: '{manifest.DescriptorSchemaHash}'.");
        }
        if (manifest.DescriptorSchemaVersion != DesktopBridge.DescriptorSchemaVersion ||
            !string.Equals(manifest.DescriptorSchemaHash, DesktopBridge.DescriptorSchemaHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SharpTS GUI descriptor schema mismatch: host version {DesktopBridge.DescriptorSchemaVersion} " +
                $"hash {DesktopBridge.DescriptorSchemaHash}; application version {manifest.DescriptorSchemaVersion} " +
                $"hash {manifest.DescriptorSchemaHash}.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GuiAppManifest))]
internal sealed partial class GuiHostJsonContext : JsonSerializerContext;
