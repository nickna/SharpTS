using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class JSONTests
{
    // JSON.parse basic tests
    [Fact]
    public void JSON_Parse_Number()
    {
        var source = """
            let result: any = JSON.parse("42");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void JSON_Parse_String()
    {
        var source = """
            let result: any = JSON.parse('"hello"');
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void JSON_Parse_Boolean()
    {
        var source = """
            let result: any = JSON.parse("true");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void JSON_Parse_Null()
    {
        var source = """
            let result: any = JSON.parse("null");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void JSON_Parse_Object()
    {
        var source = """
            let result: any = JSON.parse('{"name":"Alice","age":30}');
            console.log(result.name);
            console.log(result.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void JSON_Parse_Array()
    {
        var source = """
            let result: any = JSON.parse("[1, 2, 3]");
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void JSON_Parse_NestedObject()
    {
        var source = """
            let result: any = JSON.parse('{"outer":{"inner":42}}');
            console.log(result.outer.inner);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void JSON_Parse_WithReviver()
    {
        var source = """
            let result: any = JSON.parse('{"a":1,"b":2}', (key: any, value: any): any => {
                if (typeof value === "number") {
                    return value * 2;
                }
                return value;
            });
            console.log(result.a);
            console.log(result.b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n4\n", output);
    }

    // JSON.stringify basic tests
    [Fact]
    public void JSON_Stringify_Number()
    {
        var source = """
            let result: string = JSON.stringify(42);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void JSON_Stringify_String()
    {
        var source = """
            let result: string = JSON.stringify("hello");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("\"hello\"\n", output);
    }

    [Fact]
    public void JSON_Stringify_Boolean()
    {
        var source = """
            let result: string = JSON.stringify(true);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void JSON_Stringify_Null()
    {
        var source = """
            let result: string = JSON.stringify(null);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void JSON_Stringify_Object()
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\"a\":1,\"b\":2}\n", output);
    }

    [Fact]
    public void JSON_Stringify_Array()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("[1,2,3]\n", output);
    }

    [Fact]
    public void JSON_Stringify_WithIndent()
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, 2);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\n  \"a\": 1\n}\n", output);
    }

    [Fact]
    public void JSON_Stringify_WithReplacerArray()
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let result: string = JSON.stringify(obj, ["a", "c"]);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\"a\":1,\"c\":3}\n", output);
    }

    [Fact]
    public void JSON_Stringify_EmptyObject()
    {
        var source = """
            let obj: {} = {};
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{}\n", output);
    }

    [Fact]
    public void JSON_Stringify_EmptyArray()
    {
        var source = """
            let arr: number[] = [];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("[]\n", output);
    }

    [Fact]
    public void JSON_Roundtrip()
    {
        var source = """
            let original: { name: string, age: number } = { name: "Alice", age: 30 };
            let json: string = JSON.stringify(original);
            let parsed: any = JSON.parse(json);
            console.log(parsed.name);
            console.log(parsed.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    // Enhanced JSON.stringify tests

    [Fact]
    public void JSON_Stringify_ClassInstance()
    {
        var source = """
            class Person {
                name: string;
                age: number;
                constructor(name: string, age: number) {
                    this.name = name;
                    this.age = age;
                }
            }
            let p: Person = new Person("Bob", 25);
            let result: string = JSON.stringify(p);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\"name\":\"Bob\",\"age\":25}\n", output);
    }

    [Fact]
    public void JSON_Stringify_ClassInstance_ToJSON()
    {
        var source = """
            class Data {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
                toJSON(): { custom: number } {
                    return { custom: this.value * 10 };
                }
            }
            let d: Data = new Data(5);
            let result: string = JSON.stringify(d);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\"custom\":50}\n", output);
    }

    [Fact]
    public void JSON_Stringify_BigInt_Throws()
    {
        var source = """
            try {
                let result: string = JSON.stringify(123n);
                console.log("should not reach here");
            } catch (e) {
                console.log("caught error");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("caught error\n", output);
    }

    [Fact]
    public void JSON_Stringify_StringIndent_Tab()
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, "\t");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\n\t\"a\": 1\n}\n", output);
    }

    [Fact]
    public void JSON_Stringify_StringIndent_Custom()
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, ">>>");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\n>>>\"a\": 1\n}\n", output);
    }

    [Fact]
    public void JSON_Stringify_NestedClassInstance()
    {
        var source = """
            class Inner {
                value: number;
                constructor(v: number) {
                    this.value = v;
                }
            }
            class Outer {
                inner: Inner;
                constructor(i: Inner) {
                    this.inner = i;
                }
            }
            let o: Outer = new Outer(new Inner(42));
            let result: string = JSON.stringify(o);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\"inner\":{\"value\":42}}\n", output);
    }

    [Fact]
    public void JSON_Stringify_ClassInstanceWithIndent()
    {
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            let p: Point = new Point(10, 20);
            let result: string = JSON.stringify(p, null, 2);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("{\n  \"x\": 10,\n  \"y\": 20\n}\n", output);
    }
}
