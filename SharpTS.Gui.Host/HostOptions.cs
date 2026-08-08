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
    string TracePath,
    string? ValidateDepsDirectory);

internal static class HostOptionsParser
{
    public static HostOptions Parse(string[] args, GuestMode defaultMode)
    {
        GuestMode mode = defaultMode;
        bool autoClose = false;
        bool headless = false;
        string? tracePath = null;
        string? validateDeps = null;

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
                case "--trace" when i + 1 < args.Length:
                    tracePath = args[++i];
                    break;
                case "--validate-deps" when i + 1 < args.Length:
                    validateDeps = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[i]}'.");
            }
        }

        tracePath ??= Path.Combine(
            AppContext.BaseDirectory,
            $"sharpts-gui-{mode.ToString().ToLowerInvariant()}-trace.json");
        return new HostOptions(mode, autoClose, headless, tracePath, validateDeps);
    }

    public static bool ShouldShowFatalDialog(string[] args) =>
        !args.Contains("--headless", StringComparer.Ordinal) &&
        !args.Contains("--auto-close", StringComparer.Ordinal);
}
