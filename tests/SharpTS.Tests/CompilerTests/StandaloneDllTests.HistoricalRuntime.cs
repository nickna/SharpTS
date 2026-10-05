using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    // The shard runner partitions by method. Bound each method's serial work
    // while retaining every source and all standalone/hosted declaration paths.
    public static IEnumerable<object[]> HistoricalRuntimeDeploymentCases(int partition)
        => HistoricalRuntimeRegressionTests.Cases().Where((_, index) => index % 6 == partition);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 0)]
    public void Isolated_HistoricalRuntimeSources0(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 1)]
    public void Isolated_HistoricalRuntimeSources1(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 2)]
    public void Isolated_HistoricalRuntimeSources2(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 3)]
    public void Isolated_HistoricalRuntimeSources3(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 4)]
    public void Isolated_HistoricalRuntimeSources4(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);

    [Theory, MemberData(nameof(HistoricalRuntimeDeploymentCases), 5)]
    public void Isolated_HistoricalRuntimeSources5(string file, string source, string expected)
        => HistoricalRuntimeDeploymentTests.AssertDeployment(file, source, expected);
}
