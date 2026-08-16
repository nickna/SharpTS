using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #543: an error caught by a <c>try</c>/<c>catch</c> <em>inside</em> a compiled
/// generator body must satisfy <c>instanceof</c>, not merely carry the right <c>.name</c>/<c>.message</c>.
/// The catch binding in the generator's <c>MoveNext</c> previously left the caught value as something
/// whose prototype chain failed the <c>instanceof</c> check while the structural fields still read
/// correctly. The compiled path now produces a real error object in both the immediate and the
/// flag-based (post-yield) try shapes.
///
/// <para>
/// The runtime "not a function" cases are CompiledOnly: the interpreter has a separate, broader gap —
/// a property access / call on <c>undefined</c> throws a raw host "Runtime Error" string rather than a
/// guest <c>TypeError</c>, even at top level outside any generator — tracked in #676. Explicitly thrown
/// errors carry correct identity in both modes and are asserted across both.
/// </para>
/// </summary>
public class GeneratorErrorIdentityTests
{
    [Theory, CompiledOnlyData]
    public void RuntimeTypeError_CaughtInGeneratorBody_InstanceofHolds(ExecutionMode mode)
    {
        // The exact #543 repro: a "not a function" TypeError caught inside the generator body.
        var source = """
            function* g() {
              const o: any = undefined;
              try { o.foo(); } catch (e: any) { console.log((e instanceof TypeError) + " " + e.name); }
              yield 1;
            }
            g().next();
            """;

        Assert.Equal("true TypeError\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void RuntimeTypeError_CaughtInGeneratorBody_AfterYield_InstanceofHolds(ExecutionMode mode)
    {
        // The flag-based shape: a yield before the throwing call forces the try into the flag-based
        // scheme, whose catch binding must also produce a real TypeError.
        var source = """
            function* g() {
              const o: any = undefined;
              try { yield 0; o.foo(); } catch (e: any) { console.log((e instanceof TypeError) + " " + e.name); }
              yield 1;
            }
            const it = g(); it.next(); it.next();
            """;

        Assert.Equal("true TypeError\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExplicitlyThrownError_CaughtInGeneratorBody_InstanceofHolds(ExecutionMode mode)
    {
        // Explicit `throw new RangeError(...)` caught in-body keeps its identity in both modes.
        var source = """
            function* g() {
              try { yield 0; throw new RangeError("rr"); }
              catch (e: any) { console.log((e instanceof RangeError) + " " + (e instanceof Error) + " " + e.name + " " + e.message); }
              yield 1;
            }
            const it = g(); it.next(); it.next();
            """;

        Assert.Equal("true true RangeError rr\n", TestHarness.Run(source, mode));
    }
}
