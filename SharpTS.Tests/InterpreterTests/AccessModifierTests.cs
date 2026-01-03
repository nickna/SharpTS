using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class AccessModifierTests
{
    // Private Fields
    [Fact]
    public void Private_Field_AccessibleWithinClass()
    {
        var source = """
            class SecureBox {
                private secret: string;
                constructor(secret: string) {
                    this.secret = secret;
                }
                getSecret(): string {
                    return this.secret;
                }
            }
            let box: SecureBox = new SecureBox("password123");
            console.log(box.getSecret());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("password123\n", output);
    }

    [Fact]
    public void Private_Method_AccessibleWithinClass()
    {
        var source = """
            class Calculator {
                private double(x: number): number {
                    return x * 2;
                }
                quadruple(x: number): number {
                    return this.double(this.double(x));
                }
            }
            let calc: Calculator = new Calculator();
            console.log(calc.quadruple(5));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    // Protected Fields
    [Fact]
    public void Protected_Field_AccessibleInSubclass()
    {
        var source = """
            class Parent {
                protected key: string;
                constructor() {
                    this.key = "secret-key";
                }
            }
            class Child extends Parent {
                getKey(): string {
                    return this.key;
                }
            }
            let c: Child = new Child();
            console.log(c.getKey());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("secret-key\n", output);
    }

    [Fact]
    public void Protected_Method_AccessibleInSubclass()
    {
        var source = """
            class Animal {
                protected makeNoise(): string {
                    return "noise";
                }
            }
            class Dog extends Animal {
                bark(): string {
                    return this.makeNoise() + "!";
                }
            }
            let d: Dog = new Dog();
            console.log(d.bark());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("noise!\n", output);
    }

    // Public Fields (default)
    [Fact]
    public void Public_Field_AccessibleEverywhere()
    {
        var source = """
            class Person {
                public name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            let p: Person = new Person("Alice");
            console.log(p.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Public_Method_AccessibleEverywhere()
    {
        var source = """
            class Greeter {
                public greet(name: string): string {
                    return "Hello, " + name;
                }
            }
            let g: Greeter = new Greeter();
            console.log(g.greet("World"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World\n", output);
    }

    // Readonly Fields
    [Fact]
    public void Readonly_Field_CanBeReadAfterInit()
    {
        var source = """
            class Entity {
                readonly id: number;
                constructor(id: number) {
                    this.id = id;
                }
            }
            let e: Entity = new Entity(42);
            console.log(e.id);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    // Combined Modifiers
    [Fact]
    public void Private_Readonly_Field_Works()
    {
        var source = """
            class Config {
                private readonly apiKey: string;
                constructor(key: string) {
                    this.apiKey = key;
                }
                getKey(): string {
                    return this.apiKey;
                }
            }
            let c: Config = new Config("abc123");
            console.log(c.getKey());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("abc123\n", output);
    }

    // Multiple Members with Different Modifiers
    [Fact]
    public void MixedModifiers_WorkTogether()
    {
        var source = """
            class User {
                public name: string;
                private password: string;
                protected role: string;
                readonly createdAt: number;

                constructor(name: string, password: string, role: string) {
                    this.name = name;
                    this.password = password;
                    this.role = role;
                    this.createdAt = 12345;
                }

                checkPassword(input: string): boolean {
                    return this.password == input;
                }
            }
            let u: User = new User("alice", "secret", "admin");
            console.log(u.name);
            console.log(u.checkPassword("secret"));
            console.log(u.createdAt);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("alice\ntrue\n12345\n", output);
    }
}
