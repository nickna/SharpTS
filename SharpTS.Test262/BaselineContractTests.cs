using Xunit;

namespace SharpTS.Test262;

public class BaselineContractTests
{
    [Fact]
    public void Header_IsVersionedAndPinsCorpus()
    {
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        Assert.StartsWith(
            "# SharpTS baseline-format=1 suite=Test262 corpus=" + revision + " — ",
            Test262Baseline.Header(revision));
    }

    [Fact]
    public void Header_RejectsMalformedCorpusRevision()
    {
        Assert.Throws<ArgumentException>(() => Test262Baseline.Header("not-a-revision"));
    }

    [Theory]
    [InlineData("Pass", Test262Outcome.Pass)]
    [InlineData("Skipped:skip-feature:Proxy", Test262Outcome.Skipped)]
    [InlineData("unknown", Test262Outcome.RuntimeError)]
    public void Bucket_ParsesOutcome(string bucket, Test262Outcome expected)
    {
        Assert.Equal(expected, Test262Bucket.ParseOutcome(bucket));
    }
}
