using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for TypeScript Intersection Types (A &amp; B). Runs against both interpreter and compiler.
/// Intersection types combine multiple types into one, requiring values to satisfy ALL constituent types.
/// </summary>
public class IntersectionTests
{
    #region Basic Intersection of Interfaces

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ObjectTypes_MergesProperties(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: Named & Aged = { name: "Alice", age: 30 };
            console.log(person.name);
            console.log(person.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_RequiresAllProperties(ExecutionMode mode)
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }

            let obj: A & B = { x: 1, y: "hello" };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_MissingProperty_TypeError(ExecutionMode mode)
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }

            let obj: A & B = { x: 1 };
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Multiple Intersections

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_MultipleTypes_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntwo\ntrue\n", output);
    }

    #endregion

    #region Type Alias with Intersection

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_TypeAlias_Works(ExecutionMode mode)
    {
        var source = """
            interface A { x: number; }
            interface B { y: string; }
            type AB = A & B;

            let obj: AB = { x: 10, y: "test" };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\ntest\n", output);
    }

    #endregion

    #region Compatible Property Merge

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_CompatibleProperties_Merges(ExecutionMode mode)
    {
        var source = """
            interface A { name: string; value: number; }
            interface B { name: string; extra: boolean; }

            let obj: A & B = { name: "test", value: 42, extra: true };
            console.log(obj.name);
            console.log(obj.value);
            console.log(obj.extra);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n42\ntrue\n", output);
    }

    #endregion

    #region Precedence Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_BindsTighterThanUnion_MatchesFirst(ExecutionMode mode)
    {
        var source = """
            interface A { a: string; }
            interface B { b: number; }
            interface C { c: boolean; }

            // A | B & C should be A | (B & C)
            let x: A | B & C = { a: "hello" };
            console.log(x.a);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_BindsTighterThanUnion_MatchesSecond(ExecutionMode mode)
    {
        var source = """
            interface A { a: string; }
            interface B { b: number; }
            interface C { c: boolean; }

            // A | B & C should be A | (B & C)
            let y: A | B & C = { b: 42, c: true };
            console.log(y.b);
            console.log(y.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\ntrue\n", output);
    }

    #endregion

    #region Special Types

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithUnknown_ProducesOtherType(ExecutionMode mode)
    {
        var source = """
            type T = string & unknown;
            let x: T = "hello";
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithAny_ProducesAny(ExecutionMode mode)
    {
        var source = """
            type T = string & any;
            let x: T = 42;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithNever_ProducesNever(ExecutionMode mode)
    {
        var source = """
            type T = string & never;
            let x: T = "test";
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Primitive Intersections

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ConflictingPrimitives_ProducesNever(ExecutionMode mode)
    {
        var source = """
            type Impossible = string & number;
            let x: Impossible = "test";
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Property Conflicts

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ConflictingPropertyTypes_PropertyBecomesNever(ExecutionMode mode)
    {
        var source = """
            interface A { prop: string; }
            interface B { prop: number; }

            type AB = A & B;
            let x: AB = { prop: "test" };
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Function Parameter

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_AsFunctionParameter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\n42\n", output);
    }

    #endregion

    #region Nested Intersection in Union

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_NestedInUnion_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntest\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_NestedInUnion_MatchesSecondBranch(ExecutionMode mode)
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }

            type Complex = (A & B) | C;

            let y: Complex = { c: true };
            console.log(y.c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ClassWithInterface_Works(ExecutionMode mode)
    {
        var source = """
            interface Printable { text: string; }

            class Document {
                title: string;
                constructor(title: string) {
                    this.title = title;
                }
            }

            // Object literal satisfying both class shape and interface
            let doc: Document & Printable = { title: "Report", text: "Content here" };
            console.log(doc.title);
            console.log(doc.text);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Report\nContent here\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_TwoInterfaces_WithMethods(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nGoodbye\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ArrayOfIntersection_Works(ExecutionMode mode)
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
            console.log(people[1].age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\nBob\n25\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_ArrayOfIntersection_MissingProperty_TypeError(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let people: (Named & Aged)[] = [
                { name: "Alice", age: 30 },
                { name: "Bob" }
            ];
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_GenericConstraint_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_GenericConstraint_MissingProperty_TypeError(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            function process<T extends Named & Aged>(item: T): void {
                console.log(item.name);
            }

            process({ name: "Alice" });
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_AsReturnType_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithOptionalProperties_Works(ExecutionMode mode)
    {
        var source = """
            interface A { x: number; y?: string; }
            interface B { z: boolean; }

            let obj: A & B = { x: 1, z: true };
            console.log(obj.x);
            console.log(obj.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithOptionalProperties_AllProvided(ExecutionMode mode)
    {
        var source = """
            interface A { x: number; y?: string; }
            interface B { z: boolean; }

            let obj: A & B = { x: 1, y: "hello", z: true };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_DeeplyNested_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntwo\ntrue\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_InUnionWithNull_Works(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: (Named & Aged) | null = { name: "Alice", age: 30 };
            console.log(person.name);
            console.log(person.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_InUnionWithNull_NullValue(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            interface Aged { age: number; }

            let person: (Named & Aged) | null = null;
            console.log(person);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_SamePropertySameType_Merges(ExecutionMode mode)
    {
        var source = """
            interface A { shared: string; a: number; }
            interface B { shared: string; b: boolean; }

            let obj: A & B = { shared: "common", a: 1, b: true };
            console.log(obj.shared);
            console.log(obj.a);
            console.log(obj.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("common\n1\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Intersection_WithLiteralType_Works(ExecutionMode mode)
    {
        var source = """
            interface Named { name: string; }
            type Status = { status: "active" };

            let obj: Named & Status = { name: "Alice", status: "active" };
            console.log(obj.name);
            console.log(obj.status);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\nactive\n", output);
    }

    #endregion
}
