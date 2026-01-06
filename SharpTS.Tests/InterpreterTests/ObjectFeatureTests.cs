using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ObjectFeatureTests
{
    // Property Shorthand
    [Fact]
    public void Object_PropertyShorthand_Works()
    {
        var source = """
            let name: string = "Alice";
            let age: number = 30;
            let obj: { name: string, age: number } = { name, age };
            console.log(obj.name);
            console.log(obj.age);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void Object_MixedShorthandAndExplicit_Works()
    {
        var source = """
            let x: number = 10;
            let obj: { x: number, y: number } = { x, y: 20 };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n", output);
    }

    // Method Shorthand
    [Fact]
    public void Object_MethodShorthand_Works()
    {
        var source = """
            let obj: { add(a: number, b: number): number } = {
                add(a: number, b: number): number {
                    return a + b;
                }
            };
            console.log(obj.add(3, 4));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void Object_MethodWithDefaultParams_Works()
    {
        var source = """
            let obj: { greet(name: string): string } = {
                greet(name: string = "World"): string {
                    return "Hello, " + name;
                }
            };
            console.log(obj.greet("Alice"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\n", output);
    }

    // Object Rest Pattern
    [Fact]
    public void Object_RestPattern_Works()
    {
        var source = """
            let obj: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };
            let { x, ...rest }: { x: number, y: number, z: number } = obj;
            console.log(x);
            console.log(rest.y);
            console.log(rest.z);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void Object_RestPattern_MultipleExtracted_Works()
    {
        var source = """
            let data: { id: number, name: string, age: number, city: string } = { id: 1, name: "Alice", age: 30, city: "NYC" };
            let { id, name, ...others }: { id: number, name: string, age: number, city: string } = data;
            console.log(id);
            console.log(name);
            console.log(others.age);
            console.log(others.city);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\nAlice\n30\nNYC\n", output);
    }

    // Object.keys
    [Fact]
    public void Object_Keys_ReturnsPropertyNames()
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let keys: string[] = Object.keys(obj);
            console.log(keys.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    // Object.values
    [Fact]
    public void Object_Values_ReturnsPropertyValues()
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            console.log(values[0]);
            console.log(values[1]);
            console.log(values[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void Object_Values_WithMixedTypes()
    {
        var source = """
            let obj: { name: string, age: number, active: boolean } = { name: "Alice", age: 30, active: true };
            let values: any[] = Object.values(obj);
            console.log(values.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    // Object.entries
    [Fact]
    public void Object_Entries_ReturnsKeyValuePairs()
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let entries: any[] = Object.entries(obj);
            console.log(entries.length);
            console.log(entries[0][0]);
            console.log(entries[0][1]);
            console.log(entries[1][0]);
            console.log(entries[1][1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\na\n1\nb\n2\n", output);
    }

    // Object.keys on class instance
    [Fact]
    public void Object_Keys_OnClassInstance()
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let keys: string[] = Object.keys(p);
            console.log(keys.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    // Object.values on class instance
    [Fact]
    public void Object_Values_OnClassInstance()
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let values: any[] = Object.values(p);
            console.log(values.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    // Object.entries on class instance
    [Fact]
    public void Object_Entries_OnClassInstance()
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(n: string, a: number) {
                    this.name = n;
                    this.age = a;
                }
            }
            let p = new Person("Alice", 30);
            let entries: any[] = Object.entries(p);
            console.log(entries.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    // Empty Object
    [Fact]
    public void Object_Empty_Works()
    {
        var source = """
            let obj: {} = {};
            console.log(typeof obj);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\n", output);
    }

    // Nested Object Literals
    [Fact]
    public void Object_Nested_Works()
    {
        var source = """
            let obj: { outer: { inner: number } } = { outer: { inner: 42 } };
            console.log(obj.outer.inner);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    // Object Property Assignment
    [Fact]
    public void Object_PropertyAssignment_Works()
    {
        var source = """
            let obj: { x: number } = { x: 1 };
            obj.x = 10;
            console.log(obj.x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    // Object with Array Property
    [Fact]
    public void Object_WithArrayProperty_Works()
    {
        var source = """
            let obj: { items: number[] } = { items: [1, 2, 3] };
            console.log(obj.items.length);
            console.log(obj.items[1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n2\n", output);
    }
}
