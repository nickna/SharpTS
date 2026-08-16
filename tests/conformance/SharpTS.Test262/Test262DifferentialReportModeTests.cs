using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

public sealed class Test262DifferentialReportModeTests
{
    private readonly ITestOutputHelper _output;

    public Test262DifferentialReportModeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Generate_from_committed_baselines()
    {
        if (!Test262ReportMode.IsEnabled()) return;

        var projectDirectory = Test262Paths.TryFindProjectDir();
        Assert.NotNull(projectDirectory);
        var reportPath = Test262ReportMode.Generate(projectDirectory);
        _output.WriteLine($"wrote {reportPath}");
    }
}
