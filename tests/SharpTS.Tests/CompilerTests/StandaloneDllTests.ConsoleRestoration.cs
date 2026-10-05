using System.Diagnostics;
using System.Reflection;
using SharpTS.ConsoleRestorationFixture;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Isolated_Issue1930ConsoleRestoration_UsesFreshTestModule(int process)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(ConsoleRestorationFixtureMarker).Assembly.Location);
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        var result = TestProcess.Run(start, TimeSpan.FromSeconds(30), $"Console contract fresh process {process}");
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Equal("console restoration controls passed\n", result.StandardOutput.Replace("\r\n", "\n"));
    }
}
