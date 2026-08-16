namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Locates the repository root from the test bin directory. One implementation
/// replaces the byte-identical private copies in Build/Compiler/network tests
/// (2026-07 cleanup).
/// </summary>
public static class RepoPaths
{
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "SharpTS.sln")) &&
                File.Exists(Path.Combine(dir, ".gitmodules")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root");
    }
}
