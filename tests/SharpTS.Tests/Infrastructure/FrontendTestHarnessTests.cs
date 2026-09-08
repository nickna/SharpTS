using Xunit;
using Xunit.Sdk;

namespace SharpTS.Tests.Infrastructure;

public class FrontendTestHarnessTests
{
    [Theory]
    [InlineData("const value: number = 1;", true)]
    [InlineData("const value: number = 'wrong';", false)]
    [InlineData("const value = ;", false)]
    [InlineData("const value = 123_;", false)]
    public void Check_DistinguishesSuccessFromDiagnostics(string source, bool success)
        => Assert.Equal(success, FrontendTestHarness.Check(source).IsSuccess);

    [Fact]
    public void InternalFailure_IsNotAcceptedAsADiagnostic()
    {
        var error = Assert.Throws<TrueException>(() => FrontendTestHarness.Check("", mode: "crash"));
        Assert.Contains("injected frontend defect", error.Message);
    }

    [Fact]
    public void Timeout_TerminatesWorkerAndAllowsNextProbe()
    {
        Assert.Throws<TimeoutException>(() => FrontendTestHarness.Check("", 500, "hang"));
        Assert.True(FrontendTestHarness.Check("const after = 1;").IsSuccess);
    }
}
