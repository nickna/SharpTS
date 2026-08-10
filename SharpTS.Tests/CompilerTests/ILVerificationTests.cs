using SharpTS.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests that verify compiled assemblies produce valid IL.
/// These tests catch IL generation bugs at test time rather than runtime.
/// </summary>
public class ILVerificationTests
{
    [Fact]
    public void SameModuleExportBindingsInFunctionBody_PassILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["state.ts"] = """
                export const seed = 4;
                export let value = seed;
                export function advance(): number { value += seed; return value; }
                """,
            ["main.ts"] = """
                import { advance } from './state';
                console.log(advance());
                """
        };

        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");

        Assert.Empty(errors);
    }

    [Fact]
    public void HttpServerLifecycleRuntime_PassesILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((_req: any, res: any) => {
                    res.end('ok');
                });
                server.closeAllConnections();
                console.log(typeof server.listen);
                """
        };

        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");

        Assert.Empty(errors);
    }

    [Fact]
    public void BasicArithmetic_PassesILVerification()
    {
        var source = """
            let x = 10 + 5;
            console.log(x);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void ClassWithMethods_PassesILVerification()
    {
        var source = """
            class Calculator {
                add(a: number, b: number): number {
                    return a + b;
                }
            }
            let calc = new Calculator();
            console.log(calc.add(3, 4));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void AsyncAwait_PassesILVerification()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }

            async function main() {
                let result = await getValue();
                console.log(result);
            }

            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }

    // #357: a member-access increment (obj.prop++ / ++obj.prop / arr[i]++ / --arr[i]) inside an
    // async/generator body underflowed the state machine's MoveNext — the shared base emitter only
    // handled Expr.Variable operands and emitted nothing for Expr.Get/Expr.GetIndex. These pin the
    // four distinct state-machine emitter contexts (async function, generator, async arrow, async
    // generator) plus both operand kinds (property and index).

    [Fact]
    public void AsyncFunctionPropertyIncrement_PassesILVerification()
    {
        var source = """
            async function go(): Promise<void> {
                const obj = { x: 1 };
                const a = obj.x++;   // postfix returns the original
                const b = ++obj.x;   // prefix returns the new value
                console.log(a, b, obj.x);
            }
            go();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1 3 3\n", output);
    }

    [Fact]
    public void AsyncFunctionIndexIncrement_PassesILVerification()
    {
        var source = """
            async function go(): Promise<void> {
                const arr = [5, 6];
                const a = arr[0]++;  // postfix returns the original
                const b = --arr[1];  // prefix decrement returns the new value
                console.log(a, b, arr[0], arr[1]);
            }
            go();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5 5 6 5\n", output);
    }

    [Fact]
    public void GeneratorMemberIncrement_PassesILVerification()
    {
        var source = """
            function* gen(): Generator<number> {
                const o = { n: 5 };
                o.n++;
                yield o.n;
                yield --o.n;
            }
            const g = gen();
            console.log(g.next().value, g.next().value);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("6 5\n", output);
    }

    [Fact]
    public void AsyncArrowMemberIncrement_PassesILVerification()
    {
        var source = """
            const run = async (): Promise<void> => {
                const o = { c: 41 };
                console.log(++o.c, o.c);
            };
            run();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42 42\n", output);
    }

    [Fact]
    public void AsyncGeneratorMemberIncrement_PassesILVerification()
    {
        var source = """
            async function* agen(): AsyncGenerator<number> {
                const arr = [100];
                arr[0]++;
                yield arr[0];
            }
            async function main(): Promise<void> {
                const ag = agen();
                console.log((await ag.next()).value);
            }
            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("101\n", output);
    }

    [Fact]
    public void AsyncStaticFieldIncrement_PassesILVerification()
    {
        // Class.field++ inside async needs the own/inherited static-field shadow handling (#339)
        // that EmitCompoundSet already has; the generic dynamic path would desync the write (→ PDS)
        // from the static-typed Ldsfld read (count would print 10, not 12).
        var source = """
            class Base { static shared: number = 100; }
            class Derived extends Base {}
            class Counter { static count: number = 10; }
            async function go(): Promise<void> {
                Counter.count++;     // own field: 11
                ++Counter.count;     // own field: 12
                Derived.shared++;    // inherited: own-shadow 101, Base.shared stays 100
                console.log(Counter.count, Derived.shared, Base.shared);
            }
            go();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("12 101 100\n", output);
    }

    // #457: an increment/decrement whose operand's RECEIVER or INDEX contains an `await` —
    // (await getObj()).n++, --(await getArr())[0], arr[await idx()]++ — emitted invalid IL.
    // The await inside the receiver/index allocates an async resume label (and a state-dispatch
    // jump-table entry); the increment must still emit that await so the label is marked. Before
    // #357's Get/GetIndex arms the base emitter skipped the operand entirely, so the label was
    // defined and branched to but never marked ("Label N has not been marked") at Save time — a
    // distinct symptom of the same gap that produced #357's StackUnderflow for a plain receiver.
    // These pin all three await positions across the async function, async arrow, and async
    // generator state-machine emitters; SpillBoxed emits the await (suspending with an empty stack)
    // and stores the result before the read-modify-write.

    [Fact]
    public void AsyncFunctionAwaitInReceiverIncrement_PassesILVerification()
    {
        var source = """
            let capO: { n: number } = { n: 0 };
            let capA: number[] = [];
            async function mkO(): Promise<{ n: number }> { capO = { n: 1 }; return capO; }
            async function mkA(): Promise<number[]> { capA = [10, 20]; return capA; }
            async function idx(): Promise<number> { return 0; }
            async function go(): Promise<void> {
                (await mkO()).n++;        // await in Get receiver:   capO.n -> 2
                --(await mkA())[1];       // await in GetIndex recv:  capA[1] -> 19
                const arr = [5, 6];
                arr[await idx()]++;       // await in index position: arr[0] -> 6
                console.log(capO.n, capA[1], arr[0]);
            }
            go();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("2 19 6\n", output);
    }

    [Fact]
    public void AsyncArrowAwaitInReceiverIncrement_PassesILVerification()
    {
        var source = """
            let cap: { c: number } = { c: 0 };
            async function mk(): Promise<{ c: number }> { cap = { c: 41 }; return cap; }
            const run = async (): Promise<void> => {
                ++(await mk()).c;         // cap.c -> 42
                console.log(cap.c);
            };
            run();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncGeneratorAwaitInReceiverIncrement_PassesILVerification()
    {
        var source = """
            let cap: number[] = [];
            async function mk(): Promise<number[]> { cap = [100]; return cap; }
            async function* agen(): AsyncGenerator<number> {
                (await mk())[0]++;        // cap[0] -> 101
                yield cap[0];
            }
            async function main(): Promise<void> {
                const ag = agen();
                console.log((await ag.next()).value);
            }
            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("101\n", output);
    }

    // ---- #414: async-arrow / generator spill across a suspension ----

    [Fact]
    public void AsyncFunctionContainingAsyncArrow_PassesILVerification()
    {
        // #414 Defect A: the self-boxed kickoff for an async function that defines an async
        // arrow re-emitted `unbox` (a controlled-mutability managed pointer ILVerify rejects).
        // The kickoff now takes a single verifiable `Unsafe.Unbox<T>` byref.
        var source = """
            async function m() {
                const f = async () => { return 1; };
                await f();
            }
            m();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void StandaloneAsyncArrowSpillAcrossAwait_PassesILVerification()
    {
        // #414 Defect B: a value spilled before an await in an async arrow. The await is of an
        // inline `new Promise(...)`; awaiting a *function call* in an async arrow trips a
        // separate pre-existing IL gap that would mask this one.
        var source = """
            const f = async () => {
                const a = "A" + (await new Promise<number>(r => setTimeout(() => r(1), 5)));
                const b = a + "B" + (await new Promise<number>(r => setTimeout(() => r(2), 3)));
                console.log(b);
            };
            f();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void GeneratorSpillAcrossYield_PassesILVerification()
    {
        // #414 Defect E: a value spilled before a yield in a generator.
        var source = """
            function* g() { console.log("PFX:" + (yield 1) + "|" + (yield 2)); }
            for (const x of g()) {}
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void GeneratorSpillAcrossYieldStar_PassesILVerification()
    {
        // #414 Defect E: a value spilled before a yield* delegation.
        var source = """
            function* inner() { yield 1; yield 2; }
            function* g() { console.log("PFX:" + (yield* inner())); }
            for (const x of g()) {}
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AwaitInRestParamCallArg_PassesILVerification()
    {
        // #413: await inside an argument of a call whose args are assembled as a rest/varargs
        // array. The args must be spilled off the IL evaluation stack before the array is built;
        // otherwise the array reference sits stacked across the suspension and the MoveNext body
        // fails verification with PathStackDepth (and throws InvalidProgramException at runtime).
        var source = """
            function g(...a: any[]): string { return a.join(","); }
            async function m() {
                console.log(g("x", await new Promise<number>(r => setTimeout(() => r(1), 5))));
            }
            m();
            """;

        // Verify-only: the executor arrow captures variables, which trips the in-memory
        // reference-assembly run path (see #343); verification alone is the precise guard here.
        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AwaitWithRegularParamBeforeRestParamCall_PassesILVerification()
    {
        // #413 variant: a typed regular parameter precedes the rest parameter, and the await is
        // in a rest-position argument. The regular arg is spilled as a boxed object and must be
        // coerced back to its declared (string) slot when loaded for the call.
        // (The rest-join result is hoisted to a local so the body avoids the unrelated #434
        // BackwardBranch verify bug — `<expr> + rest.join(...)` directly — keeping this test
        // focused on the #413 call-site fix.)
        var source = """
            function h(x: string, ...rest: any[]): string { const s = rest.join(","); return x + "|" + s; }
            async function m() {
                console.log(h("X", "y", await new Promise<string>(r => setTimeout(() => r("Z"), 5))));
            }
            m();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AwaitInFixedArityCallArg_PassesILVerification()
    {
        // #436: a fixed-arity (non-rest) call where an earlier argument precedes an await. The
        // direct-call path spilled earlier args to parameter-typed IL locals; a typed local does
        // not survive a deferred MoveNext re-entry and the spilled-then-reloaded value mismatched
        // its declared slot under the verifier (StackUnexpected). Earlier args are now spilled to
        // registered, boxed locals and coerced back to their declared slot (string / number).
        var source = """
            function concat2(x: string, y: string): string { return x + y; }
            function add3(a: number, b: number, c: number): number { return a + b + c; }
            async function m() {
                console.log(concat2("A", await new Promise<string>(r => setTimeout(() => r("B"), 5))));
                console.log(add3(1, await new Promise<number>(r => setTimeout(() => r(2), 5)), 3));
            }
            m();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AwaitInMethodCallArg_PassesILVerification()
    {
        // #439: a method call (instance, static, optional-chain) with an await in an argument that
        // follows an earlier argument. These dispatch paths left the receiver and earlier args on
        // the IL stack (or in unregistered temps) across the suspension — StackUnexpected /
        // PathStackDepth, plus a runtime crash. Receiver and args are now spilled to registered
        // locals and each loaded argument is coerced to its declared parameter slot.
        var source = """
            class C {
                m(a: string, b: string): string { return a + b; }
                static s(a: string, b: string): string { return a + b; }
            }
            const o: any = { m(a: string, b: string): string { return a + b; } };
            async function main() {
                const c = new C();
                console.log(c.m("A", await new Promise<string>(r => setTimeout(() => r("B"), 5))));
                console.log(C.s("A", await new Promise<string>(r => setTimeout(() => r("B"), 5))));
                console.log(o?.m("A", await new Promise<string>(r => setTimeout(() => r("B"), 5))));
            }
            main();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AwaitInDispatchableStringMethodArg_PassesILVerification()
    {
        // #614: an await in an argument of a call to a runtime-dispatchable string method
        // (substring/substr/charAt/…) on an any-typed receiver. The string fast path and the generic
        // GetProperty path are mutually exclusive at runtime but both contained the arguments at emit
        // time, so each await was emitted twice — desyncing the await-state counter and crashing the
        // compiler ("key 'N' was not present in the dictionary"). Covers the optional-chain branch
        // (direct switch case + default fallback) and the non-optional dynamic-dispatch sibling.
        var source = """
            const s: any = "hello";
            const o: any = { substring(a: number, b: number): string { return "" + a + b; } };
            async function main() {
                console.log(s?.substring(await new Promise<number>(r => setTimeout(() => r(1), 5)), 4));
                console.log(s?.charAt(await new Promise<number>(r => setTimeout(() => r(1), 5))));
                console.log(s.substring(await new Promise<number>(r => setTimeout(() => r(1), 5)), 4));
                console.log(o?.substring(await new Promise<number>(r => setTimeout(() => r(2), 5)), 7));
            }
            main();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AsyncArrowAwaitOfCall_PassesILVerification()
    {
        // #441: `await <functionCall>` inside an async arrow emitted invalid IL (StackUnexpected in
        // the arrow's MoveNext) while the same shape in a regular async function verified. Root
        // cause: the arrow's EmitLiteral override boxed numeric literals and set StackType=Unknown,
        // desyncing EmitConversionForParameter's unboxed-double fast-path, so calling a function
        // with a numeric parameter (e.g. defer's `ms`) stored a boxed object into a double IL slot.
        // The arrow now inherits the base EmitLiteral (unboxed value types with a tracked StackType).
        var source = """
            function defer(v: any, ms: number): Promise<any> { return new Promise(r => setTimeout(() => r(v), ms)); }
            const f = async () => { const x = await defer(1, 5); console.log(x); };
            f();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void AsyncArrowCallWithNumericArgument_PassesILVerification()
    {
        // #441 root cause in isolation (no await): calling any function with a `number` parameter
        // from inside an async arrow stored a boxed double into a double IL slot under the old
        // eager-boxing EmitLiteral override.
        var source = """
            function inc(n: number): number { return n + 1; }
            const f = async () => { console.log(inc(5)); };
            f();
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void NewPromiseInsideFunctionBody_PassesILVerification()
    {
        // #442 (already fixed by #393's non-async Promise<T> return-slot fix): `new Promise(executor)`
        // inside a function/arrow body verifies. Guards the exact constructor-with-executor shape
        // (closure/delegate construction + Promise newobj) that #393's `Promise.resolve(...)` tests
        // did not cover. A non-async function returning Promise<T> previously mapped its return slot
        // to Task<T> while the body produced the runtime $TSPromise (object) → StackUnexpected.
        var source = """
            function p(): Promise<number> { return new Promise<number>(r => r(1)); }
            const q = (n: number): Promise<number> => new Promise<number>(r => setTimeout(() => r(n), 5));
            const z = p();
            const w = q(2);
            console.log("made");
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void Closures_PassesILVerification()
    {
        var source = """
            function makeCounter(): () => number {
                let count = 0;
                return () => {
                    count = count + 1;
                    return count;
                };
            }

            let counter = makeCounter();
            console.log(counter());
            console.log(counter());
            """;

        // For now, just verify IL without running (runtime has issues with rewritten assemblies)
        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void Inheritance_PassesILVerification()
    {
        var source = """
            class Animal {
                speak(): string {
                    return "...";
                }
            }

            class Dog extends Animal {
                speak(): string {
                    return "Woof!";
                }
            }

            let dog = new Dog();
            console.log(dog.speak());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("Woof!\n", output);
    }

    [Fact]
    public void DerivedConstructorWithSuper_PassesILVerification()
    {
        // A subclass with an EXPLICIT constructor calling super(...) emitted a
        // base-ctor `call` (Object..ctor) before super() chained the real parent
        // ctor, so the verifier saw a base-ctor call on an already-initialized /
        // wrong-base `this` — CallCtor. The program ran correctly; the IL was
        // merely unverifiable. Now the Object..ctor is skipped when super() will
        // chain the real base. Covers no-arg and arg'd bases, parameter
        // properties, multi-level chains, and generic bases. (#287)
        var source = """
            class A0 { constructor() {} }
            class B0 extends A0 { constructor() { super(); } }
            class A1 { constructor(public x: number) {} }
            class B1 extends A1 { y: number; constructor() { super(5); this.y = 9; } }
            class C1 extends B1 { constructor() { super(); } }
            class Box<T> { constructor(public v: T) {} }
            class IntBox extends Box<number> { constructor() { super(42); } }
            console.log(new B0() instanceof A0);
            const b1 = new B1();
            console.log(b1.x, b1.y);
            const c1 = new C1();
            console.log(c1.x, c1.y);
            console.log(new IntBox().v);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("true\n5 9\n5 9\n42\n", output);
    }

    [Fact]
    public void ClassExpressionExtendsClassExpression_PassesILVerification()
    {
        // A class expression extending ANOTHER class expression resolved its
        // superclass by the generated $ClassExpr_N name instead of the source
        // variable name, so the parent type was never set — the child silently
        // extended System.Object while super() still chained the real parent
        // ctor, tripping CallCtor / ThisUninitReturn, and `instanceof` against
        // the parent returned false. With the parent type now set, the IL
        // verifies and instanceof is correct. (#296)
        var source = """
            const Animal = class {
                constructor(public name: string) {}
                speak(): string { return this.name + " makes a sound"; }
            };
            const Dog = class extends Animal {
                constructor(name: string) { super(name); }
                speak(): string { return this.name + " barks"; }
            };
            const a = new Animal("Generic");
            const d = new Dog("Fido");
            console.log(a.speak());
            console.log(d.speak(), d instanceof Animal);
            """;

        // Verify-only: the reference-assembly rewrite in CompileVerifyAndRun
        // mangles the ldtoken-derived MethodInfo used by class-expression method
        // dispatch (BadImageFormatException at run time). Runtime behavior of
        // class-expression inheritance is covered by the shared-mode tests in
        // ClassExpressionTests, which run the real compiled output.
        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void ClassExpressionInheritedMethodDispatch_PassesILVerification()
    {
        // The compiled per-class GetProperty helper only dispatched a class's
        // OWN members, so a method inherited from a base class resolved to
        // undefined under the (always dynamic) class-expression dispatch and the
        // call threw. GetProperty now delegates to the base class. Three-level
        // chain exercises recursive delegation: Puppy inherits Dog.speak. (#297)
        var source = """
            const Animal = class {
                constructor(public name: string) {}
                speak(): string { return this.name + " makes a sound"; }
            };
            const Dog = class extends Animal {
                constructor(name: string) { super(name); }
                speak(): string { return this.name + " barks"; }
            };
            const Puppy = class extends Dog {
                constructor() { super("Rex"); }
            };
            const p = new Puppy();
            console.log(p.speak(), p.name, p instanceof Dog, p instanceof Animal);
            """;

        // Verify-only (see ClassExpressionExtendsClassExpression note). Runtime
        // inherited-dispatch behavior is asserted by the shared-mode test
        // ClassExpressionTests.ClassExpression_MultiLevelInheritedMethod.
        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void Generators_PassesILVerification()
    {
        var source = """
            function* range(start: number, end: number) {
                for (let i = start; i < end; i = i + 1) {
                    yield i;
                }
            }

            for (let n of range(1, 4)) {
                console.log(n);
            }
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1\n2\n3\n", output);
    }

    // Regression test for issue #189: CLI `--compile … --verify` output is NOT
    // rewritten to reference-assembly facades (it binds to System.Private.CoreLib),
    // and verifying it against the ref-assembly universe produced thousands of
    // false StackUnexpected/ThrowOrCatchOnlyExceptionType errors.
    [Fact]
    public void CoreLibBoundOutput_PassesILVerification()
    {
        var source = """
            let arr = [1, 2, 3];
            let doubled = arr.map(x => x * 2);
            console.log(doubled);
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            var lexer = new SharpTS.Parsing.Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new SharpTS.Parsing.Parser(tokens);
            var statements = parser.ParseOrThrow();

            var checker = new SharpTS.TypeSystem.TypeChecker();
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new SharpTS.Compilation.DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            // useReferenceAssemblies: false — same shape as plain CLI --compile output
            var compiler = new SharpTS.Compilation.ILCompiler("test", preserveConstEnums: false, useReferenceAssemblies: false, sdkPath: null);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            var errors = TestHarness.VerifyIL(dllPath);

            Assert.Empty(errors);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    [Fact]
    public void FunctionReturningStringConcat_PassesILVerification()
    {
        // A `: string` function whose body produces a runtime-helper result (string concat
        // or member get) previously left an `object` on the stack where the verifier expected
        // a string, raising StackUnexpected even though it ran correctly. (#275)
        var source = """
            function cat(a: string): string { return "hi " + a; }
            function viaLocal(a: string): string { let r = "x" + a; return r; }
            function withNumber(a: number): string { return "val=" + a; }
            interface Named { name: string; }
            function pick(n: Named): string { return n.name; }
            console.log(cat("z"));
            console.log(viaLocal("y"));
            console.log(withNumber(42));
            console.log(pick({ name: "w" }));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("hi z\nxy\nval=42\nw\n", output);
    }

    [Fact]
    public void FunctionReturningTypedCollection_PassesILVerification()
    {
        // A function declared `: number[]` (or Map/Set, or async Promise<T[]>) maps its return
        // slot to List<T>/Dictionary<,>/HashSet<>, but the runtime value is a dynamic
        // $Array/TSMap/TSSet carried as object — not CLR-assignable to the declared collection.
        // That left an object on the stack where the verifier expected the collection, raising
        // StackUnexpected even though it ran correctly. The return type now falls back to object. (#278)
        var source = """
            function nums(): number[] { return [1, 2, 3]; }
            function strs(): string[] { return ["a", "b"]; }
            function mkMap(): Map<string, number> { const m = new Map<string, number>(); m.set("x", 1); return m; }
            function mkSet(): Set<number> { const s = new Set<number>(); s.add(5); return s; }
            class Box { getNums(): number[] { return [7, 8]; } }
            console.log(nums().length);
            console.log(strs().join(","));
            console.log(mkMap().get("x"));
            console.log(mkSet().has(5));
            console.log(new Box().getNums().length);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("3\na,b\n1\ntrue\n2\n", output);
    }

    [Fact]
    public void NonAsyncFunctionReturningPromise_PassesILVerification()
    {
        // A NON-async function/arrow declared `: Promise<T>` maps its return slot (strictly) to
        // Task<T>, but the body returns the runtime $TSPromise carried as object — not CLR-assignable
        // to the Task slot. That left an object on the stack where the verifier expected Task<T>,
        // raising StackUnexpected even though it ran (the JIT tolerates the reference-type store).
        // The return type now falls back to object. Async functions are unaffected: they hardcode a
        // Task<object> stub whose state machine builds a real Task. Covers both a top-level function
        // and an arrow (both route through ParameterTypeResolver.ResolveReturnType). (#393)
        var source = """
            function wrap(): Promise<number> { return Promise.resolve(42); }
            const arrow = (): Promise<string> => Promise.resolve("hi");
            async function main() {
                console.log(await wrap());
                console.log(await arrow());
            }
            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\nhi\n", output);
    }

    [Fact]
    public void TimersPromisesImport_PassesILVerification()
    {
        // Exact #393 repro: importing timers/promises emitted the re-export wrappers
        // $M_promises_setTimeout / $M_promises_setImmediate as non-async functions declared
        // `: Promise<any>`, whose Task<object> return slot didn't match the object the body
        // produced (StackUnexpected). setInterval returns AsyncIterable<any> (→ object slot) and
        // was always clean. Verifies the whole module — covers all three wrappers regardless of
        // which member is imported (all are emitted). (#393)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const r = await setTimeout(10, 'tick');
                    console.log(r);
                }
                main();
                """
        };

        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");

        Assert.Empty(errors);
    }

    [Fact]
    public void FsFacadeImport_PassesILVerification()
    {
        // #1246: importing `fs` emitted 68 unverifiable methods across the facade. Two shapes,
        // both a value stored into a narrower slot without the verifier-required bridge:
        //   (1) `object` → `string` — a $M_fs helper stages an `any`-typed argument (e.g.
        //       `__joinPath(src, names[i])` in cpSync) into a string parameter temp with no castclass;
        //   (2) `$Promise` → `Task<object>` — the promise primitives return a wrapped $Promise which
        //       the callback helpers (`__cbData`/`__cbVoid`) took in a `Promise<any>` → Task<object>
        //       parameter slot. Fixed by (1) a castclass on string parameter coercion and (2) widening
        //       non-async Promise<T> parameter slots to object (symmetric to #393's return-slot fix).
        //       Verifies the whole facade — every $M_fs_* method is emitted regardless of what is used.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                console.log(fs.statSync('.').isDirectory());
                """
        };

        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");

        Assert.Empty(errors);
    }

    [Fact]
    public void ObjectArgumentToStringParameter_PassesILVerification()
    {
        // #1246 class 1, minimal repro: an `any`-typed value (here a bracket read off an `any`
        // array, which the emitter types as `object`) passed to a `string` parameter left the
        // object on the stack where the callee's string slot was expected — no castclass. The
        // JIT tolerated it; ILVerify reported StackUnexpected {Found object, Expected string}.
        var source = """
            function join(a: string, b: string): string { return a + "/" + b; }
            const names: any = ["x", "y"];
            console.log(join("d", names[0]));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("d/x\n", output);
    }

    [Fact]
    public void PromiseValueToPromiseParameter_PassesILVerification()
    {
        // #1246 class 2, minimal repro: a runtime $Promise (from Promise.resolve / new Promise —
        // not a real CLR Task, unlike an async function's result) passed to a `Promise<any>`
        // parameter, whose slot mapped to Task<object>. Storing the $Promise into the Task<object>
        // slot ran fine but failed ILVerify {Found $Promise, Expected Task<object>}. The parameter
        // slot is now widened to object.
        var source = """
            function consume(p: Promise<any>, cb: any): void { p.then((v: any) => cb(v)); }
            consume(Promise.resolve(5), (v: any) => console.log(v));
            consume(new Promise<any>((res: any) => res(7)), (v: any) => console.log(v));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5\n7\n", output);
    }

    [Fact]
    public void ClassGetPropertyWithTypedGetter_PassesILVerification()
    {
        // The compiler-generated GetProperty dispatch helper invokes a typed getter (e.g. the
        // auto getter for a `public x: number` parameter property, or an explicit `get`) and
        // returned its value-type result into GetProperty's object slot without boxing — the
        // verifier reported StackUnexpected even though it ran. Generic-class getters returning a
        // type parameter had the same gap. The getter result is now boxed. (#279)
        var source = """
            class Foo { constructor(public x: number) {} }
            class Bar {
                private _v: number = 10;
                get doubled(): number { return this._v * 2; }
                get label(): string { return "bar"; }
                get flag(): boolean { return true; }
            }
            class Box<T> {
                constructor(public item: T) {}
                get value(): T { return this.item; }
            }
            function mkFoo(): Foo { return new Foo(7); }
            const b = new Bar();
            console.log(mkFoo().x);
            console.log(b.doubled);
            console.log(b.label);
            console.log(b.flag);
            console.log(new Box<number>(99).value);
            console.log(new Box<string>("hi").value);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("7\n20\nbar\ntrue\n99\nhi\n", output);
    }

    [Fact]
    public void GenericClassExpression_PassesILVerification()
    {
        // A generic class EXPRESSION emits a real open .NET generic type. Its $IHasFields
        // bodies (get_Fields / GetProperty / SetProperty) widened a generic-parameter-typed
        // backing field (`__Value : T`) into the object-typed dispatch slots without boxing,
        // producing unverifiable IL (StackUnexpected). Generic-parameter fields now live in the
        // `_fields` dictionary, matching generic class declarations, and the open generic is
        // closed (via inference / explicit type args) before Newobj at the `new` site. (#291)
        var source = """
            const Box = class<T> {
                constructor(private value: T) {}
                get contents(): T { return this.value; }
            };
            console.log(new Box("hello").contents);
            console.log(new Box<number>(42).contents);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("hello\n42\n", output);
    }

    [Fact]
    public void TryCatchFinally_PassesILVerification()
    {
        var source = """
            function test(): string {
                try {
                    throw "test error";
                } catch (e) {
                    return "caught";
                } finally {
                    console.log("finally");
                }
            }

            console.log(test());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("finally\ncaught\n", output);
    }

    // #318: a `string`-inferred function body can legitimately return `$Undefined`
    // (the checker infers `string` for `cond ? "x" : undefined`). A narrow `string`
    // return slot cannot carry that sentinel — the prior #275 castclass crashed at
    // runtime with InvalidCastException. ResolveReturnType now maps string->object.
    [Fact]
    public void InferredStringReturn_WithUndefinedBranch_BlockBody_DoesNotCrash()
    {
        var source = """
            function pick(n: any) {
                return n > 2 ? "big" : undefined;
            }
            console.log(pick(3));
            console.log(pick(1));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("big\nundefined\n", output);
    }

    // #318: expression-body arrow whose `string` return loads a captured variable
    // through a display class leaves a statically-object value against the slot,
    // which previously failed IL verification (StackUnexpected). No castclass is
    // safe here (it would crash on $Undefined), so the slot is dropped to object.
    // #343: reference-assembly compilation (used by CompileVerifyAndRun) used to trip
    // a BadImageFormatException constructing $TSFunction over this captured-var arrow —
    // root-caused to PEPacker emitting nil MethodDef ParamList row-pointers, fixed in
    // NickNa.PEPacker 1.0.2. This now exercises the ref-asm runtime path end to end.
    [Fact]
    public void InferredStringReturn_CapturedVarArrow_PassesILVerification()
    {
        var source = """
            const outer = () => {
                let v = "a";
                const setB = () => { v = v + "b"; };
                const readV = () => v;
                setB();
                return readV();
            };
            console.log(outer());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("ab\n", output);
    }

    // #318 guard: an undefined string-keyed group must stay `undefined` (not be
    // coerced to null or "undefined"), which an isinst/Convert.ToString coercion
    // at the return site would have corrupted — observable through Map keys.
    [Fact]
    public void InferredStringReturn_UndefinedMapKey_PreservedDoesNotCoerce()
    {
        var source = """
            const items = [1, 2, 3, 4];
            const g = Map.groupBy(items, (n: any) => n > 2 ? "big" : undefined);
            console.log(g.get("big"));
            console.log(g.get(undefined));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("[3, 4]\n[1, 2]\n", output);
    }

    // #318 guard: a genuinely string-typed function (no undefined in its value
    // domain) still verifies and returns the right string after the slot change.
    [Fact]
    public void TypedStringReturn_StillWorks()
    {
        var source = """
            function greet(name: string): string {
                return "hello " + name;
            }
            const up = (s: string): string => s.toUpperCase();
            console.log(greet("world"));
            console.log(up("abc"));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("hello world\nABC\n", output);
    }

    // #344: a `: number` function whose body returns `undefined as any` would, with an
    // unboxed `double` return slot, coerce the `undefined` sentinel to `NaN`. The checker
    // flags the undefined-reachable return so the compiler widens the slot to object,
    // matching the interpreter (`undefined`).
    [Fact]
    public void TypedNumberReturn_UndefinedAsAny_StaysUndefined()
    {
        var source = """
            function f(n: any): number {
                if (n > 2) return 42;
                return undefined as any;
            }
            console.log(f(3));
            console.log(f(1));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #344: the boolean analogue — an unboxed `bool` slot would coerce `undefined` to `false`.
    [Fact]
    public void TypedBooleanReturn_UndefinedAsAny_StaysUndefined()
    {
        var source = """
            function g(n: any): boolean {
                if (n > 2) return true;
                return undefined as any;
            }
            console.log(g(3));
            console.log(g(1));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("true\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #344: an expression-bodied arrow with a `: number` return whose ternary hides the
    // `undefined` in a branch (`42 | any` collapses to `42` at the top level). The value-flow
    // check recurses into ternary branches so the slot is still widened.
    [Fact]
    public void TypedNumberArrow_TernaryUndefinedBranch_StaysUndefined()
    {
        var source = """
            const af = (n: any): number => (n > 2 ? 42 : (undefined as any));
            console.log(af(3));
            console.log(af(1));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #344 guard: a genuinely numeric/boolean function (no undefined in its value domain)
    // keeps its sound unboxed slot — it must still verify and compute correctly. `area(2)+1`
    // consumes the return numerically, exercising the unboxed double path.
    [Fact]
    public void TypedNumericReturn_SoundBody_StillWorks()
    {
        var source = """
            function add(a: number, b: number): number { return a + b; }
            function area(r: number): number { return r * r * 3; }
            function isBig(n: number): boolean { return n > 10; }
            console.log(add(2, 3));
            console.log(area(2) + 1);
            console.log(isBig(5));
            console.log(isBig(20));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5\n13\nfalse\ntrue\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #367: a `: number` LOCAL unsoundly assigned `undefined as any` and then returned. The
    // return expression is statically `number`, so the #344 detection (which keys on the return
    // value's own type) cannot see it. The taint pass object-slots the local so the unboxed
    // double slot does not coerce the sentinel to NaN, matching the interpreter (`undefined`).
    [Fact]
    public void TypedNumberLocal_HoldingUndefined_ReturnedStaysUndefined()
    {
        var source = """
            function h(n: any): number {
                let x: number = undefined as any;
                if (n > 2) x = 42;
                return x;
            }
            console.log(h(1));
            console.log(h(5));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\n42\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #367: taint reaches the returned local transitively (`z = y` where `y` holds the sentinel).
    // The numeric-typed initializer of `z` would otherwise give it an unboxed double slot that
    // coerces `undefined` to NaN at the store, before the return is even reached.
    [Fact]
    public void TypedNumberLocal_TransitiveTaint_StaysUndefined()
    {
        var source = """
            function trans(): number {
                let y: number = undefined as any;
                let z: number = y;
                return z;
            }
            console.log(trans());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #367: the taint assignment is lexically AFTER the return that observes it, reachable only
    // via a loop back-edge. The whole-body taint pass is order-independent, so the earlier return
    // is still flagged and the local object-slotted.
    [Fact]
    public void TypedNumberLocal_LoopBackEdgeTaint_StaysUndefined()
    {
        var source = """
            function loop(n: number): number {
                let x: number = 0;
                for (let i = 0; i < n; i++) {
                    if (i > 0) return x;
                    x = undefined as any;
                }
                return 7;
            }
            console.log(loop(2));
            console.log(loop(0));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\n7\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #367: the same local-slot corruption inside a class method and getter (their `return`
    // already uses an object slot, but the unboxed local would coerce the sentinel first).
    [Fact]
    public void TypedNumberLocal_InMethodAndGetter_StaysUndefined()
    {
        var source = """
            class C {
                m(): number { let x: number = 0; x = undefined as any; return x; }
                get g(): number { let y: number = 1; y = undefined as any; return y; }
            }
            const c = new C();
            console.log(c.m());
            console.log(c.g);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #367 guard: a `: number` local that never receives an `any`/`undefined` value keeps its
    // sound unboxed double slot and computes correctly — the taint pass must not over-widen.
    [Fact]
    public void TypedNumberLocal_SoundBody_StillUsesNumericValue()
    {
        var source = """
            function clean(n: number): number {
                let x: number = 5;
                if (n > 2) x = 42;
                return x + 1;
            }
            console.log(clean(1));
            console.log(clean(10));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("6\n43\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372 case 1: an inferred-return (no annotation) function whose returned local holds the
    // undefined sentinel. The return type is inferred as `number`, so it is double-slotted just like
    // an explicit `: number` — but the #367 taint pass was gated on a declared number/boolean return
    // and never ran here. The pass now runs regardless of return type, so the local and the inferred
    // return slot are both widened to object and the sentinel survives.
    [Fact]
    public void TypedNumberLocal_InferredReturn_StaysUndefined()
    {
        var source = """
            function inf(n: any) { let x: number = 0; x = undefined as any; return x; }
            console.log(inf(1));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372 case 2: a `: number` local holding the undefined sentinel is observed by something other
    // than a return (here `console.log`) inside a void function. The corruption happens at the local
    // store, independent of any return, so object-slotting the local must not be gated on the return.
    [Fact]
    public void TypedNumberLocal_ObservedInVoidFunction_StaysUndefined()
    {
        var source = """
            function obs(): void { let x: number = 0; x = undefined as any; console.log(x); }
            obs();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372 case 3: a `: number` PARAMETER reassigned the undefined sentinel. A parameter has no local
    // declaration to mark; it is matched by name in the taint pass and its arg slot widened to object.
    // The same shape for a `: boolean` parameter widens the bool arg slot.
    [Fact]
    public void TypedNumberParam_ReassignedUndefined_StaysUndefined()
    {
        var source = """
            function p(x: number): number { x = undefined as any; return x; }
            function q(b: boolean): boolean { b = undefined as any; return b; }
            console.log(p(1));
            console.log(q(true));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372: the same tainted-parameter shape inside an instance method, a static method, and a
    // constructor — each resolves parameter slots through a different resolver, all of which must
    // widen a flagged number/boolean parameter to object.
    [Fact]
    public void TypedNumberParam_ReassignedUndefined_InMethodStaticAndCtor_StaysUndefined()
    {
        var source = """
            class C {
                m(x: number): number { x = undefined as any; return x; }
                static s(x: number): number { x = undefined as any; return x; }
                constructor(n: number) { n = undefined as any; this.r = n; }
                r: any;
            }
            const c = new C(5);
            console.log(c.m(1));
            console.log(C.s(2));
            console.log(c.r);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\nundefined\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372 (pre-existing param-assignment bug, discovered while fixing #372 and filed as a follow-up):
    // reassigning a `: number` parameter with a SOUND numeric value would box the value and `Starg`
    // it into the unboxed double arg slot, failing IL verification (StackUnexpected) and reading back
    // garbage. The assignment now converts the value to the parameter slot's declared type first.
    [Fact]
    public void TypedNumberParam_SoundReassignment_ComputesCorrectly()
    {
        var source = """
            function a(x: number): number { x = 99; return x; }
            function c(x: number): number { x = x + 1; return x * 2; }
            console.log(a(10));
            console.log(c(10));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("99\n22\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #372 guard: a `: number` parameter that never receives an `any`/`undefined` value keeps its
    // sound unboxed double arg slot — the taint pass must not over-widen parameters either.
    [Fact]
    public void TypedNumberParam_SoundBody_StillUsesNumericValue()
    {
        var source = """
            function scale(x: number): number { let y: number = x * 2; return y + 1; }
            console.log(scale(10));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("21\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #402: the same StackUnexpected corruption applied to `: boolean` params, which compile to an
    // unboxed `bool` arg slot. Reassigning one (literal or computed) must Unbox_Any the boxed value
    // back to bool before Starg.
    [Fact]
    public void TypedBooleanParam_SoundReassignment_ComputesCorrectly()
    {
        var source = """
            function a(x: boolean): boolean { x = false; return x; }
            function c(x: boolean): boolean { x = !x; return x; }
            console.log(a(true));
            console.log(c(true));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("false\nfalse\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #402: a `: string` param compiles to a `string` arg slot (a reference type), so the conversion
    // before Starg is a `castclass` rather than `Unbox_Any`. Reassigning one must not box-and-Starg
    // an `object` into the `string` slot.
    [Fact]
    public void TypedStringParam_SoundReassignment_ComputesCorrectly()
    {
        var source = """
            function a(x: string): string { x = "hi"; return x; }
            function c(x: string): string { x = x + "!"; return x; }
            console.log(a("a"));
            console.log(c("a"));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("hi\na!\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #537: single-double-argument Date setters (setTime/setDate/setMilliseconds/setUTCDate/
    // setUTCMilliseconds/setYear) used as a bare statement or assigned to a number previously left
    // the tracked stack type as Double, so the caller boxed the already-boxed result a second time
    // (StackUnexpected). The DateEmitter now records the boxed result as a reference type.
    [Fact]
    public void DateSingleArgSetters_PassILVerification()
    {
        var source = """
            const d = new Date(2024, 5, 15);
            d.setDate(10);                  // bare statement (local)
            d.setMilliseconds(500);         // bare statement
            d.setUTCDate(3);                // bare statement (UTC)
            d.setYear(99);                  // bare statement (Annex B)
            const n: number = d.setTime(0); // assigned to a number
            console.log(n);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("0\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #536 / #538: multi-argument setters (object[] form) and the Date.UTC/Date.parse statics emit
    // verifiable IL and match the interpreter.
    [Fact]
    public void DateMultiArgSettersAndStatics_PassILVerification()
    {
        var source = """
            const d = new Date(0);
            d.setUTCFullYear(2020, 5, 15);
            d.setUTCHours(13, 30, 45, 500);
            const t: number = Date.UTC(2024, 0, 1);
            const p: number = Date.parse('2024-01-15T10:30:00Z');
            console.log(d.getUTCMonth(), d.getUTCMinutes(), t, p);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5 30 1704067200000 1705314600000\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #539: toLocale* with locale/options emits verifiable IL (the reflection-based options path)
    // and matches the interpreter. Exact locale-formatted output is host-dependent, so assert on
    // stable localized substrings (see IntlDateTimeFormatTests) plus interpreter parity.
    [Fact]
    public void DateToLocaleWithOptions_PassesILVerification()
    {
        var source = """
            const d = new Date(Date.UTC(2024, 0, 15, 12, 0, 0));
            const s = d.toLocaleDateString('en-US', { dateStyle: 'full', timeZone: 'UTC' });
            console.log(s.includes('Monday'), s.includes('January'), s.includes('2024'));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("true true true\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #573: Date/RegExp/Map/Set parameters, returns, and fields mapped (strictly) to the CLR types
    // DateTime/Regex/Dictionary/HashSet, but their runtime values are dynamic $TSDate/$RegExp/$Map/
    // $Set carried as object. A typed slot failed strict ILVerify with StackUnexpected and a
    // castclass at the call/use site threw InvalidCastException. The slots must fall back to object,
    // matching the return-slot rule that already covered the collections.
    [Fact]
    public void DateRegExpMapSetParametersAndReturns_PassILVerification()
    {
        var source = """
            function takeDate(d: Date): number { return d.getUTCFullYear(); }
            function makeDate(): Date { return new Date(0); }
            function takeRegExp(r: RegExp): boolean { return r.test("x"); }
            function makeRegExp(): RegExp { return /a/; }
            function takeMap(m: Map<string, number>): number { return m.size; }
            function takeSet(s: Set<number>): number { return s.size; }
            class C { m(d: Date): number { return d.getUTCFullYear(); } }
            console.log(takeDate(new Date(0)));
            console.log(makeDate().getUTCFullYear());
            console.log(takeRegExp(/x/));
            console.log(makeRegExp().test("a"));
            console.log(takeMap(new Map([["a", 1]])));
            console.log(takeSet(new Set([1, 2, 3])));
            console.log(new C().m(new Date(0)));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1970\n1970\ntrue\ntrue\n1\n3\n1970\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #573: a Date-typed parameter-property (`constructor(public when: Date)`) generated a DateTime
    // backing field; storing the runtime $TSDate threw InvalidCastException. The field slot must be
    // object too — generalizing the original typed-array carve-out in GetFieldType.
    [Fact]
    public void DateParameterPropertyField_PassesILVerification()
    {
        var source = """
            class Holder { constructor(public when: Date) {} }
            const h = new Holder(new Date(0));
            console.log(h.when.getUTCFullYear());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1970\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #568: a `T | undefined` parameter compiled into a non-nullable slot — String for a reference
    // T (so storing the $Undefined sentinel threw InvalidCastException), or double? for a value T
    // (unverifiable IL). Such parameters must use an object slot, as locals already do. The body
    // reassigns the parameter to `undefined` and observes it, exercising both store and read.
    // #751: indexing a typed-array (number[]/boolean[]) variable emitted an unverifiable merge — the
    // typed List<T> fast path left a native double/bool on the stack where the sibling
    // $Array/List<object>/fallback paths (and every consumer, which reads the clobbered
    // StackType=Unknown) expected an object ref. It ran only because the typed branch is dead for an
    // $Array-backed value. The fast path now boxes its result so every branch converges on object.
    // Covers GET and SET, number and boolean, typed-backed (empty literal + push) and object-backed
    // (non-empty literal) arrays, and the original spread/destructure-of-a-numeric-Set repro.
    [Fact]
    public void TypedArrayIndexReadAndWrite_PassesILVerification()
    {
        var source = """
            const a: number[] = [1, 2, 3];
            const x = a[0];
            a[1] = 9;
            const b: boolean[] = [true, false];
            const y = b[0];
            b[1] = true;
            const c: number[] = [];
            c.push(5);
            let sum = 0;
            for (let i = 0; i < 3; i++) sum += a[i];
            console.log(x, a[1], y, b[1], c[0], sum);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1 9 true true 5 13\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void NumericSetSpreadAndDestructure_PassesILVerification()
    {
        // #751 original repro: materializing a numeric Set via spread, then index-reading the
        // resulting number[], and array-destructuring a numeric Set (inherits the same path).
        var source = """
            const out = [...new Set<number>([1, 2, 3])];
            console.log(out[0], out.length);
            const [first, second] = new Set<number>([10, 20]);
            console.log(first, second);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("1 3\n10 20\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void UndefinedUnionParameterReassignment_PassesILVerification()
    {
        var source = """
            function fs(x: string | undefined): string { x = undefined; return typeof x; }
            function fn(x: number | undefined): string { x = undefined; return typeof x; }
            console.log(fs("hi"));
            console.log(fn(3));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("undefined\nundefined\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RestParam_StringConcatWithJoinCall_PassesILVerification()
    {
        // Issue #434: "p" + rest.join(",") caused BackwardBranch IL error because
        // EmitGetListFromArrayOrList's typed-list conversion loop used a
        // jump-to-condition pattern that placed the backward-branch target in dead
        // code, making the stack height indeterminate when "p" was already on the
        // evaluation stack.
        var source = """
            function h(...rest: any[]): string { return "p" + rest.join(","); }
            console.log(h("a", "b"));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("pa,b\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ForLoopPushAndTwoPartStringConcatWithJoin_PassesILVerification()
    {
        // Issue #437: "[ " + parts.join(",") triggered BackwardBranch when parts.push()
        // was called in a preceding for-loop, because EmitGetListFromArrayOrList's
        // typed-list loop had a jump-to-condition backward branch target in dead code.
        // Fixed by #434 (condition-at-top loop in ArrayEmitter).
        var source = """
            function f(n: number): string {
              const parts: string[] = [];
              for (let i = 0; i < n; i++) { parts.push("x"); }
              return "[ " + parts.join(", ");
            }
            console.log(f(3));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("[ x, x, x\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ForLoopPushAndThreePartStringConcatWithJoin_PassesILVerification()
    {
        // Issue #437: "[ " + parts.join(", ") + " ]" (3-part concat via
        // StringConcatOptimizer → EmitStringConcatWithOverload) also triggered
        // BackwardBranch. Same root cause and fix as the 2-part variant above.
        var source = """
            function f(n: number): string {
              const parts: string[] = [];
              for (let i = 0; i < n; i++) { parts.push("x"); }
              return "[ " + parts.join(", ") + " ]";
            }
            console.log(f(3));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("[ x, x, x ]\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #445: function with a typed-array parameter (T[]) emits unverifiable IL at every call site.
    // The strict CLR mapping of string[] is List<string> and number[] is List<double>, but the
    // runtime value is always $TSArray/List<object>. ILVerify rejects the mismatch because generics
    // are invariant — List<object> is not assignable to List<string>. Fixed in #643: CoerceParamSlotType
    // falls back to object for any List<T> via IsDynamicRuntimeCollection, so the parameter slot (and
    // the call-site target type) is object, which accepts any reference value.
    // Covers all variants from the issue's characterization table: void/string/array returns,
    // literal/variable args, number[] params, class methods, and inner functions.
    [Fact]
    public void TypedArrayParameter_PassesILVerification()
    {
        var source = """
            function f1(items: string[]): void { console.log(items.length); }
            f1(["a", "b"]);

            function f2(items: string[]): void { console.log(items.length); }
            const arr: string[] = ["x", "y", "z"];
            f2(arr);

            function f3(xs: number[]): void { console.log(xs[0]); }
            f3([1, 2, 3]);

            function f4(items: string[]): string { return items[0]; }
            console.log(f4(["ok"]));

            function f5(items: string[]): string[] { return items; }
            console.log(f5(["r"])[0]);

            class C {
                process(items: string[]): number { return items.length; }
            }
            console.log(new C().process(["a", "b", "c"]));

            function outer(): void {
                function inner(xs: number[]): void { console.log(xs.length); }
                inner([10, 20]);
            }
            outer();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("2\n3\n1\nok\nr\n3\n2\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    // #886: assigning to a built-in property whose setter ends by boxing the result and
    // returning (RegExp.lastIndex, process.exitCode) left _stackType == Double stale over a
    // boxed value. A downstream consumer (top-level await-drain wrapper, or arithmetic on the
    // assignment result) then trusted the stale type and emitted a second box / native op,
    // producing unverifiable IL (StackUnexpected: ref 'float64' vs Double). The setters now
    // route the trailing box through EmitBoxDouble (box + SetStackUnknown).

    [Fact]
    public void RegExpLastIndexAssignment_TopLevel_PassesILVerification()
    {
        var source = """
            const r = /a/;
            r.lastIndex = 5;
            console.log(r.lastIndex);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RegExpLastIndexAssignment_FromVariable_PassesILVerification()
    {
        var source = """
            const r = /a/;
            let n = 5;
            r.lastIndex = n;
            console.log(r.lastIndex);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("5\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RegExpLastIndexAssignment_FromStringDefersCoercionAndPassesILVerification()
    {
        // Assignment preserves the ordinary data property's raw value. exec performs
        // ToLength at read time, then global write-back replaces it with a number.
        var source = """
            const r = /a/g;
            r.lastIndex = "1.9" as any;
            console.log(typeof r.lastIndex + ":" + r.lastIndex);
            r.exec("ba");
            console.log(r.lastIndex);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("string:1.9\n2\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RegExpLastIndexAssignment_UsedAsValue_PassesILVerification()
    {
        // The assignment result is consumed by arithmetic inside a function body. Before the fix
        // this failed with ExpectedNumericType {Found=ref 'float64'} (native add on a boxed double).
        var source = """
            function f(): number {
                const r = /a/;
                let x = (r.lastIndex = 5) + 1;
                return x;
            }
            console.log(f());
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("6\n", output);
        Assert.Equal(output, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ProcessExitCodeAssignment_Literal_ProducesVerifiableIL()
    {
        // Uses CompileAndVerifyOnly (not ...AndRun) because setting a non-zero exit code would
        // make the harness treat the process as failed. Before the fix this produced two errors:
        // an unconditional Convert.ToDouble on a native double, plus the trailing double-box.
        var source = """
            process.exitCode = 5;
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void ProcessExitCodeAssignment_FromVariable_ProducesVerifiableIL()
    {
        var source = """
            let n = 3;
            process.exitCode = n;
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);

        Assert.Empty(errors);
    }
}
