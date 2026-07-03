using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Pins DnsWireProtocol behavior against a loopback fake DNS server: per-record-type
/// RDATA decoding, name decompression, TC-bit TCP fallback, error rcodes, malformed
/// responses and transaction-ID validation. These tests pass the server address
/// explicitly, so they are independent of env-var seams and safe to run in parallel.
/// (The compiled-IL twin of this wire protocol is covered by
/// SharedTests.BuiltInModules.DnsFakeServerModuleTests.)
/// </summary>
public class DnsWireProtocolFakeServerTests
{
    private static FakeDnsServer SingleRecordServer(int type, byte[] rdata, byte[]? name = null) =>
        new((request, _) => DnsPackets.Response(request, 0, DnsPackets.Record(type, rdata, name)));

    [Fact]
    public void Query_ARecord_DecodesAddress()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeA, DnsPackets.A("93.184.216.34"));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeA, server.Address);

        Assert.Equal("93.184.216.34", Assert.Single(result));
    }

    [Fact]
    public void Query_AaaaRecord_DecodesAddress()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeAAAA, DnsPackets.Aaaa("2606:2800:220:1:248:1893:25c8:1946"));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeAAAA, server.Address);

        Assert.Equal("2606:2800:220:1:248:1893:25c8:1946", Assert.Single(result));
    }

    [Fact]
    public void Query_Mx_DecodesCompressedExchangeName()
    {
        // exchange = label "mail" + pointer to the question name → "mail.example.com";
        // exercises the label-then-jump path of ReadName.
        using var server = SingleRecordServer(DnsWireProtocol.TypeMX,
            DnsPackets.Mx(10, DnsPackets.LabelsThenPointer("mail")));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeMX, server.Address);

        var record = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result));
        Assert.Equal("mail.example.com", record["exchange"]);
        Assert.Equal(10.0, record["priority"]);
    }

    [Fact]
    public void Query_Txt_DecodesMultipleChunks()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeTXT,
            DnsPackets.Txt("chunk-one", "chunk-two"));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var chunks = Assert.IsType<List<object?>>(Assert.Single(result));
        Assert.Equal(new object?[] { "chunk-one", "chunk-two" }, chunks);
    }

    [Fact]
    public void Query_Srv_DecodesAllFields()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeSRV,
            DnsPackets.Srv(priority: 1, weight: 5, port: 5060, DnsPackets.LabelsThenPointer("sip")));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeSRV, server.Address);

        var record = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result));
        Assert.Equal("sip.example.com", record["name"]);
        Assert.Equal(5060.0, record["port"]);
        Assert.Equal(1.0, record["priority"]);
        Assert.Equal(5.0, record["weight"]);
    }

    [Theory]
    [InlineData(DnsWireProtocol.TypeNS)]
    [InlineData(DnsWireProtocol.TypeCNAME)]
    [InlineData(DnsWireProtocol.TypePTR)]
    public void Query_NameRecord_DecodesPointerOnlyRdata(int type)
    {
        // RDATA is a bare compression pointer at the question name.
        using var server = SingleRecordServer(type, DnsPackets.PointerToQuestionName);

        var result = (List<object?>)DnsWireProtocol.Query("example.com", type, server.Address);

        Assert.Equal("example.com", Assert.Single(result));
    }

    [Fact]
    public void Query_Soa_DecodesAllFields()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeSOA,
            DnsPackets.Soa(
                DnsPackets.LabelsThenPointer("ns1"),
                DnsPackets.LabelsThenPointer("hostmaster"),
                serial: 2024010101, refresh: 7200, retry: 900, expire: 1209600, minimum: 86400));

        var record = (Dictionary<string, object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeSOA, server.Address);

        Assert.Equal("ns1.example.com", record["nsname"]);
        Assert.Equal("hostmaster.example.com", record["hostmaster"]);
        Assert.Equal(2024010101.0, record["serial"]);
        Assert.Equal(7200.0, record["refresh"]);
        Assert.Equal(900.0, record["retry"]);
        Assert.Equal(1209600.0, record["expire"]);
        Assert.Equal(86400.0, record["minttl"]);
    }

    [Fact]
    public void Query_Caa_DecodesFlagsTagAndValue()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeCAA,
            DnsPackets.Caa(flags: 0x80, "issue", "ca.example.net"));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeCAA, server.Address);

        var record = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result));
        Assert.Equal(128.0, record["critical"]);
        Assert.Equal("ca.example.net", record["issue"]);
    }

    [Fact]
    public void Query_Naptr_DecodesAllFields()
    {
        using var server = SingleRecordServer(DnsWireProtocol.TypeNAPTR,
            DnsPackets.Naptr(order: 100, preference: 50, "s", "SIP+D2T", "",
                DnsPackets.LabelsThenPointer("_sip._tcp")));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeNAPTR, server.Address);

        var record = Assert.IsType<Dictionary<string, object?>>(Assert.Single(result));
        Assert.Equal(100.0, record["order"]);
        Assert.Equal(50.0, record["preference"]);
        Assert.Equal("s", record["flags"]);
        Assert.Equal("SIP+D2T", record["service"]);
        Assert.Equal("", record["regexp"]);
        Assert.Equal("_sip._tcp.example.com", record["replacement"]);
    }

    [Fact]
    public void Query_UncompressedAnswerName_Parses()
    {
        // Answer NAME spelled out as labels instead of the usual pointer.
        using var server = SingleRecordServer(DnsWireProtocol.TypeTXT,
            DnsPackets.Txt("plain"), name: DnsPackets.Name("example.com"));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var chunks = Assert.IsType<List<object?>>(Assert.Single(result));
        Assert.Equal("plain", Assert.Single(chunks));
    }

    [Fact]
    public void Query_SkipsRecordsOfOtherTypes()
    {
        // CNAME + TXT in one response; a TXT query must skip the CNAME via RDLENGTH.
        using var server = new FakeDnsServer((request, _) => DnsPackets.Response(request, 0,
            DnsPackets.Record(DnsWireProtocol.TypeCNAME, DnsPackets.Name("alias.example.net")),
            DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("after-cname"))));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var chunks = Assert.IsType<List<object?>>(Assert.Single(result));
        Assert.Equal("after-cname", Assert.Single(chunks));
    }

    [Fact]
    public void Query_TruncatedUdpResponse_FallsBackToTcp()
    {
        using var server = new FakeDnsServer(
            (request, _) => DnsPackets.Truncated(request),
            tcpResponder: request => DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("via-tcp"))));

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var chunks = Assert.IsType<List<object?>>(Assert.Single(result));
        Assert.Equal("via-tcp", Assert.Single(chunks));
        Assert.Equal(1, server.QueryCount);
        Assert.Equal(1, server.TcpQueryCount);
    }

    [Theory]
    [InlineData((byte)1, "EFORMERR")]
    [InlineData((byte)3, "ENOTFOUND")]
    [InlineData((byte)4, "ENOTIMP")]
    public void Query_NonTransientErrorRcode_ThrowsWithoutRetry(byte rcode, string expectedCode)
    {
        using var server = new FakeDnsServer((request, _) => DnsPackets.Response(request, rcode));

        var ex = Assert.Throws<NodeError>(() =>
            DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address));

        Assert.Contains(expectedCode, ex.Message);
        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(1, server.QueryCount); // unlike SERVFAIL/REFUSED, these are final
    }

    [Fact]
    public void Query_NoMatchingAnswers_ThrowsEnodata()
    {
        using var server = new FakeDnsServer((request, _) => DnsPackets.Response(request));

        var ex = Assert.Throws<NodeError>(() =>
            DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address));

        Assert.Contains("ENODATA", ex.Message);
        Assert.Equal("ENODATA", ex.Code);
    }

    [Fact]
    public void Query_ShortResponse_ThrowsEaiFail()
    {
        // 5 bytes with a valid transaction ID — passes the ID check, fails header parsing.
        using var server = new FakeDnsServer((request, _) =>
            [request[0], request[1], 0x81, 0x80, 0x00]);

        var ex = Assert.Throws<NodeError>(() =>
            DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address));

        Assert.Contains("EAI_FAIL", ex.Message);
        Assert.Equal("EAI_FAIL", ex.Code);
    }

    [Fact]
    public void Query_MismatchedTransactionId_DiscardedThenRetried()
    {
        using var server = new FakeDnsServer((request, index) =>
        {
            var response = DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("hello")));
            return index == 0 ? DnsPackets.WithCorruptedId(response) : response;
        });

        var result = (List<object?>)DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var chunks = Assert.IsType<List<object?>>(Assert.Single(result));
        Assert.Equal("hello", Assert.Single(chunks));
        Assert.Equal(2, server.QueryCount);
    }

    [Fact]
    public void Query_PersistentMismatchedTransactionId_ThrowsEtimeoutAfterRetries()
    {
        using var server = new FakeDnsServer((request, _) =>
            DnsPackets.WithCorruptedId(DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("spoofed")))));

        var ex = Assert.Throws<NodeError>(() =>
            DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address));

        Assert.Contains("ETIMEOUT", ex.Message);
        Assert.Equal("ETIMEOUT", ex.Code);
        Assert.Equal(3, server.QueryCount); // initial attempt + MaxRetries (2)
    }
}
