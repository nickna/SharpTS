using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ArrayStaticTests
{
    [Fact]
    public void Array_IsArray_ReturnsTrue_ForArray()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_IsArray_ReturnsTrue_ForEmptyArray()
    {
        var source = """
            let arr: number[] = [];
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_IsArray_ReturnsFalse_ForNumber()
    {
        var source = """
            let num: number = 42;
            console.log(Array.isArray(num));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Array_IsArray_ReturnsFalse_ForString()
    {
        var source = """
            let str: string = "hello";
            console.log(Array.isArray(str));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Array_IsArray_ReturnsFalse_ForObject()
    {
        var source = """
            let obj: { x: number } = { x: 1 };
            console.log(Array.isArray(obj));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Array_IsArray_ReturnsFalse_ForNull()
    {
        var source = """
            let val: any = null;
            console.log(Array.isArray(val));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Array_From_CreatesArrayFromArray()
    {
        var source = """
            let arr = Array.from([1, 2, 3]);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void Array_From_CreatesArrayFromString()
    {
        var source = """
            let arr = Array.from("abc");
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Fact]
    public void Array_From_WithMapFunction()
    {
        var source = """
            let arr = Array.from([1, 2, 3], (x) => x * 2);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n2\n4\n6\n", output);
    }

    [Fact]
    public void Array_From_MapFunctionWithIndex()
    {
        var source = """
            let arr = Array.from([10, 20, 30], (val, idx) => val + idx);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n21\n32\n", output);
    }

    [Fact]
    public void Array_From_EmptyArray()
    {
        var source = """
            let arr = Array.from([]);
            console.log(arr.length);
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\ntrue\n", output);
    }

    [Fact]
    public void Array_From_FromSet()
    {
        var source = """
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            s.add(3);
            let arr = Array.from(s);
            console.log(arr.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Array_From_WithGenerator()
    {
        var source = """
            function* gen(): Generator<number> {
                yield 1;
                yield 2;
                yield 3;
            }
            let arr = Array.from(gen());
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void Array_From_FromMap()
    {
        var source = """
            let m = new Map<string, number>();
            m.set("a", 1);
            m.set("b", 2);
            m.set("c", 3);
            let arr = Array.from(m);
            console.log(arr.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }
}
