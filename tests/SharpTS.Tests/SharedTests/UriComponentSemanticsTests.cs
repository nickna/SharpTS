using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UriComponentSemanticsTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "encode_punctuation", """
            console.log(encodeURIComponent("!'()*"));console.log(encodeURIComponent("-_.~"));
            """, "!'()*\n-_.~\n" };
        yield return new object[] { "decode_malformed", """
            try{console.log(decodeURIComponent("%"));}catch(e){console.log(e.name);}console.log(decodeURIComponent("%25"));
            """, "URIError\n%\n" };
        yield return new object[] { "decode_invalid_utf8", """
            try{console.log(decodeURIComponent("%E0%A4"));}catch(e){console.log(e.name);}console.log(decodeURIComponent("%41"));
            """, "URIError\nA\n" };
        yield return new object[] { "encode_lone_surrogate", """
            try{console.log(encodeURIComponent("\uD800"));}catch(e){console.log(e.name);}console.log(encodeURIComponent("A"));
            """, "URIError\nA\n" };
        yield return new object[] { "malformed-percent-variants", """
            let count=0;for(const s of ["%","%0","%GG","%A-","text%2","%1g","%g1"]){try{decodeURIComponent(s);}catch(e){if(e instanceof URIError&&e.name==="URIError")count++;}}console.log(count);console.log(decodeURIComponent("a%2520b+%2B"));
            """, "7\na%20b++\n" };
        yield return new object[] { "invalid-utf8-variants", """
            let count=0;for(const s of ["%80","%C0%AF","%E0%A4","%E0%80%80","%ED%A0%80","%F4%90%80%80","%F5%80%80%80","%F0%9F%98","%C2x%A2","%E2%28%A1"]){try{decodeURIComponent(s);}catch(e){if(e instanceof URIError)count++;}}console.log(count);
            """, "10\n" };
        yield return new object[] { "surrogate-variants", """
            let count=0;for(const s of ["\uD800","\uDC00","\uD800A","A\uDC00"]){try{encodeURIComponent(s);}catch(e){if(e instanceof URIError)count++;}}console.log(count);console.log(encodeURIComponent("\uD83D\uDE00"));
            """, "4\n%F0%9F%98%80\n" };
        yield return new object[] { "valid-unicode-boundaries", """
            for(const s of ["\u0000","\u007F","\u0080","\u07FF","\u0800","\uFFFF","\uD800\uDC00","\uDBFF\uDFFF"]){const encoded=encodeURIComponent(s);console.log(encoded,decodeURIComponent(encoded)===s);}
            """, "%00 true\n%7F true\n%C2%80 true\n%DF%BF true\n%E0%A0%80 true\n%EF%BF%BF true\n%F0%90%80%80 true\n%F4%8F%BF%BF true\n" };
        yield return new object[] { "literal-surrogates-and-hex-case", """
            const s=decodeURIComponent("\uD800%41\uDC00");console.log(s.length,s.charCodeAt(0),s.charCodeAt(1),s.charCodeAt(2));console.log(decodeURIComponent("%c3%a9")===decodeURIComponent("%C3%A9"));
            """, "3 55296 65 56320\ntrue\n" };
        yield return new object[] { "value-and-borrowed-calls", """
            const encode:any=encodeURIComponent;const decode:any=decodeURIComponent;console.log(encode.call(null,"!'()*"),decode.apply({},["%21%27%28%29%2A"]));for(const fn of [encode.bind(null,"\uD800"),decode.bind(null,"%C0%80")]){try{fn();}catch(e){console.log(e.name,e instanceof URIError);}}
            """, "!'()* !'()*\nURIError true\nURIError true\n" };
        yield return new object[] { "global-this-calls", """
            console.log(globalThis.encodeURIComponent("!'()*"));const root:any=globalThis;try{root.decodeURIComponent("%E0%A4");}catch(e){console.log(e.name,e instanceof URIError);}const decode:any=root["decodeURIComponent"];try{decode("%GG");}catch(e){console.log(e.name);}console.log(decode("%41"));
            """, "!'()*\nURIError true\nURIError\nA\n" };
        yield return new object[] { "coercion-before-validation", """
            const original=new Error("coerce");const value:any={toString(){throw original;}};for(const fn of [encodeURIComponent,decodeURIComponent]){try{fn(value);}catch(e){console.log(e===original,e.name);}}let calls=0;const s:any={toString(){calls++;return "%GG";}};try{decodeURIComponent(s);}catch(e){console.log(e.name,calls);}
            """, "true Error\ntrue Error\nURIError 1\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void UriComponentsPreservePunctuationAndRejectInvalidEncoding(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
