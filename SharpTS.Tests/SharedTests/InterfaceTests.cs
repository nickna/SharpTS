using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for interface declarations, structural typing, and implements clause. Runs against both interpreter and compiler.
/// </summary>
public class InterfaceTests
{
    #region Structural Typing

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ObjectLiteralMatches_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ExcessProperties_FreshLiteral_Rejected(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            let obj: Named = { name: "test", extra: 42 };
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Excess property: 'extra'", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ExcessProperties_NonFreshLiteral_Allowed(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            let tmp = { name: "test", extra: 42 };
            let obj: Named = tmp;  // Structural typing - non-fresh
            console.log(obj.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_AsParameter_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\n", output);
    }

    #endregion

    #region Implements Clause

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Implements_SingleInterface_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Implements_MultipleInterfaces_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Bob\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Implements_WithExtends_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nA product\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Implements_GetterSatisfiesProperty_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_WithMethods_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_PolymorphicUse_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Woof\nMeow\n", output);
    }

    #endregion

    #region Optional Interface Members

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_OptionalProperty_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("prod\ndev\ntrue\n", output);
    }

    #endregion

    #region Interface-to-Interface Compatibility

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ToInterface_SameStructure_Compatible(ExecutionMode mode)
    {
        var source = """
            interface Point {
                x: number;
                y: number;
            }
            function makePoint(): Point {
                return { x: 1, y: 2 };
            }
            let points: Point[] = [];
            points.push(makePoint());
            console.log(points[0].x);
            console.log(points[0].y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ToInterface_ReturnedFromFunction_Compatible(ExecutionMode mode)
    {
        var source = """
            interface FileStats {
                name: string;
                size: number;
            }
            function getStats(): FileStats {
                return { name: "test.txt", size: 100 };
            }
            function process(stats: FileStats): void {
                console.log(stats.name);
            }
            process(getStats());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test.txt\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Interface_ToInterface_WithArrayPush_Compatible(ExecutionMode mode)
    {
        var source = """
            interface Item {
                id: number;
                value: string;
            }
            function createItem(id: number, value: string): Item {
                return { id: id, value: value };
            }
            let items: Item[] = [];
            items.push(createItem(1, "first"));
            items.push(createItem(2, "second"));
            console.log(items.length);
            console.log(items[0].value);
            console.log(items[1].value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\nfirst\nsecond\n", output);
    }

    #endregion
}
