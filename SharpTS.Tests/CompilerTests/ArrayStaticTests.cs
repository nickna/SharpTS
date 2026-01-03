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
}
