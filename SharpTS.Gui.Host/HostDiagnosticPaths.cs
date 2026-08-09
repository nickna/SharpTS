using System.Reflection;

namespace SharpTS.Gui.Host;

internal static class HostDiagnosticPaths
{
    public const int RetainedDefaultTraceCount = 20;
    public const int RetainedDefaultErrorCount = 10;

    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SharpTS.Gui");

    public static string TraceDirectory => Path.Combine(RootDirectory, "Traces");

    public static string ErrorDirectory => Path.Combine(RootDirectory, "Errors");

    public static string CreateTracePath(GuestMode mode) => Path.Combine(
        TraceDirectory,
        $"sharpts-gui-host-{SafeName(GetApplicationName())}-{mode.ToString().ToLowerInvariant()}-" +
        $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.json");

    public static string CreateErrorPath(string directory, string applicationName) => Path.Combine(
        directory,
        $"sharpts-gui-error-{SafeName(applicationName)}-" +
        $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");

    public static string GetApplicationName() =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? "application";

    public static void Prune(string directory, string searchPattern, int retainedCount)
    {
        if (retainedCount < 1 || !Directory.Exists(directory))
            return;

        try
        {
            FileInfo[] files = new DirectoryInfo(directory)
                .EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .Skip(retainedCount)
                .ToArray();
            foreach (FileInfo file in files)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Retention is best-effort diagnostics housekeeping.
                }
            }
        }
        catch
        {
            // A successful diagnostic write is still useful if enumeration is unavailable.
        }
    }

    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) ? '-' : character));
    }
}
