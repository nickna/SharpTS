using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// #705/#723/#737/#739: in compiled mode, default/optional parameters on class-declaration members
/// were mishandled — the body emitters skipped the runtime entry prologue, value-type-defaulted
/// params kept an unboxed slot that cannot hold the <c>undefined</c> sentinel, and direct calls
/// padded omitted optionals with CLR null instead of <c>undefined</c>. This pins the fixes:
/// <list type="bullet">
/// <item><b>#705/#723</b>: value-type defaults fire (omit → default, explicit <c>undefined</c> →
/// default, present → used) for free functions, constructors, private methods, and now <b>instance
/// and static methods</b> — the latter via value-type slot widening (static: direct; instance:
/// hierarchy-consistent, so override matching is preserved).</item>
/// <item><b>#737</b>: defaults also fire on <b>generator and async-generator methods</b> (their
/// state machines now run a default-parameter prologue).</item>
/// <item><b>#739</b>: a direct instance/static method or constructor call pads an omitted trailing
/// optional (no-default) param with the <c>undefined</c> sentinel, so <c>typeof</c> reads
/// "undefined" (not "object").</item>
/// </list>
/// All run in both modes to pin interpreter/compiler parity.
/// </summary>
public class ClassMethodDefaultParameterTests
{
    // ---- Constructors (parameter properties, value-type + reference) -------

