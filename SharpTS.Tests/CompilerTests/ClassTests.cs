using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ClassTests
{
    [Fact]
    public void ClassDeclaration_CreatesInstance()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void ClassMethod_CanBeInvoked()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void ClassInheritance_ExtendsParent()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Rex barks\n", output);
    }

    [Fact]
    public void SuperCall_InvokesParentMethod()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void MultipleFields_InitializedCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n4\n", output);
    }

    [Fact]
    public void MethodWithParameters_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n24\n", output);
    }

    [Fact]
    public void MethodCallingOtherMethod_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("25\n", output);
    }

    [Fact]
    public void FieldModification_PersistsChanges()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void InheritedField_AccessibleFromChild()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void MultipleInstances_IndependentState()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n10\n2\n", output);
    }
}
