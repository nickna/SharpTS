using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.DebugAdapter.Protocol;

internal sealed class DapProtocolConnection(Stream input, Stream output) : IAsyncDisposable
{
    internal const int MaximumHeaderBytes = 8 * 1024;
    internal const int MaximumPayloadBytes = 16 * 1024 * 1024;

    private static readonly byte[] HeaderDelimiter = "\r\n\r\n"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _outgoingSequence;
    private bool _disposed;

    public async ValueTask<DapRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        byte[]? rented = null;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(MaximumHeaderBytes);
            int headerLength = await ReadHeaderAsync(rented, cancellationToken).ConfigureAwait(false);
            if (headerLength == 0)
                return null;

            int contentLength = ParseContentLength(rented.AsSpan(0, headerLength));
            byte[] payload = ArrayPool<byte>.Shared.Rent(contentLength);
            try
            {
                await ReadExactlyAsync(payload.AsMemory(0, contentLength), cancellationToken)
                    .ConfigureAwait(false);
                return ParseRequest(payload.AsMemory(0, contentLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public Task SendResponseAsync(
        DapRequest request,
        bool success,
        object? body = null,
        string? message = null,
        int? errorId = null,
        CancellationToken cancellationToken = default)
    {
        object? responseBody = errorId is null
            ? body
            : new
            {
                error = new
                {
                    id = errorId.Value,
                    format = message ?? "Request failed.",
                    showUser = true,
                },
            };
        return SendAsync(new
        {
            seq = NextSequence(),
            type = "response",
            requestSeq = request.Sequence,
            success,
            command = request.Command,
            message,
            body = responseBody,
        }, cancellationToken);
    }

    public Task SendEventAsync(
        string eventName,
        object? body = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(new
        {
            seq = NextSequence(),
            type = "event",
            @event = eventName,
            body,
        }, cancellationToken);

    private int NextSequence() => Interlocked.Increment(ref _outgoingSequence);

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async ValueTask<int> ReadHeaderAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        int count = 0;
        int delimiterMatched = 0;
        while (count < MaximumHeaderBytes)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count, 1), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                if (count == 0)
                    return 0;
                throw new DapProtocolException("DAP input closed in the middle of a header.");
            }

            byte current = buffer[count++];
            delimiterMatched = current == HeaderDelimiter[delimiterMatched]
                ? delimiterMatched + 1
                : current == HeaderDelimiter[0] ? 1 : 0;
            if (delimiterMatched == HeaderDelimiter.Length)
                return count - HeaderDelimiter.Length;
        }

        throw new DapProtocolException($"DAP header exceeds {MaximumHeaderBytes} bytes.");
    }

    private static int ParseContentLength(ReadOnlySpan<byte> headerBytes)
    {
        foreach (byte value in headerBytes)
        {
            if (value > 0x7F)
                throw new DapProtocolException("DAP header is not ASCII.");
        }
        string header;
        try
        {
            header = Encoding.ASCII.GetString(headerBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new DapProtocolException($"DAP header is not ASCII: {exception.Message}");
        }

        int? contentLength = null;
        foreach (string rawLine in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = rawLine.IndexOf(':');
            if (colon <= 0)
                throw new DapProtocolException($"Malformed DAP header line '{rawLine}'.");
            string name = rawLine[..colon].Trim();
            string value = rawLine[(colon + 1)..].Trim();
            if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (contentLength is not null)
                throw new DapProtocolException("DAP message has duplicate Content-Length headers.");
            if (!int.TryParse(value, out int parsed) || parsed <= 0 || parsed > MaximumPayloadBytes)
            {
                throw new DapProtocolException(
                    $"Content-Length must be between 1 and {MaximumPayloadBytes} bytes.");
            }
            contentLength = parsed;
        }

        return contentLength
            ?? throw new DapProtocolException("DAP message is missing Content-Length.");
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await input.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new DapProtocolException("DAP input closed in the middle of a payload.");
            offset += read;
        }
    }

    private static DapRequest ParseRequest(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new DapProtocolException("DAP payload must be a JSON object.");
            if (!root.TryGetProperty("type", out JsonElement type)
                || type.GetString() != "request")
            {
                throw new DapProtocolException("DAP client payload must have type 'request'.");
            }
            if (!root.TryGetProperty("seq", out JsonElement sequence)
                || !sequence.TryGetInt32(out int requestSequence)
                || requestSequence <= 0)
            {
                throw new DapProtocolException("DAP request has an invalid sequence number.");
            }
            if (!root.TryGetProperty("command", out JsonElement commandElement)
                || string.IsNullOrWhiteSpace(commandElement.GetString()))
            {
                throw new DapProtocolException("DAP request is missing its command.");
            }
            JsonElement arguments = root.TryGetProperty("arguments", out JsonElement value)
                ? value.Clone()
                : default;
            return new DapRequest(requestSequence, commandElement.GetString()!, arguments);
        }
        catch (JsonException exception)
        {
            throw new DapProtocolException($"Invalid DAP JSON: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _writeGate.WaitAsync().ConfigureAwait(false);
        _writeGate.Release();
        _writeGate.Dispose();
    }
}
