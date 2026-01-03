using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void JSON_Parse_String()
    {
        var source = """
            let result: any = JSON.parse('"hello"');
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void JSON_Parse_Boolean()
    {
        var source = """
            let result: any = JSON.parse("true");
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void JSON_Parse_Null()
    {
        var source = """
            let result: any = JSON.parse("null");
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void JSON_Parse_NestedObject()
    {
        var source = """
            let result: any = JSON.parse('{"outer":{"inner":42}}');
            console.log(result.outer.inner);
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void JSON_Stringify_String()
    {
        var source = """
            let result: string = JSON.stringify("hello");
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("\"hello\"\n", output);
    }

    [Fact]
    public void JSON_Stringify_Boolean()
    {
        var source = """
            let result: string = JSON.stringify(true);
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void JSON_Stringify_Null()
    {
        var source = """
            let result: string = JSON.stringify(null);
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n30\n", output);
    }
}