    [Theory, ModeData]
    public void Constructor_ParameterProperty_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class D { constructor(public name: string, public age: number = 99) {} }
            console.log(new D("Bob").age);
            """;
        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Constructor_ParameterProperty_NumericDefault_ExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            class D { constructor(public name: string, public age: number = 99) {} }
            console.log(new D("Bob", undefined).age);
            """;
        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Constructor_ParameterProperty_NumericDefault_Present(ExecutionMode mode)
    {
        var source = """
            class D { constructor(public name: string, public age: number = 99) {} }
            console.log(new D("Bob", 7).age);
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Constructor_StringDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            class D { constructor(public name: string = "anon") {} }
            console.log(new D().name);
            """;
        Assert.Equal("anon\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Constructor_BooleanDefault_ExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            class D { active: boolean; constructor(on: boolean = true) { this.active = on; } }
            console.log(new D(undefined).active);
            console.log(new D(false).active);
            """;
        Assert.Equal("true\nfalse\n", TestHarness.Run(source, mode));
    }

    // ---- Private methods (instance + static, value-type + reference) -------

    [Theory, ModeData]
    public void PrivateMethod_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            class C {
              #add(a: number, b: number = 10): number { return a + b; }
              run(): number { return this.#add(5); }
            }
            console.log(new C().run());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethod_NumericDefault_ExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
              #add(a: number, b: number = 10): number { return a + b; }
              run(): number { return this.#add(5, undefined); }
            }
            console.log(new C().run());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethod_MultipleDefaults_AllOmitted(ExecutionMode mode)
    {
        var source = """
            class C {
              #sum(a: number = 1, b: number = 2, c: number = 3): number { return a + b + c; }
              run(): number { return this.#sum(); }
            }
            console.log(new C().run());
            """;
        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrivateMethod_PartialApplication_RemainingDefaults(ExecutionMode mode)
    {
        var source = """
            class C {
              #sum(a: number, b: number = 2, c: number = 3): number { return a + b + c; }
              run(): number { return this.#sum(10); }
            }
            console.log(new C().run());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticPrivateMethod_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            class C {
              static #add(a: number, b: number = 10): number { return a + b; }
              static run(): number { return C.#add(5); }
            }
            console.log(C.run());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    // ---- Free functions (value-type default + explicit undefined, #705 (a)) ----

    [Theory, ModeData]
    public void FreeFunction_NumericDefault_ExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            function p(a: string, b: number = 2): number { return a.length + b; }
            console.log(p("x", undefined));
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FreeFunction_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            function p(a: string, b: number = 2): number { return a.length + b; }
            console.log(p("x"));
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    // ---- Reference-type defaults on (virtual) instance & static methods ----
    // These fire without changing the slot type, so override matching is preserved.

    [Theory, ModeData]
    public void InstanceMethod_StringDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            class C { greet(name: string = "World"): string { return "Hi " + name; } }
            console.log(new C().greet());
            console.log(new C().greet("Bob"));
            """;
        Assert.Equal("Hi World\nHi Bob\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticMethod_StringDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            class C { static greet(name: string = "World"): string { return "Hi " + name; } }
            console.log(C.greet());
            """;
        Assert.Equal("Hi World\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceMethod_ArgPresent_DefaultNotApplied(ExecutionMode mode)
    {
        // Argument present: no default-firing needed; value-type slot stays fast and correct.
        var source = """
            class C { add(a: number, b: number = 10): number { return a + b; } }
            console.log(new C().add(5, 20));
            """;
        Assert.Equal("25\n", TestHarness.Run(source, mode));
    }

    // ---- Override safety: widening must NOT change a virtual method's signature ----

    [Theory, ModeData]
    public void Override_DerivedAddsDefault_StillOverrides(ExecutionMode mode)
    {
        // Regression guard: a derived override that adds a default to a value-type param the base
        // declares as required must still override (a base-typed call dispatches to the derived
        // method). Widening the derived param to object would silently break this. (#705)
        var source = """
            class B { m(x: number): number { return x; } }
            class D extends B { m(x: number = 5): number { return x * 2; } }
            const b: B = new D();
            console.log(b.m(3));
            """;
        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_BothDefault_DispatchesToDerived(ExecutionMode mode)
    {
        var source = """
            class B { greet(n: string = "B"): string { return "B:" + n; } }
            class D extends B { greet(n: string = "D"): string { return "D:" + n; } }
            const b: B = new D();
            console.log(b.greet());
            """;
        Assert.Equal("D:D\n", TestHarness.Run(source, mode));
    }

    // ---- Different-arity override: derived adds trailing optional/default params (#790) ----
    // A base-typed call must reach the derived override even though its wider CLR arity would
    // otherwise take a new vtable slot. Fixed by emitting a base-arity override bridge.

    [Theory, ModeData]
    public void Override_DerivedAddsValueTypeDefault_BaseTypedDispatchesToDerived(ExecutionMode mode)
    {
        var source = """
            class Base { m(x: number): number { return x; } }
            class Derived extends Base { m(x: number, y: number = 100): number { return x + y; } }
            const b: Base = new Derived();
            console.log(b.m(3));
            """;
        Assert.Equal("103\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_DerivedAddsReferenceTypeDefault_BaseTypedDispatchesToDerived(ExecutionMode mode)
    {
        // Reference-type added default proves the gap is dispatch, not default-firing.
        var source = """
            class B2 { g(x: number): string { return "x" + x; } }
            class D2 extends B2 { g(x: number, s: string = "Z"): string { return "x" + x + s; } }
            const b2: B2 = new D2();
            console.log(b2.g(3));
            """;
        Assert.Equal("x3Z\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_DerivedAddsNoDefaultOptional_BaseTypedDispatchesToDerived(ExecutionMode mode)
    {
        // Added optional with no default: the derived runs with the param undefined.
        var source = """
            class Base { m(x: number): string { return "B:" + x; } }
            class Derived extends Base { m(x: number, y?: number): string { return "D:" + x + ":" + (y === undefined ? "u" : y); } }
            const b: Base = new Derived();
            console.log(b.m(3));
            """;
        Assert.Equal("D:3:u\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_DerivedAddsMultipleDefaults_BaseTypedDispatchesToDerived(ExecutionMode mode)
    {
        var source = """
            class P { f(a: number): string { return "P:" + a; } }
            class Q extends P { f(a: number, b: number = 7, c: string = "z"): string { return "Q:" + a + ":" + b + ":" + c; } }
            const p: P = new Q();
            console.log(p.f(1));
            """;
        Assert.Equal("Q:1:7:z\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_ThreeLevelChain_BaseAndMidTypedReachMostDerived(ExecutionMode mode)
    {
        // Each level adds an arity; both a base-typed and a mid-typed call must land on L2.
        var source = """
            class L0 { m(x: number): string { return "L0:" + x; } }
            class L1 extends L0 { m(x: number, y: number = 1): string { return "L1:" + x + ":" + y; } }
            class L2 extends L1 { m(x: number, y: number = 1, z: number = 2): string { return "L2:" + x + ":" + y + ":" + z; } }
            const asL0: L0 = new L2();
            const asL1: L1 = new L2();
            console.log(asL0.m(3));
            console.log(asL1.m(3));
            """;
        Assert.Equal("L2:3:1:2\nL2:3:1:2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_DerivedAddsDefault_DirectCallStillWorks(ExecutionMode mode)
    {
        // Regression: the direct (derived-typed) call path is unaffected by the bridge.
        var source = """
            class Base { m(x: number): number { return x; } }
            class Derived extends Base { m(x: number, y: number = 100): number { return x + y; } }
            const d = new Derived();
            console.log(d.m(3));
            """;
        Assert.Equal("103\n", TestHarness.Run(source, mode));
    }

    // ---- Value-type defaults on (virtual) instance & static methods (#723/#737) ----

    [Theory, ModeData]
    public void InstanceMethod_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class C { add(a: number, b: number = 10): number { return a + b; } }
            console.log(new C().add(5));
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceMethod_NumericDefault_ExplicitUndefined(ExecutionMode mode)
    {
        var source = """
            class C { add(a: number, b: number = 10): number { return a + b; } }
            console.log(new C().add(5, undefined));
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticMethod_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class C { static add(a: number, b: number = 10): number { return a + b; } }
            console.log(C.add(5));
            console.log(C.add(5, undefined));
            """;
        Assert.Equal("15\n15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceMethod_BooleanDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class C { flag(on: boolean = true): boolean { return on; } }
            console.log(new C().flag());
            console.log(new C().flag(false));
            """;
        Assert.Equal("true\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Override_DerivedAddsDefault_OmittedFiresDerivedDefault(ExecutionMode mode)
    {
        // The derived override adds a value-type default the base declares as required; calling it
        // with the argument omitted must fire the derived default. The hierarchy-consistent widening
        // makes both `m` slots `object`, so the derived prologue can observe `undefined`. (#737)
        var source = """
            class B { m(x: number): number { return x; } }
            class D extends B { m(x: number = 5): number { return x * 2; } }
            console.log(new D().m());
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    // ---- Generator / async-generator method defaults (#737) ----

    [Theory, ModeData]
    public void GeneratorMethod_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class C { *gen(a: number, b: number = 10): Generator<number> { yield a; yield b; } }
            console.log([...new C().gen(1)].join(","));
            console.log([...new C().gen(1, 2)].join(","));
            console.log([...new C().gen(1, undefined)].join(","));
            """;
        Assert.Equal("1,10\n1,2\n1,10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FreeGenerator_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            function* gen(a: number, b: number = 7): Generator<number> { yield a; yield b; }
            console.log([...gen(3)].join(","));
            """;
        Assert.Equal("3,7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorMethod_NumericDefault_Omitted(ExecutionMode mode)
    {
        var source = """
            class C { async *agen(a: number, b: number = 3): AsyncGenerator<number> { yield a; yield b; } }
            (async () => {
              const out: number[] = [];
              for await (const v of new C().agen(100)) out.push(v);
              console.log(out.join(","));
            })();
            """;
        Assert.Equal("100,3\n", TestHarness.Run(source, mode));
    }

    // ---- #739: direct call pads omitted optional with `undefined`, not null ----

    [Theory, ModeData]
    public void InstanceMethod_OmittedOptional_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { m(x?: any): string { return typeof x; } }
            console.log(new C().m());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StaticMethod_OmittedOptional_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { static s(x?: any): string { return typeof x; } }
            console.log(C.s());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Constructor_OmittedOptional_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { kind: string; constructor(x?: any) { this.kind = typeof x; } }
            console.log(new C().kind);
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    // ---- Generator `??` over an omitted optional (regression: #739 padding exposed a latent
    //      state-machine nullish-coalescing bug that ignored the `$Undefined` sentinel) ----

    [Theory, ModeData]
    public void GeneratorNullishCoalescing_OmittedOptional_EvaluatesRight(ExecutionMode mode)
    {
        // `pop(error?)` does `error ?? this.arr.pop()`. With `error` omitted (→ `undefined`), the
        // right side MUST run so the stack drains and the driving `while` terminates — otherwise the
        // generator spins forever (the real-world yaml-parser hang). The compiled state-machine `??`
        // previously caught only CLR null, not the `$Undefined` sentinel.
        var source = """
            class P {
              arr: number[] = [1, 2, 3];
              *pop(error?: any): Generator<string> {
                const token = error ?? this.arr.pop();
                yield "t" + token;
              }
              *drain(): Generator<string> { while (this.arr.length > 0) yield* this.pop(); }
            }
            console.log([...new P().drain()].join(","));
            """;
        Assert.Equal("t3,t2,t1\n", TestHarness.Run(source, mode));
    }

    // ---- IL verification guard ---------------------------------------------

    [Fact]
    public void NonVirtualValueTypeDefaults_ProduceVerifiableIL()
    {
        // Widening defaulted value-type params to object (free fn / private / ctor) and padding
        // private calls must emit verifiable IL — no `ldarg; brfalse` on a double/bool slot, no
        // unbalanced private-call stack.
        var source = """
            class C {
              #p(a: number, b: number = 7): number { return a + b; }
              run(): number { return this.#p(1); }
            }
            class D { constructor(public age: number = 99) {} }
            function f(x: number, y: number = 3): number { return x + y; }
            console.log(new C().run() + new D().age + f(1));
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }

    [Fact]
    public void InstanceStaticAndGeneratorDefaults_ProduceVerifiableIL()
    {
        // Widening value-type defaults on instance (hierarchy-consistent) and static methods,
        // padding omitted optionals with the undefined sentinel, and the generator/async-generator
        // default prologues must all emit verifiable IL.
        var source = """
            class B { m(x: number): number { return x; } }
            class D extends B { m(x: number = 5): number { return x * 2; } }
            class C {
              add(a: number, b: number = 10): number { return a + b; }
              static st(a: number, b: number = 2): number { return a + b; }
              opt(x?: any): string { return typeof x; }
              *gen(a: number, b: number = 1): Generator<number> { yield a; yield b; }
              async *agen(a: number, b: number = 1): AsyncGenerator<number> { yield a; yield b; }
            }
            console.log(new D().m() + C.st(1) + new C().add(1) + new C().opt() + [...new C().gen(1)].length);
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }
}
