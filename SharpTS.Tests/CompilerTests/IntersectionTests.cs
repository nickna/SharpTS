using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Compiler tests for TypeScript Intersection Types (A &amp; B).
/// These tests verify that intersection types work correctly when compiled to IL.
/// </summary>
public class IntersectionTests
{
    // Basic Intersection of Interfaces
    [Fact]
    public void Intersection_ObjectTypes_MergesProperties()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: Named & Aged = { name: "Alice", age: 30 };
            console.log(person.name);
            console.log(person.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void Intersection_RequiresAllProperties()
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }

            let obj: A & B = { x: 1, y: "hello" };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\nhello\n", output);
    }

    // Multiple Intersections
    [Fact]
    public void Intersection_MultipleTypes_Works()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }

            let obj: A & B & C = { a: 1, b: "two", c: true };
            console.log(obj.a);
            console.log(obj.b);
            console.log(obj.c);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\ntwo\ntrue\n", output);
    }

    // Type Alias with Intersection
    [Fact]
    public void Intersection_TypeAlias_Works()
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }
            type AB = A & B;

            let obj: AB = { x: 10, y: "test" };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\ntest\n", output);
    }

    // Compatible Property Merge
    [Fact]
    public void Intersection_CompatibleProperties_Merges()
    {
        var source = """
            interface A { name: string; value: number; }
            interface B { name: string; extra: boolean; }

            let obj: A & B = { name: "test", value: 42, extra: true };
            console.log(obj.name);
            console.log(obj.value);
            console.log(obj.extra);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n42\ntrue\n", output);
    }

    // Precedence: & binds tighter than |
    [Fact]
    public void Intersection_BindsTighterThanUnion()
    {
        var source = """
            interface A { a: string; }
            interface B { b: number; }
            interface C { c: boolean; }

            let x: A | B & C = { a: "hello" };
            console.log(x.a);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    // Special Types
    [Fact]
    public void Intersection_WithUnknown_ProducesOtherType()
    {
        var source = """
            type T = string & unknown;
            let x: T = "hello";
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    // Function Parameter
    [Fact]
    public void Intersection_AsFunctionParameter()
    {
        var source = """
            interface Printable { text: string; }
            interface Identifiable { id: number; }

            function process(item: Printable & Identifiable): void {
                console.log(item.text);
                console.log(item.id);
            }

            process({ text: "Hello", id: 42 });
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\n42\n", output);
    }

    // Nested Intersection in Union
    [Fact]
    public void Intersection_NestedInUnion_Works()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }

            type Complex = (A & B) | C;

            let x: Complex = { a: 1, b: "test" };
            console.log(x.a);
            console.log(x.b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\ntest\n", output);
    }

    // ========================================
    // EDGE CASE TESTS (Compiler)
    // ========================================

    // Edge Case: Array of Intersection Types
    [Fact]
    public void Intersection_ArrayOfIntersection_Works()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let people: (Named & Aged)[] = [
                { name: "Alice", age: 30 },
                { name: "Bob", age: 25 }
            ];

            console.log(people[0].name);
            console.log(people[0].age);
            console.log(people[1].name);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\nBob\n", output);
    }

    // Edge Case: Generic Constraint with Intersection
    [Fact]
    public void Intersection_GenericConstraint_Works()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            function process<T extends Named & Aged>(item: T): void {
                console.log(item.name);
                console.log(item.age);
            }

            process({ name: "Alice", age: 30 });
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    // Edge Case: Intersection as Return Type
    [Fact]
    public void Intersection_AsReturnType_Works()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            function createPerson(): Named & Aged {
                return { name: "Alice", age: 30 };
            }

            let person = createPerson();
            console.log(person.name);
            console.log(person.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    // Edge Case: Intersection with Optional Properties
    [Fact]
    public void Intersection_WithOptionalProperties_Works()
    {
        var source = """
            interface A { x: number; y?: string; }
            interface B { z: boolean; }

            let obj: A & B = { x: 1, z: true };
            console.log(obj.x);
            console.log(obj.z);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\ntrue\n", output);
    }

    // Edge Case: Deeply Nested Intersections
    [Fact]
    public void Intersection_DeeplyNested_Works()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }
            interface D { d: number; }

            type Complex = (A & B) & (C & D);

            let obj: Complex = { a: 1, b: "two", c: true, d: 4 };
            console.log(obj.a);
            console.log(obj.b);
            console.log(obj.c);
            console.log(obj.d);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\ntwo\ntrue\n4\n", output);
    }

    // ========================================
    // TYPE ERROR TESTS (Compiler)
    // These fail at type-check phase, before compilation
    // ========================================

    [Fact]
    public void Intersection_MissingProperty_TypeError()
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }

            let obj: A & B = { x: 1 };
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Intersection_ConflictingPrimitives_ProducesNever()
    {
        var source = """
            type Impossible = string & number;
            let x: Impossible = "test";
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Intersection_ConflictingPropertyTypes_PropertyBecomesNever()
    {
        var source = """
            interface A { prop: string; }
            interface B { prop: number; }

            type AB = A & B;
            let x: AB = { prop: "test" };
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Intersection_WithNever_ProducesNever()
    {
        var source = """
            type T = string & never;
            let x: T = "test";
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Intersection_ArrayOfIntersection_MissingProperty_TypeError()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let people: (Named & Aged)[] = [
                { name: "Alice", age: 30 },
                { name: "Bob" }
            ];
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void Intersection_GenericConstraint_MissingProperty_TypeError()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            function process<T extends Named & Aged>(item: T): void {
                console.log(item.name);
            }

            process({ name: "Alice" });
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    // ========================================
    // ADDITIONAL FEATURE TESTS (Compiler)
    // ========================================

    [Fact]
    public void Intersection_ClassWithInterface_Works()
    {
        var source = """
            interface Printable { text: string; }

            class Document {
                title: string;
                constructor(title: string) {
                    this.title = title;
                }
            }

            let doc: Document & Printable = { title: "Report", text: "Content here" };
            console.log(doc.title);
            console.log(doc.text);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Report\nContent here\n", output);
    }

    [Fact]
    public void Intersection_TwoInterfaces_WithMethods()
    {
        var source = """
            interface Greeter {
                greet(): string;
            }

            interface Farewell {
                bye(): string;
            }

            let obj: Greeter & Farewell = {
                greet(): string { return "Hello"; },
                bye(): string { return "Goodbye"; }
            };

            console.log(obj.greet());
            console.log(obj.bye());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\nGoodbye\n", output);
    }

    [Fact]
    public void Intersection_WithOptionalProperties_AllProvided()
    {
        var source = """
            interface A { x: number; y?: string; }
            interface B { z: boolean; }

            let obj: A & B = { x: 1, y: "hello", z: true };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.z);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\nhello\ntrue\n", output);
    }

    [Fact]
    public void Intersection_WithAny_ProducesAny()
    {
        var source = """
            type T = string & any;
            let x: T = 42;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Intersection_BindsTighterThanUnion_MatchesSecond()
    {
        var source = """
            interface A { a: string; }
            interface B { b: number; }
            interface C { c: boolean; }

            let y: A | B & C = { b: 42, c: true };
            console.log(y.b);
            console.log(y.c);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\ntrue\n", output);
    }

    [Fact]
    public void Intersection_InUnionWithNull_Works()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: (Named & Aged) | null = { name: "Alice", age: 30 };
            console.log(person.name);
            console.log(person.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void Intersection_InUnionWithNull_NullValue()
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: (Named & Aged) | null = null;
            console.log(person);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void Intersection_SamePropertySameType_Merges()
    {
        var source = """
            interface A { shared: string; a: number; }
            interface B { shared: string; b: boolean; }

            let obj: A & B = { shared: "common", a: 1, b: true };
            console.log(obj.shared);
            console.log(obj.a);
            console.log(obj.b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("common\n1\ntrue\n", output);
    }

    [Fact]
    public void Intersection_WithLiteralType_Works()
    {
        var source = """
            interface Named { name: string; }
            type Status = { status: "active" };

            let obj: Named & Status = { name: "Alice", status: "active" };
            console.log(obj.name);
            console.log(obj.status);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\nactive\n", output);
    }

    [Fact]
    public void Intersection_NestedInUnion_MatchesSecondBranch()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }

            type Complex = (A & B) | C;

            let y: Complex = { c: true };
            console.log(y.c);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }
}
