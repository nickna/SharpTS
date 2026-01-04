using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class AbstractClassTests
{
    [Fact]
    public void AbstractClass_ConcreteChild_CanBeInstantiated()
    {
        var source = """
            abstract class Shape {
                abstract area(): number;
            }
            class Circle extends Shape {
                radius: number;
                constructor(r: number) {
                    super();
                    this.radius = r;
                }
                area(): number { return this.radius * this.radius * 3; }
            }
            let c = new Circle(10);
            console.log(c.area());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("300\n", output);
    }

    [Fact]
    public void AbstractClass_WithConcreteMethods_InheritsCorrectly()
    {
        var source = """
            abstract class Animal {
                abstract speak(): string;
                describe(): string { return "I am an animal"; }
            }
            class Dog extends Animal {
                speak(): string { return "Woof"; }
            }
            let d = new Dog();
            console.log(d.speak());
            console.log(d.describe());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Woof\nI am an animal\n", output);
    }

    [Fact]
    public void AbstractAccessor_ImplementedInChild_Works()
    {
        var source = """
            abstract class Named {
                abstract get name(): string;
            }
            class Person extends Named {
                _name: string;
                constructor() {
                    super();
                    this._name = "John";
                }
                get name(): string { return this._name; }
            }
            let p = new Person();
            console.log(p.name);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("John\n", output);
    }

    [Fact]
    public void MultipleAbstractMethods_AllImplemented_Works()
    {
        var source = """
            abstract class Serializable {
                abstract toJson(): string;
                abstract fromJson(json: string): void;
            }
            class MyData extends Serializable {
                value: number;
                constructor() {
                    super();
                    this.value = 42;
                }
                toJson(): string { return "json"; }
                fromJson(json: string): void { this.value = 100; }
            }
            let d = new MyData();
            console.log(d.toJson());
            console.log(d.value);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("json\n42\n", output);
    }

    [Fact]
    public void AbstractClass_ExtendsAbstract_DefersImplementation()
    {
        var source = """
            abstract class Animal {
                abstract speak(): string;
            }
            abstract class Mammal extends Animal {
                abstract walk(): string;
            }
            class Dog extends Mammal {
                speak(): string { return "Woof"; }
                walk(): string { return "Walking"; }
            }
            let d = new Dog();
            console.log(d.speak());
            console.log(d.walk());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Woof\nWalking\n", output);
    }

    [Fact]
    public void AbstractClass_ThreeLevelChain_Works()
    {
        var source = """
            abstract class Base {
                abstract getValue(): number;
            }
            abstract class Middle extends Base {
                getDoubleValue(): number { return this.getValue() * 2; }
            }
            class Concrete extends Middle {
                getValue(): number { return 21; }
            }
            let c = new Concrete();
            console.log(c.getValue());
            console.log(c.getDoubleValue());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("21\n42\n", output);
    }

    [Fact]
    public void AbstractClass_Constructor_CalledViaSuper()
    {
        var source = """
            abstract class Base {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                abstract greet(): string;
            }
            class Derived extends Base {
                constructor(name: string) {
                    super(name);
                }
                greet(): string { return "Hello, " + this.name; }
            }
            let d = new Derived("World");
            console.log(d.greet());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void AbstractClass_WithFields_Works()
    {
        var source = """
            abstract class Counter {
                count: number;
                constructor() {
                    this.count = 0;
                }
                abstract increment(): void;
                getCount(): number { return this.count; }
            }
            class DoubleCounter extends Counter {
                constructor() {
                    super();
                }
                increment(): void { this.count = this.count + 2; }
            }
            let c = new DoubleCounter();
            c.increment();
            c.increment();
            console.log(c.getCount());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void AbstractClass_PolymorphicBehavior()
    {
        var source = """
            abstract class Shape {
                abstract area(): number;
            }
            class Square extends Shape {
                side: number;
                constructor(s: number) {
                    super();
                    this.side = s;
                }
                area(): number { return this.side * this.side; }
            }
            class Circle extends Shape {
                radius: number;
                constructor(r: number) {
                    super();
                    this.radius = r;
                }
                area(): number { return this.radius * this.radius * 3; }
            }
            function printArea(shape: Shape): void {
                console.log(shape.area());
            }
            printArea(new Square(5));
            printArea(new Circle(10));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("25\n300\n", output);
    }

    [Fact]
    public void AbstractClass_AbstractSetter_Works()
    {
        var source = """
            abstract class Settable {
                abstract set value(v: number);
                abstract get value(): number;
            }
            class Impl extends Settable {
                _value: number;
                constructor() {
                    super();
                    this._value = 0;
                }
                set value(v: number) { this._value = v; }
                get value(): number { return this._value; }
            }
            let i = new Impl();
            i.value = 42;
            console.log(i.value);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }
}
