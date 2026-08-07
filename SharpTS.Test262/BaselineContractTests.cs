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

    [Theory]
    [InlineData("Pass", true)]
    [InlineData("Fail", false)]
    [InlineData("Skipped:reason", false)]
    public void Bucket_IdentifiesPasses(string bucket, bool expected)
    {
        Assert.Equal(expected, Test262Bucket.IsPass(bucket));
    }

    [Theory]
    [InlineData("Skipped", true)]
    [InlineData("Skipped:skip-feature:Proxy", true)]
    [InlineData("Pass", false)]
    public void Bucket_IdentifiesSkips(string bucket, bool expected)
    {
        Assert.Equal(expected, Test262Bucket.IsSkipped(bucket));
    }

    [Fact]
    public void Baseline_reads_pinned_corpus_revision()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string revision = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
            File.WriteAllText(path, Test262Baseline.Header(revision) + "\na.js Pass\n");

            Assert.Equal(revision.ToLowerInvariant(), Test262Baseline.ReadCorpusRevision(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
