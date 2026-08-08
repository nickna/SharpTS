using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for string methods. Runs against both interpreter and compiler.
/// </summary>
public class StringMethodTests
{
    [Fact]
    public void String_ToUpperCase_IsLocaleIndependent_InInterpreter()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("I\n", TestHarness.RunInterpreted("console.log('i'.toUpperCase());"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void String_ToLowerCase_IsLocaleIndependent_InInterpreter()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("i\n", TestHarness.RunInterpreted("console.log('I'.toLowerCase());"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void String_Split_ObservesRegExpPrototypeSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const original: any = RegExp.prototype[Symbol.split];
            RegExp.prototype[Symbol.split] = function(value: any, limit: any): any {
                console.log(this instanceof RegExp);
                console.log(value + ":" + limit);
                return "custom";
            };
            console.log("aba".split(/a/, 2));
            RegExp.prototype[Symbol.split] = original;
            """);

        Assert.Equal("true\naba:2\ncustom\n", output);
    }

    [Fact]
    public void String_ReplaceAll_ObservesRegExpPrototypeSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const original: any = RegExp.prototype[Symbol.replace];
            RegExp.prototype[Symbol.replace] = function(value: any, replacement: any): any {
                console.log(this instanceof RegExp);
                console.log(value + ":" + replacement);
                return "custom";
            };
            console.log("aba".replaceAll(/a/g, "x"));
            RegExp.prototype[Symbol.replace] = original;
            """);

        Assert.Equal("true\naba:x\ncustom\n", output);
    }

    [Fact]
    public void String_EndsWith_UsesSymbolMatchForRegExpDetection_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const disabled: any = /x/;
            disabled[Symbol.match] = false;
            console.log("value/x/tail".endsWith(disabled, 8));

            const enabled: any = { toString: function(): string { return "x"; } };
            enabled[Symbol.match] = true;
            try {
                "xyz".endsWith(enabled);
            } catch (error) {
                console.log("threw");
            }
            """);

        Assert.Equal("true\nthrew\n", output);
    }

    [Fact]
    public void String_StartsWith_UsesSymbolMatchForRegExpDetection_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const disabled: any = /x/;
            disabled[Symbol.match] = false;
            console.log("/x/value".startsWith(disabled));

            const enabled: any = { toString: function(): string { return "x"; } };
            enabled[Symbol.match] = true;
            try {
                "xyz".startsWith(enabled);
            } catch (error) {
                console.log("threw");
            }
            """);

        Assert.Equal("true\nthrew\n", output);
    }

    [Fact]
    public void String_Includes_UsesSymbolMatchForRegExpDetection_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const disabled: any = /x/;
            disabled[Symbol.match] = false;
            console.log("/x/".includes(disabled));

