using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// A SYNC arrow nested in an ASYNC METHOD that WRITES a variable captured from the method scope must
/// share storage with the method body (live capture), not snapshot it by value. Compiled mode used to
/// drop the write: async methods only lifted direct-child ASYNC-arrow writes into the function display
/// class (#682) and never registered the method in <c>FunctionAstNodes</c>, so a nested sync arrow could
/// not resolve the method's DC and fell back to a by-value snapshot field (interpreted 6,6 vs compiled
/// 6,5). The fix registers the method's function DC in Phase 4 (mirroring generator methods / free async
/// functions) lifting the shared captured locals. The interpreter has always been correct.
/// </summary>
public class AsyncMethodNestedCaptureTests
{
    [Theory, ModeData]
    public void AsyncInstanceMethod_SyncArrowWritesCapturedLocal(ExecutionMode mode)
    {
        var source = """
            class C {
              async m(): Promise<string> {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                await Promise.resolve(0);
                return f() + "," + r;
              }
            }
            async function main() { console.log(await new C().m()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncStaticMethod_SyncArrowWritesCapturedLocal(ExecutionMode mode)
    {
        var source = """
            class C {
              static async m(): Promise<string> {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                await Promise.resolve(0);
                return f() + "," + r;
              }
            }
            async function main() { console.log(await C.m()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_SyncArrowCompoundAssignCapturedLocal(ExecutionMode mode)
    {
        // `+=` and `++` go through distinct emitter paths than plain `=` — both must route to the DC.
        var source = """
            class C {
              async m(): Promise<string> {
                let r = 5;
                const f = () => { r += 1; r++; return r; };
                await Promise.resolve(0);
                return f() + "," + r;
              }
            }
            async function main() { console.log(await new C().m()); }
            main();
            """;

        Assert.Equal("7,7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_TwoArrowsWriteDistinctCapturedLocals(ExecutionMode mode)
    {
        // Two independent captured locals, each written by its own arrow, must not cross-contaminate.
        var source = """
            class C {
              async m(): Promise<string> {
                let a = 1;
                let b = 10;
                const f = () => { a = a + 1; return a; };
                const g = () => { b = b + 1; return b; };
                await Promise.resolve(0);
                return f() + "," + g() + "," + a + "," + b;
              }
            }
            async function main() { console.log(await new C().m()); }
            main();
            """;

        Assert.Equal("2,11,2,11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_WriteCapturedBlockScopeShadow_DoesNotLeak(ExecutionMode mode)
    {
        // Combines this fix with #838: the written capture is a nested-block shadow of an outer binding;
        // the inner shadow's DC field must not collide with the outer same-named binding.
        var source = """
            class C {
              async m(): Promise<string> {
                const out: string[] = [];
                const r = 100;
                {
                  let r = 5;
                  const f = () => { r = r + 1; return r; };
                  await Promise.resolve(0);
                  out.push(String(f()));
                  out.push(String(r));
                }
                out.push(String(r));
                return out.join(",");
              }
            }
            async function main() { console.log(await new C().m()); }
            main();
            """;

        Assert.Equal("6,6,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_AsyncArrowWriteCaptureStillWorks(ExecutionMode mode)
    {
        // Regression guard for #682: a direct-child ASYNC arrow writing a captured method local still
        // shares storage through the (now Phase-4-registered) function DC.
        var source = """
            class C {
              async m(): Promise<number> {
                let sum = 0;
                const f = async () => { sum += 5; };
                await f();
                return sum;
              }
            }
            async function main() { console.log(await new C().m()); }
            main();
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }
}
