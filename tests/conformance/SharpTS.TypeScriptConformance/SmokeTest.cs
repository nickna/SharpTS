using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Scaffolding smoke test. Verifies the submodule is wired up and the path
/// helpers find the expected directories. Requires the pinned submodule;
/// use the harness-only filter for local builds without the corpus. The real
/// runner lands in issue #84.
/// </summary>
public class SmokeTest
{
    private readonly ITestOutputHelper _output;

    public SmokeTest(ITestOutputHelper output) => _output = output;

    [Trait("Category", "Corpus")]
    [Fact]
    public void Submodule_LayoutIsResolvable()
    {
        var root = TypeScriptConformancePaths.RequireRoot();

        _output.WriteLine($"TypeScript checkout: {root}");

        Assert.True(
            Directory.Exists(TypeScriptConformancePaths.ConformanceDir(root)),
            $"Expected conformance corpus at {TypeScriptConformancePaths.ConformanceDir(root)}");
        Assert.True(
            Directory.Exists(TypeScriptConformancePaths.BaselinesDir(root)),
            $"Expected baselines dir at {TypeScriptConformancePaths.BaselinesDir(root)}");
        Assert.True(
            Directory.Exists(TypeScriptConformancePaths.LibDir(root)),
            $"Expected lib dir at {TypeScriptConformancePaths.LibDir(root)}");
    }
}
