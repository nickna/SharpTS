using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for access modifiers (public, private, protected, readonly). Runs against both interpreter and compiler.
/// </summary>
public class AccessModifierTests
{
    #region Private Fields

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Private_Field_AccessibleWithinClass(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("password123\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Private_Method_AccessibleWithinClass(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Protected Fields

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Protected_Field_AccessibleInSubclass(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("secret-key\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Protected_Method_AccessibleInSubclass(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("noise!\n", output);
    }

    #endregion

    #region Public Fields

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Public_Field_AccessibleEverywhere(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Public_Method_AccessibleEverywhere(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\n", output);
    }

    #endregion

    #region Readonly Fields

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readonly_Field_CanBeReadAfterInit(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Combined Modifiers

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Private_Readonly_Field_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("abc123\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MixedModifiers_WorkTogether(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("alice\ntrue\n12345\n", output);
    }

    #endregion
}
