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
}
