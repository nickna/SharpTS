using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiler gap surfaced by the attempted <c>util</c> stdlib
/// migration: global class names (<c>Promise</c>, <c>Buffer</c>, <c>TextEncoder</c>,
/// <c>TextDecoder</c>, <c>FinalizationRegistry</c>, <c>Proxy</c>, <c>BroadcastChannel</c>)
/// were not resolvable as bare identifiers — only via constructor-expression
/// pattern matching (<c>new TextEncoder()</c>) or namespace-call pattern matching
/// (<c>Promise.resolve()</c>). This made <c>x instanceof Promise</c>,
/// <c>typeof Buffer === 'function'</c>, and any stdlib module that carries one
/// of these as a value throw <c>Undefined variable 'X'</c>.
/// </summary>
/// <remarks>
/// Fixed by extending the type checker's bare-identifier allowlist
/// (<see cref="SharpTS.TypeSystem.TypeChecker.CheckVariable"/>), registering
/// <c>TextEncoder</c>/<c>TextDecoder</c> in <c>BuiltInConstructorFactory</c>,
/// adding <c>Buffer</c> to the interpreter's singleton globals, and registering
/// a minimal <c>Promise</c> constructor sentinel. The compiled path's
/// <c>TryEmitBuiltInClassType</c> now also covers <c>Buffer</c> so bare
/// references emit a <c>Ldtoken</c> of the matching runtime type.
/// </remarks>
public class BareBuiltInClassRefTests
{
    [Theory, ModeData]
    public void Promise_BareValue_TypeofIsFunction(ExecutionMode mode)
    {
        var source = @"
            const ref: any = Promise;
            console.log(typeof ref);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Promise_InstanceOf_ResolvedPromise(ExecutionMode mode)
    {
        var source = @"
            const p = Promise.resolve(42);
            console.log(p instanceof Promise);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Buffer_BareValue_TypeofIsFunction(ExecutionMode mode)
    {
        var source = @"
            const ref: any = Buffer;
            console.log(typeof ref);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void Buffer_InstanceOf_FromReturnsTrue(ExecutionMode mode)
    {
        var source = @"
            const b = Buffer.from([104, 105]);
            console.log(b instanceof Buffer);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TextEncoder_BareValue_TypeofIsFunction(ExecutionMode mode)
    {
        var source = @"
            const ref: any = TextEncoder;
            console.log(typeof ref);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void TextEncoder_InstanceOf_NewInstance(ExecutionMode mode)
    {
        var source = @"
            const e = new TextEncoder();
            console.log(e instanceof TextEncoder);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TextDecoder_BareValue_TypeofIsFunction(ExecutionMode mode)
    {
        var source = @"
            const ref: any = TextDecoder;
            console.log(typeof ref);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void TextDecoder_InstanceOf_NewInstance(ExecutionMode mode)
    {
        var source = @"
            const d = new TextDecoder();
            console.log(d instanceof TextDecoder);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void BareBuiltIn_InstanceOf_NegativeCases(ExecutionMode mode)
    {
        // Negative checks — each bare-referencable class should reject the
        // wrong LHS without throwing.
        var source = @"
            console.log({} instanceof Promise);
            console.log({} instanceof Buffer);
            console.log({} instanceof TextEncoder);
            console.log({} instanceof TextDecoder);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\n", output);
    }

    // ── #331: typed-array / buffer constructors as bare values ────────────
    // `new Int8Array(...)` was intercepted as a New expression, but the
    // bare-identifier form (`var x = Int8Array`, `typeof Uint8Array`,
    // `x instanceof ArrayBuffer`) threw `Undefined variable` in compiled mode.

    [Theory, ModeData]
    public void TypedArrayCtors_BareValue_TypeofAndTag(ExecutionMode mode)
    {
        // One representative per element width/signedness/kind plus the three
        // buffer/view constructors — every value must report typeof "function"
        // and brand "[object Function]" (the #314 tag rule applied to the
        // System.Type tokens these resolve to).
        var source = """
            var ctors: any[] = [
                Int8Array, Uint8Array, Uint8ClampedArray, Int16Array, Uint16Array,
                Int32Array, Uint32Array, Float32Array, Float64Array,
                BigInt64Array, BigUint64Array,
                ArrayBuffer, SharedArrayBuffer, DataView,
            ];
            for (var i = 0; i < ctors.length; i++) {
                console.log(typeof ctors[i], Object.prototype.toString.call(ctors[i]));
            }
            """;
        var output = TestHarness.Run(source, mode);
        var expectedLine = "function [object Function]\n";
        Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat(expectedLine, 14)), output);
    }

    // ── #334: typed-array / buffer instances recognised by `instanceof` ───
    // Compiled routes through the emitted Type token's IsAssignableFrom;
    // interp brand-checks each constructor value's instance predicate
    // (IBuiltInTypeConstructor). Both must agree with Node.

    [Theory, ModeData]
    public void TypedArrayCtors_InstanceOf_OwnConstructor(ExecutionMode mode)
    {
        var source = """
            console.log(new Int8Array(4) instanceof Int8Array);
            console.log(new Float64Array(2) instanceof Float64Array);
            console.log(new ArrayBuffer(8) instanceof ArrayBuffer);
            console.log(new SharedArrayBuffer(8) instanceof SharedArrayBuffer);
            console.log(new DataView(new ArrayBuffer(8)) instanceof DataView);
            console.log(({} as any) instanceof Int8Array);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void TypedArrayCtors_InstanceOf_DistinctElementTypesDoNotMatch(ExecutionMode mode)
    {
        // Each typed array directly extends the abstract %TypedArray%, not each
        // other — so cross-element-type `instanceof` is false (matches Node).
        var source = """
            console.log(new Int8Array(4) instanceof Uint8Array);
            console.log(new Float32Array(2) instanceof Float64Array);
            console.log(new ArrayBuffer(8) instanceof SharedArrayBuffer);
            console.log(new Int8Array(4) instanceof ArrayBuffer);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void BinaryTypes_InstanceOf_Object(ExecutionMode mode)
    {
        // Every typed-array / buffer / view instance is also an Object (#334).
        var source = """
            console.log(new Int8Array(4) instanceof Object);
            console.log(new ArrayBuffer(8) instanceof Object);
            console.log(new SharedArrayBuffer(8) instanceof Object);
            console.log(new DataView(new ArrayBuffer(8)) instanceof Object);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Primitives_InstanceOf_Object_AreFalse(ExecutionMode mode)
    {
        // Per ECMA-262 OrdinaryHasInstance, a primitive (or undefined) operand
        // short-circuits to false — only non-primitive objects satisfy
        // `instanceof Object`. Compiled mode previously matched System.Object
        // via IsAssignableFrom, which is true for every boxed primitive and the
        // undefined sentinel (#342); interp already excluded them (#334).
        var source = """
            console.log((5) instanceof Object);
            console.log("s" instanceof Object);
            console.log(true instanceof Object);
            console.log(null instanceof Object);
            console.log(undefined instanceof Object);
            console.log(Symbol("x") instanceof Object);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Objects_InstanceOf_Object_AreTrue(ExecutionMode mode)
    {
        // Every non-primitive guest value — object/array literals, functions,
        // and built-in instances — is an Object (#342). (Boxed-primitive
        // wrappers like `new Number(5)` are also objects, but interp currently
        // produces a bare primitive for those — see #360 — so they're excluded
        // from this cross-mode test.)
        var source = """
            console.log(({}) instanceof Object);
            console.log(([]) instanceof Object);
            console.log((() => 1) instanceof Object);
            console.log(new Map() instanceof Object);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }
}
