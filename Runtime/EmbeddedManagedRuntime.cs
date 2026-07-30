using System.Reflection;

namespace SharpTS.Runtime;

/// <summary>
/// Extracts the managed SharpTS runtime carried by the Native AOT SKU. The
/// payload is optional for ordinary developer builds and mandatory for release
/// Native AOT publishes; see <c>SharpTSManagedRuntimePayloadPath</c>.
/// </summary>
internal static class EmbeddedManagedRuntime
{
    internal const string ResourceName = "SharpTS.ManagedRuntime.dll";

    internal static bool TryExtractTo(string destinationPath, out string? error)
    {
        using Stream? payload = typeof(EmbeddedManagedRuntime).Assembly
            .GetManifestResourceStream(ResourceName);
        if (payload is null)
        {
            error = $"embedded resource '{ResourceName}' is not present";
            return false;
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        string? destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (destinationDirectory is null)
        {
            error = $"could not resolve the output directory for '{destinationPath}'";
            return false;
        }

        string temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                payload.CopyTo(destination);
            }

            File.Move(temporaryPath, fullDestination, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            try { File.Delete(temporaryPath); } catch { }
            error = ex.Message;
            return false;
        }
    }
}
