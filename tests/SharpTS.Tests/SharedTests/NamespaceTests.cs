using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for TypeScript namespace declarations, including nested namespaces,
/// dotted syntax, declaration merging, and namespace members (functions, classes, enums).
/// Runs against both interpreter and compiler where supported.
/// </summary>
public class NamespaceTests
{
    #region Namespace-level variable access from member functions (#567)

    // A function declared in a namespace must be able to read namespace-level var/let/const members.
    // Compiled mode previously stored those only as runtime members of the namespace object, invisible
    // to the member function body, throwing "Undefined variable". The fix also backs each with a static
    // field surfaced to the function's resolver. These pin every variant from the issue.

    [Theory, ModeData]
    public void NamespaceFunction_ReadsNamespaceConst(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 7; export function f() { return val; } export const result = f(); }
            console.log(N.result);
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_ReadsNamespaceLet(ExecutionMode mode)
    {
        var code = @"
            namespace N { let val = 7; export function f() { return val; } export const r = f(); }
            console.log(N.r);
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { export var val = 7; export function f() { return val; } export const r = f(); }
            console.log(N.r);
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_ReadsExportedConst_AndExternalAccessStillWorks(ExecutionMode mode)
    {
        var code = @"
            namespace N { export const val = 7; export function f() { return val; } export const r = f(); }
            console.log(N.r + "","" + N.val);
        ";
        Assert.Equal("7,7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceFunction_ReadsOuterAndInnerVars(ExecutionMode mode)
    {
        var code = @"
            namespace Out {
                const a = 1;
                export namespace In {
                    const b = 2;
                    export function g() { return a + b; }
                    export const r = g();
                }
            }
            console.log(Out.In.r);
        ";
        Assert.Equal("3\n", TestHarness.Run(code, mode));
    }

    // The original #567 fix only reached the plain-function path. Generators, async
    // functions, and class members emit their bodies through separate paths (state-machine
    // MoveNext methods, class method/accessor/constructor emission) that built their
    // top-level-static-var view without the namespace augmentation, so they could not
    // resolve a namespace var by bare name — silently returning undefined (state machines)
    // or throwing "Undefined variable" (class members). These pin each path.

    [Theory, ModeData]
    public void NamespaceGenerator_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 4; export function* g() { yield val; } }
            console.log(N.g().next().value);
        ";
        Assert.Equal("4\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceAsyncFunction_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 7; export async function a() { return val; } }
            N.a().then(x => console.log(x));
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceClassMethod_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 3; export class C { m() { return val; } } }
            console.log(new N.C().m());
        ";
        Assert.Equal("3\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceClassStaticMethod_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 5; export class C { static m() { return val; } } }
            console.log(N.C.m());
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceClassAccessor_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 8; export class C { get x() { return val; } } }
            console.log(new N.C().x);
        ";
        Assert.Equal("8\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceClassConstructor_ReadsNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace N { const val = 6; export class C { y: number; constructor() { this.y = val; } } }
            console.log(new N.C().y);
        ";
        Assert.Equal("6\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceGenerator_ReadsOuterNamespaceVar(ExecutionMode mode)
    {
        var code = @"
            namespace Out {
                const a = 10;
                export namespace In {
                    const b = 5;
                    export function* g() { yield a + b; }
                }
            }
            console.log(Out.In.g().next().value);
        ";
        Assert.Equal("15\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Mutable exported namespace variable is a live binding (#623)

    // An exported `let`/`var` that a member function mutates must be a live view through
    // external `N.x` access too, matching tsc/Node — not the snapshot taken at declaration.
    // Interpreter: the namespace object exposes a live binding onto the namespace scope slot.
    // Compiled: external `N.x` reads the var's static backing field (the one member functions
    // write), instead of the namespace object's declaration-time member. const members never
    // change, so their external value is unaffected.

    [Theory, ModeData]
    public void MutableExportedLet_MutationVisibleViaExternalAccess(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export let count = 0;
                export function inc() { count = count + 1; }
                export function get() { return count; }
            }
            N.inc();
            N.inc();
            console.log(N.get());
            console.log(N.count);
        ";
        Assert.Equal("2\n2\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void MutableExportedVar_MutationVisibleViaExternalAccess(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export var count = 0;
                export function inc() { count++; }
            }
            N.inc();
            N.inc();
            N.inc();
            console.log(N.count);
        ";
        Assert.Equal("3\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void MutableExportedLet_ExternalAccessReflectsEachMutation(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export let v = 1;
                export function set(n: number) { v = n; }
            }
            console.log(N.v);
            N.set(10);
            console.log(N.v);
            N.set(20);
            console.log(N.v);
        ";
        Assert.Equal("1\n10\n20\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void ConstExportedMember_ExternalAccessUnaffected(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export const c = 5;
                export function get() { return c; }
            }
            console.log(N.get());
            console.log(N.c);
        ";
        Assert.Equal("5\n5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespace_MutableExport_LiveViaExternalAccess(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export namespace M {
                    export let count = 0;
                    export function inc() { count++; }
                }
            }
            N.M.inc();
            N.M.inc();
            console.log(N.M.count);
        ";
        Assert.Equal("2\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Basic Namespace Features (Both Modes)

    [Theory, ModeData]
    public void BasicNamespaceWithFunction(ExecutionMode mode)
    {
        var code = @"
            namespace Foo {
                export function bar(): number { return 42; }
            }
            console.log(Foo.bar());
        ";
        Assert.Equal("42\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceWithVariable(ExecutionMode mode)
    {
        var code = @"
            namespace Constants {
                export const PI: number = 3.14159;
            }
            console.log(Constants.PI);
        ";
        Assert.Equal("3.14159\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespace(ExecutionMode mode)
    {
        var code = @"
            namespace Outer {
                export namespace Inner {
                    export function greet(): string { return ""Hello""; }
                }
            }
            console.log(Outer.Inner.greet());
        ";
        Assert.Equal("Hello\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DottedNamespaceSyntax(ExecutionMode mode)
    {
        var code = @"
            namespace A.B.C {
                export let value: number = 123;
            }
            console.log(A.B.C.value);
        ";
        Assert.Equal("123\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DeclarationMerging(ExecutionMode mode)
    {
        var code = @"
            namespace Merged {
                export function foo(): number { return 1; }
            }
            namespace Merged {
                export function bar(): number { return 2; }
            }
            console.log(Merged.foo() + Merged.bar());
        ";
        Assert.Equal("3\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceMultipleFunctions(ExecutionMode mode)
    {
        var code = @"
            namespace Utils {
                export function add(a: number, b: number): number { return a + b; }
                export function multiply(a: number, b: number): number { return a * b; }
            }
            console.log(Utils.add(2, 3));
            console.log(Utils.multiply(4, 5));
        ";
        Assert.Equal("5\n20\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DeeplyNestedDottedNamespace(ExecutionMode mode)
    {
        var code = @"
            namespace Company.Product.Feature.SubFeature {
                export const version: string = ""1.0.0"";
            }
            console.log(Company.Product.Feature.SubFeature.version);
        ";
        Assert.Equal("1.0.0\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceMergingWithDottedSyntax(ExecutionMode mode)
    {
        var code = @"
            namespace A.B {
                export let x: number = 10;
            }
            namespace A.B {
                export let y: number = 20;
            }
            console.log(A.B.x + A.B.y);
        ";
        Assert.Equal("30\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Namespace with Classes (Both Modes)

    [Theory, ModeData]
    public void NamespaceWithClass(ExecutionMode mode)
    {
        var code = @"
            namespace Shapes {
                export class Circle {
                    radius: number;
                    constructor(r: number) {
                        this.radius = r;
                    }
                    area(): number { return 3.14159 * this.radius * this.radius; }
                }
            }
            let c = new Shapes.Circle(2);
            console.log(c.area());
        ";
        // PI * 2 * 2 = 12.56636
        Assert.StartsWith("12.566", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DeepNestedNamespaceClass(ExecutionMode mode)
    {
        var code = @"
            namespace Company.Products.Widgets {
                export class Button {
                    label: string;
                    constructor(l: string) { this.label = l; }
                }
            }
            let btn = new Company.Products.Widgets.Button(""Click me"");
            console.log(btn.label);
        ";
        Assert.Equal("Click me\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceWithGenericClass(ExecutionMode mode)
    {
        var code = @"
            namespace Collections {
                export class Box<T> {
                    value: T;
                    constructor(v: T) { this.value = v; }
                }
            }
            let numBox = new Collections.Box<number>(42);
            console.log(numBox.value);
            let strBox = new Collections.Box<string>(""hello"");
            console.log(strBox.value);
        ";
        Assert.Equal("42\nhello\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceClassInheritance(ExecutionMode mode)
    {
        var code = @"
            namespace Animals {
                export class Animal {
                    name: string;
                    constructor(n: string) { this.name = n; }
                }
                export class Dog extends Animal {
                    constructor(n: string) { super(n); }
                    bark(): string { return this.name + "" says woof!""; }
                }
            }
            let dog = new Animals.Dog(""Rex"");
            console.log(dog.bark());
        ";
        Assert.Equal("Rex says woof!\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Namespace with Enums (Both Modes)

    [Theory, ModeData]
    public void NamespaceWithEnum(ExecutionMode mode)
    {
        var code = @"
            namespace Config {
                export enum LogLevel { Debug, Info, Warn, Error }
            }
            console.log(Config.LogLevel.Error);
        ";
        Assert.Equal("3\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Compiled namespace member fixes (#656, #657, #659, #660, #467)

    // #656 — a namespace must be resolvable by its bare name from a NON-member function body.
    // Compiled mode previously only carried the namespace static field on a subset of emission
    // contexts, so a top-level function reading `N.c` threw "Undefined variable 'N'".

    [Theory, ModeData]
    public void Namespace_ResolvableByBareName_FromTopLevelFunction(ExecutionMode mode)
    {
        var code = @"
            namespace N { export const c = 5; }
            function f() { return N.c; }
            console.log(f());
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void Namespace_MemberCall_FromTopLevelFunction(ExecutionMode mode)
    {
        var code = @"
            namespace N { export function ping() { return 9; } }
            function f() { return N.ping(); }
            console.log(f());
        ";
        Assert.Equal("9\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void Namespace_ExportedVar_IsLiveBinding_FromAsyncFunctionBody(ExecutionMode mode)
    {
        // The async function reads N.count AFTER two mutations; it must observe the live
        // binding (2), not the declaration-time snapshot (0). The #623 redirect now lives in
        // the shared ExpressionEmitterBase so state-machine bodies pick it up too (#656).
        var code = @"
            namespace N { export let count = 0; export function inc() { count++; } }
            async function f() { return N.count; }
            N.inc(); N.inc();
            f().then(v => console.log(v));
        ";
        Assert.Equal("2\n", TestHarness.Run(code, mode));
    }

    // #657 — namespace var/function members must not collide with module-level or
    // sibling-namespace bindings of the same name.

    [Theory, ModeData]
    public void NamespaceVar_DoesNotClobber_ModuleVar(ExecutionMode mode)
    {
        var code = @"
            const v = 100;
            namespace N { const v = 5; export const w = v; }
            console.log(v, N.w);
        ";
        Assert.Equal("100 5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void SiblingNamespaceFunctions_SameName_DoNotCollide(ExecutionMode mode)
    {
        var code = @"
            namespace A { export function f() { return 1; } }
            namespace B { export function f() { return 2; } }
            console.log(A.f(), B.f());
        ";
        Assert.Equal("1 2\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_CallsNonExportedSibling(ExecutionMode mode)
    {
        var code = @"
            namespace N { function helper() { return 41; } export function f() { return helper() + 1; } }
            console.log(N.f());
        ";
        Assert.Equal("42\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_Recursive(ExecutionMode mode)
    {
        var code = @"
            namespace N { export function fib(n: number): number { return n < 2 ? n : fib(n-1) + fib(n-2); } }
            console.log(N.fib(10));
        ";
        Assert.Equal("55\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_FallsBackToTopLevelFunction(ExecutionMode mode)
    {
        var code = @"
            function g() { return 9; }
            namespace N { export function f() { return g(); } }
            console.log(N.f());
        ";
        Assert.Equal("9\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NamespaceFunction_ShadowsTopLevelFunctionOfSameName(ExecutionMode mode)
    {
        // A namespace member function with the same simple name as a top-level function must
        // stay distinct: N.f() is 5, the bare top-level f() is 100. A sibling reference inside
        // the namespace resolves to the namespace's own helper, shadowing the top-level one.
        var code = @"
            function helper() { return 100; }
            namespace N { function helper() { return 5; } export function f() { return helper(); } }
            console.log(N.f(), helper());
        ";
        Assert.Equal("5 100\n", TestHarness.Run(code, mode));
    }

    // #659 — arrow-function and async-generator namespace members must be callable via N.member.

    [Theory, ModeData]
    public void ArrowFunctionNamespaceMember_IsCallable(ExecutionMode mode)
    {
        var code = @"
            namespace N { export const f = () => 42; }
            console.log(N.f());
        ";
        Assert.Equal("42\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorNamespaceMember_IsCallableAndIterable(ExecutionMode mode)
    {
        // The async-generator member is callable and iterable via `for await` inside an async
        // FUNCTION. (The same loop inside an async ARROW trips a separate, pre-existing async-
        // arrow `for await` lowering bug tracked by #430/#645, independent of namespaces.)
        var code = @"
            namespace N { export async function* g() { yield 1; yield 2; } }
            async function run() { for await (const x of N.g()) console.log(x); }
            run();
        ";
        Assert.Equal("1\n2\n", TestHarness.Run(code, mode));
    }

    // #660 — a nested function declaration inside a namespace member function must be callable.

    [Theory, ModeData]
    public void NestedFunctionDeclaration_InsideNamespaceMemberFunction(ExecutionMode mode)
    {
        var code = @"
            namespace N {
                export function f() {
                    function inner() { return 1; }
                    return inner();
                }
            }
            console.log(N.f());
        ";
        Assert.Equal("1\n", TestHarness.Run(code, mode));
    }

    // #467 — a namespace-scoped `export const` parses as Stmt.Const, so it carries the narrowed
    // literal type (and reassignment is rejected), matching tsc and the module-export fix #428.

    [Theory, ModeData]
    public void NamespaceExportConst_HasLiteralType(ExecutionMode mode)
    {
        // Before #467 this raised TS2322 ("Type 'number' is not assignable to type '5'") because
        // the namespace const was parsed as a mutable Stmt.Var and its type widened to number.
        var code = @"
            namespace N { export const x = 5; }
            const exact: 5 = N.x;
            console.log(exact);
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    [Fact]
    public void NamespaceExportConst_ReassignmentIsRejected()
    {
        var code = @"
            namespace N { export const x = 5; }
            N.x = 6;
        ";
        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(code));
    }

    #endregion

    #region Nested namespace bare-name reference into enclosing namespace scope (#665)

    // A function declared in a NESTED namespace must resolve a bare reference to a member of its
    // ENCLOSING namespace — the enclosing members are in lexical scope of the nested bodies (tsc).
    // The type checker previously rejected this ("Undefined variable") in both modes because a
    // nested namespace is fully checked during the enclosing namespace's first pass, before its
    // sibling functions were registered; CheckNamespace now hoists function declarations first.

    [Theory, ModeData]
    public void NestedNamespaceFunction_CallsNonExportedEnclosingFunction(ExecutionMode mode)
    {
        // The exact issue repro: O.I.f() reaches O's non-exported helper by bare name.
        var code = @"
            namespace O {
                function outerHelp() { return 7; }
                export namespace I {
                    export function f() { return outerHelp(); }
                }
            }
            console.log(O.I.f());
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceFunction_ForwardReferencesEnclosingFunction(ExecutionMode mode)
    {
        // The enclosing helper is declared AFTER the nested namespace — function hoisting makes it
        // visible regardless of source order.
        var code = @"
            namespace O {
                export namespace I {
                    export function f() { return outerHelp(); }
                }
                function outerHelp() { return 7; }
            }
            console.log(O.I.f());
        ";
        Assert.Equal("7\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceFunction_ReferencesEnclosingClassByBareName(ExecutionMode mode)
    {
        var code = @"
            namespace O {
                class Helper { val = 42; }
                export namespace I {
                    export function f() { return new Helper().val; }
                }
            }
            console.log(O.I.f());
        ";
        Assert.Equal("42\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceFunction_ReferencesSiblingNamespaceByBareName(ExecutionMode mode)
    {
        // B.f names sibling namespace A by its simple name (A is a member of the enclosing O). The
        // checker already admitted this (namespaces register eagerly); compiled codegen previously
        // threw at runtime because namespace fields are keyed by full path — ResolveNamespaceField
        // now walks enclosing prefixes so the bare name reaches O.A.
        var code = @"
            namespace O {
                export namespace A { export function g() { return 5; } }
                export namespace B { export function f() { return A.g(); } }
            }
            console.log(O.B.f());
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DeeplyNestedNamespaceFunction_ReferencesOutermostFunction(ExecutionMode mode)
    {
        var code = @"
            namespace A {
                function help() { return 1; }
                export namespace B {
                    export namespace C {
                        export function f() { return help(); }
                    }
                }
            }
            console.log(A.B.C.f());
        ";
        Assert.Equal("1\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespaceFunction_OwnHelperShadowsEnclosingOfSameName(ExecutionMode mode)
    {
        // The nested namespace declares its own `help`; the bare reference resolves to the inner
        // one (2), shadowing the enclosing namespace's `help` (1).
        var code = @"
            namespace O {
                function help() { return 1; }
                export namespace I {
                    function help() { return 2; }
                    export function f() { return help(); }
                }
            }
            console.log(O.I.f());
        ";
        Assert.Equal("2\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespace_ShadowsSameNamedTopLevelNamespace(ExecutionMode mode)
    {
        // Nested O.A shadows the top-level A for bare references from sibling O.B.
        // The interpreter previously merged O.A's members into the global A and left
        // namespaceEnvO without an "A" binding, so the pre-resolved distance-2 lookup
        // returned Undefined → "Only instances and objects have properties" (#746).
        var code = @"
            namespace A { export function g() { return 100; } }
            namespace O {
              export namespace A { export function g() { return 5; } }
              export namespace B { export function f() { return A.g(); } }
            }
            console.log(O.B.f());
            console.log(A.g());
        ";
        Assert.Equal("5\n100\n", TestHarness.Run(code, mode));
    }

    #endregion

    #region Nested namespace reference to the enclosing namespace by its OWN dotted name (#745)

    // Distinct from #665 (bare references to enclosing MEMBERS): here a nested member names the
    // ENCLOSING namespace itself (or an ancestor) by its own dotted name. The checker previously
    // rejected this ("Undefined variable 'O'") in both modes because CheckNamespace binds the
    // namespace's own name only AFTER its body — including nested bodies — is fully checked.
    // CheckNamespace now self-binds its own name into its body scope during the first pass.
    // Scope note: only first-pass `types` members (nested namespaces, classes, etc.) resolve via
    // the own dotted name; an enclosing function/var by the namespace's own name (`O.outerFunc()`)
    // is not covered — its bare form works via #665.

    [Theory, ModeData]
    public void NestedNamespace_ReferencesEnclosingNamespaceByOwnDottedName(ExecutionMode mode)
    {
        // The exact issue repro: O.B.f names O by its own name to reach O.A.g.
        var code = @"
            namespace O {
                export namespace A { export function g() { return 5; } }
                export namespace B { export function f() { return O.A.g(); } }
            }
            console.log(O.B.f());
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void DeeplyNested_ReferencesOutermostNamespaceByDottedName(ExecutionMode mode)
    {
        // A three-level-deep member names the outermost namespace by its own dotted name.
        var code = @"
            namespace Outer {
                export namespace X { export function v() { return 9; } }
                export namespace Mid {
                    export namespace Inner {
                        export function f() { return Outer.X.v(); }
                    }
                }
            }
            console.log(Outer.Mid.Inner.f());
        ";
        Assert.Equal("9\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespace_BareAndOwnDottedNameCoexist(ExecutionMode mode)
    {
        // The same body uses both the bare sibling form (A.g(), #665) and the own dotted name
        // (O.A.g(), #745) — both must resolve to the same member.
        var code = @"
            namespace O {
                export namespace A { export function g() { return 5; } }
                export namespace B { export function f() { return A.g() + O.A.g(); } }
            }
            console.log(O.B.f());
        ";
        Assert.Equal("10\n", TestHarness.Run(code, mode));
    }

    [Theory, ModeData]
    public void NestedNamespace_OwnDottedNameIsUnambiguousVsSameNamedTopLevel(ExecutionMode mode)
    {
        // A top-level A exists, but the dotted path O.A is unambiguous: it resolves the nested
        // O.A (5), never the top-level A (100).
        var code = @"
            namespace A { export function g() { return 100; } }
            namespace O {
                export namespace A { export function g() { return 5; } }
                export namespace B { export function f() { return O.A.g(); } }
            }
            console.log(O.B.f());
        ";
        Assert.Equal("5\n", TestHarness.Run(code, mode));
    }

    #endregion
}
