using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for tagged template literal support (ES2018).
/// Runs against both interpreter and compiler.
/// </summary>
public class TaggedTemplateLiteralTests
{
    #region Basic Tagged Templates

    [Theory, ModeData]
    public void Basic_TaggedTemplate_CallsTagFunction(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("tagged\n1\nhello", output);
    }

    [Theory, ModeData]
    public void Basic_TaggedTemplate_WithInterpolation(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.join("_") + ":" + values.join(",");
            }
            const name = "world";
            const result = tag`hello ${name}!`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("hello _!:world", output);
    }

    [Theory, ModeData]
    public void Basic_TaggedTemplate_MultipleInterpolations(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("a=[1], b=[2], c=[3]", output);
    }

    #endregion

    #region Raw Strings

    [Theory, ModeData]
    public void Raw_Property_PreservesBackslashes(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.raw[0];
            }
            const result = tag`hello\nworld`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("hello\\nworld", output);
    }

    [Theory, ModeData]
    public void Cooked_Vs_Raw_Difference(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("different", output);
    }

    [Theory, ModeData]
    public void Raw_Property_WithMultipleParts(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.raw.join("|");
            }
            const x = 1;
            const result = tag`a\nb${x}c\td`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("a\\nb|c\\td", output);
    }

    #endregion

    #region String.raw

    [Theory, ModeData]
    public void StringRaw_PreservesRawStrings(ExecutionMode mode)
    {
        var code = """
            const result = String.raw`hello\nworld`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("hello\\nworld", output);
    }

    [Theory, ModeData]
    public void StringRaw_WithInterpolation(ExecutionMode mode)
    {
        var code = """
            const name = "test";
            const result = String.raw`C:\Users\${name}\path`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("C:\\Users\\test\\path", output);
    }

    [Theory, ModeData]
    public void StringRaw_MultipleInterpolations(ExecutionMode mode)
    {
        var code = """
            const a = "A";
            const b = "B";
            const result = String.raw`${a}\n${b}`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("A\\nB", output);
    }

    #endregion

    #region Tag Function Return Types

    [Theory, ModeData]
    public void Tag_ReturnsNumber(ExecutionMode mode)
    {
        var code = """
            function countParts(strings: any, ...values: any[]): number {
                return strings.length + values.length;
            }
            const a = 1;
            const result = countParts`hello ${a} world`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("3", output);
    }

    [Theory, ModeData]
    public void Tag_ReturnsArray(ExecutionMode mode)
    {
        var code = """
            function collectParts(strings: any, ...values: any[]): any[] {
                return [...strings, ...values];
            }
            const a = "X";
            const result = collectParts`hello ${a} world`;
            console.log(result.length);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("3", output);
    }

    [Theory, ModeData]
    public void Tag_ReturnsObject(ExecutionMode mode)
    {
        var code = """
            function createObject(strings: any, ...values: any[]): any {
                return { strings, values };
            }
            const result = createObject`hello ${"world"}!`;
            console.log(result.values[0]);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("world", output);
    }

    #endregion

    #region Arrow Functions as Tags

    [Theory, ModeData]
    public void ArrowFunction_AsTag(ExecutionMode mode)
    {
        var code = """
            const tag = (strings: any, ...values: any[]) => strings.join("-");
            const result = tag`a${1}b${2}c`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("a-b-c", output);
    }

    [Theory, ModeData]
    public void ArrowFunction_ReturnsString(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("hello WORLD!", output);
    }

    #endregion

    #region Method as Tag

    [Theory, ModeData]
    public void ObjectMethod_AsTag(ExecutionMode mode)
    {
        var code = """
            const obj = {
                prefix: ">>",
                tag(strings: string[], ...values: any[]): string {
                    let joined = strings.join("");
                    let p = this.prefix;
                    return p + joined;
                }
            };
            const result = obj.tag`hello world`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal(">>hello world", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void NoInterpolations_EmptyTag(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.length + ":" + values.length;
            }
            const result = tag`simple`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("1:0", output);
    }

    [Theory, ModeData]
    public void AllInterpolations_NoLiteralText(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return strings.join("|") + ":" + values.join(",");
            }
            const result = tag`${1}${2}${3}`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("|||:1,2,3", output);
    }

    [Theory, ModeData]
    public void NestedTemplates(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("outer:inner:hello", output);
    }

    [Theory, ModeData]
    public void ExpressionAsValue(ExecutionMode mode)
    {
        var code = """
            function tag(strings: any, ...values: any[]): string {
                return values.map((v: any) => typeof v).join(",");
            }
            const result = tag`${1 + 2}${true}${"str"}${[1,2]}`;
            console.log(result);
            """;
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("number,boolean,string,object", output);
    }

    [Theory, ModeData]
    public void ArraysFrozen(ExecutionMode mode)
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
        var output = TestHarness.Run(code, mode).Trim();
        Assert.Equal("hello", output);
    }

    #endregion
}
