using SharpTS.Diagnostics.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// An async arrow nested directly inside a TOP-LEVEL (standalone) async arrow that captures a
/// variable from the enclosing arrow's scope. Compiled mode registers the inner arrow as its own
/// standalone state machine; #641 wired the single read-only capture and #684 the multi-capture
/// case: every captured value rides in a single <c>object[]</c> passed as the $TSFunction target
/// slot, which the stub unpacks into the inner state machine's capture fields. Writing a capture
/// is still rejected with a clear compile error rather than miscompiled (#684/#682): a standalone
/// arrow captures by value, so the write could not propagate back.
/// </summary>
public class AsyncArrowNestedCaptureTests
{
    [Theory, ModeData]
    public void NestedAsyncArrow_ReadsSingleCapture(ExecutionMode mode)
    {
        var source = """
            const f = async () => {
              const base = 100;
              const x = await (async () => base + 5)();
              console.log(x);
            };
            f();
            """;

        Assert.Equal("105\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_ReadsCaptureAndOwnParameter(ExecutionMode mode)
    {
        var source = """
            const f = async () => {
              const base = 100;
              const inner = async (k: number) => base + k;
              const x = await inner(5);
              console.log(x);
            };
            f();
            """;

        Assert.Equal("105\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void NestedAsyncArrow_ReadsSingleCapture_PassesILVerification()
    {
        var source = """
            const f = async () => {
              const base = 100;
              const x = await (async () => base + 5)();
              console.log(x);
            };
            f();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("105\n", output);
    }

    [Fact]
    public void NestedAsyncArrow_WritesCapture_RejectedInCompiledMode()
    {
        // Standalone arrows capture by value, so a write cannot propagate back. Compiled mode must
        // reject this rather than silently dropping the write (#684/#682); the interpreter is correct.
        var source = """
            const f = async () => {
              let n = 1;
              const g = async () => { n = await (async () => n + 10)(); };
              await g();
              console.log(n);
            };
            f();
            """;

        Assert.Equal("11\n", TestHarness.RunInterpreted(source));
        Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_MultipleCaptures(ExecutionMode mode)
    {
        // Three captures from the enclosing top-level async arrow ride together in the
        // single object[] target slot (#684); the stub unpacks each into its own field.
        var source = """
            const f = async () => {
              const a = 10, b = 20, c = 30;
              const inner = async () => a + b + c;
              console.log(await inner());
            };
            f();
            """;

        Assert.Equal("60\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_MultipleCaptures_MixedWithOwnParameter(ExecutionMode mode)
    {
        // Captures are unpacked from the object[] target before the arrow's own parameter,
        // so capture/param ordering in the stub must stay distinct.
        var source = """
            const f = async () => {
              const a = 1, b = 2;
              const inner = async (k: number) => a + b + k;
              console.log(await inner(100));
            };
            f();
            """;

        Assert.Equal("103\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_MultipleCapturesOfEnclosingParameters(ExecutionMode mode)
    {
        // Captures resolve through the enclosing arrow's ParameterFields (not LocalFields),
        // a distinct LoadVariableForCapture branch; both ride the object[] target slot.
        var source = """
            const f = async (p: number, q: number) => {
              const inner = async () => p + q;
              console.log(await inner());
            };
            f(5, 37);
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneAsyncArrow_MultipleCapturesFromEnclosingFunction(ExecutionMode mode)
    {
        // A standalone async arrow (no enclosing async state machine — here the enclosing
        // scope is a plain function) capturing multiple of that function's locals was
        // independently broken (#684 note): ILEmitter packed an object[] target but the
        // stub read each capture from a separate arg. Now both agree on the single
        // object[] slot. (Top-level `const` captures resolve to static/entry-point fields,
        // not standalone captures, so a plain function is what exercises this path.)
        var source = """
            function outer() {
              const a = 10, b = 20, c = 30;
              const inner = async () => a + b + c;
              inner().then(x => console.log(x));
            }
            outer();
            """;

        Assert.Equal("60\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_TransitiveCaptureThroughStandaloneArrow(ExecutionMode mode)
    {
        // `a` is captured by `g` (a standalone capture on g's own state machine, because g
        // reads it directly) and then again by `h` nested inside g — so when building h's
        // capture array we must re-read `a` from g's capture field, not drop it to null.
        // (The "relay-only" shape where the intermediate arrow does NOT read the variable and
        // captures it solely to forward to a deeper arrow is exercised below — #716.)
        var source = """
            const f = async () => {
              const a = 7;
              const g = async () => {
                const seen = a;
                const h = async () => a + 1;
                return seen + await h();
              };
              console.log(await g());
            };
            f();
            """;

        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_RelayOnlyCaptureToDeeperArrow(ExecutionMode mode)
    {
        // #716: the intermediate arrow `g` does NOT itself read `a` — it captures it solely so the
        // deeper nested `h` can. ClosureAnalyzer now propagates the capture up the standalone async
        // arrow chain (onto async-arrow frames only), so g's state machine carries `a` and forwards
        // it; previously g omitted `a`, h read it as null, and compiled mode printed 1 instead of 8.
        var source = """
            const f = async () => {
              const a = 7;
              const g = async () => {
                const h = async () => a + 1;
                return await h();
              };
              console.log(await g());
            };
            f();
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_RelayOnlyCaptureThroughTwoIntermediates(ExecutionMode mode)
    {
        // #716 across two relay-only intermediates: both `g` and `h` forward `a` to the deepest `k`.
        var source = """
            const f = async () => {
              const a = 5;
              const g = async () => {
                const h = async () => {
                  const k = async () => a * 2;
                  return await k();
                };
                return await h();
              };
              console.log(await g());
            };
            f();
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_RelayOnlyMultipleCaptures(ExecutionMode mode)
    {
        // #716: more than one relay-only capture forwarded through the intermediate arrow.
        var source = """
            const f = async () => {
              const a = 3; const b = 100;
              const g = async () => {
                const h = async () => a + b;
                return await h();
              };
              console.log(await g());
            };
            f();
            """;

        Assert.Equal("103\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void NestedAsyncArrow_RelayOnlyCaptureToDeeperArrow_PassesILVerification()
    {
        var source = """
            const f = async () => {
              const a = 7;
              const g = async () => {
                const h = async () => a + 1;
                return await h();
              };
              console.log(await g());
            };
            f();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void NestedAsyncArrow_MultipleCaptures_PassesILVerification()
    {
        var source = """
            const f = async () => {
              const a = 10, b = 20, c = 30;
              const inner = async () => a + b + c;
              console.log(await inner());
            };
            f();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void StandaloneAsyncArrow_SingleCaptureFromFunction_PassesILVerification()
    {
        // Exercises the module-level (ILEmitter) standalone-capture path with exactly one
        // capture, which now also rides the single object[] target slot (previously a raw
        // value). Guards against a regression in the unified single/multi capture passing.
        var source = """
            function outer() {
              const base = 41;
              const inner = async () => base + 1;
              inner().then(x => console.log(x));
            }
            outer();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }
}
