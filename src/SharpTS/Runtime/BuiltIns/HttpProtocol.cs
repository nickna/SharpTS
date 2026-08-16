using System.Text;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Minimal HTTP/1.1 request reader used by the interpreter HTTPS server (#1049), which runs the
/// HTTP pipeline directly over a TLS <see cref="System.IO.Stream"/> (HttpListener cannot be used
/// with an app-supplied certificate). Parses the request line, headers, and a Content-Length body.
/// </summary>
public static class HttpProtocol
{
    public sealed record ParsedRequest(
        string Method,
        string Url,
        string HttpVersion,
        Dictionary<string, object?> HeadersLower,
        List<object?> RawHeaders,
        byte[] Body);

    /// <summary>
    /// Reads a single HTTP/1.1 request from the stream, or null on a clean EOF before any bytes.
    /// </summary>
    public static async Task<ParsedRequest?> ReadRequestAsync(System.IO.Stream stream, CancellationToken token)
    {
        var headerBytes = new List<byte>(1024);
        var buffer = new byte[4096];
        int headerEnd = -1;
        byte[] leftover = Array.Empty<byte>();

        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (n == 0)
            {
                if (headerBytes.Count == 0) return null; // clean EOF
                break;
            }
            for (int i = 0; i < n; i++) headerBytes.Add(buffer[i]);
            headerEnd = FindHeaderEnd(headerBytes);
            if (headerBytes.Count > 1024 * 1024) break; // guard against runaway headers
        }

        if (headerEnd < 0) return null;

        // Header section is [0..headerEnd); the body begins at headerEnd+4.
        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray(), 0, headerEnd);
        int bodyStart = headerEnd + 4;
        if (bodyStart < headerBytes.Count)
            leftover = headerBytes.GetRange(bodyStart, headerBytes.Count - bodyStart).ToArray();

        var lines = headerText.Split("\r\n");
        var requestLine = lines.Length > 0 ? lines[0] : "";
        var parts = requestLine.Split(' ');
        var method = parts.Length > 0 ? parts[0] : "GET";
        var url = parts.Length > 1 ? parts[1] : "/";
        var version = parts.Length > 2 && parts[2].StartsWith("HTTP/") ? parts[2].Substring(5) : "1.1";

        var headersLower = new Dictionary<string, object?>();
        var rawHeaders = new List<object?>();
        int contentLength = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var name = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            rawHeaders.Add(name);
            rawHeaders.Add(value);
            var lower = name.ToLowerInvariant();
            if (lower == "set-cookie")
            {
                if (headersLower.TryGetValue(lower, out var existing) && existing is List<object?> list)
                    list.Add(value);
                else
                    headersLower[lower] = new List<object?> { value };
            }
            else
            {
                headersLower[lower] = value;
            }
            if (lower == "content-length" && int.TryParse(value, out var cl))
                contentLength = cl;
        }

        // Read the remaining body bytes (Content-Length minus what we already buffered).
        byte[] body;
        if (contentLength <= 0)
        {
            body = Array.Empty<byte>();
        }
        else
        {
            var bodyBuf = new byte[contentLength];
            int have = Math.Min(leftover.Length, contentLength);
            Array.Copy(leftover, bodyBuf, have);
            int total = have;
            while (total < contentLength)
            {
                int n = await stream.ReadAsync(bodyBuf.AsMemory(total, contentLength - total), token);
                if (n == 0) break;
                total += n;
            }
            body = bodyBuf;
        }

        return new ParsedRequest(method, url, version, headersLower, rawHeaders, body);
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 0; i + 3 < bytes.Count; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n' &&
                bytes[i + 2] == (byte)'\r' && bytes[i + 3] == (byte)'\n')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Serializes an HTTP/1.1 response (status line + headers + body) to bytes.
    /// </summary>
    public static byte[] SerializeResponse(int statusCode, string statusMessage,
        IEnumerable<KeyValuePair<string, string>> headers, byte[] body)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(statusMessage).Append("\r\n");
        foreach (var (name, value) in headers)
            sb.Append(name).Append(": ").Append(value).Append("\r\n");
        sb.Append("\r\n");
        var head = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[head.Length + body.Length];
        Array.Copy(head, result, head.Length);
        Array.Copy(body, 0, result, head.Length, body.Length);
        return result;
    }
}