            const enabled: any = { toString: function(): string { return "x"; } };
            enabled[Symbol.match] = true;
            try {
                "x".includes(enabled);
            } catch (error) {
                console.log("threw");
            }
            """);

        Assert.Equal("true\nthrew\n", output);
    }

    [Fact]
    public void String_MatchAll_PreservesBorrowedReceiverForSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const receiver: any = { marker: "original" };
            receiver.toString = function(): string { throw new Error("unexpected"); };
            const pattern: any = {};
            pattern[Symbol.matchAll] = function(value: any): any {
                console.log(value === receiver);
                console.log(this === pattern);
                return "matches";
            };
            console.log(String.prototype.matchAll.call(receiver, pattern));
            """);

        Assert.Equal("true\ntrue\nmatches\n", output);
    }

    [Fact]
    public void String_Split_PreservesBorrowedReceiverAndLimitForSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const receiver: any = { marker: "original" };
            receiver.toString = function(): string { throw new Error("unexpected"); };
            const limit: any = { marker: "raw" };
            limit.valueOf = function(): number { throw new Error("unexpected"); };
            const separator: any = {};
            separator[Symbol.split] = function(value: any, receivedLimit: any): any {
                console.log(value === receiver);
                console.log(receivedLimit === limit);
                console.log(this === separator);
                return "split";
            };
            console.log(String.prototype.split.call(receiver, separator, limit));
            """);

        Assert.Equal("true\ntrue\ntrue\nsplit\n", output);
    }

    [Fact]
    public void String_Search_PreservesBorrowedReceiverForSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const receiver: any = { marker: "original" };
            receiver.toString = function(): string { throw new Error("unexpected"); };
            const pattern: any = {};
            pattern[Symbol.search] = function(value: any): any {
                console.log(value === receiver);
                console.log(this === pattern);
                return "found";
            };
            console.log(String.prototype.search.call(receiver, pattern));
            """);

        Assert.Equal("true\ntrue\nfound\n", output);
    }

    [Fact]
    public void String_Match_PreservesBorrowedReceiverForSymbolHook_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const receiver: any = { marker: "original" };
            receiver.toString = function(): string { throw new Error("unexpected"); };
            const pattern: any = {};
            pattern[Symbol.match] = function(value: any): any {
                console.log(value === receiver);
                console.log(this === pattern);
                return "matched";
            };
            console.log(String.prototype.match.call(receiver, pattern));
            """);

        Assert.Equal("true\ntrue\nmatched\n", output);
    }

    [Fact]
    public void String_ReplaceAll_InvokesCustomSymbolReplace_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const search: any = {};
            search[Symbol.replace] = function(value: any, replacement: any): any {
                return value + ":" + replacement;
            };
            console.log("abc".replaceAll(search, "x"));
            """);

        Assert.Equal("abc:x\n", output);
    }

    [Fact]
    public void String_ReplaceAll_CoercesSearchBeforeReplacement_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const search: any = { toString: function(): string {
                console.log("search"); return "a";
            }};
            const replacement: any = { toString: function(): string {
                console.log("replacement"); return "x";
            }};
            console.log("aba".replaceAll(search, replacement));
            """);

        Assert.Equal("search\nreplacement\nxbx\n", output);
    }

    [Fact]
    public void String_ReplaceAll_UsesObservableRegexFlags_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const search: any = /a/g;
            Object.defineProperty(search, "flags", { value: "" });
            try {
                "aba".replaceAll(search, "x");
            } catch (error) {
                console.log("threw");
            }
            """);

        Assert.Equal("threw\n", output);
    }



    #region String Properties

    [Theory, ModeData]
    public void String_Length_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log("hello".length);
            console.log("".length);
            console.log("abc".length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n0\n3\n", output);
    }

    #endregion

    #region Basic String Methods

    [Fact]
    public void String_Replace_ExpandsPlainSearchSubstitutions_InInterpreter()
    {
        var source = """
            console.log("abc".replace("b", "[$$][$&][$`][$']"));
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("a[$][b][a][c]c\n", output);
    }

    [Theory, ModeData]
    public void String_CharAt_ReturnsCharacter(ExecutionMode mode)
    {
        var source = """
            console.log("hello".charAt(0));
            console.log("hello".charAt(1));
            console.log("hello".charAt(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h\ne\no\n", output);
    }

    [Theory, ModeData]
    public void String_Substring_WithStartAndEnd_ReturnsSubstring(ExecutionMode mode)
    {
        var source = """
            console.log("hello".substring(1, 4));
            console.log("hello".substring(0, 2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ell\nhe\n", output);
    }

    [Theory, ModeData]
    public void String_Substring_WithStartOnly_ReturnsToEnd(ExecutionMode mode)
    {
        var source = """
            console.log("hello".substring(2));
            console.log("hello".substring(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("llo\nhello\n", output);
    }

    [Theory, ModeData]
    public void String_IndexOf_ReturnsIndex(ExecutionMode mode)
    {
        var source = """
            console.log("hello".indexOf("l"));
            console.log("hello".indexOf("o"));
            console.log("hello".indexOf("x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n-1\n", output);
    }

    [Theory, ModeData]
    public void String_ToUpperCase_ReturnsUpperCase(ExecutionMode mode)
    {
        var source = """
            console.log("hello".toUpperCase());
            console.log("Hello World".toUpperCase());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\nHELLO WORLD\n", output);
    }

    [Theory, ModeData]
    public void String_ToLowerCase_ReturnsLowerCase(ExecutionMode mode)
    {
        var source = """
            console.log("HELLO".toLowerCase());
            console.log("Hello World".toLowerCase());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nhello world\n", output);
    }

    [Theory, ModeData]
    public void String_Trim_RemovesWhitespace(ExecutionMode mode)
    {
        var source = """
            console.log("  hello  ".trim());
            console.log("no space".trim());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nno space\n", output);
    }

    [Theory, ModeData]
    public void String_Replace_ReplacesFirstOccurrence(ExecutionMode mode)
    {
        var source = """
            console.log("hello".replace("l", "x"));
            console.log("hello world".replace("o", "0"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hexlo\nhell0 world\n", output);
    }

    [Theory, ModeData]
    public void String_Split_ReturnsArray(ExecutionMode mode)
    {
        var source = """
            let parts: string[] = "a,b,c".split(",");
            console.log(parts.length);
            console.log(parts[0]);
            console.log(parts[1]);
            console.log(parts[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Theory, ModeData]
    public void String_Split_WithEmptyDelimiter_SplitsChars(ExecutionMode mode)
    {
        var source = """
            let chars: string[] = "abc".split("");
            console.log(chars.length);
            console.log(chars[0]);
            console.log(chars[1]);
            console.log(chars[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Theory, ModeData]
    public void String_Includes_ReturnsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log("hello world".includes("world"));
            console.log("hello world".includes("foo"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void String_StartsWith_ReturnsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log("hello world".startsWith("hello"));
            console.log("hello world".startsWith("world"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void String_EndsWith_ReturnsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log("hello world".endsWith("world"));
            console.log("hello world".endsWith("hello"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion

    #region String with Variables

    [Theory, ModeData]
    public void String_MethodsOnVariable_Work(ExecutionMode mode)
    {
        var source = """
            let s: string = "Hello World";
            console.log(s.length);
            console.log(s.toUpperCase());
            console.log(s.indexOf("o"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\nHELLO WORLD\n4\n", output);
    }

    [Theory, ModeData]
    public void String_ChainedMethods_Work(ExecutionMode mode)
    {
        var source = """
            console.log("  Hello  ".trim().toUpperCase());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\n", output);
    }

    #endregion

    #region Slice Method

    [Theory, ModeData]
    public void String_Slice_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("hello".slice(1, 4));
            console.log("hello".slice(2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ell\nllo\n", output);
    }

    [Theory, ModeData]
    public void String_Slice_NegativeIndices(ExecutionMode mode)
    {
        var source = """
            console.log("hello".slice(-3));
            console.log("hello".slice(-4, -1));
            console.log("hello".slice(1, -1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("llo\nell\nell\n", output);
    }

    [Theory, ModeData]
    public void String_Slice_EdgeCases(ExecutionMode mode)
    {
        var source = """
            console.log("hello".slice(10));
            console.log("hello".slice(3, 1));
            console.log("".slice(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\n\n\n", output);
    }

    #endregion

    #region Repeat Method

    [Theory, ModeData]
    public void String_Repeat_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("ab".repeat(3));
            console.log("x".repeat(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ababab\nxxxxx\n", output);
    }

    [Theory, ModeData]
    public void String_Repeat_EdgeCases(ExecutionMode mode)
    {
        var source = """
            console.log("hello".repeat(0));
            console.log("".repeat(5));
            console.log("a".repeat(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\n\na\n", output);
    }

    #endregion

    #region Pad Methods

    [Theory, ModeData]
    public void String_PadStart_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("5".padStart(3, "0"));
            console.log("abc".padStart(6, "123"));
            console.log("hello".padStart(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("005\n123abc\n     hello\n", output);
    }

    [Theory, ModeData]
    public void String_PadStart_EdgeCases(ExecutionMode mode)
    {
        var source = """
            console.log("hello".padStart(3));
            console.log("hi".padStart(5, ""));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nhi\n", output);
    }

    [Theory, ModeData]
    public void String_PadEnd_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("5".padEnd(3, "0"));
            console.log("abc".padEnd(6, "123"));
            console.log("hello".padEnd(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("500\nabc123\nhello     \n", output);
    }

    [Theory, ModeData]
    public void String_PadEnd_EdgeCases(ExecutionMode mode)
    {
        var source = """
            console.log("hello".padEnd(3));
            console.log("hi".padEnd(5, ""));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nhi\n", output);
    }

    #endregion

    #region CharCodeAt Method

    [Theory, ModeData]
    public void String_CharCodeAt_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("ABC".charCodeAt(0));
            console.log("ABC".charCodeAt(1));
            console.log("hello".charCodeAt(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("65\n66\n111\n", output);
    }

    [Theory, ModeData]
    public void String_CharCodeAt_OutOfRange(ExecutionMode mode)
    {
        var source = """
            console.log("hello".charCodeAt(10));
            console.log("hello".charCodeAt(-1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("NaN\nNaN\n", output);
    }

    #endregion

    #region Concat Method

    [Theory, ModeData]
    public void String_Concat_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("hello".concat(" ", "world"));
            console.log("a".concat("b", "c", "d"));
            console.log("test".concat());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello world\nabcd\ntest\n", output);
    }

    #endregion

    #region LastIndexOf Method

    [Theory, ModeData]
    public void String_LastIndexOf_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("hello hello".lastIndexOf("hello"));
            console.log("hello hello".lastIndexOf("l"));
            console.log("hello".lastIndexOf("x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n9\n-1\n", output);
    }

    #endregion

    #region Trim Methods

    [Theory, ModeData]
    public void String_TrimStart_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("  hello  ".trimStart());
            console.log("hello".trimStart());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello  \nhello\n", output);
    }

    [Theory, ModeData]
    public void String_TrimEnd_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("  hello  ".trimEnd());
            console.log("hello".trimEnd());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("  hello\nhello\n", output);
    }

    #endregion

    #region ReplaceAll Method

    [Theory, ModeData]
    public void String_ReplaceAll_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("hello".replaceAll("l", "x"));
            console.log("aaa".replaceAll("a", "b"));
            console.log("hello world".replaceAll("o", "0"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hexxo\nbbb\nhell0 w0rld\n", output);
    }

    [Theory, ModeData]
    public void String_ReplaceAll_EdgeCases(ExecutionMode mode)
    {
        // ECMA-262 22.1.3.20: empty search inserts replacement at every
        // position 0..length (one between each char + start + end), so
        // "hello".replaceAll("", "x") → "xhxexlxlxox", not "hello".
        var source = """
            console.log("hello".replaceAll("x", "y"));
            console.log("hello".replaceAll("", "x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nxhxexlxlxox\n", output);
    }

    #endregion

    #region At Method

    [Theory, ModeData]
    public void String_At_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log("hello".at(0));
            console.log("hello".at(2));
            console.log("hello".at(-1));
            console.log("hello".at(-2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h\nl\no\nl\n", output);
    }

    [Theory, ModeData]
    public void String_At_OutOfRange(ExecutionMode mode)
    {
        var source = """
            console.log("hello".at(10));
            console.log("hello".at(-10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\n", output);
    }

    #endregion

    #region String.fromCharCode Static Method

    [Theory, ModeData]
    public void String_FromCharCode_BasicUsage(ExecutionMode mode)
    {
        var source = """
            console.log(String.fromCharCode(72, 101, 108, 108, 111));
            console.log(String.fromCharCode(65));
            console.log(String.fromCharCode(87, 111, 114, 108, 100));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nA\nWorld\n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_NoArguments(ExecutionMode mode)
    {
        var source = """
            console.log(String.fromCharCode());
            console.log("empty:" + String.fromCharCode());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\nempty:\n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_SingleCharacter(ExecutionMode mode)
    {
        var source = """
            console.log(String.fromCharCode(65));
            console.log(String.fromCharCode(97));
            console.log(String.fromCharCode(48));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("A\na\n0\n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_SpecialCharacters(ExecutionMode mode)
    {
        var source = """
            console.log(String.fromCharCode(10));
            console.log(String.fromCharCode(9));
            console.log(String.fromCharCode(32));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\n\n\t\n \n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_WithVariables(ExecutionMode mode)
    {
        var source = """
            const h = 72;
            const e = 101;
            const l = 108;
            const o = 111;
            console.log(String.fromCharCode(h, e, l, l, o));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_RoundTripWithCharCodeAt(ExecutionMode mode)
    {
        var source = """
            const original = "Test";
            const c0 = original.charCodeAt(0);
            const c1 = original.charCodeAt(1);
            const c2 = original.charCodeAt(2);
            const c3 = original.charCodeAt(3);
            const reconstructed = String.fromCharCode(c0, c1, c2, c3);
            console.log(reconstructed);
            console.log(original === reconstructed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Test\ntrue\n", output);
    }

    [Theory, ModeData]
    public void String_FromCharCode_LargeCodePoints(ExecutionMode mode)
    {
        // Values > 65535 should be truncated to 16-bit (& 0xFFFF)
        var source = """
            console.log(String.fromCharCode(65536));
            console.log(String.fromCharCode(65537));
            console.log(String.fromCharCode(65601));
            """;

        var output = TestHarness.Run(source, mode);
        // 65536 & 0xFFFF = 0 (null char, prints as empty in console)
        // 65537 & 0xFFFF = 1 (control char)
        // 65601 & 0xFFFF = 65 = 'A'
        Assert.Contains("A", output);
    }

    #endregion

    #region String.fromCodePoint Static Method

    [Theory, ModeData]
    public void String_FromCodePoint_InvalidCodePoint_MessageIncludesValue(ExecutionMode mode)
    {
        // #731: the RangeError message must carry the offending code-point value
        // for parity with the interpreter and tsc/V8 (compiled previously dropped it).
        var source = """
            try { String.fromCodePoint(-1); }
            catch (e: any) { console.log(e.message); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Invalid code point -1\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_BasicBMP(ExecutionMode mode)
    {
        var source = """
            console.log(String.fromCodePoint(72, 101, 108, 108, 111));
            console.log(String.fromCodePoint(65));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nA\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_NoArguments(ExecutionMode mode)
    {
        var source = """
            console.log(">" + String.fromCodePoint() + "<");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("><\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_SupplementaryCharacters(ExecutionMode mode)
    {
        // U+1F600 = 128512 (Grinning Face emoji)
        // U+1D11E = 119070 (Musical Symbol G Clef)
        var source = """
            const emoji = String.fromCodePoint(128512);
            console.log(emoji.length);
            const clef = String.fromCodePoint(119070);
            console.log(clef.length);
            """;

        var output = TestHarness.Run(source, mode);
        // Supplementary characters require 2 UTF-16 code units (surrogate pair)
        Assert.Equal("2\n2\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_MixedBMPAndSupplementary(ExecutionMode mode)
    {
        var source = """
            const s = String.fromCodePoint(65, 128512, 66);
            console.log(s.length);
            console.log(s.charCodeAt(0));
            console.log(s.charCodeAt(3));
            """;

        var output = TestHarness.Run(source, mode);
        // 'A' (1) + emoji (2 surrogates) + 'B' (1) = 4
        Assert.Equal("4\n65\n66\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_WithVariables(ExecutionMode mode)
    {
        var source = """
            const cp = 9731;
            const result = String.fromCodePoint(cp);
            console.log(result.codePointAt(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9731\n", output);
    }

    [Theory, ModeData]
    public void String_FromCodePoint_LoneSurrogates(ExecutionMode mode)
    {
        // ECMA-262 §22.1.2.2 + §11.1.3 UTF16EncodeCodePoint: lone surrogates
        // (0xD800–0xDFFF) are valid code points that encode to a single UTF-16
        // code unit. .NET's char.ConvertFromUtf32 rejects them, so this guards
        // the surrogate-aware encoding path (regressed RegExp/CharacterClassEscapes).
        var source = """
            const lo = String.fromCodePoint(0xDC00);
            console.log(lo.length + " " + lo.charCodeAt(0));
            const hi = String.fromCodePoint(0xD800);
            console.log(hi.length + " " + hi.charCodeAt(0));
            console.log(String.fromCodePoint(0x10FFFF).length);
            """;

        var output = TestHarness.Run(source, mode);
        // Lone low/high surrogates → length-1 strings; max code point → 2 units.
        Assert.Equal("1 56320\n1 55296\n2\n", output);
    }

    #endregion

    #region String.prototype.codePointAt

    [Theory, ModeData]
    public void String_CodePointAt_BasicBMP(ExecutionMode mode)
    {
        var source = """
            console.log("ABC".codePointAt(0));
            console.log("ABC".codePointAt(1));
            console.log("ABC".codePointAt(2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("65\n66\n67\n", output);
    }

    [Theory, ModeData]
    public void String_CodePointAt_OutOfRange(ExecutionMode mode)
    {
        var source = """
            console.log("ABC".codePointAt(3));
            console.log("ABC".codePointAt(-1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\n", output);
    }

    [Theory, ModeData]
    public void String_CodePointAt_SurrogatePair(ExecutionMode mode)
    {
        // Create a string with a supplementary character and read it back
        var source = """
            const emoji = String.fromCodePoint(128512);
            console.log(emoji.codePointAt(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("128512\n", output);
    }

    [Theory, ModeData]
    public void String_CodePointAt_SecondSurrogate(ExecutionMode mode)
    {
        // Accessing index 1 of a surrogate pair should return the low surrogate's code unit value
        var source = """
            const emoji = String.fromCodePoint(128512);
            const lowSurrogate = emoji.codePointAt(1);
            console.log(lowSurrogate);
            console.log(emoji.charCodeAt(1));
            console.log(lowSurrogate === emoji.charCodeAt(1));
            """;

        var output = TestHarness.Run(source, mode);
        var lines = output.TrimEnd('\n').Split('\n');
        // The low surrogate is a regular BMP character, so codePointAt == charCodeAt
        Assert.Equal(lines[1], lines[0]);
        Assert.Equal("true", lines[2]);
    }

    [Theory, ModeData]
    public void String_CodePointAt_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const cp = 9731;
            const s = String.fromCodePoint(cp);
            console.log(s.codePointAt(0) === cp);
            const cp2 = 128512;
            const s2 = String.fromCodePoint(cp2);
            console.log(s2.codePointAt(0) === cp2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void String_CodePointAt_MatchesCharCodeAtForBMP(ExecutionMode mode)
    {
        var source = """
            const s = "Hello";
            let allMatch = true;
            for (let i = 0; i < s.length; i++) {
                if (s.codePointAt(i) !== s.charCodeAt(i)) {
                    allMatch = false;
                }
            }
            console.log(allMatch);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region New Methods on Variable and Chained

    [Theory, ModeData]
    public void String_NewMethods_OnVariable(ExecutionMode mode)
    {
        var source = """
            let s: string = "Hello World";
            console.log(s.slice(0, 5));
            console.log(s.repeat(2));
            console.log(s.lastIndexOf("o"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nHello WorldHello World\n7\n", output);
    }

    [Theory, ModeData]
    public void String_NewMethods_Chained(ExecutionMode mode)
    {
        var source = """
            console.log("  hello  ".trimStart().trimEnd().padStart(10, "-"));
            console.log("abc".repeat(2).slice(1, 5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-----hello\nbcab\n", output);
    }

    #endregion

    #region Normalize Method

    [Theory, ModeData]
    public void String_Normalize_DefaultNFC(ExecutionMode mode)
    {
        // \u00e9 is precomposed é (NFC), e\u0301 is decomposed e + combining accent (NFD)
        // NFC should compose e+combining accent into single precomposed char
        var source = """
            const composed = "\u00e9";
            const decomposed = "e\u0301";
            console.log(composed.length);
            console.log(decomposed.length);
            console.log(decomposed.normalize().length);
            console.log(decomposed.normalize() === composed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n1\ntrue\n", output);
    }

    [Theory, ModeData]
    public void String_Normalize_NFD(ExecutionMode mode)
    {
        var source = """
            const composed = "\u00e9";
            const nfd = composed.normalize("NFD");
            console.log(nfd.length);
            console.log(nfd === "e\u0301");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue\n", output);
    }

    [Theory, ModeData]
    public void String_Normalize_AllForms(ExecutionMode mode)
    {
        var source = """
            const s = "\u00e9";
            console.log(s.normalize("NFC").length);
            console.log(s.normalize("NFD").length);
            console.log(s.normalize("NFKC").length);
            console.log(s.normalize("NFKD").length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void String_Normalize_AlreadyNormalized(ExecutionMode mode)
    {
        var source = """
            const s = "hello";
            console.log(s.normalize() === s);
            console.log(s.normalize("NFC") === s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region LocaleCompare Method

    [Theory, ModeData]
    public void String_LocaleCompare_Equal(ExecutionMode mode)
    {
        var source = """
            console.log("abc".localeCompare("abc"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void String_LocaleCompare_LessThan(ExecutionMode mode)
    {
        var source = """
            console.log("abc".localeCompare("def"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory, ModeData]
    public void String_LocaleCompare_GreaterThan(ExecutionMode mode)
    {
        var source = """
            console.log("def".localeCompare("abc"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void String_LocaleCompare_WithVariables(ExecutionMode mode)
    {
        var source = """
            const a: string = "apple";
            const b: string = "banana";
            const result: number = a.localeCompare(b);
            console.log(result < 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion
}
