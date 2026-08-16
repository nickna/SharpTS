using System.Net;
using System.Text;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Builders and inspectors for RFC 1035 DNS packets, for use with
/// <see cref="FakeDnsServer"/> responders. Responses echo the request's
/// transaction ID and question section; answer records default to a compression
/// pointer at the question name (0xC00C), as real resolvers emit.
/// </summary>
public static class DnsPackets
{
    /// <summary>Compression pointer to the question name at offset 12.</summary>
    public static byte[] PointerToQuestionName => [0xC0, 0x0C];

    // ---- request inspectors -------------------------------------------------

    /// <summary>QTYPE of the (single) question in a request.</summary>
    public static int QueryType(byte[] request)
    {
        var offset = SkipQuestionName(request);
        return (request[offset] << 8) | request[offset + 1];
    }

    /// <summary>Dotted question name of a request.</summary>
    public static string QueryName(byte[] request)
    {
        var sb = new StringBuilder();
        var offset = 12;
        while (request[offset] != 0)
        {
            var len = request[offset++];
            if (sb.Length > 0)
                sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(request, offset, len));
            offset += len;
        }
        return sb.ToString();
    }

    private static int SkipQuestionName(byte[] request)
    {
        var offset = 12;
        while (request[offset] != 0)
            offset += request[offset] + 1;
        return offset + 1;
    }

    private static int QuestionEnd(byte[] request) => SkipQuestionName(request) + 4; // QTYPE + QCLASS

    // ---- response builders --------------------------------------------------

    /// <summary>
    /// Builds a response echoing the request's ID and question, with the given
    /// rcode and pre-encoded answer records (see <see cref="Record"/>).
    /// </summary>
    public static byte[] Response(byte[] request, byte rcode = 0, params byte[][] answers)
    {
        var questionEnd = QuestionEnd(request);
        var response = new List<byte>(questionEnd + answers.Sum(a => a.Length));
        for (var i = 0; i < questionEnd; i++)
            response.Add(request[i]);
        response[2] = 0x81;                       // QR=1, RD=1
        response[3] = (byte)(0x80 | (rcode & 0x0F)); // RA=1
        response[6] = (byte)(answers.Length >> 8);   // ANCOUNT
        response[7] = (byte)(answers.Length & 0xFF);
        foreach (var answer in answers)
            response.AddRange(answer);
        return response.ToArray();
    }

    /// <summary>Response with the TC bit set and no answers — forces TCP fallback.</summary>
    public static byte[] Truncated(byte[] request)
    {
        var response = Response(request);
        response[2] = 0x83; // QR=1, TC=1, RD=1
        return response;
    }

    /// <summary>Copy of a response with its transaction ID flipped.</summary>
    public static byte[] WithCorruptedId(byte[] response)
    {
        var corrupted = (byte[])response.Clone();
        corrupted[0] ^= 0xFF;
        corrupted[1] ^= 0xFF;
        return corrupted;
    }

    /// <summary>
    /// Encodes one resource record: NAME (defaults to a compression pointer at
    /// the question name), TYPE, CLASS IN, TTL 60, RDLENGTH, RDATA.
    /// </summary>
    public static byte[] Record(int type, byte[] rdata, byte[]? name = null)
    {
        name ??= PointerToQuestionName;
        var record = new List<byte>(name.Length + 10 + rdata.Length);
        record.AddRange(name);
        record.Add((byte)(type >> 8));
        record.Add((byte)(type & 0xFF));
        record.AddRange([0x00, 0x01]);             // CLASS = IN
        record.AddRange([0x00, 0x00, 0x00, 0x3C]); // TTL = 60
        record.Add((byte)(rdata.Length >> 8));
        record.Add((byte)(rdata.Length & 0xFF));
        record.AddRange(rdata);
        return record.ToArray();
    }

    // ---- name encoders ------------------------------------------------------

    /// <summary>Uncompressed name: "a.b" → [1]a[1]b[0].</summary>
    public static byte[] Name(string dotted)
    {
        var bytes = new List<byte>();
        foreach (var label in dotted.TrimEnd('.').Split('.'))
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)labelBytes.Length);
            bytes.AddRange(labelBytes);
        }
        bytes.Add(0x00);
        return bytes.ToArray();
    }

    /// <summary>
    /// Labels followed by a pointer to the question name: "mail" decodes to
    /// "mail.&lt;question name&gt;". Exercises the label-then-jump path of name
    /// decompression.
    /// </summary>
    public static byte[] LabelsThenPointer(string labels)
    {
        var bytes = new List<byte>();
        foreach (var label in labels.TrimEnd('.').Split('.'))
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)labelBytes.Length);
            bytes.AddRange(labelBytes);
        }
        bytes.AddRange(PointerToQuestionName);
        return bytes.ToArray();
    }

    // ---- RDATA builders -----------------------------------------------------

    public static byte[] A(string ip) => IPAddress.Parse(ip).GetAddressBytes();

    public static byte[] Aaaa(string ip) => IPAddress.Parse(ip).GetAddressBytes();

    public static byte[] Mx(int preference, byte[] exchange) =>
        [.. UInt16(preference), .. exchange];

    public static byte[] Txt(params string[] chunks)
    {
        var rdata = new List<byte>();
        foreach (var chunk in chunks)
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            rdata.Add((byte)bytes.Length);
            rdata.AddRange(bytes);
        }
        return rdata.ToArray();
    }

    public static byte[] Srv(int priority, int weight, int port, byte[] target) =>
        [.. UInt16(priority), .. UInt16(weight), .. UInt16(port), .. target];

    public static byte[] Soa(byte[] mname, byte[] rname, uint serial, uint refresh,
        uint retry, uint expire, uint minimum) =>
        [.. mname, .. rname, .. UInt32(serial), .. UInt32(refresh), .. UInt32(retry),
         .. UInt32(expire), .. UInt32(minimum)];

    public static byte[] Caa(byte flags, string tag, string value)
    {
        var tagBytes = Encoding.ASCII.GetBytes(tag);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        return [flags, (byte)tagBytes.Length, .. tagBytes, .. valueBytes];
    }

    public static byte[] Naptr(int order, int preference, string flags, string service,
        string regexp, byte[] replacement) =>
        [.. UInt16(order), .. UInt16(preference), .. CharString(flags),
         .. CharString(service), .. CharString(regexp), .. replacement];

    private static byte[] CharString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return [(byte)bytes.Length, .. bytes];
    }

    private static byte[] UInt16(int value) => [(byte)(value >> 8), (byte)(value & 0xFF)];

    private static byte[] UInt32(uint value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
