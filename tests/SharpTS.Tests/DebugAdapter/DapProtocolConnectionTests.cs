using System.Text;
using System.Text.Json;
using SharpTS.DebugAdapter.Protocol;
using Xunit;

namespace SharpTS.Tests.DebugAdapter;

public sealed class DapProtocolConnectionTests
{
    [Fact]
    public async Task ReadsSplitUnicodeAndCoalescedMessages()
    {
        byte[] bytes = Frame("""{"seq":1,"type":"request","command":"evaluate","arguments":{"expression":"π + '😀'"}}""")
            .Concat(Frame("""{"seq":2,"type":"request","command":"threads"}"""))
            .ToArray();
        await using var input = new ChunkedReadStream(bytes, maximumChunk: 3);
        await using var output = new MemoryStream();
        await using var connection = new DapProtocolConnection(input, output);

        DapRequest first = Assert.IsType<DapRequest>(await connection.ReadRequestAsync(default));
        DapRequest second = Assert.IsType<DapRequest>(await connection.ReadRequestAsync(default));

        Assert.Equal(1, first.Sequence);
        Assert.Equal("evaluate", first.Command);
        Assert.Equal("π + '😀'", first.Arguments.GetProperty("expression").GetString());
        Assert.Equal(2, second.Sequence);
        Assert.Equal("threads", second.Command);
        Assert.Null(await connection.ReadRequestAsync(default));
    }

    [Theory]
    [InlineData("Content-Type: application/json\r\n\r\n{}", "missing Content-Length")]
    [InlineData("Content-Length: nope\r\n\r\n{}", "Content-Length must")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}", "duplicate Content-Length")]
    public async Task RejectsInvalidHeaders(string framed, string expected)
    {
        await using var input = new MemoryStream(Encoding.ASCII.GetBytes(framed));
        await using var output = new MemoryStream();
        await using var connection = new DapProtocolConnection(input, output);

        DapProtocolException exception = await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await connection.ReadRequestAsync(default));

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsInvalidJson()
    {
        await using var input = new MemoryStream(Frame("{not-json}"));
        await using var output = new MemoryStream();
        await using var connection = new DapProtocolConnection(input, output);

        DapProtocolException exception = await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await connection.ReadRequestAsync(default));

        Assert.Contains("Invalid DAP JSON", exception.Message);
    }

    [Fact]
    public async Task RejectsPayloadThatClosesEarly()
    {
        await using var input = new MemoryStream(
            Encoding.ASCII.GetBytes("Content-Length: 20\r\n\r\n{}"));
        await using var connection = new DapProtocolConnection(input, Stream.Null);

        DapProtocolException exception = await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await connection.ReadRequestAsync(default));

        Assert.Contains("middle of a payload", exception.Message);
    }

    [Fact]
    public async Task RejectsNonAsciiHeader()
    {
        byte[] header = Encoding.UTF8.GetBytes("Content-Léngth: 2\r\n\r\n{}");
        await using var connection = new DapProtocolConnection(
            new MemoryStream(header), Stream.Null);

        DapProtocolException exception = await Assert.ThrowsAsync<DapProtocolException>(async () =>
            await connection.ReadRequestAsync(default));

        Assert.Contains("not ASCII", exception.Message);
    }

    [Fact]
    public async Task WritesProtocolCleanResponseAndEvent()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        await using var connection = new DapProtocolConnection(input, output);
        var request = new DapRequest(7, "initialize", default);

        await connection.SendResponseAsync(request, true, new { supportsConfigurationDoneRequest = true });
        await connection.SendEventAsync("initialized");

        output.Position = 0;
        await using var reader = new DapProtocolConnection(output, Stream.Null);
        DapRequest? notARequest = null;
        // The production reader intentionally accepts client requests only. Verify framing and
        // JSON directly for server messages so an event can never be mistaken for log text.
        List<JsonElement> messages = await ReadServerMessagesAsync(output.ToArray());
        Assert.Equal(2, messages.Count);
        Assert.Equal("response", messages[0].GetProperty("type").GetString());
        Assert.Equal(7, messages[0].GetProperty("requestSeq").GetInt32());
        Assert.Equal("event", messages[1].GetProperty("type").GetString());
        Assert.Equal("initialized", messages[1].GetProperty("event").GetString());
        Assert.Null(notARequest);
    }

    internal static byte[] Frame(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        return Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n")
            .Concat(payload)
            .ToArray();
    }

    internal static async Task<List<JsonElement>> ReadServerMessagesAsync(byte[] bytes)
    {
        var messages = new List<JsonElement>();
        int offset = 0;
        while (offset < bytes.Length)
        {
            int delimiter = Find(bytes, offset, "\r\n\r\n"u8);
            Assert.True(delimiter >= 0);
            string header = Encoding.ASCII.GetString(bytes, offset, delimiter - offset);
            int length = int.Parse(header.Split(':', 2)[1]);
            int payloadStart = delimiter + 4;
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(payloadStart, length));
            messages.Add(document.RootElement.Clone());
            offset = payloadStart + length;
        }
        await Task.CompletedTask;
        return messages;
    }

    private static int Find(byte[] haystack, int start, ReadOnlySpan<byte> needle)
    {
        for (int index = start; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
                return index;
        }
        return -1;
    }

    private sealed class ChunkedReadStream(byte[] bytes, int maximumChunk) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumChunk)], cancellationToken);
    }
}
