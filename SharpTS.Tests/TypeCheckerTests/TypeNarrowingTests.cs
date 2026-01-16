using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for type narrowing in TypeChecker.
/// Note: Type narrowing (control flow analysis) is not yet implemented in SharpTS.
/// These tests document the expected behavior and are skipped where unimplemented.
/// </summary>
public class TypeNarrowingTests
{
    #region Typeof Narrowing - Basic Runtime Checks

    [Fact]
    public void TypeofString_RuntimeCheck()
    {
        // typeof check works at runtime with 'any' type
        var source = """
            function process(value: any): string {
                if (typeof value === "string") {
                    return value.toUpperCase();
                }
                return "" + value;
            }

            console.log(process("hello"));
            console.log(process(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n42\n", result);
    }

    [Fact]
    public void TypeofNumber_RuntimeCheck()
    {
        var source = """
            function double(value: any): number {
                if (typeof value === "number") {
                    return value * 2;
                }
                return 0;
            }

            console.log(double(21));
            console.log(double("21"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n0\n", result);
    }

    [Fact(Skip = "Type narrowing after typeof check not yet implemented - TypeChecker doesn't narrow union types")]
    public void TypeofString_NarrowsUnionType()
    {
        // This would be the ideal behavior where typeof narrows the type
        var source = """
            function process(value: string | number): string {
                if (typeof value === "string") {
                    return value.toUpperCase();
                }
                return "" + value;
            }

            console.log(process("hello"));
            console.log(process(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n42\n", result);
    }

    #endregion

    #region Instanceof Narrowing

    [Fact]
    public void Instanceof_RuntimeCheck()
    {
        var source = """
            class Animal {
                speak(): void {
                    console.log("...");
                }
            }
            class Dog extends Animal {
                bark(): void {
                    console.log("woof");
                }
            }

            function handle(animal: Animal): void {
                if (animal instanceof Dog) {
                    (animal as Dog).bark();
                } else {
                    animal.speak();
                }
            }

            handle(new Dog());
            handle(new Animal());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("woof\n...\n", result);
    }

    [Fact(Skip = "Type narrowing after instanceof check not yet implemented")]
    public void Instanceof_NarrowsToClass()
    {
        // Ideal behavior: instanceof should narrow the type so we don't need explicit cast
        var source = """
            class Animal {
                speak(): void {
                    console.log("...");
                }
            }
            class Dog extends Animal {
                bark(): void {
                    console.log("woof");
                }
            }

            function handle(animal: Animal): void {
                if (animal instanceof Dog) {
                    animal.bark();  // Should work without cast after instanceof
                } else {
                    animal.speak();
                }
            }

            handle(new Dog());
            handle(new Animal());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("woof\n...\n", result);
    }

    #endregion

    #region Truthiness Narrowing

    [Fact]
    public void TruthinessCheck_RuntimeBehavior()
    {
        // Use any type to avoid type narrowing requirements
        var source = """
            function greet(name: any): string {
                if (name) {
                    return "Hello, " + name;
                }
                return "Hello, stranger";
            }

            console.log(greet("Alice"));
            console.log(greet(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\nHello, stranger\n", result);
    }

    [Fact]
    public void NullishCoalescing_Works()
    {
        var source = """
            function greet(name: string | null): string {
                return "Hello, " + (name ?? "stranger");
            }

            console.log(greet("Alice"));
            console.log(greet(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\nHello, stranger\n", result);
    }

    #endregion

    #region In Operator

    [Fact]
    public void InOperator_RuntimeCheck()
    {
        var source = """
            interface Cat {
                meow(): void;
            }
            interface Dog {
                bark(): void;
            }

            let cat: Cat = {
                meow: () => console.log("meow")
            };

            let dog: Dog = {
                bark: () => console.log("woof")
            };

            if ("meow" in cat) {
                cat.meow();
            }
            if ("bark" in dog) {
                dog.bark();
            }
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("meow\nwoof\n", result);
    }

    [Fact(Skip = "Type narrowing with 'in' operator not yet implemented")]
    public void InOperator_NarrowsTypeWithProperty()
    {
        // Ideal behavior: 'in' operator should narrow union types
        var source = """
            interface Cat {
                meow(): void;
            }
            interface Dog {
                bark(): void;
            }

            function speak(pet: Cat | Dog): void {
                if ("meow" in pet) {
                    pet.meow();
                } else {
                    pet.bark();
                }
            }

            speak({ meow: () => console.log("meow") });
            speak({ bark: () => console.log("woof") });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("meow\nwoof\n", result);
    }

    #endregion

    #region Discriminated Unions

    [Fact(Skip = "Discriminated union narrowing not yet implemented")]
    public void DiscriminatedUnion_StringLiteralDiscriminant()
    {
        var source = """
            interface Circle {
                kind: "circle";
                radius: number;
            }
            interface Square {
                kind: "square";
                side: number;
            }

            type Shape = Circle | Square;

            function area(shape: Shape): number {
                if (shape.kind === "circle") {
                    return 3.14159 * shape.radius * shape.radius;
                } else {
                    return shape.side * shape.side;
                }
            }

            console.log(area({ kind: "circle", radius: 10 }));
            console.log(area({ kind: "square", side: 5 }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.StartsWith("314", result);
        Assert.Contains("25", result);
    }

    #endregion

    #region Equality Narrowing

    [Fact(Skip = "Type narrowing after null check not yet implemented")]
    public void NullCheck_RuntimeBehavior()
    {
        // Without type narrowing, returning `value` after null check still fails
        var source = """
            function process(value: string | null): string {
                if (value === null) {
                    return "was null";
                }
                return value;  // Would need type narrowing to know value is string here
            }

            console.log(process(null));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("was null\nhello\n", result);
    }

    [Fact]
    public void NullCheck_WithExplicitCast()
    {
        // Workaround: use explicit cast or any type
        var source = """
            function process(value: string | null): string {
                if (value === null) {
                    return "was null";
                }
                return value as string;
            }

            console.log(process(null));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("was null\nhello\n", result);
    }

    [Fact(Skip = "Type narrowing after undefined check not yet implemented")]
    public void UndefinedCheck_RuntimeBehavior()
    {
        var source = """
            function process(value: number | undefined): number {
                if (value === undefined) {
                    return 0;
                }
                return value;
            }

            console.log(process(undefined));
            console.log(process(21));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n21\n", result);
    }

    #endregion

    #region Control Flow

    [Fact(Skip = "Type narrowing after control flow not yet implemented")]
    public void ControlFlow_EarlyReturn()
    {
        var source = """
            function process(value: string | null): string {
                if (value === null) {
                    return "null";
                }
                return value;
            }

            console.log(process(null));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("null\nhello\n", result);
    }

    [Fact]
    public void ControlFlow_WithExplicitCast()
    {
        // Workaround with explicit cast
        var source = """
            function process(value: string | null): string {
                if (value === null) {
                    return "null";
                }
                return value as string;
            }

            console.log(process(null));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("null\nhello\n", result);
    }

    #endregion

    #region Optional Chaining

    [Fact]
    public void OptionalChaining_Works()
    {
        var source = """
            interface User {
                name: string;
                address?: {
                    city: string;
                };
            }

            function getCity(user: User): string {
                return user.address?.city ?? "unknown";
            }

            console.log(getCity({ name: "Alice", address: { city: "NYC" } }));
            console.log(getCity({ name: "Bob" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("NYC\nunknown\n", result);
    }

    #endregion

    #region Array Type Narrowing

    [Fact(Skip = "Array.isArray type narrowing not yet implemented")]
    public void ArrayIsArray_Narrows()
    {
        var source = """
            function process(value: number | number[]): number {
                if (Array.isArray(value)) {
                    return value.length;
                }
                return value;
            }

            console.log(process([1, 2, 3]));
            console.log(process(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n42\n", result);
    }

    #endregion
}
