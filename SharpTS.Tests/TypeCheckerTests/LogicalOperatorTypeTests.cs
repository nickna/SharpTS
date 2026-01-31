using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for logical operator (|| and &&) type checking.
/// Verifies that || and && return the correct union/operand types, not boolean.
/// </summary>
public class LogicalOperatorTypeTests
{
    #region || Operator Returns Operand Types

    [Fact]
    public void OrOperator_StringOrString_ReturnsString()
    {
        var source = """
            function takesString(s: string): void {
                console.log(s);
            }
            const value: string | null = "hello";
            takesString(value || "default");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void OrOperator_NullableStringWithDefault_PassesToStringParam()
    {
        var source = """
            function takesString(s: string): void {
                console.log(s);
            }
            const value: string | null = null;
            takesString(value || "default");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void OrOperator_NumberOrNumber_ReturnsNumber()
    {
        var source = """
            function takesNumber(n: number): void {
                console.log(n);
            }
            const value: number | null = null;
            takesNumber(value || 42);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void OrOperator_ObjectPropertyWithDefault_PassesToStringParam()
    {
        // This was the original bug: url.parse().pathname || '/' was typed as boolean
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { parse } from 'url';

                function handlePath(path: string): void {
                    console.log("Path: " + path);
                }

                const parsedUrl = parse("/api/test");
                const pathname = parsedUrl.pathname || '/';
                handlePath(pathname);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Path: /api/test\n", output);
    }

    [Fact]
    public void OrOperator_ReturnsTruthyLeftValue()
    {
        var source = """
            const result = "hello" || "world";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void OrOperator_ReturnsFallbackWhenLeftFalsy()
    {
        var source = """
            const empty = "";
            const result = empty || "default";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    #endregion

    #region && Operator Returns Operand Types

    [Fact]
    public void AndOperator_BothTruthy_ReturnsRightValue()
    {
        var source = """
            const result = "hello" && "world";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("world\n", output);
    }

    [Fact]
    public void AndOperator_LeftFalsy_ReturnsLeftValue()
    {
        var source = """
            const empty = "";
            const result = empty && "world";
            console.log(result === "");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void AndOperator_NumberOrNumber_ReturnsNumber()
    {
        var source = """
            function takesNumber(n: number): void {
                console.log(n);
            }
            const value = 5;
            takesNumber(value && 10);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Chained Logical Operators

    [Fact]
    public void ChainedOr_ReturnsFirstTruthyValue()
    {
        var source = """
            const a = "";
            const b = null;
            const c = "found";
            const result = a || b || c || "default";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("found\n", output);
    }

    [Fact]
    public void ChainedAnd_ReturnsLastTruthyOrFirstFalsy()
    {
        var source = """
            const a = "first";
            const b = "second";
            const c = "third";
            const result = a && b && c;
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("third\n", output);
    }

    #endregion

    #region Type Compatibility With Functions

    [Fact]
    public void OrOperator_TypeCompatibleWithFunctionParam()
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name + "!";
            }

            const userName: string | null = null;
            const result = greet(userName || "Guest");
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Guest!\n", output);
    }

    [Fact]
    public void OrOperator_WorksWithArrayFind()
    {
        var source = """
            const items: string[] = ["apple", "banana"];
            const found = items.find((x: string) => x === "cherry");
            const result = found || "not found";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("not found\n", output);
    }

    [Fact]
    public void OrOperator_WorksWithObjectProperties()
    {
        var source = """
            interface Config {
                name?: string;
            }

            const config: Config = {};
            const name = config.name || "default";
            console.log(name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    #endregion

    #region Mixed Types

    [Fact]
    public void OrOperator_MixedTypes_CreatesUnion()
    {
        var source = """
            const flag = false;
            const result = flag || "fallback";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("fallback\n", output);
    }

    [Fact]
    public void OrOperator_NumberAndString_WorksAtRuntime()
    {
        var source = """
            const num: number | null = null;
            const result = num || "no number";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("no number\n", output);
    }

    #endregion

    #region Compiled Mode Tests

    [Fact]
    public void OrOperator_CompiledMode_StringWithDefault()
    {
        var source = """
            function takesString(s: string): void {
                console.log(s);
            }
            const value: string | null = null;
            takesString(value || "compiled default");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled default\n", output);
    }

    [Fact]
    public void AndOperator_CompiledMode_ReturnsCorrectValue()
    {
        var source = """
            const a = "first";
            const b = "second";
            const result = a && b;
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("second\n", output);
    }

    [Fact]
    public void OrOperator_CompiledMode_UrlParsing()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { parse } from 'url';

                function handlePath(path: string): void {
                    console.log("Compiled path: " + path);
                }

                const parsedUrl = parse("/compiled/test");
                const pathname = parsedUrl.pathname || '/';
                handlePath(pathname);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Compiled path: /compiled/test\n", output);
    }

    #endregion
}
