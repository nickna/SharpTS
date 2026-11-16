using Xunit;

namespace SharpTS.Tests.Infrastructure;

public class DeploymentDataTests
{
    [Fact]
    public void ChangedMethodsAndDataProvidersExpandOnlyExactMatches()
    {
        Assert.True(DeploymentDataAttribute.IsAffected("Case", "Provider", "Other|Case", null));
        Assert.True(DeploymentDataAttribute.IsAffected("Case", "Provider", null, "Provider"));
        Assert.False(DeploymentDataAttribute.IsAffected("Case", "Provider", "CaseLonger", "ProviderLonger"));
        Assert.False(DeploymentDataAttribute.IsAffected("Case", null, null, null));
    }

    [Fact]
    public void PrRetainsTwoRowsOfEveryModeAndFullRetainsTheOriginalInventory()
    {
        object[][] rows = (from hosted in new[] { false, true }
                           from mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
                           from example in Enumerable.Range(0, 5)
                           select new object[] { $"case-{example}", hosted, mode }).ToArray();
        Assert.Equal(rows, DeploymentDataAttribute.SelectRows(rows, "Full"));
        var pr = DeploymentDataAttribute.SelectRows(rows, "Pr").ToArray();
        Assert.Equal(8, pr.Length);
        foreach (var group in rows.GroupBy(row => (row[1], row[2])))
            Assert.Equal(group.Take(2), pr.Where(row => Equals(row[1], group.Key.Item1) && Equals(row[2], group.Key.Item2)));
    }

    [Fact]
    public void SmallInventoriesAndDuplicateRowsArePreserved()
    {
        object[] row = ["only"];
        Assert.Equal(new[] { row, row }, DeploymentDataAttribute.SelectRows([row, row], "Pr"));
    }

    [Fact]
    public void UnknownCoverageFailsInsteadOfSilentlySelectingASubset()
        => Assert.Throws<ArgumentException>(() => DeploymentDataAttribute.SelectRows([["case"]], "Typo").ToArray());
}
