using Xunit;

namespace SharpTS.Conformance;

public class ConformanceInputsTests
{
    [Fact]
    public void MissingCorpus_FailsWithSetupInstructions()
        => Assert.Contains("git submodule update --init external/test262",
            Assert.Throws<InvalidOperationException>(() => ConformanceInputs.RequireRoot(null, "test262")).Message);

    [Fact]
    public void EmptySelection_FailsInsteadOfReportingSuccess()
        => Assert.Throws<InvalidOperationException>(() => ConformanceInputs.RequireCases(0));

    [Fact]
    public void MissingBaseline_DoesNotCreateANewOne()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-baseline-{Guid.NewGuid():N}.txt");
        Assert.Throws<FileNotFoundException>(() => ConformanceInputs.RequireBaseline(path));
        Assert.False(File.Exists(path));
    }
}
