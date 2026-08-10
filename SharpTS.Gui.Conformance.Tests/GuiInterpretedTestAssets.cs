namespace SharpTS.Gui.Conformance.Tests;

internal static class GuiInterpretedTestAssets
{
    public static void Stage(string repositoryRoot, string configuration, string destinationRoot)
    {
        string conformanceRoot = Path.Combine(
            repositoryRoot,
            "SharpTS.Gui.Conformance.Tests",
            "obj",
            configuration,
            "net10.0",
            ".sharpts-gui-conformance");
        string configDirectory = Path.Combine(destinationRoot, ".sharpts");
        Directory.CreateDirectory(configDirectory);
        File.Copy(
            Path.Combine(conformanceRoot, "tsconfig.json"),
            Path.Combine(configDirectory, "tsconfig.json"),
            overwrite: true);
        File.Copy(
            Path.Combine(repositoryRoot, "SharpTS.Gui.Conformance.Tests", "GuiConformanceApp.json"),
            Path.Combine(configDirectory, "app.json"),
            overwrite: true);
        CopyDirectory(
            Path.Combine(conformanceRoot, "node_modules", "@sharpts", "gui"),
            Path.Combine(configDirectory, "node_modules", "@sharpts", "gui"));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
