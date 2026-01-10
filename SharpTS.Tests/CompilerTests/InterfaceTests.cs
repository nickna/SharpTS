using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class InterfaceTests
{
    // Structural Typing
    [Fact]
    public void Interface_ObjectLiteralMatches_Works()
    {
        var source = """
            interface Point {
                x: number;
                y: number;
            }
            let p: Point = { x: 10, y: 20 };
            console.log(p.x);
            console.log(p.y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n20\n", output);
    }

    [Fact]
    public void Interface_ExcessProperties_FreshLiteral_Rejected()
    {
        var source = """
            interface Named { name: string; }
            let obj: Named = { name: "test", extra: 42 };
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Excess property: 'extra'", ex.Message);
    }

    [Fact]
    public void Interface_ExcessProperties_NonFreshLiteral_Allowed()
    {
        var source = """
            interface Named { name: string; }
            let tmp = { name: "test", extra: 42 };
            let obj: Named = tmp;  // Structural typing - non-fresh
            console.log(obj.name);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void Interface_AsParameter_Works()
    {
        var source = """
            interface Printable {
                text: string;
            }
            function print(p: Printable): void {
                console.log(p.text);
            }
            print({ text: "Hello" });
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\n", output);
    }

    // Implements Clause
    [Fact]
    public void Implements_SingleInterface_Works()
    {
        var source = """
            interface Named {
                name: string;
            }
            class User implements Named {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            let u: User = new User("Alice");
            console.log(u.name);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void Implements_MultipleInterfaces_Works()
    {
        var source = """
            interface Named {
                name: string;
            }
            interface Aged {
                age: number;
            }
            class Person implements Named, Aged {
                name: string;
                age: number;
                constructor(name: string, age: number) {
                    this.name = name;
                    this.age = age;
                }
            }
            let p: Person = new Person("Bob", 30);
            console.log(p.name);
            console.log(p.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Bob\n30\n", output);
    }

    [Fact]
    public void Implements_WithExtends_Works()
    {
        var source = """
            interface Describable {
                description: string;
            }
            class Entity {
                id: number;
                constructor(id: number) {
                    this.id = id;
                }
            }
            class Product extends Entity implements Describable {
                description: string;
                constructor(id: number, description: string) {
                    super(id);
                    this.description = description;
                }
            }
            let p: Product = new Product(1, "A product");
            console.log(p.id);
            console.log(p.description);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\nA product\n", output);
    }

    [Fact]
    public void Implements_GetterSatisfiesProperty_Works()
    {
        var source = """
            interface HasLength {
                length: number;
            }
            class MyCollection implements HasLength {
                items: number[];
                constructor() {
                    this.items = [1, 2, 3];
                }
                get length(): number {
                    return this.items.length;
                }
            }
            let c: MyCollection = new MyCollection();
            console.log(c.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Interface_WithMethods_Works()
    {
        var source = """
            interface Greeter {
                greet(): string;
            }
            class FriendlyGreeter implements Greeter {
                greet(): string {
                    return "Hello!";
                }
            }
            let g: Greeter = new FriendlyGreeter();
            console.log(g.greet());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello!\n", output);
    }

    [Fact]
    public void Interface_PolymorphicUse_Works()
    {
        var source = """
            interface Animal {
                speak(): string;
            }
            class Dog implements Animal {
                speak(): string {
                    return "Woof";
                }
            }
            class Cat implements Animal {
                speak(): string {
                    return "Meow";
                }
            }
            function makeSound(a: Animal): void {
                console.log(a.speak());
            }
            makeSound(new Dog());
            makeSound(new Cat());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Woof\nMeow\n", output);
    }

    // Optional Interface Members
    [Fact]
    public void Interface_OptionalProperty_Works()
    {
        var source = """
            interface Config {
                name: string;
                debug?: boolean;
            }
            let c1: Config = { name: "prod" };
            let c2: Config = { name: "dev", debug: true };
            console.log(c1.name);
            console.log(c2.name);
            console.log(c2.debug);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("prod\ndev\ntrue\n", output);
    }
}
