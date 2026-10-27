using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ExtractedTextDecoderTests
{
    [Theory, ModeData]
    public void DecodeRequiresSelectedDecoderReceiver(ExecutionMode mode)
    {
        const string source = """
            const decoder = new TextDecoder();
            const decode = decoder.decode;
            try { decode(new Uint8Array([65])); console.log("unexpected"); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(decoder.decode(new Uint8Array([65])));
            console.log(decode.call(decoder, new Uint8Array([66])));
            console.log(decode.apply(new TextDecoder(), [new Uint8Array([67])]));
            try { decode.call({}, new Uint8Array([68])); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("true\nA\nB\nC\ntrue\n", TestHarness.Run(source, mode));
    }
}
