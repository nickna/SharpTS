using System.Net;
using System.Net.Sockets;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Loopback DNS server for deterministic wire-protocol tests. Exercises the real
/// client code (actual UDP/TCP sockets, actual packet bytes) and only replaces the
/// remote endpoint, so retry, truncation, decoding and timeout behavior can be
/// pinned without depending on the runner's network path.
///
/// The UDP responder receives the raw query and a zero-based query index and
/// returns the response datagram, or null to deliberately not answer (the client
/// then runs into its receive timeout). When a TCP responder is provided, the
/// server also listens on the same port number over TCP and speaks the 2-byte
/// length-prefixed framing of RFC 1035 §4.2.2 — used to test TC-bit fallback.
///
/// CI constraint (macOS, dotnet/runtime#81378, #64551): receive loops must use
/// async receives only — Dispose() does NOT unblock a thread stuck in a
/// synchronous Receive() there and wedges the whole testhost. Shutdown works by
/// closing the sockets and catching the resulting exceptions.
/// </summary>
public sealed class FakeDnsServer : IDisposable
{
    public delegate byte[]? UdpResponder(byte[] request, int queryIndex);

    private readonly UdpClient _udp;
    private readonly TcpListener? _tcp;
    private readonly UdpResponder _udpResponder;
    private readonly Func<byte[], byte[]>? _tcpResponder;
    private int _queryCount;
    private int _tcpQueryCount;

    public FakeDnsServer(UdpResponder udpResponder, Func<byte[], byte[]>? tcpResponder = null)
    {
        _udpResponder = udpResponder;
        _tcpResponder = tcpResponder;

        if (tcpResponder is null)
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        }
        else
        {
            // The client derives both endpoints from one "host:port" address, so
            // UDP and TCP must share the port number. Bind TCP on an ephemeral
            // port first, then bind UDP to the same number — the protocols have
            // separate port namespaces, so it is normally free.
            (_tcp, _udp) = BindSamePortPair();
        }

        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        Address = $"127.0.0.1:{Port}";
        // Fire-and-forget serve loops: they run until Dispose closes the sockets,
        // and terminal exceptions are handled inside the loops themselves.
        _ = Task.Run(ServeUdpAsync);
        if (_tcp is not null)
            _ = Task.Run(ServeTcpAsync);
    }

    /// <summary>Server address in "127.0.0.1:port" form.</summary>
    public string Address { get; }

    public int Port { get; }

    /// <summary>Number of UDP queries received (including deliberately unanswered ones).</summary>
    public int QueryCount => Volatile.Read(ref _queryCount);

    /// <summary>Number of TCP queries received.</summary>
    public int TcpQueryCount => Volatile.Read(ref _tcpQueryCount);

    private static (TcpListener Tcp, UdpClient Udp) BindSamePortPair()
    {
        // Probe random high ports rather than asking the OS for an ephemeral TCP
        // port: ephemeral allocation is sequential, and on Windows the handed-out
        // TCP port can sit inside a UDP *excluded* port range (WSAEACCES), so a
        // small retry loop would burn every attempt in the same window.
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            TcpListener? tcp = null;
            try
            {
                tcp = new TcpListener(IPAddress.Loopback, port);
                tcp.Start();
                return (tcp, new UdpClient(new IPEndPoint(IPAddress.Loopback, port)));
            }
            catch (SocketException) when (attempt < 49)
            {
                tcp?.Stop();
            }
        }
    }

    private async Task ServeUdpAsync()
    {
        try
        {
            while (true)
            {
                var query = await _udp.ReceiveAsync().ConfigureAwait(false);
                var index = Interlocked.Increment(ref _queryCount) - 1;
                var response = _udpResponder(query.Buffer, index);
                if (response is null)
                    continue; // deliberately unanswered — client times out
                await _udp.SendAsync(response, response.Length, query.RemoteEndPoint)
                    .ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ServeTcpAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _tcp!.AcceptTcpClientAsync().ConfigureAwait(false);
                var stream = client.GetStream();
                var lengthPrefix = await ReadExactAsync(stream, 2).ConfigureAwait(false);
                var queryLength = (lengthPrefix[0] << 8) | lengthPrefix[1];
                var request = await ReadExactAsync(stream, queryLength).ConfigureAwait(false);
                Interlocked.Increment(ref _tcpQueryCount);

                var response = _tcpResponder!(request);
                var framed = new byte[response.Length + 2];
                framed[0] = (byte)(response.Length >> 8);
                framed[1] = (byte)(response.Length & 0xFF);
                response.CopyTo(framed, 2);
                await stream.WriteAsync(framed).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset))
                .ConfigureAwait(false);
            if (read == 0)
                throw new IOException("client closed the connection mid-frame");
            offset += read;
        }
        return buffer;
    }

    public void Dispose()
    {
        _udp.Dispose();
        _tcp?.Dispose();
    }
}
