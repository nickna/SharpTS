using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for computed accessor names in class bodies (#261):
/// get [Symbol.toStringTag]() / static get [Symbol.species]().
/// Symbol-keyed accessors run in both modes on class declarations (compiled
/// support added in #266; module-local Symbol keys confirmed by #282) and on
/// class expressions (compiled support added in #281). Literal computed keys
/// fold to ordinary names in the parser.
/// </summary>
public class ComputedAccessorNameTests
{
    [Theory, ModeData]
    public void StaticSymbolGetter_SpeciesPattern(ExecutionMode mode)
    {
        var source = """
            class MyP extends Promise<any> {
                static get [Symbol.species]() { return Promise; }
            }
            console.log((MyP as any)[Symbol.species] === Promise);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void InstanceSymbolGetterAndSetter(ExecutionMode mode)
    {
        var source = """
            class Tagged {
                stored: any = null;
                get [Symbol.toStringTag]() { return "Tagged!"; }
                set [Symbol.toPrimitive](v: any) { this.stored = v; }
            }
            const t = new Tagged();
            console.log((t as any)[Symbol.toStringTag]);
            (t as any)[Symbol.toPrimitive] = 42;
            console.log(t.stored);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Tagged!\n42\n", output);
    }

    [Theory, ModeData]
    public void StaticSymbolGetter_InheritedThroughSubclassChain(ExecutionMode mode)
    {
        var source = """
            class Base {
                static get [Symbol.species]() { return Base; }
            }
            class Sub extends Base {}
            console.log((Sub as any)[Symbol.species] === Base);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // Regression for #281: symbol-keyed computed accessors on class EXPRESSIONS.
    // The class-expression emission path gained a .cctor registration hook +
    // synthetic-method body emission mirroring the class-declaration path (#266),
    // so these now run in both modes.
    [Theory, ModeData]
    public void SymbolAccessor_InClassExpression(ExecutionMode mode)
    {
        var source = """
            const C = class {
                get [Symbol.toStringTag]() { return "expr"; }
            };
            console.log((new C() as any)[Symbol.toStringTag]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("expr\n", output);
    }

    [Theory, ModeData]
    public void SymbolAccessor_InClassExpression_GetterSetterWithFieldAndModuleLocalKey(ExecutionMode mode)
    {
        // Getter + setter sharing one symbol slot, a well-known key reading an
        // instance field, and a module-local Symbol key — all on a class
        // expression. (#281)
        var source = """
            const mk = Symbol("mk");
            const C = class {
                _v: number = 5;
                get [Symbol.toStringTag]() { return "tag" + this._v; }
                get [mk]() { return this._v; }
                set [mk](x: number) { this._v = x; }
            };
            const c = new C() as any;
            console.log(c[Symbol.toStringTag], c[mk]);
            c[mk] = 99;
            console.log(c[Symbol.toStringTag], c[mk]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("tag5 5\ntag99 99\n", output);
    }

    [Theory, ModeData]
    public void SymbolAccessor_InClassExpression_Static(ExecutionMode mode)
    {
        // Static symbol-keyed getter on a class expression dispatches through the
        // registry's static slot. (#281)
        var source = """
            const C = class {
                static get [Symbol.toStringTag]() { return "StaticTag"; }
            };
            console.log((C as any)[Symbol.toStringTag]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("StaticTag\n", output);
    }

    // A get and set accessor for the SAME symbol must coexist (they merge into one
    // registry slot) and the .cctor registration must not disturb static field init.
    [Theory, ModeData]
    public void SymbolGetterAndSetter_SameKey_WithStaticField(ExecutionMode mode)
    {
        var source = """
            class Box {
                static tag: number = 7;
                value: any = 0;
                get [Symbol.toPrimitive]() { return this.value; }
                set [Symbol.toPrimitive](v: any) { this.value = v * 2; }
            }
            const b = new Box();
            (b as any)[Symbol.toPrimitive] = 21;
            console.log((b as any)[Symbol.toPrimitive]);
            console.log(Box.tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n7\n", output);
    }

    // Regression for #282: a MODULE-LOCAL Symbol key (not a well-known symbol)
    // must register and dispatch in compiled mode. The key expression is
    // evaluated in the lazily-run class .cctor, which executes after the
    // top-level binding is assigned, so `key` resolves correctly. Getter and
    // setter share one registry slot.
    [Theory, ModeData]
    public void SymbolAccessor_ModuleLocalKey(ExecutionMode mode)
    {
        var source = """
            const key = Symbol("k");
            class Box {
                _v: number = 10;
                get [key]() { return this._v; }
                set [key](x: number) { this._v = x; }
            }
            const b = new Box() as any;
            console.log(b[key]);
            b[key] = 42;
            console.log(b[key]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n42\n", output);
    }

    [Theory, ModeData]
    public void LiteralComputedKeys_FoldToOrdinaryNames(ExecutionMode mode)
    {
        var source = """
            class Lit {
                get ["foo"]() { return 7; }
            }
            console.log(new Lit().foo);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void StringAndKeywordAccessorNames_Parse(ExecutionMode mode)
    {
        var source = """
            class Kw {
                get "quoted"() { return 1; }
                get type() { return 2; }
            }
            console.log(new Kw()["quoted"], new Kw().type);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2\n", output);
    }
}
