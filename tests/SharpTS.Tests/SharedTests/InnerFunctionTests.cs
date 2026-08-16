using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for inner function declarations (function inside function).
/// Verifies hoisting, closure capture, recursion, and nesting.
/// </summary>
public class InnerFunctionTests
{
    [Theory, ModeData]
    public void InnerFunction_Basic(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                function inner(): string {
                    return "hello";
                }
                console.log(inner());
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_WithParameters(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                function add(a: number, b: number): number {
                    return a + b;
                }
                console.log(add(3, 4));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_Recursive(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                function factorial(n: number): number {
                    if (n <= 1) return 1;
                    return n * factorial(n - 1);
                }
                console.log(factorial(5));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("120\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_CapturingOuterVariable(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                let x: number = 10;
                function inner(): number {
                    return x + 5;
                }
                console.log(inner());
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_ModifyingCapturedVariable(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                let count: number = 0;
                function increment(): void {
                    count = count + 1;
                }
                increment();
                increment();
                increment();
                console.log(count);
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_MultipleInnerFunctions(ExecutionMode mode)
    {
        var source = """
            function outer(): void {
                function greet(name: string): string {
                    return "Hello, " + name;
                }
                function farewell(name: string): string {
                    return "Goodbye, " + name;
                }
                console.log(greet("Alice"));
                console.log(farewell("Bob"));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, Alice\nGoodbye, Bob\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_DeclaredBeforeUse(ExecutionMode mode)
    {
        // Inner function declared before first call (standard order)
        var source = """
            function outer(): void {
                function inner(): string {
                    return "declared first";
                }
                console.log(inner());
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("declared first\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_CapturingParameter(ExecutionMode mode)
    {
        var source = """
            function outer(x: number): void {
                function inner(): number {
                    return x * 2;
                }
                console.log(inner());
            }
            outer(21);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_ReturnedAsValue(ExecutionMode mode)
    {
        // Inner function used as a closure factory
        var source = """
            function makeCounter(): any {
                let count: number = 0;
                function increment(): number {
                    count = count + 1;
                    return count;
                }
                return increment;
            }
            const counter = makeCounter();
            console.log(counter());
            console.log(counter());
            console.log(counter());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_SimpleRecursion(ExecutionMode mode)
    {
        // Simplest possible recursive inner function - no typed params, string result
        var source = """
            function outer(): void {
                function countdown(n): string {
                    if (n <= 0) return "done";
                    return n + "," + countdown(n - 1);
                }
                console.log(countdown(3));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,2,1,done\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_SingleSelfCall(ExecutionMode mode)
    {
        // Just one self-call to isolate the most basic recursive behavior
        var source = """
            function outer(): void {
                function selfCall(n): string {
                    if (n <= 0) return "base";
                    return selfCall(0);
                }
                console.log(selfCall(1));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("base\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_SelfRefCheck(ExecutionMode mode)
    {
        // Check that the self-reference local is accessible as a function
        var source = """
            function outer(): void {
                function selfCall(): string {
                    let f = selfCall;
                    return "ok";
                }
                console.log(selfCall());
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_NestedInnerFunctions(ExecutionMode mode)
    {
        // Function inside function inside function
        var source = """
            function outer(): void {
                function middle(): string {
                    function inner(): string {
                        return "deep";
                    }
                    return inner();
                }
                console.log(middle());
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("deep\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_ForwardReferenceHoisted(ExecutionMode mode)
    {
        // Reduced repro of issue #40: an earlier-declared hoisted function
        // forward-references a later-declared hoisted function. Both are
        // captured via the enclosing scope; spec-correct behavior requires
        // the forward reference to resolve at call time.
        var source = """
            function outer(): string {
                function first(): string { return second(); }
                function second(): string { return "forward"; }
                return first();
            }
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("forward\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_MutualRecursion(ExecutionMode mode)
    {
        // Two hoisted functions reference each other. Before the two-pass
        // hoist, isEven's capture of isOdd snapshotted null.
        var source = """
            function outer(): void {
                function isEven(n: number): boolean {
                    if (n === 0) return true;
                    return isOdd(n - 1);
                }
                function isOdd(n: number): boolean {
                    if (n === 0) return false;
                    return isEven(n - 1);
                }
                console.log(isEven(4));
                console.log(isOdd(7));
            }
            outer();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_ThreeWayForwardReferenceCycle(ExecutionMode mode)
    {
        // Three-way cycle, all forward references.
        var source = """
            function outer(): string {
                function a(): string { return "a->" + b(); }
                function b(): string { return "b->" + c(); }
                function c(): string { return "c"; }
                return a();
            }
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a->b->c\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_ForwardReferenceInArrowBody(ExecutionMode mode)
    {
        // Exact shape from issue #40: hoisted forward-reference inside an
        // IIFE (arrow body), with the cross-reference going through the
        // enclosing arrow's scope display class.
        var source = """
            const f = (function runInContext() {
                function lodash(value: any): any {
                    return Wrapper(value);
                }
                function Wrapper(v: any): string {
                    return "wrapped:" + v;
                }
                return lodash;
            })();
            console.log(f("x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("wrapped:x\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_NewOnPeerHoistedFunction(ExecutionMode mode)
    {
        // Issue #59: `new X()` inside an inner function where X is a peer
        // hoisted function. AstVisitorBase.VisitNew was not visiting Callee
        // (asymmetric with VisitCall), so ClosureAnalyzer never saw X as a
        // referenced variable and the compiled callsA body didn't receive
        // X in its display class — `new A()` silently returned null.
        var source = """
            const f: any = (function outer() {
                function A(this: any): void { this.tag = "a-object"; }
                function callsA(): any { return new (A as any)(); }
                return callsA();
            })();
            console.log(f.tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a-object\n", output);
    }

    [Theory, ModeData]
    public void InnerFunction_NewInsideLogicalFallback(ExecutionMode mode)
    {
        // Issue #59, lodash shape: `new (x || MapCache)` — MapCache as the
        // RHS of a short-circuit inside a `new` expression still needs to
        // be registered as a reference by ClosureAnalyzer so the inner
        // function's display class receives it.
        var source = """
            const f: any = (function outer() {
                function MapCache(this: any): void { this.kind = "map"; }
                function memoize(): any {
                    const cache: any = null;
                    return new (cache || MapCache)();
                }
                return memoize();
            })();
            console.log(f.kind);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("map\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_ArrayIsArray_AsValue(ExecutionMode mode)
    {
        // Regression: `var f = Array.isArray` previously stored the boolean
        // `false` in compiled mode because $Runtime.GetProperty fell through
        // to reflection on System.Type (finding System.Type.IsArray) when
        // the ArrayStaticEmitter's property-get path returned false. Fixed
        // by emitting a $TSFunction wrapping $Runtime.IsArray at the
        // property-access site. Common pattern in lodash-style libraries
        // that cache native methods as locals at module init.
        var source = """
            const f: any = Array.isArray;
            console.log(typeof f);
            console.log(f([1, 2, 3]));
            console.log(f("not an array"));
            console.log(f(null));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\ntrue\nfalse\nfalse\n", output);
    }

    [Theory, CompiledOnlyData]
    public void BuiltIn_ClassNameProperty_Preserved(ExecutionMode mode)
    {
        // Compiled-only: regression guard that closing the Type-reflection
        // leak in $Runtime.GetProperty still preserves Function.prototype.name.
        // JS spec (§19.2.4.2) says `ClassName.name === "ClassName"`. The Type
        // branch's explicit "name" handler must still run; only the unguarded
        // reflection fallback is skipped. (Interpreter has a distinct gap
        // around class-reference `.name` access, outside this fix's scope.)
        var source = """
            class MyClass {
                static greet(): string { return "hi"; }
            }
            console.log(MyClass.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("MyClass\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_MathMethods_AsValues(ExecutionMode mode)
    {
        // Issue #60: `var f = Math.floor; f(x)` must resolve to the Math.floor
        // function. Previously `Math` emitted as a null constant and
        // `MathStaticEmitter.TryEmitStaticPropertyGet` only handled PI/E,
        // so value-form method access fell through to $Runtime.GetProperty(null, …)
        // and yielded null. Every native-method caching pattern
        // (`var nativeMax = Math.max` in lodash et al.) silently produced
        // a null-callable that returned null on every invocation.
        var source = """
            const floor: any = Math.floor;
            const ceil: any = Math.ceil;
            const max: any = Math.max;
            const min: any = Math.min;
            const abs: any = Math.abs;
            const round: any = Math.round;
            console.log(typeof floor, typeof max, typeof round);
            console.log(floor(2.9), ceil(2.1), abs(-5));
            console.log(max(1, 2, 3, 4), min(4, 3, 2, 1));
            console.log(round(2.5), round(-0.5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function function function\n2 3 5\n4 1\n3 0\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_NumberMethods_AsValues(ExecutionMode mode)
    {
        // Issue #60: Number.isInteger / isFinite / isInteger / isSafeInteger
        // as stored values. Wraps $Runtime.NumberIs* in $TSFunction.
        var source = """
            const isInt: any = Number.isInteger;
            const isFin: any = Number.isFinite;
            const isSafe: any = Number.isSafeInteger;
            console.log(typeof isInt, isInt(42), isInt(4.5), isInt("42"));
            console.log(typeof isFin, isFin(42), isFin(Infinity), isFin(NaN));
            console.log(typeof isSafe, isSafe(42), isSafe(Math.pow(2, 53)));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function true false false\nfunction true false false\nfunction true false\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_StringFromCharCode_AsValue(ExecutionMode mode)
    {
        // Issue #60: String.fromCharCode as a stored value. $Runtime's
        // helper already accepts object[] so direct $TSFunction wrapping
        // works via AdjustArgs' rest-parameter slot.
        var source = """
            const fromCC: any = String.fromCharCode;
            console.log(typeof fromCC);
            console.log(fromCC(65));
            console.log(fromCC(72, 105, 33));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nA\nHi!\n", output);
    }

    [Theory, CompiledOnlyData]
    public void BuiltIn_MathMaxMin_Variadic_WithCoercion(ExecutionMode mode)
    {
        // Compiled-only: verifies that the Math.max/min adapters coerce
        // through ToNumber per ES spec (`Math.max("2", "3") === 3`) and
        // short-circuit NaN correctly. Wrapped via the object[] rest-param
        // slot on $TSFunction.AdjustArgs.
        var source = """
            const max: any = Math.max;
            const min: any = Math.min;
            console.log(max("2", "3", "1"));
            console.log(min("5", "2", "8"));
            console.log(max());
            console.log(min());
            console.log(isNaN(max(1, NaN, 2)));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n-Infinity\nInfinity\ntrue\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_ArrayConstructor_AllForms(ExecutionMode mode)
    {
        // Issue #61: `new Array(n)` / `Array(n)` / `new Array(a, b, c)` must
        // allocate the right array, not return null.
        //   - 0 args → empty []
        //   - 1 numeric arg n → length-n array (nulls represent JS undefined)
        //   - 1 non-numeric arg x → [x]
        //   - N args → [a, b, c, …]
        var source = """
            console.log((new Array(3) as any).length);
            console.log((Array(3) as any).length);
            console.log(new Array(1, 2, 3));
            console.log(Array("hello"));
            console.log(Array());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n3\n[1, 2, 3]\n[hello]\n[]\n", output);
    }

    [Theory, CompiledOnlyData]
    public void BuiltIn_ArrayConstructor_AsValue_CallForm(ExecutionMode mode)
    {
        // Issue #61: calling an Array reference stored in a variable — the
        // pattern lodash's runInContext uses (`var Array = context.Array; Array(n)`).
        // The runtime Invoke dispatcher recognizes System.Type callees and
        // routes IList<object> to the Array constructor helper.
        //
        // Expected output changed 2026-04-23 (issue #73 Stage E.2 M2):
        // `Array(n)` now creates a sparse n-length array of holes (ECMA-262
        // §23.1.1.1), not a dense list of nulls. `r1[0]` reads "undefined"
        // instead of "null" — matches the interpreter and real JS engines.
        var source = """
            const A: any = Array;
            const r1: any = A(2);
            console.log(r1.length, r1[0], r1[1]);
            const r2: any = A("x", "y");
            console.log(r2.length, r2[0], r2[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2 undefined undefined\n2 x y\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_NumberStringBoolean_BareIdentifiers(ExecutionMode mode)
    {
        // Issue #62: bare `Number` / `String` / `Boolean` must resolve to
        // something, not throw ReferenceError. typeof should match JS spec
        // ("function"), and storing them in a variable must round-trip.
        var source = """
            console.log(typeof Number, typeof String, typeof Boolean);
            const n: any = Number;
            const s: any = String;
            const b: any = Boolean;
            console.log(typeof n, typeof s, typeof b);
            console.log(Number === Number, String === String, Boolean === Boolean);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function function function\nfunction function function\ntrue true true\n", output);
    }

    [Theory, ModeData]
    public void BuiltIn_String_Alias_IsCallableAsArrayCallback(ExecutionMode mode)
    {
        var source = """
            const stringify: any = String;
            console.log([1, true, null].map(stringify).join("|"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1|true|null\n", output);
    }

    [Theory, CompiledOnlyData]
    public void BuiltIn_StoredTypeToken_StaticMemberDispatch(ExecutionMode mode)
    {
        // Issue #63: when a built-in type constructor is stored in a variable
        // (const A = Array; const N = Number; const S = String), accessing its
        // static members must resolve to the same $TSFunction wrapper the
        // compile-time ArrayStaticEmitter/NumberStaticEmitter/StringStaticEmitter
        // would emit at a bare Array.isArray site. Exercised by lodash's
        // runInContext pattern: `var isArray = Array.isArray;`.
        var source = """
            const A: any = Array;
            const N: any = Number;
            const S: any = String;
            const isArr = A.isArray;
            const isInt = N.isInteger;
            const fromCC = S.fromCharCode;
            console.log(typeof isArr, typeof isInt, typeof fromCC);
            console.log(isArr([1,2,3]), isArr("x"));
            console.log(isInt(42), isInt(4.5));
            console.log(fromCC(72, 105));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function function function\ntrue false\ntrue false\nHi\n", output);
    }

    [Theory, CompiledOnlyData]
    public void BuiltIn_ClassUnknownStaticProperty_IsUndefined(ExecutionMode mode)
    {
        // Compiled-only: `ClassName.nonexistentProp` must return undefined
        // per ECMAScript §7.3.2 Get, not a .NET reflection bleed-through
        // value. Before the fix, `Foo.isClass` returned System.Type.IsClass,
        // `Foo.fullName` returned Foo's .NET full type name, etc.
        var source = """
            class Foo {}
            console.log(typeof (Foo as any).isClass);
            console.log(typeof (Foo as any).fullName);
            console.log(typeof (Foo as any).isSealed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\nundefined\n", output);
    }
}
