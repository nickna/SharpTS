using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class StringMethodTests
{
    // String Properties
    [Fact]
    public void String_Length_ReturnsCorrectValue()
    {
        var source = """
            console.log("hello".length);
            console.log("".length);
            console.log("abc".length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n0\n3\n", output);
    }

    // String Methods
    [Fact]
    public void String_CharAt_ReturnsCharacter()
    {
        var source = """
            console.log("hello".charAt(0));
            console.log("hello".charAt(1));
            console.log("hello".charAt(4));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("h\ne\no\n", output);
    }

    [Fact]
    public void String_Substring_WithStartAndEnd_ReturnsSubstring()
    {
        var source = """
            console.log("hello".substring(1, 4));
            console.log("hello".substring(0, 2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ell\nhe\n", output);
    }

    [Fact]
    public void String_Substring_WithStartOnly_ReturnsToEnd()
    {
        var source = """
            console.log("hello".substring(2));
            console.log("hello".substring(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("llo\nhello\n", output);
    }

    [Fact]
    public void String_IndexOf_ReturnsIndex()
    {
        var source = """
            console.log("hello".indexOf("l"));
            console.log("hello".indexOf("o"));
            console.log("hello".indexOf("x"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n4\n-1\n", output);
    }

    [Fact]
    public void String_ToUpperCase_ReturnsUpperCase()
    {
        var source = """
            console.log("hello".toUpperCase());
            console.log("Hello World".toUpperCase());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\nHELLO WORLD\n", output);
    }

    [Fact]
    public void String_ToLowerCase_ReturnsLowerCase()
    {
        var source = """
            console.log("HELLO".toLowerCase());
            console.log("Hello World".toLowerCase());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nhello world\n", output);
    }

    [Fact]
    public void String_Trim_RemovesWhitespace()
    {
        var source = """
            console.log("  hello  ".trim());
            console.log("no space".trim());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nno space\n", output);
    }

    [Fact]
    public void String_Replace_ReplacesFirstOccurrence()
    {
        var source = """
            console.log("hello".replace("l", "x"));
            console.log("hello world".replace("o", "0"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hexlo\nhell0 world\n", output);
    }

    [Fact]
    public void String_Split_ReturnsArray()
    {
        var source = """
            let parts: string[] = "a,b,c".split(",");
            console.log(parts.length);
            console.log(parts[0]);
            console.log(parts[1]);
            console.log(parts[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Fact]
    public void String_Split_WithEmptyDelimiter_SplitsChars()
    {
        var source = """
            let chars: string[] = "abc".split("");
            console.log(chars.length);
            console.log(chars[0]);
            console.log(chars[1]);
            console.log(chars[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Fact]
    public void String_Includes_ReturnsBoolean()
    {
        var source = """
            console.log("hello world".includes("world"));
            console.log("hello world".includes("foo"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void String_StartsWith_ReturnsBoolean()
    {
        var source = """
            console.log("hello world".startsWith("hello"));
            console.log("hello world".startsWith("world"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void String_EndsWith_ReturnsBoolean()
    {
        var source = """
            console.log("hello world".endsWith("world"));
            console.log("hello world".endsWith("hello"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\nfalse\n", output);
    }

    // String with Variables
    [Fact]
    public void String_MethodsOnVariable_Work()
    {
        var source = """
            let s: string = "Hello World";
            console.log(s.length);
            console.log(s.toUpperCase());
            console.log(s.indexOf("o"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("11\nHELLO WORLD\n4\n", output);
    }

    [Fact]
    public void String_ChainedMethods_Work()
    {
        var source = """
            console.log("  Hello  ".trim().toUpperCase());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n", output);
    }

    // New String Methods

    [Fact]
    public void String_Slice_BasicUsage()
    {
        var source = """
            console.log("hello".slice(1, 4));
            console.log("hello".slice(2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ell\nllo\n", output);
    }

    [Fact]
    public void String_Slice_NegativeIndices()
    {
        var source = """
            console.log("hello".slice(-3));
            console.log("hello".slice(-4, -1));
            console.log("hello".slice(1, -1));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("llo\nell\nell\n", output);
    }

    [Fact]
    public void String_Slice_EdgeCases()
    {
        var source = """
            console.log("hello".slice(10));
            console.log("hello".slice(3, 1));
            console.log("".slice(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("\n\n\n", output);
    }

    [Fact]
    public void String_Repeat_BasicUsage()
    {
        var source = """
            console.log("ab".repeat(3));
            console.log("x".repeat(5));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ababab\nxxxxx\n", output);
    }

    [Fact]
    public void String_Repeat_EdgeCases()
    {
        var source = """
            console.log("hello".repeat(0));
            console.log("".repeat(5));
            console.log("a".repeat(1));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("\n\na\n", output);
    }

    [Fact]
    public void String_PadStart_BasicUsage()
    {
        var source = """
            console.log("5".padStart(3, "0"));
            console.log("abc".padStart(6, "123"));
            console.log("hello".padStart(10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("005\n123abc\n     hello\n", output);
    }

    [Fact]
    public void String_PadStart_EdgeCases()
    {
        var source = """
            console.log("hello".padStart(3));
            console.log("hi".padStart(5, ""));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nhi\n", output);
    }

    [Fact]
    public void String_PadEnd_BasicUsage()
    {
        var source = """
            console.log("5".padEnd(3, "0"));
            console.log("abc".padEnd(6, "123"));
            console.log("hello".padEnd(10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("500\nabc123\nhello     \n", output);
    }

    [Fact]
    public void String_PadEnd_EdgeCases()
    {
        var source = """
            console.log("hello".padEnd(3));
            console.log("hi".padEnd(5, ""));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nhi\n", output);
    }

    [Fact]
    public void String_CharCodeAt_BasicUsage()
    {
        var source = """
            console.log("ABC".charCodeAt(0));
            console.log("ABC".charCodeAt(1));
            console.log("hello".charCodeAt(4));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("65\n66\n111\n", output);
    }

    [Fact]
    public void String_CharCodeAt_OutOfRange()
    {
        var source = """
            console.log("hello".charCodeAt(10));
            console.log("hello".charCodeAt(-1));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("NaN\nNaN\n", output);
    }

    [Fact]
    public void String_Concat_BasicUsage()
    {
        var source = """
            console.log("hello".concat(" ", "world"));
            console.log("a".concat("b", "c", "d"));
            console.log("test".concat());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello world\nabcd\ntest\n", output);
    }

    [Fact]
    public void String_LastIndexOf_BasicUsage()
    {
        var source = """
            console.log("hello hello".lastIndexOf("hello"));
            console.log("hello hello".lastIndexOf("l"));
            console.log("hello".lastIndexOf("x"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n9\n-1\n", output);
    }

    [Fact]
    public void String_TrimStart_BasicUsage()
    {
        var source = """
            console.log("  hello  ".trimStart());
            console.log("hello".trimStart());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello  \nhello\n", output);
    }

    [Fact]
    public void String_TrimEnd_BasicUsage()
    {
        var source = """
            console.log("  hello  ".trimEnd());
            console.log("hello".trimEnd());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("  hello\nhello\n", output);
    }

    [Fact]
    public void String_ReplaceAll_BasicUsage()
    {
        var source = """
            console.log("hello".replaceAll("l", "x"));
            console.log("aaa".replaceAll("a", "b"));
            console.log("hello world".replaceAll("o", "0"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hexxo\nbbb\nhell0 w0rld\n", output);
    }

    [Fact]
    public void String_ReplaceAll_EdgeCases()
    {
        var source = """
            console.log("hello".replaceAll("x", "y"));
            console.log("hello".replaceAll("", "x"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nhello\n", output);
    }

    [Fact]
    public void String_At_BasicUsage()
    {
        var source = """
            console.log("hello".at(0));
            console.log("hello".at(2));
            console.log("hello".at(-1));
            console.log("hello".at(-2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("h\nl\no\nl\n", output);
    }

    [Fact]
    public void String_At_OutOfRange()
    {
        var source = """
            console.log("hello".at(10));
            console.log("hello".at(-10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\nnull\n", output);
    }

    [Fact]
    public void String_NewMethods_OnVariable()
    {
        var source = """
            let s: string = "Hello World";
            console.log(s.slice(0, 5));
            console.log(s.repeat(2));
            console.log(s.lastIndexOf("o"));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello\nHello WorldHello World\n7\n", output);
    }

    [Fact]
    public void String_NewMethods_Chained()
    {
        var source = """
            console.log("  hello  ".trimStart().trimEnd().padStart(10, "-"));
            console.log("abc".repeat(2).slice(1, 5));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("-----hello\nbcab\n", output);
    }
}
