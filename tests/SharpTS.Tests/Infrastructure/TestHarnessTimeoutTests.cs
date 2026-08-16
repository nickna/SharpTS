using System.Diagnostics;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

[Collection("HarnessTimeoutTests")]
public class TestHarnessTimeoutTests
{
    [Fact]
    public void RunInterpreted_InfiniteLoop_TimesOutAndDoesNotPoisonNextRun()
    {
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() =>
            TestHarness.RunInterpreted("while (true) {}", TimeSpan.FromMilliseconds(100)));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(75), TimeSpan.FromSeconds(3));
        Assert.Equal("after\n", TestHarness.RunInterpreted("console.log('after');"));
    }

    [Fact]
    public void RunModulesInterpreted_InfiniteLoop_TimesOutAndDoesNotPoisonNextRun()
    {
        var files = new Dictionary<string, string> { ["./main.ts"] = "while (true) {}" };
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() => TestHarness.RunModules(
            files, "./main.ts", ExecutionMode.Interpreted, TimeSpan.FromMilliseconds(100)));

        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(75), TimeSpan.FromSeconds(3));
        Assert.Equal("after\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["./main.ts"] = "console.log('after');" },
            "./main.ts",
            ExecutionMode.Interpreted));
    }
}

[CollectionDefinition("HarnessTimeoutTests", DisableParallelization = true)]
public class HarnessTimeoutTestsCollection
{
}
