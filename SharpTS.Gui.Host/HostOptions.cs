namespace SharpTS.Gui.Host;

internal enum GuestMode
{
    Interpreted,
    Compiled
}
internal sealed record HostOptions(
    GuestMode Mode,
    bool AutoClose,
    bool Headless,
    string? TracePath,
    bool IsTracePathHostManaged,
    string? ValidateDepsDirectory,
    bool ValidateCompiledOnly);

internal static class HostOptionsParser
{
    public static HostOptions Parse(string[] args, GuestMode defaultMode)
    {
        GuestMode mode = defaultMode;
        bool autoClose = false;
        bool headless = false;
        string? tracePath = null;
        bool traceRequested = false;
        bool explicitTracePath = false;
        string? validateDeps = null;
        bool validateCompiledOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    mode = args[++i].ToLowerInvariant() switch
                    {
                        "interpreted" => GuestMode.Interpreted,
                        "compiled" => GuestMode.Compiled,
                        var value => throw new ArgumentException(
                            $"--mode expects interpreted or compiled; got '{value}'.")
                    };
                    break;
                case "--auto-close":
                    autoClose = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--trace":
                    traceRequested = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        tracePath = args[++i];
                        explicitTracePath = true;
                    }
                    break;
                case "--validate-deps" when i + 1 < args.Length:
                    validateDeps = args[++i];
                    break;
                case "--validate-deps-compiled-only" when i + 1 < args.Length:
                    validateDeps = args[++i];
                    validateCompiledOnly = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[i]}'.");
            }
        }

        bool hostManagedTrace = (traceRequested || autoClose) && !explicitTracePath;
        if (hostManagedTrace)
            tracePath = HostDiagnosticPaths.CreateTracePath(mode);
        return new HostOptions(
            mode,
            autoClose,
            headless,
            tracePath,
            hostManagedTrace,
            validateDeps,
            validateCompiledOnly);
    }

    public static bool ShouldShowFatalDialog(string[] args) =>
        !args.Contains("--headless", StringComparer.Ordinal) &&
        !args.Contains("--auto-close", StringComparer.Ordinal);
}
