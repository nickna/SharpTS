using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Generators must capture outer (closure) variables BY REFERENCE — reading their live value
/// when the body runs — not snapshot the value at generator-creation time. The compiled path
/// previously copied captured top-level variables into state-machine fields when the stub ran,
/// diverging from the interpreter and from JS closure semantics (#541).
/// </summary>
public class GeneratorClosureCaptureTests
{
    [Theory, ModeData]
    public void Generator_CapturesOuterLetByReference_NotSnapshot(ExecutionMode mode)
    {
        // The canonical #541 repro: x is mutated after the generator is created but before
        // the body runs, so next() must observe the new value (42), not the creation-time one.
        var source = """
            let x: any = 1;
            function* g() { yield x; }
            const it = g();
            x = 42;
            console.log(it.next().value);
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_SelfReferenceResolvesLive(ExecutionMode mode)
    {
        // The shape from #521: the generator's own binding is assigned the generator instance
        // after creation; the body must see the live binding ("generator"), not undefined.
        var source = """
            let it: any;
            function* g() { yield (it === undefined ? "undefined" : "generator"); }
            it = g();
            console.log(it.next().value);
            """;

        Assert.Equal("generator\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_MutationBetweenYieldsIsVisible(ExecutionMode mode)
    {
        var source = """
            let x: any = 10;
            function* g() { yield x; yield x; }
            const it = g();
            const a = it.next().value;
            x = 20;
            const b = it.next().value;
            console.log(a, b);
            """;

        Assert.Equal("10 20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_WriteToCapturedVarPropagatesToOuterScope(ExecutionMode mode)
    {
        // A write to a captured variable from inside the generator body must update the
        // enclosing binding (by reference), not a private copy.
        var source = """
            let x: any = 1;
            function* g() { x = 100; yield x; }
            const it = g();
            it.next();
            console.log(x);
            """;

        Assert.Equal("100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_IncrementAndCompoundOnCapturedVar(ExecutionMode mode)
    {
        var source = """
            let x: any = 1;
            function* g() { x++; yield x; x += 10; yield x; }
            const it = g();
            const a = it.next().value;
            const b = it.next().value;
            console.log(a, b, x);
            """;

        Assert.Equal("2 12 12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodCapturesOuterByReference(ExecutionMode mode)
    {
        var source = """
            let x: any = 1;
            class K { *g() { yield x; } }
            const it = new K().g();
            x = 55;
            console.log(it.next().value);
            """;

        Assert.Equal("55\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CapturedVarInLoopBodyReadLive(ExecutionMode mode)
    {
        // Combines by-reference capture with the loop-body hoisting path (#497).
        var source = """
            let base_: any = 100;
            function* g() { for (let i = 0; i < 3; i++) yield base_ + i; }
            const it = g();
            base_ = 200;
            console.log(it.next().value, it.next().value, it.next().value);
            """;

        Assert.Equal("200 201 202\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_YieldStarDelegateSeesCapturedMutation(ExecutionMode mode)
    {
        var source = """
            let x: any = 1;
            function* inner() { yield x; yield x; }
            function* g() { yield* inner(); }
            const it = g();
            x = 9;
            console.log(it.next().value, it.next().value);
            """;

        Assert.Equal("9 9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_MultipleInstancesShareCapturedVar(ExecutionMode mode)
    {
        var source = """
            let x: any = 1;
            function* g() { yield x; }
            const a = g();
            const b = g();
            x = 7;
            console.log(a.next().value, b.next().value);
            """;

        Assert.Equal("7 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ReferencesTopLevelFunctionAsValue(ExecutionMode mode)
    {
        // Referencing a top-level function as a value inside a generator previously read a
        // never-populated capture field and threw at runtime in compiled mode.
        var source = """
            function helper() { return "H"; }
            function* g() { const f: any = helper; yield f(); }
            console.log(g().next().value);
            """;

        Assert.Equal("H\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CapturingArrowCalledInsideBody(ExecutionMode mode)
    {
        // Calling a *capturing* arrow inside a generator body previously failed in compiled mode with
        // "Non-static method requires a target" — the arrow's display instance was not bound as the
        // $TSFunction target. Foundational for lifting capturing nested function-likes (#583).
        var source = """
            function* outer() {
                const x = 10;
                const f = () => x;
                yield f();
            }
            console.log([...outer()].join(","));
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedGeneratorCapturingLocal_IsLifted(ExecutionMode mode)
    {
        // The #583 §1 repro: a nested generator that captures an enclosing local is lambda-lifted (the
        // capture becomes a leading parameter forwarded by an in-place arrow), instead of failing with
        // "Yield not supported in this context" in compiled mode.
        var source = """
            function* outer(): Generator<number> {
                const x = 10;
                function* inner(): Generator<number> { yield x; }
                yield* inner();
            }
            for (const v of outer()) console.log(v);
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_NestedAsyncCapturingLocal_IsLifted(ExecutionMode mode)
    {
        // The async analogue of #583 §1: a nested async function capturing an enclosing local lifts too.
        var source = """
            async function outer(): Promise<number> {
                const x = 10;
                async function inner(): Promise<number> { return x + 1; }
                return await inner();
            }
            outer().then(v => console.log(v));
            """;

        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_PlainNestedFunctionCapturingLocal_IsLifted(ExecutionMode mode)
    {
        // A plain function nested in a generator, capturing an enclosing local (#583 §1, case-B analogue):
        // lambda-lifted with the captured local forwarded as a leading parameter.
        var source = """
            function* outer(): Generator<number> {
                const base = 100;
                function add(d: number): number { return base + d; }
                yield add(1);
                yield add(2);
            }
            console.log([...outer()].join(","));
            """;

        Assert.Equal("101,102\n", TestHarness.Run(source, mode));
    }
}
