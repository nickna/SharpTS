#pragma warning disable SHARPTS_HOSTING001

using SharpTS.Hosting;
using System.Reflection;
using System.Text.Json;

namespace SharpTS.Gui.Host;

internal sealed record GuiAppManifest(
    string EntryPath,
    string CompiledAssembly,
    int HostedAbiVersion,
    int GuiApiVersion);

internal static class GuiPayloadLoader
{
    public static GuiAppManifest LoadFile(string baseDirectory)
    {
        string path = Path.Combine(baseDirectory, ".sharpts", "app.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"SharpTS GUI application manifest is missing: {path}");
        GuiAppManifest manifest = JsonSerializer.Deserialize<GuiAppManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"SharpTS GUI application manifest is invalid: {path}");
        ValidateAbi(manifest);
        return manifest;
    }

    public static GuiAppManifest LoadEmbedded(Assembly payloadAssembly)
    {
        using Stream stream = payloadAssembly.GetManifestResourceStream("SharpTS.Gui.App.json")
            ?? throw new InvalidOperationException("The embedded SharpTS GUI application manifest is missing.");
        GuiAppManifest manifest = JsonSerializer.Deserialize<GuiAppManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
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
    }
}
