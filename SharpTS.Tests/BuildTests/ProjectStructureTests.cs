using System.Text.RegularExpressions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.BuildTests;

/// <summary>
/// Guards the hand-maintained source-exclusion list in the root <c>SharpTS.csproj</c>.
///
/// The core library's project file sits at the repository root, so the SDK's default
/// <c>**/*.cs</c> glob would otherwise compile every sibling project's sources into
/// <c>SharpTS.dll</c>. A &lt;Compile Remove&gt; block keeps them out, but it must be
/// edited whenever a new top-level project is added. The build itself fails fast via the
/// <c>GuardAgainstForeignProjectSources</c> MSBuild target (see <c>SharpTS.csproj</c>);
/// this test is the cheap, CI-visible mirror of that invariant so a missing exclusion is
/// also surfaced as a plain test failure (issue #1144).
/// </summary>
public class ProjectStructureTests
{
    [Fact]
    public void EverySiblingProject_IsExcludedFromCoreCompilation()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var coreCsproj = Path.Combine(repoRoot, "SharpTS.csproj");
        var csprojText = File.ReadAllText(coreCsproj);

        // Directory prefixes the core explicitly removes, e.g. <Compile Remove="SharpTS.Tests/**" />.
        var excludedPrefixes = Regex.Matches(csprojText, """<Compile Remove="([^"]+?)/\*\*"\s*/>""")
            .Select(m => m.Groups[1].Value.Replace('\\', '/').TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every sibling project directory under the repo root (the core project, build
        // output, and the external submodules excluded — the submodules carry no .cs).
        // Dot-directories (.claude worktrees, .perf-probe scratch dirs, .git) are skipped
        // to mirror the SDK's DefaultItemExcludes (**/.*/**): the compiler never globs
        // sources under them, so a csproj there needs no <Compile Remove> entry — and
        // untracked working dirs must not turn this test red on dev machines (#1214).
        var siblingProjects = Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !string.Equals(p, coreCsproj, StringComparison.OrdinalIgnoreCase))
            .Where(p => !IsUnder(repoRoot, p, "external"))
            .Where(p => !HasSkippedSegment(repoRoot, p))
            .ToList();

        Assert.NotEmpty(siblingProjects); // sanity: discovery actually found the sibling projects

        var uncovered = new List<string>();
        foreach (var proj in siblingProjects)
        {
            var relDir = Path.GetRelativePath(repoRoot, Path.GetDirectoryName(proj)!)
                .Replace('\\', '/');
            bool covered = excludedPrefixes.Any(prefix =>
                relDir.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                relDir.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
            if (!covered)
                uncovered.Add(relDir);
        }

        Assert.True(uncovered.Count == 0,
            "These sibling project directories are not covered by a <Compile Remove> entry in " +
            "SharpTS.csproj, so their sources would compile into SharpTS.dll:\n  " +
            string.Join("\n  ", uncovered));
    }

    private static bool IsUnder(string repoRoot, string path, params string[] topLevelDirs)
    {
        var rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        return topLevelDirs.Any(d =>
            rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when any directory segment of the path is a dot-directory or a
    /// bin/obj output directory (at any depth — publish output can nest csproj
    /// copies under a project's own bin/).
    /// </summary>
    private static bool HasSkippedSegment(string repoRoot, string path)
    {
        var rel = Path.GetRelativePath(repoRoot, Path.GetDirectoryName(path)!);
        return rel.Split('/', '\\').Any(seg =>
            seg.StartsWith('.') ||
            seg.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            seg.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

}
