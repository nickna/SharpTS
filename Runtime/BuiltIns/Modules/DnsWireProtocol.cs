using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Pure BCL DNS wire protocol implementation (RFC 1035).
/// Used by the interpreter via DnsRecordResolver and emitted as IL for compiled mode.
/// No external dependencies — only System.Net.Sockets and System.Net.NetworkInformation.
/// </summary>
public static class DnsWireProtocol
{
    // DNS record type numbers (QTYPE)
    public const int TypeA = 1;
    public const int TypeNS = 2;
    public const int TypeCNAME = 5;
    public const int TypeSOA = 6;
    public const int TypePTR = 12;
    public const int TypeMX = 15;
    public const int TypeTXT = 16;
    public const int TypeAAAA = 28;
    public const int TypeSRV = 33;
    public const int TypeNAPTR = 35;
    public const int TypeCAA = 257;

    private static readonly Random _rng = new();
    private const int DnsPort = 53;
    private const int DefaultTimeoutMs = 5000;
    private const int MaxRetries = 2;

    /// <summary>
    /// Per-attempt timeout. SHARPTS_DNS_TIMEOUT_MS overrides the 5s default so
    /// fake-server tests can exercise the timeout path without waiting
    /// (1 + MaxRetries) × 5s. The emitted IL wire protocol honors the same
    /// variable (RuntimeEmitter.Dns.cs, DnsGetTimeoutMs).
    /// </summary>
    private static int TimeoutMs
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SHARPTS_DNS_TIMEOUT_MS");
            return env != null && int.TryParse(env, out var ms) && ms > 0 ? ms : DefaultTimeoutMs;
        }
    }

    /// <summary>
    /// Resolves DNS records of the given type for the hostname.
    /// Returns a List&lt;object?&gt; of results (dictionaries or strings), or a single
    /// Dictionary for SOA.
    /// </summary>
    public static object Query(string hostname, int queryType)
    {
        var queryPacket = BuildQuery(hostname, queryType);
        var response = SendReceive(queryPacket);
        return ParseResponse(response, queryType, hostname);
    }

    /// <summary>
    /// Resolves DNS records using a specific DNS server address.
    /// Used by dns.Resolver instances with custom servers. The optional
    /// <paramref name="localAddress"/> binds the outgoing UDP socket
    /// (Resolver.setLocalAddress, #1072).
    /// </summary>
    public static object Query(string hostname, int queryType, string serverAddress, string? localAddress = null)
    {
        var queryPacket = BuildQuery(hostname, queryType);
        var response = SendReceive(queryPacket, serverAddress, localAddress);
        return ParseResponse(response, queryType, hostname);
    }

    /// <summary>
    /// Builds a DNS query packet per RFC 1035.
    /// </summary>
    public static byte[] BuildQuery(string hostname, int queryType)
    {
        // Header (12 bytes) + Question
        var packet = new List<byte>(64);

        // Transaction ID (random)
        var id = (ushort)_rng.Next(0, 65536);
        packet.Add((byte)(id >> 8));
        packet.Add((byte)(id & 0xFF));

        // Flags: standard query, recursion desired (0x0100)
        packet.Add(0x01);
        packet.Add(0x00);

        // QDCOUNT = 1
        packet.Add(0x00);
        packet.Add(0x01);

        // ANCOUNT = 0
        packet.Add(0x00);
        packet.Add(0x00);

        // NSCOUNT = 0
        packet.Add(0x00);
        packet.Add(0x00);

        // ARCOUNT = 0
        packet.Add(0x00);
        packet.Add(0x00);

        // Question: encode hostname labels
        EncodeName(packet, hostname);

        // QTYPE (2 bytes, big-endian)
        packet.Add((byte)(queryType >> 8));
        packet.Add((byte)(queryType & 0xFF));

        // QCLASS = IN (1)
        packet.Add(0x00);
        packet.Add(0x01);

        return packet.ToArray();
    }

    /// <summary>
    /// Encodes a domain name as DNS labels.
    /// "google.com" → [6]google[3]com[0]
    /// </summary>
    private static void EncodeName(List<byte> packet, string name)
    {
        var labels = name.TrimEnd('.').Split('.');
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.Add(0x00); // root label
    }

    /// <summary>
    /// Sends a DNS query and receives the response.
    /// Uses UDP with TCP fallback if response is truncated.
    /// </summary>
    public static byte[] SendReceive(byte[] query) =>
        SendReceive(query, GetSystemDnsServer());

    /// <summary>
    /// Sends a DNS query to a specific DNS server and waits for the response.
    /// The optional <paramref name="localAddress"/> binds the outgoing UDP socket.
    /// </summary>
    public static byte[] SendReceive(byte[] query, string dnsServer, string? localAddress = null)
    {
        // Parse optional port from server address (e.g., "8.8.8.8:5353" or "[::1]:5353")
        int port = DnsPort;
        if (dnsServer.StartsWith('['))
        {
            var closeBracket = dnsServer.IndexOf(']');
            if (closeBracket > 0 && closeBracket + 1 < dnsServer.Length && dnsServer[closeBracket + 1] == ':')
            {
                port = int.Parse(dnsServer.AsSpan(closeBracket + 2));
                dnsServer = dnsServer[1..closeBracket];
            }
            else if (closeBracket > 0)
            {
                dnsServer = dnsServer[1..closeBracket];
            }
        }
        else if (dnsServer.IndexOf(':') is var colonIdx && colonIdx >= 0
                 && dnsServer.IndexOf(':', colonIdx + 1) < 0) // single colon = IPv4:port
        {
            port = int.Parse(dnsServer.AsSpan(colonIdx + 1));
            dnsServer = dnsServer[..colonIdx];
        }

        var endpoint = new IPEndPoint(IPAddress.Parse(dnsServer), port);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            UdpClient? udp = null;
            try
            {
                // Try UDP first, bound to the resolver's local address when set
                udp = localAddress != null && IPAddress.TryParse(localAddress, out var localIp)
                    ? new UdpClient(new IPEndPoint(localIp, 0))
                    : new UdpClient();
                udp.Client.SendTimeout = TimeoutMs;
                udp.Send(query, query.Length, endpoint);

                // Use Task.Wait(timeout) for reliable cross-platform timeout.
                // Neither Socket.ReceiveTimeout (SO_RCVTIMEO) nor CancellationToken-based
                // cancellation of ReceiveAsync works reliably on macOS ARM64
                // (dotnet/runtime#81378, #64551).
                var receiveTask = udp.ReceiveAsync(CancellationToken.None).AsTask();
                if (!receiveTask.Wait(TimeoutMs))
                {
                    udp.Close(); // Force-close unblocks the pending async receive
                    throw new SocketException((int)SocketError.TimedOut);
                }

                var response = receiveTask.Result.Buffer;

                // Discard datagrams whose transaction ID doesn't echo the query's
                // (stale or spoofed response) and retry; if no matching response
                // ever arrives this falls through to the ETIMEOUT below, like a
                // resolver that never answered.
                if (response.Length < 2 || response[0] != query[0] || response[1] != query[1])
                    continue;

                // Check TC (truncation) bit — byte 2, bit 1
                if (response.Length >= 3 && (response[2] & 0x02) != 0)
                {
                    // Retry with TCP
                    response = SendViaTcp(query, endpoint);
                }

                // SERVFAIL/REFUSED are usually transient resolver conditions —
                // retry them like socket errors. On the last attempt the response
                // is returned so ParseResponse surfaces ESERVFAIL/EREFUSED.
                if (attempt < MaxRetries && response.Length >= 4
                    && (response[3] & 0x0F) is 2 or 5)
                {
                    continue;
                }

                return response;
            }
            catch (SocketException) when (attempt < MaxRetries)
            {
                // Retry
            }
            finally
            {
                udp?.Dispose();
            }
        }

        throw new Exception("Runtime Error: dns.resolve ETIMEOUT DNS query timed out");
    }

    /// <summary>
    /// Sends a DNS query via TCP (for truncated responses).
    /// TCP DNS uses a 2-byte big-endian length prefix.
    /// </summary>
    private static byte[] SendViaTcp(byte[] query, IPEndPoint endpoint)
    {
        using var tcp = new TcpClient();
        tcp.SendTimeout = TimeoutMs;

        // Use Task.Wait for reliable cross-platform timeout (see SendReceive comment).
        var connectTask = tcp.ConnectAsync(endpoint.Address, endpoint.Port).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs));
        try
        {
            connectTask.GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            throw new SocketException((int)SocketError.TimedOut);
        }

        var stream = tcp.GetStream();

        // Send: 2-byte length prefix + query (writes are fast and reliable)
        var lengthPrefix = new byte[2];
        lengthPrefix[0] = (byte)(query.Length >> 8);
        lengthPrefix[1] = (byte)(query.Length & 0xFF);
        stream.Write(lengthPrefix, 0, 2);
        stream.Write(query, 0, query.Length);
        stream.Flush();

        // Receive: 2-byte length prefix + response
        var respLenBuf = new byte[2];
        ReadExactWithTimeout(stream, respLenBuf, 0, 2);
        var respLen = (respLenBuf[0] << 8) | respLenBuf[1];

        var response = new byte[respLen];
        ReadExactWithTimeout(stream, response, 0, respLen);

        return response;
    }

    private static void ReadExactWithTimeout(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var readTask = stream.ReadAsync(buffer, offset, count, CancellationToken.None);
            if (!readTask.Wait(TimeoutMs))
            {
                throw new SocketException((int)SocketError.TimedOut);
            }
            var read = readTask.Result;
            if (read == 0)
                throw new Exception("Runtime Error: dns.resolve connection closed unexpectedly");
            offset += read;
            count -= read;
        }
    }

    /// <summary>
    /// Gets the system's primary DNS server address.
    /// Falls back to 8.8.8.8 if none found.
    /// </summary>
    public static string GetSystemDnsServer()
    {
        // SHARPTS_DNS_SERVER overrides discovery ("host" or "host:port") — lets
        // tests point the resolver at a loopback fake server. The emitted IL
        // honors the same variable (RuntimeEmitter.Dns.cs, DnsGetSystemDns).
        var overrideServer = Environment.GetEnvironmentVariable("SHARPTS_DNS_SERVER");
        if (!string.IsNullOrEmpty(overrideServer))
            return overrideServer;

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var dns in props.DnsAddresses)
                {
                    // Prefer IPv4
                    if (dns.AddressFamily == AddressFamily.InterNetwork)
                        return dns.ToString();
                }
            }

            // Second pass: accept IPv6
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var dns in props.DnsAddresses)
                {
                    return dns.ToString();
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        return "8.8.8.8";
    }

    /// <summary>
    /// Parses a DNS response and returns the results.
    /// </summary>
    public static object ParseResponse(byte[] data, int queryType, string hostname)
    {
        if (data.Length < 12)
            throw new Exception($"Runtime Error: dns.resolve EAI_FAIL {hostname}");

        // Check RCODE (bits 0-3 of byte 3)
        var rcode = data[3] & 0x0F;
        if (rcode != 0)
        {
            var errorCode = rcode switch
            {
                1 => "EFORMERR",
                2 => "ESERVFAIL",
                3 => "ENOTFOUND",
                4 => "ENOTIMP",
                5 => "EREFUSED",
                _ => "EAI_FAIL"
            };
            throw new Exception($"Runtime Error: dns.resolve {errorCode} {hostname}");
        }

        // ANCOUNT
        var ancount = (data[6] << 8) | data[7];

        // Skip header (12 bytes)
        var offset = 12;

        // Skip question section: QDCOUNT questions
        var qdcount = (data[4] << 8) | data[5];
        for (var q = 0; q < qdcount; q++)
        {
            SkipName(data, ref offset);
            offset += 4; // QTYPE + QCLASS
        }

        // Parse answer records
        var results = new List<object?>();
        for (var i = 0; i < ancount; i++)
        {
            // NAME
            SkipName(data, ref offset);

            // TYPE (2), CLASS (2), TTL (4), RDLENGTH (2)
            var type = (data[offset] << 8) | data[offset + 1];
            offset += 2; // TYPE
            offset += 2; // CLASS
            offset += 4; // TTL
            var rdlength = (data[offset] << 8) | data[offset + 1];
            offset += 2; // RDLENGTH

            var rdataStart = offset;

            if (type == queryType)
            {
                var record = ParseRecord(data, ref offset, type, rdlength, rdataStart);
                if (record != null)
                    results.Add(record);
            }
            else
            {
                offset += rdlength; // skip non-matching record
            }
        }

        // SOA returns a single object, not a list
        if (queryType == TypeSOA)
        {
            if (results.Count == 0)
                throw new Exception($"Runtime Error: dns.resolveSoa ENODATA {hostname}");
            return results[0]!;
        }

        if (results.Count == 0)
        {
            var methodName = GetMethodName(queryType);
            throw new Exception($"Runtime Error: dns.{methodName} ENODATA {hostname}");
        }

        return results;
    }

    private static string GetMethodName(int queryType) => queryType switch
    {
        TypeA => "resolve",
        TypeAAAA => "resolve",
        TypeMX => "resolveMx",
        TypeTXT => "resolveTxt",
        TypeSRV => "resolveSrv",
        TypeCNAME => "resolveCname",
        TypeNS => "resolveNs",
        TypeSOA => "resolveSoa",
        TypePTR => "resolvePtr",
        TypeCAA => "resolveCaa",
        TypeNAPTR => "resolveNaptr",
        _ => "resolve"
    };

    /// <summary>
    /// Parses a single DNS resource record's RDATA.
    /// </summary>
    private static object? ParseRecord(byte[] data, ref int offset, int type, int rdlength, int rdataStart)
    {
        switch (type)
        {
            case TypeA:
            {
                if (rdlength < 4) { offset = rdataStart + rdlength; return null; }
                var ip = new IPAddress(new ReadOnlySpan<byte>(data, offset, 4));
                offset = rdataStart + rdlength;
                return ip.ToString();
            }

            case TypeAAAA:
            {
                if (rdlength < 16) { offset = rdataStart + rdlength; return null; }
                var ip = new IPAddress(new ReadOnlySpan<byte>(data, offset, 16));
                offset = rdataStart + rdlength;
                return ip.ToString();
            }

            case TypeMX:
            {
                var preference = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var exchange = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return new Dictionary<string, object?>
                {
                    ["exchange"] = exchange,
                    ["priority"] = (double)preference
                };
            }

            case TypeTXT:
            {
                // TXT records: sequence of length-prefixed strings
                var chunks = new List<object?>();
                var end = rdataStart + rdlength;
                while (offset < end)
                {
                    var strLen = data[offset];
                    offset++;
                    var text = Encoding.UTF8.GetString(data, offset, strLen);
                    offset += strLen;
                    chunks.Add(text);
                }
                offset = end;
                return chunks;
            }

            case TypeSRV:
            {
                var priority = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var weight = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var port = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var target = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return new Dictionary<string, object?>
                {
                    ["name"] = target,
                    ["port"] = (double)port,
                    ["priority"] = (double)priority,
                    ["weight"] = (double)weight
                };
            }

            case TypeCNAME:
            {
                var name = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return name;
            }

            case TypeNS:
            {
                var name = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return name;
            }

            case TypeSOA:
            {
                var mname = ReadName(data, ref offset);
                var rname = ReadName(data, ref offset);
                var serial = ReadUInt32(data, ref offset);
                var refresh = ReadUInt32(data, ref offset);
                var retry = ReadUInt32(data, ref offset);
                var expire = ReadUInt32(data, ref offset);
                var minimum = ReadUInt32(data, ref offset);
                offset = rdataStart + rdlength;
                return new Dictionary<string, object?>
                {
                    ["nsname"] = mname,
                    ["hostmaster"] = rname,
                    ["serial"] = (double)serial,
                    ["refresh"] = (double)refresh,
                    ["retry"] = (double)retry,
                    ["expire"] = (double)expire,
                    ["minttl"] = (double)minimum
                };
            }

            case TypePTR:
            {
                var name = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return name;
            }

            case TypeCAA:
            {
                var flags = data[offset];
                offset++;
                var tagLen = data[offset];
                offset++;
                var tag = Encoding.ASCII.GetString(data, offset, tagLen);
                offset += tagLen;
                var valueLen = rdlength - 2 - tagLen;
                var value = Encoding.UTF8.GetString(data, offset, valueLen);
                offset = rdataStart + rdlength;
                return new Dictionary<string, object?>
                {
                    ["critical"] = (double)(flags & 0x80),
                    [tag] = value
                };
            }

            case TypeNAPTR:
            {
                var order = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var preference = (data[offset] << 8) | data[offset + 1];
                offset += 2;
                var flags = ReadCharacterString(data, ref offset);
                var service = ReadCharacterString(data, ref offset);
                var regexp = ReadCharacterString(data, ref offset);
                var replacement = ReadName(data, ref offset);
                offset = rdataStart + rdlength;
                return new Dictionary<string, object?>
                {
                    ["flags"] = flags,
                    ["service"] = service,
                    ["regexp"] = regexp,
                    ["replacement"] = replacement,
                    ["order"] = (double)order,
                    ["preference"] = (double)preference
                };
            }

            default:
                offset = rdataStart + rdlength;
                return null;
        }
    }

    /// <summary>
    /// Reads a DNS name with label decompression (RFC 1035 §4.1.4).
    /// Returns the name with trailing dots removed.
    /// </summary>
    public static string ReadName(byte[] data, ref int offset)
    {
        var sb = new StringBuilder();
        var jumped = false;
        var savedOffset = 0;
        var maxJumps = 128; // prevent infinite loops

        while (maxJumps-- > 0)
        {
            if (offset >= data.Length)
                break;

            var len = data[offset];

            if (len == 0)
            {
                offset++;
                break;
            }

            // Pointer (top 2 bits = 11)
            if ((len & 0xC0) == 0xC0)
            {
                if (!jumped)
                    savedOffset = offset + 2;
                var pointer = ((len & 0x3F) << 8) | data[offset + 1];
                offset = pointer;
                jumped = true;
                continue;
            }

            // Regular label
            offset++;
            if (sb.Length > 0)
                sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(data, offset, len));
            offset += len;
        }

        if (jumped)
            offset = savedOffset;

        return sb.ToString();
    }

    /// <summary>
    /// Reads a DNS character-string (length-prefixed).
    /// Used in NAPTR records.
    /// </summary>
    private static string ReadCharacterString(byte[] data, ref int offset)
    {
        var len = data[offset];
        offset++;
        var result = Encoding.UTF8.GetString(data, offset, len);
        offset += len;
        return result;
    }

    /// <summary>
    /// Skips a DNS name (for traversal without reading).
    /// </summary>
    private static void SkipName(byte[] data, ref int offset)
    {
        while (offset < data.Length)
        {
            var len = data[offset];
            if (len == 0)
            {
                offset++;
                return;
            }
            if ((len & 0xC0) == 0xC0)
            {
                offset += 2; // pointer is 2 bytes
                return;
            }
            offset += 1 + len;
        }
    }

    private static uint ReadUInt32(byte[] data, ref int offset)
    {
        var value = (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                           (data[offset + 2] << 8) | data[offset + 3]);
        offset += 4;
        return value;
    }
}
