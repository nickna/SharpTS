using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Verifies that <c>new Number(x)</c>, <c>new String(x)</c>, and
/// <c>new Boolean(x)</c> produce boxed wrapper objects in both interpreter
/// and compiled mode, matching Node.js / ECMA-262 semantics (#360).
/// </summary>
public class PrimitiveWrapperTests
{
    // ── typeof ───────────────────────────────────────────────────────────────

    [Theory, ModeData]
    public void Number_NewWrapper_TypeofIsObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof new Number(5));", mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void String_NewWrapper_TypeofIsObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof new String('x'));", mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void Boolean_NewWrapper_TypeofIsObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof new Boolean(true));", mode);
        Assert.Equal("object\n", output);
    }

    // ── instanceof Object ────────────────────────────────────────────────────

    [Theory, ModeData]
    public void Number_NewWrapper_InstanceofObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new Number(5) instanceof Object);", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void String_NewWrapper_InstanceofObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new String('x') instanceof Object);", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Boolean_NewWrapper_InstanceofObject(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new Boolean(true) instanceof Object);", mode);
        Assert.Equal("true\n", output);
    }

    // ── instanceof own constructor ───────────────────────────────────────────

    [Theory, ModeData]
    public void Number_NewWrapper_InstanceofNumber(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new Number(5) instanceof Number);", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void String_NewWrapper_InstanceofString(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new String('x') instanceof String);", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Boolean_NewWrapper_InstanceofBoolean(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new Boolean(false) instanceof Boolean);", mode);
        Assert.Equal("true\n", output);
    }

    // ── primitive (non-new) is NOT an instance ───────────────────────────────
    // Per ECMA-262 OrdinaryHasInstance a bare primitive is never an instance of
    // its wrapper constructor; only boxed `new Number(5)` wrappers are. Both modes
    // now agree (#360 interp, #375 compiled).

    [Theory, ModeData]
    public void Number_Bare_NotInstanceofNumber(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((5 as any) instanceof Number);", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void String_Bare_NotInstanceofString(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(('x' as any) instanceof String);", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Boolean_Bare_NotInstanceofBoolean(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((true as any) instanceof Boolean);", mode);
        Assert.Equal("false\n", output);
    }

    // ── Symbol: bare not an instance, Object(sym) wrapper is (#449) ───────────
    // Symbol has no `new` form, so its only boxed form is `Object(Symbol())`.
    // A bare symbol primitive is NOT an instance; the boxed wrapper IS.

    [Theory, ModeData]
    public void Symbol_Bare_NotInstanceofSymbol(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((Symbol('s') as any) instanceof Symbol);", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Symbol_Boxed_InstanceofSymbol(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((Object(Symbol('s')) as any) instanceof Symbol);", mode);
        Assert.Equal("true\n", output);
    }

    // ── call form still coerces (no wrapper) ────────────────────────────────

    [Theory, ModeData]
    public void Number_CallForm_ReturnsPrimitive(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof Number('42'));", mode);
        Assert.Equal("number\n", output);
    }

    [Theory, ModeData]
    public void String_CallForm_ReturnsPrimitive(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof String(42));", mode);
        Assert.Equal("string\n", output);
    }

    [Theory, ModeData]
    public void Boolean_CallForm_ReturnsPrimitive(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(typeof Boolean(1));", mode);
        Assert.Equal("boolean\n", output);
    }

    // ── wrapper primitive value / conversion ─────────────────────────────────

    [Theory, ModeData]
    public void Number_NoArg_WrapsZero(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new Number() instanceof Number);", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void String_Length_MatchesPrimitive(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log(new String('hello').length);", mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void String_IndexedAccess_ReturnsChar(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((new String('hi') as any)[0]);", mode);
        Assert.Equal("h\n", output);
    }

    // ── method dispatch on wrappers ──────────────────────────────────────────

    [Theory, ModeData]
    public void Number_ToFixed_WorksOnWrapper(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((new Number(5) as any).toFixed(2));", mode);
        Assert.Equal("5.00\n", output);
    }

    [Theory, ModeData]
    public void String_ToUpperCase_WorksOnWrapper(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((new String('hello') as any).toUpperCase());", mode);
        Assert.Equal("HELLO\n", output);
    }

    [Theory, ModeData]
    public void Boolean_ToString_WorksOnWrapper(ExecutionMode mode)
    {
        var output = TestHarness.Run("console.log((new Boolean(true) as any).toString());", mode);
        Assert.Equal("true\n", output);
    }

    // ── valueOf override honored in general ToPrimitive (#574) ───────────────
    // ECMA-262 7.1.1: `+` and `==` ToPrimitive (default hint) an object operand,
    // which calls an own valueOf override before reading the wrapper's slot.

    [Theory, ModeData]
    public void Number_ValueOfOverride_HonoredInAddition(ExecutionMode mode)
    {
        var source = """
            const n: any = new Number(1);
            n.valueOf = function () { return 9; };
            console.log(n + 1);
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Number_ValueOfOverride_HonoredInLooseEquality(ExecutionMode mode)
    {
        var source = """
            const n: any = new Number(1);
            n.valueOf = function () { return 9; };
            console.log(n == 9);
            console.log(n == 1);
            """;
        Assert.Equal("true\nfalse\n", TestHarness.Run(source, mode));
    }

    // ── string coercion of wrappers: template literals & String() ────────────
    // ECMA-262 7.1.1 (string hint): toString first. A bare wrapper yields its
    // primitive's natural string; an own valueOf override does NOT affect a
    // string coercion; an own toString override does. Interpreter and compiled
    // must agree (the divergence these tests pin down).

    [Theory, ModeData]
    public void Number_Wrapper_TemplateLiteral_UsesPrimitive(ExecutionMode mode)
    {
        Assert.Equal("v:1\n", TestHarness.Run("const n: any = new Number(1); console.log(`v:${n}`);", mode));
    }

    [Theory, ModeData]
    public void Number_Wrapper_TemplateLiteral_IgnoresValueOfOverride(ExecutionMode mode)
    {
        // String hint resolves toString first (inherited → primitive), so a
        // valueOf override is never consulted: `${n}` is "1", not "9".
        var source = """
            const n: any = new Number(1);
            n.valueOf = function () { return 9; };
            console.log(`v:${n}`);
            """;
        Assert.Equal("v:1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Number_Wrapper_TemplateLiteral_HonorsToStringOverride(ExecutionMode mode)
    {
        var source = """
            const n: any = new Number(1);
            n.toString = function () { return "NUM"; };
            console.log(`v:${n}`);
            """;
        Assert.Equal("v:NUM\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void String_Wrapper_TemplateLiteral_UsesPrimitive(ExecutionMode mode)
    {
        Assert.Equal("v:x\n", TestHarness.Run("const s: any = new String(\"x\"); console.log(`v:${s}`);", mode));
    }

    [Theory, ModeData]
    public void String_CallForm_CoercesBoxedNumber(ExecutionMode mode)
    {
        Assert.Equal("1\n", TestHarness.Run("console.log(String(new Number(1)));", mode));
    }

    [Theory, ModeData]
    public void String_CallForm_BoxedNumber_IgnoresValueOfOverride(ExecutionMode mode)
    {
        var source = """
            const n: any = new Number(1);
            n.valueOf = function () { return 9; };
            console.log(String(n));
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void String_CallForm_BoxedString_HonorsToStringOverride(ExecutionMode mode)
    {
        var source = """
            const s: any = new String("x");
            s.toString = function () { return "STR"; };
            console.log(String(s));
            """;
        Assert.Equal("STR\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void String_CallForm_CoercesBoxedBoolean(ExecutionMode mode)
    {
        Assert.Equal("true\n", TestHarness.Run("console.log(String(new Boolean(true)));", mode));
    }
}
