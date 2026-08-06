namespace SharpTS.Test262;

/// <summary>
/// Locates the Test262 submodule checkout. The checkout lives at
/// <c>external/test262/</c> relative to the repo root, which is a few levels
/// above the test assembly's bin directory.
/// </summary>
public static class Test262Paths
{
    public static string GetCorpusRevision(string root)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git to resolve the Test262 corpus revision.");
        var revision = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || revision.Length != 40 || revision.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException($"Could not resolve the Test262 corpus revision: {error}");
        return revision.ToLowerInvariant();
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for
    /// <c>external/test262/harness/sta.js</c>. Returns null when the submodule
    /// hasn't been initialized — callers should skip rather than fail.
    /// </summary>
    public static string? TryFindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "external", "test262");
            if (File.Exists(Path.Combine(candidate, "harness", "sta.js")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static string HarnessDir(string root) => Path.Combine(root, "harness");
    public static string TestDir(string root) => Path.Combine(root, "test");

    /// <summary>
    /// Locates the <c>SharpTS.Test262/</c> project source directory — the place
    /// where <c>config/</c> and <c>baselines/</c> live. Required so tests read
    /// the live files (and can rewrite baselines with <c>UPDATE_BASELINE=1</c>)
    /// instead of stale copies in <c>bin/</c>.
    /// </summary>
    public static string? TryFindProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SharpTS.Test262");
            if (File.Exists(Path.Combine(candidate, "SharpTS.Test262.csproj")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Locates the built worker assembly (<c>SharpTS.Test262.Worker.dll</c>) used
    /// for batched subprocess execution (issue #109). Walks up from the test
    /// assembly's bin directory looking for the sibling worker project's bin.
    /// Returns null when the worker hasn't been built — callers fall back to
    /// in-process execution.
    /// </summary>
    public static string? TryFindWorkerDll()
    {
        // Test bin is at .../SharpTS.Test262/bin/<config>/<tfm>/.
        // Worker bin mirrors at .../SharpTS.Test262.Worker/bin/<config>/<tfm>/.
        var testBin = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = testBin.Name;
        var configuration = testBin.Parent?.Name;
        var bin = testBin.Parent?.Parent;
        var projectDir = bin?.Parent;
        var repoRoot = projectDir?.Parent;
        if (repoRoot is null || configuration is null) return null;

        var candidate = Path.Combine(
            repoRoot.FullName, "SharpTS.Test262.Worker", "bin", configuration, tfm,
            "SharpTS.Test262.Worker.dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
