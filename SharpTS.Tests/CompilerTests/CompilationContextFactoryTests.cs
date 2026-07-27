using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Architecture guard for the layered CompilationContext factories
/// (Compilation/ILCompiler.ContextFactories.cs): every production emission context must come
/// from a named factory, so module-top-level state (IsModuleTopLevel, #562) cannot leak into
/// function or state-machine contexts and new call sites cannot copy a stale 40-property
/// initializer.
/// </summary>
public class CompilationContextFactoryTests
{
    /// <summary>
    /// Files allowed to construct CompilationContext directly. Only the factory layer itself.
    /// If you need a new context shape, add a factory (or an Apply* overlay helper) to
    /// ILCompiler.ContextFactories.cs instead of constructing inline.
    /// </summary>
    private static readonly HashSet<string> ConstructionAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compilation/ILCompiler.ContextFactories.cs",
    };

    [Fact]
    public void CompilationContext_IsOnlyConstructedInTheFactoryLayer()
    {
        var repoRoot = FindRepoRoot();
        var compilationDir = Path.Combine(repoRoot, "Compilation");
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(compilationDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (ConstructionAllowlist.Contains(relative))
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                    continue;

                // Nested helper types (e.g. CompilationContext.ExtraScopeBinding) are fine —
                // only the context itself is factory-restricted.
                if (trimmed.Contains("new CompilationContext("))
                {
                    violations.Add($"{relative}:{i + 1}: {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} direct CompilationContext construction(s) outside the factory layer.\n" +
            "Create emission contexts via the factories in Compilation/ILCompiler.ContextFactories.cs " +
            "(CreateBaseCompilationContext / CreateModuleMemberContext / CreateModuleTopLevelContext / " +
            "CreateEntryPointTopLevelContext / ...) and apply scope-specific values as visible overlays.\n\n" +
            string.Join("\n", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "Compilation")) &&
                File.Exists(Path.Combine(dir, "SharpTS.csproj")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root");
    }
}
