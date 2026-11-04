namespace SharpTS.Gui.Conformance.Tests;

internal static class GuiConformanceBuildPaths
{
    private static readonly IReadOnlyDictionary<string, string> Paths = File.ReadAllLines(
        Path.Combine(AppContext.BaseDirectory, "gui-conformance-build-paths.txt"))
        .Select(line => line.Split('=', 2))
        .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    public static string Compiler => Paths["Compiler"];
    public static string Bridge => Paths["Bridge"];
    public static string Support => Paths["Support"];
    public static string Generator => Paths["Generator"];
    public static string HostDirectory => Path.GetDirectoryName(Paths["Host"])!;
    public static string DesktopHostDirectory => Path.GetDirectoryName(Paths["DesktopHost"])!;
    public static string Assets => Paths["Assets"];
}
