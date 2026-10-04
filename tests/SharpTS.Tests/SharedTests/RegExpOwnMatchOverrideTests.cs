using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RegExpOwnMatchOverrideTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "native_own_null", """
            const value:any=/b/;value[Symbol.match]=null;console.log("abc".match(value)===null);
            """, "true\n" };
        yield return new object[] { "own-undefined", """
            const value:any=/b/;value[Symbol.match]=undefined;console.log("abc".match(value)===null);const result:any="/b/".match(value);console.log(result[0],result.index,result.input);
            """, "true\n/b/ 0 /b/\n" };
        yield return new object[] { "own-null-stringified-pattern", """
            const value:any=/b/g;value[Symbol.match]=null;value.lastIndex=2;console.log("abc".match(value)===null);const result:any="/b/g".match(value);console.log(result[0],result.index,value.lastIndex);
            """, "true\n/b/g 0 2\n" };
        yield return new object[] { "callable-and-removal", """
            const value:any=/b/;value[Symbol.match]=function(s:any){console.log(this===value,s);return "hook";};console.log("abc".match(value));delete value[Symbol.match];console.log("abc".match(value)![0]);
            """, "true abc\nhook\nb\n" };
        yield return new object[] { "noncallable-overrides", """
            const value:any=/b/;for(const method of [false,0,17,"x",{}]){value[Symbol.match]=method;try{"abc".match(value);}catch(e){console.log(e.name,e instanceof TypeError);}}
            """, "TypeError true\nTypeError true\nTypeError true\nTypeError true\nTypeError true\n" };
        yield return new object[] { "getter-and-fallback-order", """
            let order="";const value:any=/b/;Object.defineProperty(value,Symbol.match,{get(){order+="get>";return null;}});value.toString=function(){order+="string>";return "b";};console.log("abc".match(value)![0],order);
            """, "b get>string>\n" };
        yield return new object[] { "getter-call-receiver", """
            let order="";const value:any=/b/;Object.defineProperty(value,Symbol.match,{get(){order+="get>";return function(s:any){order+="call>";console.log(this===value);return s+"!";};}});value.toString=function(){throw "unexpected";};console.log("abc".match(value),order);
            """, "true\nabc! get>call>\n" };
        yield return new object[] { "abrupt-getter-and-coercion", """
            const original=new Error("original");const value:any=/b/;Object.defineProperty(value,Symbol.match,{get(){throw original;},configurable:true});try{"abc".match(value);}catch(e){console.log(e===original);}delete value[Symbol.match];value[Symbol.match]=null;value.toString=function(){throw original;};try{"abc".match(value);}catch(e){console.log(e===original);}
            """, "true\ntrue\n" };
        yield return new object[] { "prototype-replacement-observes-fresh-matcher", """
            const value:any=/b/;value[Symbol.match]=null;RegExp.prototype[Symbol.match]=function(s:any):any{console.log(this!==value,this.source.includes("/"),this.flags==="",s);return ["hook"];};console.log("abc".match(value)![0]);delete value[Symbol.match];console.log("abc".match(value)![0]);
            """, "true true true abc\nhook\nfalse false true abc\nhook\n" };
        yield return new object[] { "native-and-ordinary-fallback-controls", """
            console.log("aba".match(/a/g)!.join(","));const native:any="abc".match(/b/);console.log(native[0],native.index,native.input);const empty:any="abc".match(undefined);console.log(empty[0]==="",empty.index,empty.input);console.log("null".match(null)![0]);
            """, "a,a\nb 1 abc\ntrue 0 abc\nnull\n" };
        yield return new object[] { "undefined-pattern-prototype-receiver", """
            RegExp.prototype[Symbol.match]=function(s:any):any{console.log(this instanceof RegExp);return [typeof s,s];};console.log("abc".match(undefined)!.join("|"));
            """, "true\nstring|abc\n" };
        yield return new object[] { "borrowed-string-receiver", """
            const value:any=/b/;value[Symbol.match]=null;const match:any=String.prototype.match;console.log(match.call("abc",value)===null,match.apply("/b/",[value])[0],match.bind("/b/",value)()[0]);
            """, "true /b/ /b/\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void MatchUsesRegExpCreateAfterNullishHooks(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
