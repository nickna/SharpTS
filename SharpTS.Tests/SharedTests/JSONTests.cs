using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for JSON.parse and JSON.stringify. Runs against both interpreter and compiler.
/// </summary>
public class JSONTests
{
    #region JSON.parse basic tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_Number(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("42");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_String(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('"hello"');
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_Boolean(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("true");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_Null(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("null");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_Object(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"name":"Alice","age":30}');
            console.log(result.name);
            console.log(result.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_Array(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse("[1, 2, 3]");
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_NestedObject(ExecutionMode mode)
    {
        var source = """
            let result: any = JSON.parse('{"outer":{"inner":42}}');
            console.log(result.outer.inner);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Parse_WithReviver(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n", output);
    }

    #endregion

    #region JSON.stringify basic tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_Number(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(42);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_String(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify("hello");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\"hello\"\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_Boolean(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(true);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_Null(ExecutionMode mode)
    {
        var source = """
            let result: string = JSON.stringify(null);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_Object(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number } = { a: 1, b: 2 };
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"b\":2}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_Array(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[1,2,3]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_WithIndent(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, 2);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n  \"a\": 1\n}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_WithReplacerArray(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };
            let result: string = JSON.stringify(obj, ["a", "c"]);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"a\":1,\"c\":3}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_EmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj: {} = {};
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            let result: string = JSON.stringify(arr);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Roundtrip(ExecutionMode mode)
    {
        var source = """
            let original: { name: string, age: number } = { name: "Alice", age: 30 };
            let json: string = JSON.stringify(original);
            let parsed: any = JSON.parse(json);
            console.log(parsed.name);
            console.log(parsed.age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    #endregion

    #region Enhanced JSON.stringify tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_ClassInstance(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"name\":\"Bob\",\"age\":25}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_ClassInstance_ToJSON(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"custom\":50}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_ObjectWithToJSON(ExecutionMode mode)
    {
        // Compiler doesn't support arrow function toJSON on object literals the same way
        var source = """
            let obj: any = {
                x: 10,
                toJSON: (): string => {
                    return "custom";
                }
            };
            let result: string = JSON.stringify(obj);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\"custom\"\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_BigInt_Throws(ExecutionMode mode)
    {
        var source = """
            try {
                let result: string = JSON.stringify(123n);
                console.log("should not reach here");
            } catch (e) {
                console.log("caught error");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_StringIndent_Tab(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, "\t");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n\t\"a\": 1\n}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_StringIndent_Custom(ExecutionMode mode)
    {
        var source = """
            let obj: { a: number } = { a: 1 };
            let result: string = JSON.stringify(obj, null, ">>>");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n>>>\"a\": 1\n}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_NestedClassInstance(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\"inner\":{\"value\":42}}\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void JSON_Stringify_ClassInstanceWithIndent(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("{\n  \"x\": 10,\n  \"y\": 20\n}\n", output);
    }

    #endregion
}
