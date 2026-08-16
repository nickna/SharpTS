using System.Reflection;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Regression tests for #721: an <c>async</c> arrow nested in an <c>async</c> class method got TWO async
/// state machines emitted — the live nested one (which the method invokes) and a redundant standalone one
/// (<c>&lt;&gt;c__AsyncArrow_N</c> with <c>&lt;&gt;captured_*</c> fields) that is never invoked.
/// <c>DefineTopLevelAsyncArrows</c> ran before class-method state machines were registered, so a method's
/// arrows looked "unclaimed" and got a dead standalone builder. The fix skips an arrow that an enclosing
/// async function/method's state machine will claim (walking the enclosing-callable chain), while leaving
/// arrows behind a sync arrow/method — or genuinely top-level — with their needed standalone builder.
/// </summary>
public class AsyncArrowNoDeadDuplicateTests
{
    private static int CountAsyncArrowTypes(Assembly asm)
    {
        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        return types.Count(t => t?.Name?.Contains("AsyncArrow") == true);
    }

    [Fact]
    public void AsyncArrowInAsyncMethod_EmitsExactlyOneStateMachine()
    {
        // The #721 repro: one async arrow `w` directly inside the async method `run`.
        var source = """
            class C {
              async run() {
                const w = async () => { await Promise.resolve(1); return 5; };
                return await w();
              }
            }
            new C().run().then(r => console.log(r));
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);

        Assert.Equal("5\n", output);
        // Exactly one async-arrow state machine — the live nested one. Before #721 a second, dead
        // standalone duplicate was also emitted.
        Assert.Equal(1, CountAsyncArrowTypes(assembly));
    }

    [Fact]
    public void NestedAsyncArrowsInAsyncMethod_NoDeadDuplicates()
    {
        // Two async arrows, one nested in the other, both inside the async method: both are claimed by
        // the method's state machine, so neither gets a standalone duplicate (was 4 types, now 2).
        var source = """
            class C {
              async run() {
                const a = async () => {
                  const w = async () => { await Promise.resolve(1); return 8; };
                  return await w();
                };
                return await a();
              }
            }
            new C().run().then(r => console.log(r));
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);

        Assert.Equal("8\n", output);
        Assert.Equal(2, CountAsyncArrowTypes(assembly));
    }

    [Fact]
    public void AsyncArrowInSyncMethod_StillEmitsItsStandalone()
    {
        // An async arrow behind a SYNC method is not claimed by any async state machine, so it still
        // needs (and gets) its standalone builder — the skip must not over-reach.
        var source = """
            class C {
              m() {
                const w = async () => { await Promise.resolve(1); return 5; };
                return w;
              }
            }
            const f = new C().m();
            f().then(r => console.log(r));
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);

        Assert.Equal("5\n", output);
        Assert.Equal(1, CountAsyncArrowTypes(assembly));
    }

    [Fact]
    public void AsyncArrowBehindSyncArrowInAsyncMethod_StillEmitsItsStandalone()
    {
        // An async arrow behind a SYNC arrow (itself in an async method) is not claimed either — the
        // sync arrow breaks the async chain — so its standalone builder must remain.
        var source = """
            class C {
              async run() {
                const a = () => {
                  const w = async () => { await Promise.resolve(1); return 7; };
                  return w;
                };
                return await a()();
              }
            }
            new C().run().then(r => console.log(r));
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);

        Assert.Equal("7\n", output);
        Assert.Equal(1, CountAsyncArrowTypes(assembly));
    }
}
