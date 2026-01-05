using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class OverrideKeywordTests
{
    [Fact]
    public void Override_BasicMethod_Works()
    {
        var source = """
            class Animal {
                speak(): string { return "..."; }
            }
            class Dog extends Animal {
                override speak(): string { return "Woof"; }
            }
            let d = new Dog();
            console.log(d.speak());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Woof\n", output);
    }

    [Fact]
    public void Override_WithAccessModifier_Works()
    {
        var source = """
            class Animal {
                speak(): string { return "..."; }
            }
            class Dog extends Animal {
                public override speak(): string { return "Woof"; }
            }
            let d = new Dog();
            console.log(d.speak());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Woof\n", output);
    }

    [Fact]
    public void Override_ProtectedMethod_Works()
    {
        var source = """
            class Animal {
                protected makeSound(): string { return "..."; }
                speak(): string { return this.makeSound(); }
            }
            class Dog extends Animal {
                protected override makeSound(): string { return "Woof"; }
            }
            let d = new Dog();
            console.log(d.speak());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Woof\n", output);
    }

    [Fact]
    public void Override_AbstractMethod_Works()
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
                override area(): number { return this.radius * this.radius * 3; }
            }
            let c = new Circle(10);
            console.log(c.area());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("300\n", output);
    }

    [Fact]
    public void Override_MultiLevelInheritance_Works()
    {
        var source = """
            class A {
                getValue(): number { return 1; }
            }
            class B extends A {
                override getValue(): number { return 2; }
            }
            class C extends B {
                override getValue(): number { return 3; }
            }
            let c = new C();
            console.log(c.getValue());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Override_SkipLevel_Works()
    {
        // Override grandparent method, parent doesn't override
        var source = """
            class A {
                getValue(): number { return 1; }
            }
            class B extends A {
            }
            class C extends B {
                override getValue(): number { return 3; }
            }
            let c = new C();
            console.log(c.getValue());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Override_Getter_Works()
    {
        var source = """
            class Base {
                get value(): number { return 1; }
            }
            class Derived extends Base {
                override get value(): number { return 42; }
            }
            let d = new Derived();
            console.log(d.value);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Override_Setter_Works()
    {
        var source = """
            class Base {
                _val: number;
                constructor() { this._val = 0; }
                set value(v: number) { this._val = v; }
                get value(): number { return this._val; }
            }
            class Derived extends Base {
                constructor() { super(); }
                override set value(v: number) { this._val = v * 2; }
            }
            let d = new Derived();
            d.value = 10;
            console.log(d.value);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Override_WithoutKeyword_StillWorks()
    {
        // Implicit override still works (override keyword is optional)
        var source = """
            class Animal {
                speak(): string { return "..."; }
            }
            class Dog extends Animal {
                speak(): string { return "Woof"; }
            }
            let d = new Dog();
            console.log(d.speak());
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Woof\n", output);
    }

    [Fact]
    public void Override_WithOverloads_Works()
    {
        var source = """
            class Animal {
                speak(times: number): string;
                speak(): string;
                speak(times?: number): string {
                    if (times !== null) {
                        return "sound " + times;
                    }
                    return "sound";
                }
            }
            class Dog extends Animal {
                override speak(times: number): string;
                override speak(): string;
                override speak(times?: number): string {
                    if (times !== null) {
                        return "woof " + times;
                    }
                    return "woof";
                }
            }
            let d = new Dog();
            console.log(d.speak());
            console.log(d.speak(3));
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("woof\nwoof 3\n", output);
    }

    // Error cases

    [Fact]
    public void Override_NoParentMethod_Errors()
    {
        var source = """
            class Animal {
            }
            class Dog extends Animal {
                override speak(): string { return "Woof"; }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("override", ex.Message);
        Assert.Contains("speak", ex.Message);
    }

    [Fact]
    public void Override_NoSuperclass_Errors()
    {
        var source = """
            class Dog {
                override speak(): string { return "Woof"; }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Parse Error", ex.Message);
        Assert.Contains("override", ex.Message);
    }

    [Fact]
    public void Override_StaticMethod_Errors()
    {
        var source = """
            class Animal {
                static speak(): string { return "..."; }
            }
            class Dog extends Animal {
                static override speak(): string { return "Woof"; }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Parse Error", ex.Message);
        Assert.Contains("Static", ex.Message);
    }

    [Fact]
    public void Override_Constructor_Errors()
    {
        var source = """
            class Animal {
                constructor() { }
            }
            class Dog extends Animal {
                override constructor() { super(); }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Parse Error", ex.Message);
        Assert.Contains("constructor", ex.Message);
    }

    [Fact]
    public void Override_NoParentGetter_Errors()
    {
        var source = """
            class Base {
            }
            class Derived extends Base {
                override get value(): number { return 42; }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("Getter", ex.Message);
        Assert.Contains("value", ex.Message);
    }

    [Fact]
    public void Override_NoParentSetter_Errors()
    {
        var source = """
            class Base {
                _val: number;
                constructor() { this._val = 0; }
            }
            class Derived extends Base {
                constructor() { super(); }
                override set value(v: number) { this._val = v; }
            }
            """;
        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("Setter", ex.Message);
        Assert.Contains("value", ex.Message);
    }
}
