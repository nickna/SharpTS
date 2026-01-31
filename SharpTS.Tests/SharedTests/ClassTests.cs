using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for class declarations, inheritance, and methods. Runs against both interpreter and compiler.
/// </summary>
public class ClassTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassDeclaration_CreatesInstance(ExecutionMode mode)
    {
        var source = """
            class Person {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            let p: Person = new Person("Alice");
            console.log(p.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassMethod_CanBeInvoked(ExecutionMode mode)
    {
        var source = """
            class Greeter {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                greet(): string {
                    return "Hello, " + this.name;
                }
            }
            let g: Greeter = new Greeter("World");
            console.log(g.greet());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassInheritance_ExtendsParent(ExecutionMode mode)
    {
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                speak(): string {
                    return this.name + " makes a sound";
                }
            }
            class Dog extends Animal {
                constructor(name: string) {
                    super(name);
                }
                speak(): string {
                    return this.name + " barks";
                }
            }
            let d: Dog = new Dog("Rex");
            console.log(d.speak());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Rex barks\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SuperCall_InvokesParentMethod(ExecutionMode mode)
    {
        var source = """
            class Base {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
                getValue(): number {
                    return this.value;
                }
            }
            class Derived extends Base {
                constructor(v: number) {
                    super(v * 2);
                }
            }
            let d: Derived = new Derived(5);
            console.log(d.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MultipleFields_InitializedCorrectly(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            let p: Point = new Point(3, 4);
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MethodWithParameters_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                add(a: number, b: number): number {
                    return a + b;
                }
                multiply(a: number, b: number): number {
                    return a * b;
                }
            }
            let calc: Calculator = new Calculator();
            console.log(calc.add(3, 5));
            console.log(calc.multiply(4, 6));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n24\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MethodCallingOtherMethod_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                square(n: number): number {
                    return n * n;
                }
                sumOfSquares(a: number, b: number): number {
                    return this.square(a) + this.square(b);
                }
            }
            let m: Calculator = new Calculator();
            console.log(m.sumOfSquares(3, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FieldModification_PersistsChanges(ExecutionMode mode)
    {
        var source = """
            class Counter {
                count: number;
                constructor() {
                    this.count = 0;
                }
                increment(): void {
                    this.count = this.count + 1;
                }
            }
            let c: Counter = new Counter();
            c.increment();
            c.increment();
            c.increment();
            console.log(c.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedField_AccessibleFromChild(ExecutionMode mode)
    {
        var source = """
            class Parent {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            class Child extends Parent {
                constructor(v: number) {
                    super(v);
                }
                doubleValue(): number {
                    return this.value * 2;
                }
            }
            let c: Child = new Child(10);
            console.log(c.doubleValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MultipleInstances_IndependentState(ExecutionMode mode)
    {
        var source = """
            class Box {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            let a: Box = new Box(1);
            let b: Box = new Box(2);
            console.log(a.value);
            console.log(b.value);
            a.value = 10;
            console.log(a.value);
            console.log(b.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n10\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_Basic(ExecutionMode mode)
    {
        // Dog extends Animal with no explicit constructor - should inherit Animal's constructor
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                speak(): string {
                    return this.name + " makes a sound";
                }
            }
            class Dog extends Animal {
                bark(): string {
                    return this.name + " barks!";
                }
            }
            let d: Dog = new Dog("Rex");
            console.log(d.bark());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Rex barks!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_MultiLevel(ExecutionMode mode)
    {
        // C extends B extends A - C should inherit A's constructor through B
        var source = """
            class A {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            class B extends A { }
            class C extends B {
                triple(): number {
                    return this.value * 3;
                }
            }
            let c: C = new C(10);
            console.log(c.triple());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_MultipleParameters(ExecutionMode mode)
    {
        // Child inherits parent's constructor with multiple parameters
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            class ColorPoint extends Point {
                getCoords(): string {
                    return "(" + this.x + ", " + this.y + ")";
                }
            }
            let p: ColorPoint = new ColorPoint(3, 4);
            console.log(p.getCoords());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("(3, 4)\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_GenericParent(ExecutionMode mode)
    {
        // StringBox extends Box<string> - should inherit Box's constructor
        var source = """
            class Box<T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
            }
            class StringBox extends Box<string> {
                upper(): string {
                    return this.value.toUpperCase();
                }
            }
            let sb: StringBox = new StringBox("hello");
            console.log(sb.upper());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_GenericParent_MultipleTypeParams(ExecutionMode mode)
    {
        // StringNumberPair extends Pair<string, number>
        var source = """
            class Pair<K, V> {
                key: K;
                value: V;
                constructor(k: K, v: V) {
                    this.key = k;
                    this.value = v;
                }
            }
            class StringNumberPair extends Pair<string, number> {
                describe(): string {
                    return this.key + " = " + this.value;
                }
            }
            let p: StringNumberPair = new StringNumberPair("count", 42);
            console.log(p.describe());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("count = 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_GenericParent_TypeParamForwarding(ExecutionMode mode)
    {
        // Derived<T> extends Base<T> - forwards type parameter
        var source = """
            class Base<T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
            }
            class Derived<T> extends Base<T> {
                getValue(): T {
                    return this.value;
                }
            }
            let d: Derived<string> = new Derived<string>("test");
            console.log(d.getValue());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_GenericParent_MixedTypeArgs(ExecutionMode mode)
    {
        // Mixed<X> extends Triple<string, X, number> - some concrete, some forwarded
        var source = """
            class Triple<A, B, C> {
                a: A;
                b: B;
                c: C;
                constructor(a: A, b: B, c: C) {
                    this.a = a;
                    this.b = b;
                    this.c = c;
                }
            }
            class Mixed<X> extends Triple<string, X, number> {
                getB(): X {
                    return this.b;
                }
            }
            let m: Mixed<boolean> = new Mixed<boolean>("hello", true, 42);
            console.log(m.a);
            console.log(m.getB());
            console.log(m.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\ntrue\n42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_GenericParent_WithOwnMethod(ExecutionMode mode)
    {
        // NumberBox extends Box<number> with its own method
        var source = """
            class Box<T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
            }
            class NumberBox extends Box<number> {
                double(): number {
                    return this.value * 2;
                }
            }
            let nb: NumberBox = new NumberBox(21);
            console.log(nb.double());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_TypeError_WrongArgCount(ExecutionMode mode)
    {
        // Should get a type error when wrong number of arguments passed
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            class Dog extends Animal { }
            let d: Dog = new Dog();
            """;

        var ex = Assert.Throws<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.Run(source, mode));
        Assert.Contains("expected at least 1 argument", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InheritedConstructor_TypeError_WrongArgType(ExecutionMode mode)
    {
        // Should get a type error when wrong argument type passed
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            class Dog extends Animal { }
            let d: Dog = new Dog(42);
            """;

        var ex = Assert.Throws<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.Run(source, mode));
        Assert.Contains("expected type 'string'", ex.Message);
    }
}
