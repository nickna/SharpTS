using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Tests for tagged template literal support (ES2018).
/// </summary>
public class TaggedTemplateLiteralTests
{
    private static void AssertOutput(string source, params string[] expectedLines)
    {
        var expected = string.Join("\n", expectedLines);
        var actual = TestHarness.RunInterpreted(source).Trim();
        Assert.Equal(expected, actual);
    }

    private static void AssertCompiledOutput(string source, params string[] expectedLines)
    {
        var expected = string.Join("\n", expectedLines);
        var actual = TestHarness.RunCompiled(source).Trim();
        Assert.Equal(expected, actual);
    }

    #region Basic Tagged Templates

    [Fact]
    public void Basic_TaggedTemplate_CallsTagFunction()
    {
        var code = """
            let received: any[] = [];
            function tag(strings: any, ...values: any[]): string {
                received = [strings, values];
                return "tagged";
            }
            const result = tag`hello`;
            console.log(result);
            console.log(received[0].length);
            console.log(received[0][0]);
            """;
        AssertOutput(code, "tagged", "1", "hello");
    }

    [Fact]
    public void Basic_TaggedTemplate_WithInterpolation()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.join("_") + ":" + values.join(",");
            }
            const name = "world";
            const result = tag`hello ${name}!`;
            console.log(result);
            """;
        AssertOutput(code, "hello _!:world");
    }

    [Fact]
    public void Basic_TaggedTemplate_MultipleInterpolations()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                let result = "";
                for (let i = 0; i < strings.length; i++) {
                    result += strings[i];
                    if (i < values.length) {
                        result += "[" + values[i] + "]";
                    }
                }
                return result;
            }
            const a = 1;
            const b = 2;
            const c = 3;
            const result = tag`a=${a}, b=${b}, c=${c}`;
            console.log(result);
            """;
        AssertOutput(code, "a=[1], b=[2], c=[3]");
    }

    #endregion

    #region Raw Strings

    [Fact]
    public void Raw_Property_PreservesBackslashes()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.raw[0];
            }
            const result = tag`hello\nworld`;
            console.log(result);
            """;
        AssertOutput(code, "hello\\nworld");
    }

    [Fact]
    public void Cooked_Vs_Raw_Difference()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                const cooked = strings[0];
                const raw = strings.raw[0];
                return cooked === raw ? "same" : "different";
            }
            const result = tag`hello\nworld`;
            console.log(result);
            """;
        AssertOutput(code, "different");
    }

    [Fact]
    public void Raw_Property_WithMultipleParts()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.raw.join("|");
            }
            const x = 1;
            const result = tag`a\nb${x}c\td`;
            console.log(result);
            """;
        AssertOutput(code, "a\\nb|c\\td");
    }

    #endregion

    #region String.raw

    [Fact]
    public void StringRaw_PreservesRawStrings()
    {
        var code = """
            const result = String.raw`hello\nworld`;
            console.log(result);
            """;
        AssertOutput(code, "hello\\nworld");
    }

    [Fact(Skip = "String.raw interpolation needs further work")]
    public void StringRaw_WithInterpolation()
    {
        var code = """
            const name = "test";
            const result = String.raw`C:\Users\${name}\path`;
            console.log(result);
            """;
        AssertOutput(code, "C:\\Users\\test\\path");
    }

    [Fact]
    public void StringRaw_MultipleInterpolations()
    {
        var code = """
            const a = "A";
            const b = "B";
            const result = String.raw`${a}\n${b}`;
            console.log(result);
            """;
        AssertOutput(code, "A\\nB");
    }

    #endregion

    #region Tag Function Return Types

    [Fact]
    public void Tag_ReturnsNumber()
    {
        var code = """
            function countParts(strings: any, ...values: any[]): number {
                return strings.length + values.length;
            }
            const a = 1;
            const result = countParts`hello ${a} world`;
            console.log(result);
            """;
        AssertOutput(code, "3");
    }

    [Fact]
    public void Tag_ReturnsArray()
    {
        var code = """
            function collectParts(strings: any, ...values: any[]): any[] {
                return [...strings, ...values];
            }
            const a = "X";
            const result = collectParts`hello ${a} world`;
            console.log(result.length);
            """;
        AssertOutput(code, "3");
    }

    [Fact]
    public void Tag_ReturnsObject()
    {
        var code = """
            function createObject(strings: any, ...values: any[]): any {
                return { strings, values };
            }
            const result = createObject`hello ${"world"}!`;
            console.log(result.values[0]);
            """;
        AssertOutput(code, "world");
    }

    #endregion

    #region Arrow Functions as Tags

    [Fact]
    public void ArrowFunction_AsTag()
    {
        var code = """
            const tag = (strings: any, ...values: any[]) => strings.join("-");
            const result = tag`a${1}b${2}c`;
            console.log(result);
            """;
        AssertOutput(code, "a-b-c");
    }

    [Fact]
    public void ArrowFunction_ReturnsString()
    {
        var code = """
            const upper = (strings: any, ...values: any[]) => {
                let result = strings[0];
                for (let i = 0; i < values.length; i++) {
                    result += (values[i] + "").toUpperCase() + strings[i + 1];
                }
                return result;
            };
            const name = "world";
            const result = upper`hello ${name}!`;
            console.log(result);
            """;
        AssertOutput(code, "hello WORLD!");
    }

    #endregion

    #region Method as Tag

    [Fact(Skip = "Shorthand method syntax with type annotations in object literals not yet supported")]
    public void ObjectMethod_AsTag()
    {
        var code = """
            const obj = {
                prefix: ">>",
                tag(strings: any, ...values: any[]): string {
                    return this.prefix + strings.join("");
                }
            };
            const result = obj.tag`hello world`;
            console.log(result);
            """;
        AssertOutput(code, ">>hello world");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NoInterpolations_EmptyTag()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.length + ":" + values.length;
            }
            const result = tag`simple`;
            console.log(result);
            """;
        AssertOutput(code, "1:0");
    }

    [Fact]
    public void AllInterpolations_NoLiteralText()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.join("|") + ":" + values.join(",");
            }
            const result = tag`${1}${2}${3}`;
            console.log(result);
            """;
        AssertOutput(code, "|||:1,2,3");
    }

    [Fact]
    public void NestedTemplates()
    {
        var code = """
            function outer(strings: any, ...values: any[]): string {
                return "outer:" + values[0];
            }
            function inner(strings: any, ...values: any[]): string {
                return "inner:" + strings[0];
            }
            const result = outer`${inner`hello`}`;
            console.log(result);
            """;
        AssertOutput(code, "outer:inner:hello");
    }

    [Fact]
    public void ExpressionAsValue()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return values.map((v: any) => typeof v).join(",");
            }
            const result = tag`${1 + 2}${true}${"str"}${[1,2]}`;
            console.log(result);
            """;
        AssertOutput(code, "number,boolean,string,object");
    }

    [Fact]
    public void ArraysFrozen()
    {
        var code = """
            let captured: any;
            function tag(strings: any, ...values: any[]): string {
                captured = strings;
                return "done";
            }
            tag`hello`;
            // Try to modify - should have no effect
            try {
                captured[0] = "modified";
            } catch(e) {
                // Ignored
            }
            console.log(captured[0]);
            """;
        AssertOutput(code, "hello");
    }

    #endregion

    #region Compiled Mode Tests

    [Fact]
    public void Compiled_Basic_TaggedTemplate()
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.join("_") + ":" + values.join(",");
            }
            const x = 42;
            const result = tag`value is ${x}!`;
            console.log(result);
            """;
        AssertCompiledOutput(code, "value is _!:42");
    }

    [Fact(Skip = "String.raw as a compiled namespace method requires additional runtime support")]
    public void Compiled_StringRaw()
    {
        var code = """
            const path = String.raw`C:\Users\name\Documents`;
            console.log(path);
            """;
        AssertCompiledOutput(code, "C:\\Users\\name\\Documents");
    }

    [Fact]
    public void Compiled_RawProperty()
    {
        var code = """
            function showRaw(strings: any): string {
                return strings.raw[0];
            }
            const result = showRaw`line1\nline2`;
            console.log(result);
            """;
        AssertCompiledOutput(code, "line1\\nline2");
    }

    #endregion
}
