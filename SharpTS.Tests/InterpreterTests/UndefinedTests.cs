using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for undefined value support in the interpreter.
/// </summary>
public class UndefinedTests
{
    [Fact]
    public void Undefined_TypeOf_ReturnsUndefined()
    {
        var source = """
            let x: undefined = undefined;
            console.log(typeof x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void Undefined_GlobalVariable_Accessible()
    {
        var source = """
            console.log(undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void Undefined_LooseEquality_WithUndefined()
    {
        var source = """
            console.log(undefined == undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Undefined_LooseEquality_WithNull()
    {
        var source = """
            console.log(undefined == null);
            console.log(null == undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Undefined_StrictEquality_WithUndefined()
    {
        var source = """
            console.log(undefined === undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Undefined_StrictEquality_WithNull_ReturnsFalse()
    {
        var source = """
            console.log(undefined === null);
            console.log(null === undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\nfalse\n", output);
    }

    [Fact]
    public void Undefined_IsFalsy()
    {
        var source = """
            if (undefined) {
                console.log("truthy");
            } else {
                console.log("falsy");
            }
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("falsy\n", output);
    }

    [Fact]
    public void Undefined_Stringify()
    {
        var source = """
            console.log(undefined);
            console.log("value is: " + undefined);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\nvalue is: undefined\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithUndefined_ReturnsDefault()
    {
        var source = """
            let x: string | undefined = undefined;
            console.log(x ?? "default");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithNull_ReturnsDefault()
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithValue_ReturnsValue()
    {
        var source = """
            let x: string | undefined = "hello";
            console.log(x ?? "default");
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void OptionalChaining_WithUndefined_ReturnsUndefined()
    {
        var source = """
            let obj: { name: string } | undefined = undefined;
            console.log(obj?.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void OptionalChaining_WithValue_ReturnsProperty()
    {
        var source = """
            let obj: { name: string } | undefined = { name: "test" };
            console.log(obj?.name);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void Undefined_NotEqualToFalsyValues()
    {
        var source = """
            console.log(undefined == 0);
            console.log(undefined == "");
            console.log(undefined == false);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Undefined_TypeAnnotation_Works()
    {
        var source = """
            let x: string | undefined;
            x = undefined;
            console.log(typeof x);
            x = "hello";
            console.log(typeof x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\nstring\n", output);
    }
}
