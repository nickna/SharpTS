using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SharpTS.DebugAdapter.Adapter;

namespace SharpTS.Tests.DebugAdapter;

internal sealed class DapProtocolHarness : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly ConcurrentQueue<JsonElement> _events = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _sequence;

    public DapProtocolHarness()
    {
        string adapterAssembly = typeof(DapAdapterSession).Assembly.Location;
        _process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { adapterAssembly },
            WorkingDirectory = Path.GetDirectoryName(adapterAssembly)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start sharpts-dap.");
        _input = _process.StandardInput.BaseStream;
        _output = _process.StandardOutput.BaseStream;
    }

    public int ProcessId => _process.Id;

    public async Task<JsonElement> RequestAsync(
        string command,
        object? arguments = null,
        TimeSpan? timeout = null,
        int? sequenceOverride = null)
    {
        int sequence = sequenceOverride ?? Interlocked.Increment(ref _sequence);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = sequence,
            type = "request",
            command,
            arguments,
        });
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await _writeGate.WaitAsync();
        try
        {
            await _input.WriteAsync(header);
            await _input.WriteAsync(payload);
            await _input.FlushAsync();
        }
        finally
        {
            _writeGate.Release();
        }

        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            JsonElement message = await ReadMessageAsync(timeoutCts.Token);
            string type = message.GetProperty("type").GetString()!;
            if (type == "event")
            {
                _events.Enqueue(message);
                continue;
            }
            if (message.GetProperty("requestSeq").GetInt32() != sequence)
                throw new InvalidOperationException("Harness observed an out-of-order response.");
            return message;
        }
    }

    public async Task<JsonElement> WaitForEventAsync(
        string eventName,
        TimeSpan? timeout = null)
    {
        if (TryTakeEvent(eventName, out JsonElement queued))
            return queued;

        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            JsonElement message = await ReadMessageAsync(timeoutCts.Token);
            if (message.GetProperty("type").GetString() != "event")
                throw new InvalidOperationException("Harness expected an event but received a response.");
            if (message.GetProperty("event").GetString() == eventName)
                return message;
            _events.Enqueue(message);
        }
    }

    private bool TryTakeEvent(string eventName, out JsonElement found)
    {
        var deferred = new List<JsonElement>();
        while (_events.TryDequeue(out JsonElement message))
        {
            if (message.GetProperty("event").GetString() == eventName)
            {
                foreach (JsonElement item in deferred)
                    _events.Enqueue(item);
                found = message;
                return true;
            }
            deferred.Add(message);
        }
        foreach (JsonElement item in deferred)
            _events.Enqueue(item);
        found = default;
        return false;
    }

    private async Task<JsonElement> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var header = new List<byte>();
        int matched = 0;
        ReadOnlyMemory<byte> delimiter = "\r\n\r\n"u8.ToArray();
        while (matched != delimiter.Length)
        {
            byte[] single = new byte[1];
            int read = await _output.ReadAsync(single, cancellationToken);
            if (read == 0)
            {
                string error = await _process.StandardError.ReadToEndAsync(cancellationToken);
                throw new EndOfStreamException($"sharpts-dap stdout closed. stderr: {error}");
            }
            byte value = single[0];
            header.Add(value);
            matched = value == delimiter.Span[matched]
                ? matched + 1
                : value == delimiter.Span[0] ? 1 : 0;
        }

        string headerText = Encoding.ASCII.GetString(header.ToArray(), 0, header.Count - 4);
        string lengthValue = headerText.Split("\r\n")
            .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Split(':', 2)[1];
        int length = int.Parse(lengthValue);
        byte[] payload = new byte[length];
        await _output.ReadExactlyAsync(payload, cancellationToken);
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                JsonElement response = await RequestAsync("disconnect", new { terminateDebuggee = true },
                    TimeSpan.FromSeconds(5));
                _ = response;
                _input.Close();
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        finally
        {
            _writeGate.Dispose();
            _process.Dispose();
        }
    }
}
