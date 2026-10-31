using Xunit;

namespace SharpTS.Test262;

public sealed class WorkerStandardErrorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(262144)]
    public async Task DrainsAllInputWhileRetainingBoundedFirstAndLastCharacters(int count)
    {
        string text = string.Concat(Enumerable.Range(0, count).Select(index => (char)('a' + index % 26)));
        using var reader = new StringReader(text);
        var result = await BatchedSubprocessRunner.CaptureWorkerStandardErrorAsync(reader);
        Assert.Equal(count, result.TotalCharacters);
        Assert.Equal(text[..Math.Min(16384, count)], result.First);
        Assert.Equal(count <= 16384 ? "" : text[Math.Max(16384, count - 16384)..], result.Last);
        Assert.Equal(count > 32768, result.Truncated);
        Assert.Null(result.ReadError);
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public async Task RetainsReadFailureAsDiagnosticData()
    {
        using var reader = new FailingReader();
        var result = await BatchedSubprocessRunner.CaptureWorkerStandardErrorAsync(reader);
        Assert.Equal(0, result.TotalCharacters);
        Assert.Empty(result.First);
        Assert.Empty(result.Last);
        Assert.False(result.Truncated);
        Assert.Equal("IOException: owned failure", result.ReadError);
    }

    private sealed class FailingReader : TextReader
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("owned failure"));
    }
}
