using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for getter and setter properties in classes. Runs against both interpreter and compiler.
/// </summary>
public class GettersSettersTests
{
    #region Basic Getter/Setter

    [Theory, ModeData]
    public void Getter_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            class Box {
                private _value: number;
                constructor() {
                    this._value = 42;
                }
                get value(): number {
                    return this._value;
                }
            }
            let b: Box = new Box();
            console.log(b.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Setter_SetsValue(ExecutionMode mode)
    {
        var source = """
            class Box {
                private _value: number;
                constructor() {
                    this._value = 0;
                }
                get value(): number {
                    return this._value;
                }
                set value(v: number) {
                    this._value = v;
                }
            }
            let b: Box = new Box();
            console.log(b.value);
            b.value = 100;
            console.log(b.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n100\n", output);
    }

    #endregion

    #region Computed Properties

    [Theory, ModeData]
    public void Getter_ComputedProperty(ExecutionMode mode)
    {
        var source = """
            class Rectangle {
                private _width: number;
                private _height: number;
                constructor(w: number, h: number) {
                    this._width = w;
                    this._height = h;
                }
                get area(): number {
                    return this._width * this._height;
                }
            }
            let r: Rectangle = new Rectangle(10, 5);
            console.log(r.area);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("50\n", output);
    }

    [Theory, ModeData]
    public void GetterOnly_ReadOnlyProperty(ExecutionMode mode)
    {
        var source = """
            class Circle {
                private _radius: number;
                constructor(r: number) {
                    this._radius = r;
                }
                get radius(): number {
                    return this._radius;
                }
                get diameter(): number {
                    return this._radius * 2;
                }
            }
            let c: Circle = new Circle(5);
            console.log(c.radius);
            console.log(c.diameter);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n10\n", output);
    }

    #endregion

    #region Multiple Properties

    [Theory, ModeData]
    public void GetterSetter_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            class Point {
                private _x: number;
                private _y: number;
                constructor() {
                    this._x = 0;
                    this._y = 0;
                }
                get x(): number {
                    return this._x;
                }
                set x(v: number) {
                    this._x = v;
                }
                get y(): number {
                    return this._y;
                }
                set y(v: number) {
                    this._y = v;
                }
            }
            let p: Point = new Point();
            p.x = 3;
            p.y = 4;
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n", output);
    }

    #endregion

    #region Temperature Conversion

    [Theory, ModeData]
    public void Getter_TemperatureConversion(ExecutionMode mode)
    {
        var source = """
            class Temperature {
                private _celsius: number;
                constructor() {
                    this._celsius = 0;
                }
                get celsius(): number {
                    return this._celsius;
                }
                set celsius(v: number) {
                    this._celsius = v;
                }
                get fahrenheit(): number {
                    return this._celsius * 9 / 5 + 32;
                }
            }
            let t: Temperature = new Temperature();
            t.celsius = 100;
            console.log(t.fahrenheit);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("212\n", output);
    }

    #endregion

    #region Chained Access

    [Theory, ModeData]
    public void GetterSetter_ChainedAccess(ExecutionMode mode)
    {
        var source = """
            class Counter {
                private _count: number;
                constructor() {
                    this._count = 0;
                }
                get count(): number {
                    return this._count;
                }
                set count(v: number) {
                    this._count = v;
                }
            }
            let c: Counter = new Counter();
            c.count = 5;
            c.count = c.count + 1;
            console.log(c.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    #endregion

    #region Constructor Initialization

    [Theory, ModeData]
    public void Getter_InitializedFromConstructor(ExecutionMode mode)
    {
        var source = """
            class Person {
                private _name: string;
                constructor(name: string) {
                    this._name = name;
                }
                get name(): string {
                    return this._name;
                }
            }
            let p: Person = new Person("Alice");
            console.log(p.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n", output);
    }

    [Theory, ModeData]
    public void Setter_InvokedViaBracketAssignment(ExecutionMode mode)
    {
        // Regression for #290: bracket assignment `obj["n"] = v` must invoke the declared setter,
        // matching dot assignment and JS [[Set]] semantics. The interpreter previously stored
        // straight into the field dictionary, desynchronizing reads (getter) from writes.
        var source = """
            class Counter {
                private _n: number = 0;
                get n(): number { return this._n; }
                set n(v: number) { this._n = v * 2; }
            }
            const c = new Counter();
            (c as any)["n"] = 5;
            console.log((c as any)["n"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void AccessorBody_ResolvesCapturedTopLevelVariable(ExecutionMode mode)
    {
        // Regression for #300: a getter/setter body referencing a top-level
        // binding (here a captured `let`) threw "ReferenceError: Undefined
        // variable" in compiled mode — EmitAccessorBody was the lone body-
        // emission context that omitted the top-level-variable-access wiring
        // every other context (methods, ctors, functions, …) sets. A normal
        // method body resolving the same identifier always worked.
        var source = """
            let counter = 5;
            class Box {
                get tag(): string { return "v" + counter; }
                set tag(v: string) { counter = counter + v.length; }
            }
            const b = new Box();
            console.log(b.tag);
            b.tag = "abc";
            console.log(b.tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("v5\nv8\n", output);
    }

    [Theory, ModeData]
    public void GetterOnly_BracketWrite_IsNoOp(ExecutionMode mode)
    {
        // Regression for #293: in compiled mode, a bracket write to a getter-only
        // property created a shadowing own data field in _fields, so subsequent
        // reads returned that field instead of invoking the getter. The interpreter
        // correctly no-ops the write (sloppy-mode JS semantics). Covers both class
        // declarations and class expressions.
        var source = """
            class RO { get x(): number { return 99; } }
            const r = new RO();
            (r as any)["x"] = 1;
            console.log((r as any)["x"]);

            const ROExpr = class { get y(): number { return 7; } };
            const r2 = new ROExpr();
            (r2 as any)["y"] = 5;
            console.log((r2 as any)["y"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n7\n", output);
    }

    [Theory, ModeData]
    public void GetterWithSetter_BracketWrite_InvokesSetter(ExecutionMode mode)
    {
        // Companion to GetterOnly_BracketWrite_IsNoOp: a property that DOES have a
        // setter must still have the setter invoked on a bracket write (the no-op
        // path must not over-block accessors that are writable).
        var source = """
            class RW {
                private _v: number = 10;
                get x(): number { return this._v; }
                set x(val: number) { this._v = val; }
            }
            const o = new RW();
            (o as any)["x"] = 50;
            console.log(o.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("50\n", output);
    }

    #endregion

    #region Top-level variable capture (#300)

    [Theory, ModeData]
    public void Getter_ResolvesCapturedTopLevelVariable(ExecutionMode mode)
    {
        // #300: in compiled mode an accessor body that referenced a top-level
        // binding threw `ReferenceError: Undefined variable` because the accessor
        // emit context omitted the four top-level-variable-access properties every
        // other body-emission path sets. An ordinary method dodged it.
        var source = """
            let counter = 5;
            const label = "v";
            class D {
                get tag(): string { return label + counter; }
            }
            console.log(new D().tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("v5\n", output);
    }

    [Theory, ModeData]
    public void SetterAndStaticGetter_ResolveCapturedTopLevelVariable(ExecutionMode mode)
    {
        // Cover the setter and static-getter accessor shapes too — all share the
        // EmitAccessorBody context that was missing top-level-var access.
        var source = """
            let base = 10;
            const factor = 3;
            class C {
                _v: number = 0;
                get scaled(): number { return this._v * factor + base; }
                set scaled(n: number) { this._v = n - base; }
                static get answer(): number { return base + 32; }
            }
            const c = new C();
            c.scaled = 13;
            console.log(c._v);
            console.log(c.scaled);
            console.log(C.answer);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n19\n42\n", output);
    }

    #endregion
}
