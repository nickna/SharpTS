using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Pins the Class.staticMethod dispatch shared between ILEmitter.EmitCall and
/// TryEmitGetCalleeViaBaseClass (TryEmitClassStaticDispatch). The two paths
/// had drifted before — the inline ILEmitter copy once omitted the
/// Promise-subclass arm (MyP.resolve(1) threw), and its argument boxing used
/// EmitBoxIfNeeded while the base used EnsureBoxed (now the EnsureBoxedArg
/// seam). These tests exercise the dispatch with boxing-sensitive arguments
/// from both a top-level (sync ILEmitter) and an async (state-machine base
/// helper) call site.
/// </summary>
public class StaticDispatchBoxingTests
{
    [Theory, ModeData]
    public void StaticMethod_BoxedPrimitiveArgs_SyncTopLevel(ExecutionMode mode)
    {
        var source = """
            class Probe {
                static kind(v: any): string { return typeof v; }
            }
            console.log(Probe.kind(42));
            console.log(Probe.kind(1 + 2));
            console.log(Probe.kind(true));
            console.log(Probe.kind(3 > 2));
            console.log(Probe.kind("s"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\nnumber\nboolean\nboolean\nstring\n", output);
    }

    [Theory, ModeData]
    public void StaticMethod_BoxedPrimitiveArgs_InsideAsync(ExecutionMode mode)
    {
        var source = """
            class Probe {
                static kind(v: any): string { return typeof v; }
            }
            async function go(): Promise<void> {
                console.log(Probe.kind(42));
                console.log(Probe.kind(1 + 2));
                console.log(Probe.kind(true));
                const x: number = await Promise.resolve(5);
                console.log(Probe.kind(x * 2));
            }
            go();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\nnumber\nboolean\nnumber\n", output);
    }

    [Theory, ModeData]
    public void PromiseSubclassStatic_InsideAsync(ExecutionMode mode)
    {
        var source = """
            class MyP<T> extends Promise<T> {}
            async function go(): Promise<void> {
                const v = await MyP.resolve(11);
                console.log("got", v);
                console.log(MyP.resolve(1) instanceof MyP);
            }
            go();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("got 11\ntrue\n", output);
    }
}
