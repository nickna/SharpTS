using System.Diagnostics;
using System.Text;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void ProcessCapture_DecodesBomAndCharactersAcrossSingleByteReads(string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            "utf32le" => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            "utf32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        const string expected = "Aé😀中|\uFEFFtail";
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(expected)];
        using var stream = new BomPrefixReadStream(new SingleByteReadStream(bytes));
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);

        Assert.Equal(expected, reader.ReadToEnd());
    }

    [Theory]
    [InlineData("")]
    [InlineData("EF")]
    [InlineData("EFBB")]
    [InlineData("EF41")]
    [InlineData("FE")]
    [InlineData("FE41")]
    [InlineData("FF")]
    [InlineData("FFFE")]
    [InlineData("FFFE00")]
    [InlineData("00")]
    [InlineData("0000")]
    [InlineData("0000FE")]
    [InlineData("000041")]
    public void ProcessCapture_PreservesIncompleteAndNonBomPrefixesAtEof(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        using var reference = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true);
        using var stream = new BomPrefixReadStream(new SingleByteReadStream(bytes));
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);

        Assert.Equal(reference.ReadToEnd(), reader.ReadToEnd());
    }

    [Fact]
    public void ProcessCapture_ReturnsOrdinaryByteWithoutWaitingForMoreOutput()
    {
        using var stream = new BomPrefixReadStream(new SingleByteReadStream([(byte)'A'], failAfterBytes: true));
        byte[] buffer = new byte[1024];

        Assert.Equal(1, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal((byte)'A', buffer[0]);
    }

    private sealed class SingleByteReadStream(byte[] bytes, bool failAfterBytes = false) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (failAfterBytes && Position == Length)
                throw new IOException("Capture waited for output after a complete ordinary byte.");
            return base.Read(buffer, offset, Math.Min(count, 1));
        }
    }

    [Fact]
    public async Task Isolated_ProcessCapture_PreservesUtf8BomAcrossSplitReads()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        using var process = Process.Start(CaptureFixtureStart(directory.Path, "utf8-output"))!;
        using var cancellation = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        // Force every UTF-8/BOM sequence across native read boundaries on Linux.
        // Other platforms exercise their unchanged async capture/decoder on the same child.
        var stdout = new StandalonePipeCapture(process.StandardOutput, clock, cancellation.Token,
            linuxReadChunkBytes: 1);
        var stderr = new StandalonePipeCapture(process.StandardError, clock, cancellation.Token);
        try
        {
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(0, process.ExitCode);
            Assert.True(stdout.WaitForStop(5000));
            Assert.True(stderr.WaitForStop(5000));
            Assert.Equal("Aé😀中|tail", await stdout.Completion);
            Assert.Equal("utf8-stderr", await stderr.Completion);
            Assert.NotNull(stdout.Snapshot().Eof);
            Assert.Null(stdout.Snapshot().Canceled);
            Assert.Null(stdout.Snapshot().Fault);
        }
        finally
        {
            await cancellation.CancelAsync();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5000));
            Assert.True(stdout.WaitForStop(5000));
            Assert.True(stderr.WaitForStop(5000));
        }
    }

    [Fact]
    public async Task Isolated_ProcessCapture_CancellationCompletesWithoutEof()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        using var process = Process.Start(CaptureFixtureStart(directory.Path, "child"))!;
        using var cancellation = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var stdout = new StandalonePipeCapture(process.StandardOutput, clock, cancellation.Token);
        var stderr = new StandalonePipeCapture(process.StandardError, clock, cancellation.Token);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => stdout.Snapshot().ReadStarted is not null &&
                stderr.Snapshot().ReadStarted is not null, TimeSpan.FromSeconds(5)));
            Assert.False(process.HasExited);
            await cancellation.CancelAsync();
            foreach (var capture in new[] { stdout, stderr })
            {
                Assert.True(capture.WaitForStop(5000));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.Completion);
                Assert.NotNull(capture.Snapshot().Canceled);
                Assert.Null(capture.Snapshot().Eof);
                Assert.Null(capture.Snapshot().Fault);
                Assert.Equal(0, capture.Snapshot().CharacterCount);
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5000));
            Assert.True(stdout.WaitForStop(5000));
            Assert.True(stderr.WaitForStop(5000));
        }
    }
}
