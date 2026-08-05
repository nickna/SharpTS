using System.Text.RegularExpressions;
using SharpTS.Diagnostics;
using Xunit;

namespace SharpTS.Tests.Architecture;

/// <summary>
/// Source-level architecture guards for the Native AOT reflection seams (#1324).
/// The analyzer ratchet pins the warning inventory at zero, but it is satisfied
/// by any suppression — these tests make the seam routing itself a build
/// failure, so a new raw emit-path generic instantiation (which would throw
/// PlatformNotSupportedException in the native compiler) or a resurrected
/// CustomAttributeBuilder call cannot land by copy-pasting a suppression.
/// </summary>
public class AotSeamArchitectureTests
{
    // Implementation files that legitimately contain the raw calls the rest of
    // the codebase must route through them.
    private static readonly string[] MakeGenericImplementationFiles =
    [
        Path.Combine("Compilation", "EmitGenerics.cs"),
        Path.Combine("Runtime", "DotNet", "ManagedDotNetInterop.cs"),
    ];

    // Qualifiers that route through a seam: EmitGenerics (emit path, with the
    // TypeBuilderInstantiation/MethodBuilderInstantiation AOT fallbacks),
    // TypeProvider instances (conventionally `_types`, pure delegation to
    // EmitGenerics), and ManagedDotNetInterop (interpreter open-world binder,
    // guarded by IsDynamicCodeSupported).
    private static readonly Regex RoutedMakeGeneric =
        new(@"\b(EmitGenerics|_types|ManagedDotNetInterop)\.MakeGeneric(Type|Method)\(", RegexOptions.Compiled);

    private static readonly Regex AnyMakeGeneric =
        new(@"\.MakeGeneric(Type|Method)\(", RegexOptions.Compiled);

    private static readonly Regex CustomAttributeBuilderUse =
        new(@"\bnew\s+CustomAttributeBuilder\b|\bCustomAttributeBuilder\s+\w|\(CustomAttributeBuilder\b", RegexOptions.Compiled);

    [Fact]
    public void MakeGeneric_calls_route_through_a_seam()
    {
        var offenders = new List<string>();
        foreach (var (file, relative) in EnumerateProductSources())
        {
            bool isImplementationFile = MakeGenericImplementationFiles.Any(impl =>
                relative.Equals(impl, StringComparison.OrdinalIgnoreCase));
            if (isImplementationFile)
                continue;

            foreach (var (line, number) in CodeLines(file))
            {
                if (!AnyMakeGeneric.IsMatch(line))
                    continue;
                if (RoutedMakeGeneric.IsMatch(line))
                    continue;
                offenders.Add($"{relative}:{number}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Raw MakeGenericType/MakeGenericMethod call sites found. Route them through " +
            "EmitGenerics (emit path) or ManagedDotNetInterop (interpreter interop) so the " +
            "Native AOT fallback/guard applies:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void CustomAttributeBuilder_stays_retired()
    {
        // CustomAttributeBuilder cannot run in the Native AOT compiler; every emit
        // site was converted to CustomAttributeEncoder's raw ECMA-335 blobs (#1324
        // Phase 2). Non-comment references may not come back.
        var offenders = new List<string>();
        foreach (var (file, relative) in EnumerateProductSources())
        {
            foreach (var (line, number) in CodeLines(file))
            {
                if (CustomAttributeBuilderUse.IsMatch(line))
                    offenders.Add($"{relative}:{number}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "CustomAttributeBuilder usage found; emit attribute blobs through " +
            "CustomAttributeEncoder instead:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void NativeAot_workflows_use_the_stable_managed_build_diagnostic_guard()
    {
        string root = FindRepoRoot();
        string guardPath = Path.Combine(root, "scripts", "assert-managed-build-required.sh");
        string guard = File.ReadAllText(guardPath);
        Assert.Contains(DiagnosticCode.ManagedBuildRequired.ToSharpTSCode(), guard);

        foreach (string workflowName in new[] { "ci.yml", "publish.yml" })
        {
            string workflow = File.ReadAllText(
                Path.Combine(root, ".github", "workflows", workflowName));
            Assert.Contains("scripts/assert-managed-build-required.sh", workflow);
            Assert.DoesNotContain(
                "child_process.fork in compiled output is not available", workflow);
        }
    }

    /// <summary>
    /// Enumerates the .cs sources compiled into SharpTS.dll: every source
    /// directory under the repo root except test/benchmark/tool projects,
    /// build output, and vendored/scratch trees.
    /// </summary>
    private static IEnumerable<(string FullPath, string Relative)> EnumerateProductSources()
    {
        string root = FindRepoRoot();
        string[] excludedTopLevel =
        [
            "SharpTS.Tests", "SharpTS.Test262", "SharpTS.Test262.Worker",
            "SharpTS.TypeScriptConformance", "SharpTS.Microbenchmarks",
            "SharpTS.Sdk", "SharpTS.Sdk.Tasks", "SharpTS.LanguageServer",
            "Examples", "benchmarks", "stdlib", "docs", "scripts", "external",
        ];

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments[0].StartsWith('.'))
                continue; // .git, .codex, .claude worktrees
            if (excludedTopLevel.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                continue;
            if (segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
                segments.Contains("obj", StringComparer.OrdinalIgnoreCase))
                continue;

            yield return (file, relative);
        }
    }

    /// <summary>Yields non-comment source lines with their 1-based line numbers.</summary>
    private static IEnumerable<(string Line, int Number)> CodeLines(string file)
    {
        int number = 0;
        foreach (var line in File.ReadLines(file))
        {
            number++;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            yield return (line, number);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpTS.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (SharpTS.csproj) above " + AppContext.BaseDirectory);
    }
}
