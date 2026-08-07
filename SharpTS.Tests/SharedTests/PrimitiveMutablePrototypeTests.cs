using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for issue #474: Number/Boolean/String.prototype must be mutable ordinary
/// objects, and boxed primitive wrappers produced by ToObject must read array-like
/// state (length, indexed elements) through the prototype chain.
///
/// Interpreter-only because compiled mode already passes these cases.
/// </summary>
public class PrimitiveMutablePrototypeTests
{
    // ── Number.prototype mutability ──────────────────────────────────────────

    [Fact]
    public void NumberPrototype_IndexAssignment_DoesNotThrow()
    {
        var source = """
            (Number.prototype as any)[0] = 42;
            console.log((Number.prototype as any)[0]);
            """;
        Assert.Equal("42\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void NumberPrototype_LengthAssignment_DoesNotThrow()
    {
        var source = """
            (Number.prototype as any).length = 3;
            console.log((Number.prototype as any).length);
            """;
        Assert.Equal("3\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void NumberPrototype_MemberAssignment_DoesNotThrow()
    {
        var source = """
            (Number.prototype as any).foo = "bar";
            console.log((Number.prototype as any).foo);
            """;
        Assert.Equal("bar\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // ── Boolean.prototype mutability ─────────────────────────────────────────

    [Fact]
    public void BooleanPrototype_IndexAssignment_DoesNotThrow()
    {
        var source = """
            (Boolean.prototype as any)[0] = true;
            console.log((Boolean.prototype as any)[0]);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void BooleanPrototype_LengthAssignment_DoesNotThrow()
    {
        var source = """
            (Boolean.prototype as any).length = 1;
            console.log((Boolean.prototype as any).length);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void BooleanPrototype_DeletedToStringFallsBackToObjectPrototype()
    {
        var source = """
            delete (Boolean.prototype as any).toString;
            console.log(Boolean.prototype.toString());
            console.log((new Boolean() as any).toString());
            """;
        Assert.Equal(
            "[object Boolean]\n[object Boolean]\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // ── String.prototype mutability ──────────────────────────────────────────

    [Fact]
    public void StringPrototype_IndexAssignment_DoesNotThrow()
    {
        var source = """
            (String.prototype as any)[99] = "x";
            console.log((String.prototype as any)[99]);
            """;
        Assert.Equal("x\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void StringPrototype_MemberAssignment_DoesNotThrow()
    {
        var source = """
            (String.prototype as any).customProp = 42;
            console.log((String.prototype as any).customProp);
            """;
        Assert.Equal("42\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // ── Boxed number primitive reads through Number.prototype (issue repro) ──

    [Fact]
    public void Every_NumberPrimitive_CallbackRunsAndReceivesNumberWrapper()
    {
        // Exact repro from #474: Number.prototype[0]=1, .length=1 let every()
        // iterate once over the boxed 2.5 wrapper, whose `o` arg is instanceof Number.
        var source = """
            (Number.prototype as any)[0] = 1;
            (Number.prototype as any).length = 1;
            console.log(Array.prototype.every.call(2.5 as any, function (v: any, i: any, o: any) {
              return o instanceof Number;
            }));
            """;
        Assert.Equal("true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void Every_NumberPrimitive_CallbackReceivesExpectedElement()
    {
        // Element at index 0 should be Number.prototype[0] (the value we set).
        var source = """
            (Number.prototype as any)[0] = 99;
            (Number.prototype as any).length = 1;
            let elem: any = undefined;
            Array.prototype.every.call(2.5 as any, function (v: any): any { elem = v; return true; });
            console.log(elem);
            """;
        Assert.Equal("99\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // ── Boxed boolean primitive reads through Boolean.prototype ──────────────

    [Fact]
    public void Every_BooleanPrimitive_CallbackRunsAndReceivesBooleanWrapper()
    {
        var source = """
            (Boolean.prototype as any)[0] = false;
            (Boolean.prototype as any).length = 1;
            console.log(Array.prototype.every.call(false as any, function (v: any, i: any, o: any) {
              return o instanceof Boolean;
            }));
            """;
        Assert.Equal("true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // ── some / forEach / filter / reduce also work ───────────────────────────

    [Fact]
    public void Some_NumberPrimitive_ReturnsTrue()
    {
        var source = """
            (Number.prototype as any)[0] = 1;
            (Number.prototype as any).length = 1;
            console.log(Array.prototype.some.call(0 as any, function (v: any, i: any, o: any) {
              return o instanceof Number;
            }));
            """;
        Assert.Equal("true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void ForEach_NumberPrimitive_CallbackInvoked()
    {
        var source = """
            (Number.prototype as any)[0] = 7;
            (Number.prototype as any).length = 1;
            let count = 0;
            Array.prototype.forEach.call(1 as any, function (): void { count++; });
            console.log(count);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
