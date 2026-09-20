using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection(DnsFakeServerEnvCollection.Name)]
public class DnsLookupOptionsTests
{
    internal const string Program = """
        import { lookup, getDefaultResultOrder, setDefaultResultOrder } from 'dns/promises';
        import { lookup as callbackLookup } from 'dns';
        async function main() {
            const original = await lookup('127.0.0.1', {family: 4});
            console.log(original.address);
            console.log(original.family);
            const opposite = await lookup('127.0.0.1', {family: 6, all: true});
            console.log(opposite.length);
            console.log(opposite[0].address);
            console.log(opposite[0].family);
            const ipv6 = await lookup('2001:DB8::1', {family: 4});
            console.log(ipv6.address);
            console.log(ipv6.family);
            const mapped = await lookup('::FFFF:192.0.2.1', {all: true});
            console.log(mapped[0].address);
            console.log(mapped[0].family);
            const ipv4s = await lookup('localhost', {family: 4, all: true});
            console.log(ipv4s.length > 0 && ipv4s.every(x => x.family === 4));
            const raw = await lookup('localhost', {all: true, order: 'verbatim'});
            const expected4 = raw.filter(x => x.family === 4).concat(raw.filter(x => x.family === 6));
            const expected6 = raw.filter(x => x.family === 6).concat(raw.filter(x => x.family === 4));
            const ordered4 = await lookup('localhost', {all: true, order: 'ipv4first'});
            const ordered6 = await lookup('localhost', {all: true, order: 'ipv6first'});
            console.log(JSON.stringify(ordered4) === JSON.stringify(expected4));
            console.log(JSON.stringify(ordered6) === JSON.stringify(expected6));
            const previousOrder = getDefaultResultOrder();
            setDefaultResultOrder('ipv6first');
            try {
                console.log(JSON.stringify(await lookup('localhost', {all: true})) === JSON.stringify(expected6));
                console.log(JSON.stringify(await lookup('localhost')) === JSON.stringify(expected6[0]));
                console.log((await lookup('localhost', 4)).family === 4);
                const legacy = await lookup('localhost', {all: true, verbatim: false});
                const override = await lookup('localhost', {all: true, order: 'ipv4first', verbatim: true});
                console.log(JSON.stringify(legacy) === JSON.stringify(expected4));
                console.log(JSON.stringify(override) === JSON.stringify(expected4));
                console.log((await lookup('localhost', {family: 4})).family);
            } finally { setDefaultResultOrder(previousOrder); }
            await new Promise(resolve => callbackLookup('127.0.0.1', {family: 6, all: true}, (error, values) => {
                console.log(error === null);
                console.log(values.length);
                console.log(values[0].address);
                console.log(values[0].family);
                resolve(null);
            }));
        }
        main();
        """;

    internal const string Expected = "127.0.0.1\n4\n1\n127.0.0.1\n4\n2001:DB8::1\n6\n::FFFF:192.0.2.1\n6\n"
        + "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n4\ntrue\n1\n127.0.0.1\n4\n";

    internal static void RequireLocalhostIPv4()
    {
        // Query the OS resolver before running guest code: this is a host
        // prerequisite, not a reason to hide a failed SharpTS lookup.
        var addresses = System.Net.Dns.GetHostAddresses("localhost");
        Skip.IfNot(addresses.Any(address =>
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork),
            "DNS lookup options require localhost to resolve to IPv4.");
    }

    [SkippableTheory, ModeData]
    public void LookupPreservesLiteralsAndHonorsFamilyAndResultOptions(ExecutionMode mode)
    {
        RequireLocalhostIPv4();
        var output = TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = Program }, "main.ts", mode);
        Assert.Equal(Expected, output);
    }
}
