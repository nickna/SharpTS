using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Tests DnsWireProtocol retry behavior against a local fake DNS server.
/// SERVFAIL/REFUSED responses are transient resolver conditions and must be
/// retried like socket errors before surfacing an error.
/// </summary>
public class DnsWireProtocolRetryTests
{
    private const byte Servfail = 2;
    private const byte Refused = 5;

    private static byte[] TxtHello(byte[] request) =>
        DnsPackets.Response(request, 0,
            DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("hello")));

    [Theory]
    [InlineData(Servfail)]
    [InlineData(Refused)]
    public void Query_TransientErrorThenSuccess_Retries(byte rcode)
    {
        using var server = new FakeDnsServer((request, index) =>
            index < 1 ? DnsPackets.Response(request, rcode) : TxtHello(request));

        var result = DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address);

        var records = Assert.IsType<List<object?>>(result);
        var record = Assert.IsType<List<object?>>(Assert.Single(records));
        Assert.Equal("hello", Assert.Single(record));
        Assert.Equal(2, server.QueryCount);
    }

    [Fact]
    public void Query_PersistentServfail_ThrowsEservfailAfterRetries()
    {
        using var server = new FakeDnsServer((request, _) =>
            DnsPackets.Response(request, Servfail));

        var ex = Assert.Throws<NodeError>(() =>
            DnsWireProtocol.Query("example.com", DnsWireProtocol.TypeTXT, server.Address));

        Assert.Contains("ESERVFAIL", ex.Message);
        Assert.Equal("ESERVFAIL", ex.Code);
        Assert.Equal(3, server.QueryCount); // initial attempt + MaxRetries (2)
    }
}
