using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for built-in namespaces/constructors bound as value-position globals:
/// AbortSignal, Intl, ReadableStream, WritableStream, TransformStream,
/// MessageChannel. Interpreter bindings landed with #208; compiled-mode
/// equivalents with #224 (namespace singletons + ConstructDynamicValue +
/// the GetProperty abort-signal dict branch) and #222 (real emitted
/// $MessageChannel/$MessagePort types).
/// </summary>
public class NamespaceGlobalBindingTests
{
    [Theory, ModeData]
    public void AbortSignal_StaticAbort_Resolves(ExecutionMode mode)
    {
        var source = """
            const s: any = AbortSignal.abort("why");
            console.log(s.aborted);
            console.log(s.reason);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nwhy\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_DirectConstruction_Throws(ExecutionMode mode)
    {
        var source = """
            try {
                new (AbortSignal as any)();
            } catch (e) {
                console.log("threw");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    [Theory, ModeData]
    public void Intl_ValuePosition_AndMemberConstruction(ExecutionMode mode)
    {
        var source = """
            const I: any = Intl;
            const nf = new I.NumberFormat("en-US");
            console.log(nf.format(1234.5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,234.5\n", output);
    }

    [Theory, ModeData]
    public void WebStreamConstructors_ValuePosition_AndInstanceof(ExecutionMode mode)
    {
        var source = """
            const ctor: any = ReadableStream;
            console.log(typeof ctor);
            const rs = new ctor({ start(c: any) { c.enqueue(1); c.close(); } });
            console.log(rs instanceof (ReadableStream as any));
            console.log(typeof (WritableStream as any), typeof (TransformStream as any));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\ntrue\nfunction function\n", output);
    }

    // ReadableStream.from(iterable) eagerly drains the iterable into a closed
    // stream (#269). Both modes are covered now that compiled mode emits a
    // $ReadableStream.from static (interp uses SharpTSWebStreamsConstructors).
    [Theory, ModeData]
    public void WebStreamConstructors_From(ExecutionMode mode)
    {
        var source = """
            const fromStream = (ReadableStream as any).from(["a"]);
            console.log(typeof fromStream);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    // #269: the produced stream yields each element of the iterable followed by
    // done — verified by draining it through a reader.
    [Theory, ModeData]
    public void WebStreamConstructors_From_DrainsIterable(ExecutionMode mode)
    {
        var source = """
            async function main() {
                const s = (ReadableStream as any).from(["a", "b"]);
                const r = s.getReader();
                const a = await r.read();
                const b = await r.read();
                const c = await r.read();
                console.log(a.value, a.done);
                console.log(b.value, b.done);
                console.log(c.done);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a false\nb false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void MessageChannel_ValuePosition(ExecutionMode mode)
    {
        var source = """
            const MC: any = MessageChannel;
            console.log(typeof MC);
            const mc = new MC();
            console.log(!!mc.port1, !!mc.port2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\ntrue true\n", output);
    }
}
