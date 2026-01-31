using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for getter and setter properties in classes. Runs against both interpreter and compiler.
/// </summary>
public class GettersSettersTests
{
    #region Basic Getter/Setter

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    #endregion
}
