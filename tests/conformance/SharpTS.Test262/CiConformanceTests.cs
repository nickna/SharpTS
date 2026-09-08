using SharpTS.Conformance;
using Xunit;

namespace SharpTS.Test262;

/// <summary>A bounded PR gate: independently assert each selected file passes in each mode.</summary>
[Trait("Category", "Corpus")]
[Trait("Category", "CiSmoke")]
public class CiConformanceTests
{
    public static IEnumerable<object[]> Cases => ConformanceInputs.ReadSmokeCases(
        Test262Paths.TryFindProjectDir() ?? throw new InvalidOperationException("Test262 project directory missing."))
        .SelectMany(path => new[] { new object[] { path, Test262ExecutionMode.Interpreted },
            new object[] { path, Test262ExecutionMode.Compiled } });

    [Theory]
    [MemberData(nameof(Cases))]
    public void SelectedCase_Passes(string path, Test262ExecutionMode mode)
    {
        string root = Test262Paths.RequireRoot();
        string file = Path.Combine(root, path);
        Assert.True(File.Exists(file), $"Missing pinned Test262 case: {path}");
        var result = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true).RunOne(file, mode);
        Assert.True(result.Outcome == Test262Outcome.Pass, $"{path} ({mode}): {result.Outcome}: {result.Message}");
    }
}
