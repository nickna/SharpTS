using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// A generator function with no explicit <c>Generator&lt;T&gt;</c> return annotation now infers its yield
/// type from the operand types of its <c>yield</c> / <c>yield*</c> expressions, instead of reusing the
/// (usually empty → <c>void</c>) inferred RETURN type (#548). Once the yield type is real, <c>for...of</c>
/// binds it directly rather than degrading to <c>any</c>, and a nested generator no longer compiles to an
/// invalid <c>IEnumerator&lt;System.Void&gt;</c> (#532).
/// </summary>
public class GeneratorYieldInferenceTests
{
    // ---- #548: yield type derived from yield / yield* operands (was always void) ----

    [Fact]
    public void Generator_InfersYieldType_FromYieldExpressions()
    {
        // The headline #548 example: g() infers Generator<number> (was Generator<void>), so the spread is
        // number[] and reading an element as number type-checks.
        var source = """
            function* g() { yield 1; yield 2; }
            const arr: number[] = [...g()];
            const n: number = arr[0];
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_InferredYield_IsNotVoid_WrongConsumerRejected()
    {
        // The inferred element is number, so assigning it to string is a genuine TS2322 — previously the
        // element was void and the error named the wrong type.
        var source = """
            function* g() { yield 1; }
            const s: string = [...g()][0];
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void Generator_ForOf_BindsInferredYieldType()
    {
        // VisitForOf now extracts Generator.YieldType (it deliberately did not before, because a
        // delegating-only generator inferred a void yield — now fixed).
        var source = """
            function* g() { yield 1; yield 2; }
            for (const x of g()) { const n: number = x; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_ForOf_WrongElementUse_Rejected()
    {
        var source = """
            function* g() { yield 1; }
            for (const x of g()) { const s: string = x; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Generator_YieldStar_ContributesDelegatedElementType()
    {
        // yield* over an iterable contributes the delegate's element type to the enclosing yield type.
        var source = """
            function* g() { yield* [1, 2, 3]; }
            const arr: number[] = [...g()];
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_YieldStar_ElementMismatch_Rejected()
    {
        var source = """
            function* g() { yield* [1, 2, 3]; }
            const arr: string[] = [...g()];
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Generator_MixedYields_UnionsYieldType()
    {
        var source = """
            function* g() { yield 1; yield "a"; }
            const arr: (number | string)[] = [...g()];
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_ReturnValue_DoesNotBecomeYieldType()
    {
        // The `return` value is the (discarded) TReturn, not the yield type: a generator that only yields
        // numbers but returns a string still infers Generator<number>, so the string return must not make
        // the elements assignable to string.
        var source = """
            function* g() { yield 1; return "done"; }
            const s: string = [...g()][0];
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Theory, ModeData]
    public void Generator_InferredYield_IteratesInBothModes(ExecutionMode mode)
    {
        // Type-level change must not perturb interpretation or IL codegen.
        var source = """
            function* g() { yield 10; yield 20; yield 30; }
            let sum = 0;
            for (const x of g()) { sum += x; }
            console.log(sum);
            """;
        Assert.Equal("60\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_EmptyYieldsNever_RunsInBothModes(ExecutionMode mode)
    {
        // An empty generator infers Generator<never>; spreading it yields an empty array. The never element
        // (which maps to System.Void) must fall back to an object collection slot rather than crashing IL
        // emission with "System.Void may not be used as a type argument".
        var source = """
            function outer() { function* g() {} return [...g()]; }
            console.log(outer().length);
            """;
        Assert.Equal("0\n", TestHarness.Run(source, mode));
    }

    // ---- #532: nested generator declaration infers its yield type (compiled no longer System.Void) ----

    [Theory, ModeData]
    public void NestedGenerator_InfersYieldType_RunsInBothModes(ExecutionMode mode)
    {
        // The nested generator's yield type is inferred (number), so the outer function returns number[]
        // and the compiled path no longer emits an invalid IEnumerator<System.Void> (#532).
        var source = """
            function outer() {
              function* g() { yield 42; }
              return [...g()];
            }
            console.log(outer()[0]);
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void NestedGenerator_InferredYield_SatisfiesAnnotatedReturn()
    {
        var source = """
            function outer(): number[] {
              function* g() { yield 42; }
              return [...g()];
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- #661/#658: a class method's inferred return type is now observable at the call site ----
    // CheckClassBody computes the inferred (un-annotated) method return type during the body pass and
    // re-publishes the frozen class afterwards, so `new C().m()` no longer reads the `<inferred>` placeholder.

    [Theory, ModeData]
    public void GeneratorMethod_InferredYield_IsIterable_RunsInBothModes(ExecutionMode mode)
    {
        // The #661 headline (also reported as #687): spreading a generator method with no explicit
        // Generator<T> return type used to fail "must be an iterable type ... got '<inferred>'". The
        // inferred Generator<number> is now visible at the call site, so the spread type-checks.
        var source = """
            class C { *m() { yield 1; yield 2; } }
            console.log([...new C().m()][0]);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GeneratorMethod_InferredYield_WrongElementUse_Rejected()
    {
        // The inferred method element is number, so binding it to string is a genuine TS2322 (not a vacuous
        // pass through the old `<inferred>` ~ any placeholder).
        var source = """
            class C { *m() { yield 1; } }
            const s: string = [...new C().m()][0];
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void GeneratorMethod_InferredYield_ForOf_RunsInBothModes(ExecutionMode mode)
    {
        var source = """
            class C { *m() { yield 10; yield 20; } }
            let total = 0;
            for (const v of new C().m()) { total += v; }
            console.log(total);
            """;
        Assert.Equal("30\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorMethod_InferredYield_RunsInBothModes(ExecutionMode mode)
    {
        // The inferred async generator method computes AsyncGenerator<number> (its yield type), so
        // `for await...of` binds number and the compiled state machine emits a valid element type.
        var source = """
            class C { async *m() { yield 1; yield 2; } }
            async function run() {
              let total = 0;
              for await (const v of new C().m()) { total += v; }
              console.log(total);
            }
            run();
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void StaticGeneratorMethod_InferredYield_Interpreted()
    {
        // The inferred return type propagates for a STATIC generator method too (CheckClassBody writes the
        // resolved type into StaticMethods). Interpreter-only: the compiler does not yet lower a static
        // generator method's body ("Yield not supported in this context"), a pre-existing limitation
        // independent of return-type inference — it fails the same way with an explicit return type (#692).
        var source = """
            class C { static *gen() { yield 7; } }
            console.log([...C.gen()][0]);
            """;
        Assert.Equal("7\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OrdinaryMethod_InferredReturn_WrongAssignment_Rejected()
    {
        // #658 (the non-generator face of the same propagation gap): an ordinary inferred method's return
        // type now reaches the call site, so a bad assignment is the TS2322 tsc reports rather than a
        // vacuous pass through `<inferred>` ~ any.
        var source = """
            class C { name() { return "hi"; } }
            const n: number = new C().name();
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void OrdinaryMethod_InferredReturn_CorrectUse_RunsInBothModes(ExecutionMode mode)
    {
        // The same propagation must not over-reject: a correctly-typed consumer of the inferred return runs.
        var source = """
            class C { name() { return "hi"; } }
            const s: string = new C().name();
            console.log(s);
            """;
        Assert.Equal("hi\n", TestHarness.Run(source, mode));
    }
}
