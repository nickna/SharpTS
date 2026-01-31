using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for undefined value support. Runs against both interpreter and compiler.
/// </summary>
public class UndefinedTests
{
    #region Basic Undefined Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_TypeOf_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            let x: undefined = undefined;
            console.log(typeof x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_GlobalVariable_Accessible(ExecutionMode mode)
    {
        var source = """
            console.log(undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region Equality Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_LooseEquality_WithUndefined(ExecutionMode mode)
    {
        var source = """
            console.log(undefined == undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_LooseEquality_WithNull(ExecutionMode mode)
    {
        var source = """
            console.log(undefined == null);
            console.log(null == undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_StrictEquality_WithUndefined(ExecutionMode mode)
    {
        var source = """
            console.log(undefined === undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_StrictEquality_WithNull_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            console.log(undefined === null);
            console.log(null === undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_NotEqualToFalsyValues(ExecutionMode mode)
    {
        var source = """
            console.log(undefined == 0);
            console.log(undefined == "");
            console.log(undefined == false);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    #endregion

    #region Truthiness Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_IsFalsy(ExecutionMode mode)
    {
        var source = """
            if (undefined) {
                console.log("truthy");
            } else {
                console.log("falsy");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("falsy\n", output);
    }

    #endregion

    #region String Conversion Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_Stringify(ExecutionMode mode)
    {
        var source = """
            console.log(undefined);
            console.log("value is: " + undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nvalue is: undefined\n", output);
    }

    #endregion

    #region Nullish Coalescing Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithUndefined_ReturnsDefault(ExecutionMode mode)
    {
        var source = """
            let x: string | undefined = undefined;
            console.log(x ?? "default");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithNull_ReturnsDefault(ExecutionMode mode)
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithValue_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            let x: string | undefined = "hello";
            console.log(x ?? "default");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Optional Chaining Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_WithUndefined_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } | undefined = undefined;
            console.log(obj?.name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_WithValue_ReturnsProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } | undefined = { name: "test" };
            console.log(obj?.name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Type Annotation Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_TypeAnnotation_Works(ExecutionMode mode)
    {
        var source = """
            let x: string | undefined;
            x = undefined;
            console.log(typeof x);
            x = "hello";
            console.log(typeof x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nstring\n", output);
    }

    #endregion
}
