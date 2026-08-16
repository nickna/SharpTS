using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Arrow / function-expression callbacks written INSIDE a generator (<c>function*</c>) body.
/// The compiled path previously failed to collect arrows nested in a yielded expression (so an
/// array-method callback compiled to a null "not callable" value) and emitted capturing arrows
/// with a null display-class target ("Non-static method requires a target"); a <c>this</c> used
/// only inside such an arrow left the generator's receiver field undefined (NRE). See #435 / #669.
/// The interpreter has always been correct.
/// </summary>
public class GeneratorArrowBodyTests
{
    [Theory, ModeData]
    public void Generator_NonCapturingArrowCallbackInsideYield(ExecutionMode mode)
    {
        // #435: the arrow lives inside the yielded expression, so it was never collected →
        // compiled `map` callback was null ("Array.prototype.map callback is not callable").
        var source = """
            function* g(): Generator<string> {
              const a = [1, 2, 3];
              yield "m=" + a.map(n => n * 2).join(",");
            }
            let s = ""; for (const v of g()) s += v;
            console.log(s);
            """;

        Assert.Equal("m=2,4,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CapturingClosureReadsLoopVariable(ExecutionMode mode)
    {
        // #669: per-iteration `for (let k …)` bindings captured by a closure created inside the
        // generator body. Each closure must observe its own iteration's value (0,1,2).
        var source = """
            function* gen() {
              const fns: any[] = [];
              for (let k = 0; k < 3; k++) { fns.push(() => k); }
              let out = "";
              for (let i = 0; i < fns.length; i++) { out += fns[i](); }
              yield out;
            }
            console.log(gen().next().value);
            """;

        Assert.Equal("012\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ArrowReadsCapturedLocal(ExecutionMode mode)
    {
        var source = """
            function* g() {
              const base = 10;
              yield [1, 2, 3].map(x => x + base).join(",");
            }
            console.log(g().next().value);
            """;

        Assert.Equal("11,12,13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ArrowReadsCapturedParameter(ExecutionMode mode)
    {
        var source = """
            function* g(off: number) {
              yield [1, 2, 3].map(x => x + off).join(",");
            }
            console.log(g(100).next().value);
            """;

        Assert.Equal("101,102,103\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ArrowReadsCapturedTopLevelVar(ExecutionMode mode)
    {
        // #732: a TOP-LEVEL (module-level) variable captured by an arrow inside the generator body.
        // The arrow reaches it through its $entryPointDC field, which the compiled generator MoveNext
        // previously never populated → NullReferenceException when the arrow ran. Distinct from a
        // captured generator LOCAL (snapshot path); top-level vars route through the entry-point DC.
        var source = """
            const base = 10;
            function* g() {
              yield [1, 2, 3].map(x => x + base).join(",");
            }
            console.log(g().next().value);
            """;

        Assert.Equal("11,12,13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ArrowWritesCapturedTopLevelVar(ExecutionMode mode)
    {
        // #732 (write case): an arrow inside the generator body WRITES a captured top-level binding.
        // Top-level captures route through $entryPointDC (not the arrow's snapshot field map), so the
        // write reaches the module variable directly — no function display class involved.
        var source = """
            let outer = 0;
            function* g() { [1, 2, 3].forEach(n => outer += n); yield outer; }
            console.log(g().next().value);
            console.log(outer);
            """;

        Assert.Equal("6\n6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodArrowReadsCapturedTopLevelVar(ExecutionMode mode)
    {
        // #732 for an INSTANCE generator method — the separate EmitGeneratorMethodBody context also
        // needed the per-arrow $entryPointDC field map wired in.
        var source = """
            const base = 10;
            class C { *gen() { yield [1, 2, 3].map(x => x + base).join(","); } }
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("11,12,13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodArrowWritesCapturedTopLevelVar(ExecutionMode mode)
    {
        // #732 instance-method write case.
        var source = """
            let outer = 0;
            class C { *gen() { [1, 2, 3].forEach(n => outer += n); yield outer; } }
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodArrowCapturesThis(ExecutionMode mode)
    {
        // #435/#669: `this` is referenced only inside the arrow, so the generator analyzer must
        // still materialize the receiver (<>4__this) for the arrow's capture to be non-null.
        var source = """
            class C {
              v = 7;
              *gen() { yield [1, 2, 3].map(x => x + this.v).join(","); }
            }
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("8,9,10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ForEachMutatesCapturedObjectNotBinding(ExecutionMode mode)
    {
        // A callback that mutates the captured array OBJECT (push) — not the binding — is fine:
        // the reference is shared, only the binding-write case (#674) is unsupported.
        var source = """
            function* g() {
              const acc: number[] = [];
              [1, 2, 3].forEach(x => acc.push(x * 2));
              yield acc.join(",");
            }
            console.log(g().next().value);
            """;

        Assert.Equal("2,4,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ForEachWritesCapturedBinding(ExecutionMode mode)
    {
        // #674: an arrow that WRITES a captured generator local. The mutation must reach the
        // generator's own storage — a shared function-level display class threaded through the
        // generator state machine in compiled mode (the interpreter has always been correct).
        var source = """
            function* g() {
              const a = [1, 2, 3]; let s = "";
              a.forEach(n => s += n);
              yield s;
            }
            console.log(g().next().value);
            """;

        Assert.Equal("123\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CallbackAccumulatesIntoCapturedNumber(ExecutionMode mode)
    {
        // #674, numeric accumulator — the canonical `reduce`-by-side-effect shape.
        var source = """
            function* g() {
              let sum = 0;
              [1, 2, 3].forEach(n => sum += n);
              yield sum;
            }
            console.log(g().next().value);
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_MultipleMutatedCaptures(ExecutionMode mode)
    {
        // #674: two distinct captured locals mutated by one callback — each gets its own DC field.
        var source = """
            function* g() {
              let sum = 0; let product = 1;
              [1, 2, 3, 4].forEach(n => { sum += n; product *= n; });
              yield `${sum},${product}`;
            }
            console.log(g().next().value);
            """;

        Assert.Equal("10,24\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CallbackMutatesCapturedParameter(ExecutionMode mode)
    {
        // #674: the mutated capture is a generator PARAMETER — the stub seeds it into the DC.
        var source = """
            function* g(acc: number) {
              [1, 2, 3].forEach(n => acc += n);
              yield acc;
            }
            console.log(g(100).next().value);
            """;

        Assert.Equal("106\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedCallbackWritesCaptureThroughToGenerator(ExecutionMode mode)
    {
        // #674 (the case the compile-time guard could not see): a NESTED arrow writes a variable
        // captured all the way through to the generator scope. The DC threads through both arrows.
        var source = """
            function* g() {
              let sum = 0;
              [[1, 2], [3, 4]].forEach(row => row.forEach(m => sum += m));
              yield sum;
            }
            console.log(g().next().value);
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_MutatedAndReadOnlyCapturesMixed(ExecutionMode mode)
    {
        // #674: a read-only capture (`base`, by-value snapshot) and a mutated capture (`total`,
        // function DC) coexist in the same callback.
        var source = """
            function* g() {
              const base = 10; let total = 0;
              [1, 2, 3].forEach(n => total += n + base);
              yield total;
            }
            console.log(g().next().value);
            """;

        Assert.Equal("36\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_CapturedWriteSurvivesAcrossYield(ExecutionMode mode)
    {
        // #674: the mutated capture is also live across a yield. The DC lives on a state-machine
        // field, so it persists across the suspension just like a hoisted local.
        var source = """
            function* g() {
              let s = 0;
              yield "before:" + s;
              [1, 2, 3].forEach(n => s += n);
              yield "after:" + s;
            }
            const it = g();
            let out = it.next().value + "|" + it.next().value;
            console.log(out);
            """;

        Assert.Equal("before:0|after:6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodWritesCapturedBinding(ExecutionMode mode)
    {
        // #724: an INSTANCE generator method whose arrow WRITES a captured method local. The method's
        // state machine is now wired with a function display class (registered in DefineClass before
        // PropagateFunctionDCRequirements), the instance-method analogue of #674's free-function path.
        var source = """
            class C {
              *gen() {
                let s = 0;
                [1, 2, 3].forEach(n => s += n);
                yield s;
              }
            }
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodCallbackMutatesCapturedParameter(ExecutionMode mode)
    {
        // #724: the mutated capture is a method PARAMETER (value-typed in the stub). The instance stub
        // seeds it into the DC at arg offset 1 (this at 0), boxing the value type before the store.
        var source = """
            class C { *gen(acc: number) { [1, 2, 3].forEach(n => acc += n); yield acc; } }
            console.log(new C().gen(100).next().value);
            """;

        Assert.Equal("106\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodMixesThisAndMutatedLocal(ExecutionMode mode)
    {
        // #724 + #435: the arrow reads `this` (via the state machine's ThisField) AND writes a captured
        // local (via the function DC) in the same callback.
        var source = """
            class C {
              base = 10;
              *gen() { let s = 0; [1, 2, 3].forEach(n => s += n + this.base); yield s; }
            }
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("36\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InstanceMethodCapturedWriteSurvivesAcrossYield(ExecutionMode mode)
    {
        // #724: the mutated capture is live across a yield — the DC lives on a state-machine field so
        // it persists across the suspension.
        var source = """
            class C {
              *gen() { let s = 0; yield "b:" + s; [1, 2, 3].forEach(n => s += n); yield "a:" + s; }
            }
            const it = new C().gen();
            console.log(it.next().value + "|" + it.next().value);
            """;

        Assert.Equal("b:0|a:6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_TwoClassesSameMethodNameWriteCaptureIndependently(ExecutionMode mode)
    {
        // #724: two classes declaring a `*g()` with a write-capture must get DISTINCT function display
        // classes — the registry key is qualified by class name, so they don't clobber each other.
        var source = """
            class A { *g() { let s = 0; [1, 2].forEach(n => s += n); yield s; } }
            class B { *g() { let s = 100; [1, 2].forEach(n => s += n); yield s; } }
            console.log(new A().g().next().value, new B().g().next().value);
            """;

        Assert.Equal("3 103\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_WritesCapturedBinding(ExecutionMode mode)
    {
        // #725: a sync arrow inside an ASYNC generator (`async function*`) body that WRITES a captured
        // generator local. The async-generator state machine — a reference type — is now wired with a
        // function display class (the async analogue of #674), so the write reaches shared storage and
        // the generator yields 6. Previously the compiled path SILENTLY dropped the write (yielded 0),
        // then (after #674) fail-fast; now it works.
        var source = """
            async function* g() {
              let sum = 0;
              [1, 2, 3].forEach(n => sum += n);
              yield sum;
            }
            (async () => { for await (const v of g()) console.log(v); })();
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_CapturedWriteSurvivesAcrossAwait(ExecutionMode mode)
    {
        // #725: the mutated capture is live across an actual `await` suspension — the DC lives on the
        // (reference-type) state machine, so it persists across the await like a hoisted local.
        var source = """
            async function* g() {
              let sum = 0;
              await Promise.resolve(1);
              [1, 2, 3].forEach(n => sum += n);
              yield sum;
            }
            (async () => { for await (const v of g()) console.log(v); })();
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_MixedMutatedAndReadOnlyCaptures(ExecutionMode mode)
    {
        // #725: a read-only capture (`base`, by-value snapshot) and a mutated capture (`total`, function
        // DC) coexist in the same callback — the snapshot/DC split mirrors the sync generator case.
        var source = """
            async function* g() {
              const base = 10; let total = 0;
              [1, 2, 3].forEach(n => total += n + base);
              yield total;
            }
            (async () => { for await (const v of g()) console.log(v); })();
            """;

        Assert.Equal("36\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_InstanceMethodWritesCapturedBinding(ExecutionMode mode)
    {
        // #725: the async-generator INSTANCE method analogue — the function DC is registered in
        // DefineClass (before Phase 5) and wired into the method's state machine at emit time.
        var source = """
            class C {
              async *gen() {
                let s = 0;
                [1, 2, 3].forEach(n => s += n);
                yield s;
              }
            }
            (async () => { for await (const v of new C().gen()) console.log(v); })();
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ArrowReadsCapturedTopLevelVar(ExecutionMode mode)
    {
        // #725 also threads $entryPointDC into arrows nested in an async generator body (the async
        // analogue of #732), so a captured TOP-LEVEL variable read works rather than NRE-ing.
        var source = """
            const base = 10;
            async function* g() { yield [1, 2, 3].map(x => x + base).join(","); }
            (async () => { for await (const v of g()) console.log(v); })();
            """;

        Assert.Equal("11,12,13\n", TestHarness.Run(source, mode));
    }

    // #792: a defaulted parameter that is ALSO captured-and-mutated by a nested arrow. The default
    // prologue (#737) wrote the default to the state-machine field, but a captured parameter's live
    // storage is the function DC field (#674/#724/#725) — so an omitted argument left the arrow
    // reading the $Undefined sentinel (NaN for value defaults, the missing string for ref defaults).
    // Only the omitted-arg path was broken; supplying the argument always worked.

    [Theory, ModeData]
    public void Generator_DefaultedParamCapturedByArrow_OmittedUsesDefault(ExecutionMode mode)
    {
        // value-type default, captured by a forEach arrow — free-function generator.
        var source = """
            function* g(acc: number = 5): Generator<number> { [1, 2, 3].forEach(n => acc += n); yield acc; }
            console.log([...g()][0]);
            """;

        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_DefaultedParamCapturedByArrow_SuppliedWins(ExecutionMode mode)
    {
        // Regression guard: a supplied argument must beat the default on the captured path too.
        var source = """
            function* g(acc: number = 5): Generator<number> { [1, 2, 3].forEach(n => acc += n); yield acc; }
            console.log([...g(100)][0]);
            """;

        Assert.Equal("106\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_RefDefaultedParamCapturedByArrow_InstanceMethod(ExecutionMode mode)
    {
        // reference-type (string) default, captured — instance generator method.
        var source = """
            class C { *gen(s: string = "x"): Generator<string> { [1, 2, 3].forEach(n => s += n); yield s; } }
            console.log([...new C().gen()][0]);
            console.log([...new C().gen("Y")][0]);
            """;

        Assert.Equal("x123\nY123\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_DefaultedParamCapturedByArrow_OmittedUsesDefault(ExecutionMode mode)
    {
        // value-type default, captured — async generator.
        var source = """
            async function* ag(acc: number = 5) { [1, 2, 3].forEach(n => acc += n); yield acc; }
            (async () => { for await (const v of ag()) console.log(v); })();
            """;

        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ClassExpressionMethodWritesCapturedBinding(ExecutionMode mode)
    {
        // #789: the class-EXPRESSION analogue of #724. Class expressions are only collected during the
        // Phase-5 arrow walk, so their generator-method function display classes are registered in
        // FinalizeArrowFunctionCollection (before PropagateFunctionDCRequirements) rather than DefineClass.
        var source = """
            const C = class {
              *gen(arr: number[]) {
                let sum = 0;
                arr.forEach(n => sum += n);
                yield sum;
              }
            };
            console.log([...new C().gen([1, 2, 3])].join(","));
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ClassExpressionMethodMutatesCapturedParameter(ExecutionMode mode)
    {
        // #789: the mutated capture is a method PARAMETER (value-typed in the stub) — class-expr analogue
        // of the #724 parameter case.
        var source = """
            const C = class { *gen(acc: number) { [1, 2, 3].forEach(n => acc += n); yield acc; } };
            console.log(new C().gen(100).next().value);
            """;

        Assert.Equal("106\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ClassExpressionMethodMixesThisAndMutatedLocal(ExecutionMode mode)
    {
        // #789: the arrow reads `this` (state machine ThisField) AND writes a captured local (function DC)
        // in the same callback — class-expr analogue of the #724 this+local case.
        var source = """
            const C = class {
              base = 10;
              *gen() { let s = 0; [1, 2, 3].forEach(n => s += n + this.base); yield s; }
            };
            console.log(new C().gen().next().value);
            """;

        Assert.Equal("36\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_ClassExpressionMethodCapturedWriteSurvivesAcrossYield(ExecutionMode mode)
    {
        // #789: the mutated capture is live across a yield — the DC lives on a state-machine field so it
        // persists across the suspension (class-expr analogue of the #724 cross-yield case).
        var source = """
            const C = class {
              *gen() { let s = 0; yield "b:" + s; [1, 2, 3].forEach(n => s += n); yield "a:" + s; }
            };
            const it = new C().gen();
            console.log(it.next().value + "|" + it.next().value);
            """;

        Assert.Equal("b:0|a:6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ClassExpressionMethodWritesCapturedBinding(ExecutionMode mode)
    {
        // #789: the async-generator class-EXPRESSION analogue of #725 — the function DC is registered in
        // FinalizeArrowFunctionCollection and wired into the method's (reference-type) state machine.
        var source = """
            const C = class {
              async *gen() {
                let s = 0;
                [1, 2, 3].forEach(n => s += n);
                yield s;
              }
            };
            (async () => { for await (const v of new C().gen()) console.log(v); })();
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }
}
