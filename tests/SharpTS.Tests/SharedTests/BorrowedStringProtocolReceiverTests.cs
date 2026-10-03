using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BorrowedStringProtocolReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "borrowed_receiver", """
            const value:any={[Symbol.match](s:any){return typeof s+":"+s;}};console.log(String.prototype.match.call(77 as any,value));
            """, "number:77\n" };
        yield return new object[] { "object-identity-without-conversion", """
            const receiver:any={toString(){throw "unexpected";}};const value:any={[Symbol.match](s:any){console.log(this===value,s===receiver);return "hook";}};console.log(String.prototype.match.call(receiver,value));
            """, "true true\nhook\n" };
        yield return new object[] { "getter-before-call", """
            let order="";const receiver:any={toString(){order+="string>";return "abc";}};const value:any={};Object.defineProperty(value,Symbol.match,{get(){order+="get>";return function(s:any){order+="call>";return s===receiver;};}});console.log(String.prototype.match.call(receiver,value),order);
            """, "true get>call>\n" };
        yield return new object[] { "fallback-coercion-order", """
            let order="";const receiver:any={toString(){order+="receiver>";return "abc";}};const value:any={toString(){order+="pattern>";return "b";}};Object.defineProperty(value,Symbol.match,{get(){order+="get>";return null;}});const result:any=String.prototype.match.call(receiver,value);console.log(result[0],order);
            """, "b get>receiver>pattern>\n" };
        yield return new object[] { "abrupt-coercion-order", """
            let order="";const original=new Error("original");const receiver:any={toString(){order+="receiver>";throw original;}};const value:any={toString(){order+="pattern>";return "b";}};Object.defineProperty(value,Symbol.match,{get(){order+="get>";return undefined;}});try{String.prototype.match.call(receiver,value);}catch(e){console.log(e===original,order);}
            """, "true get>receiver>\n" };
        yield return new object[] { "abrupt-getter-precedes-conversion", """
            let calls=0;const original=new Error("original");const receiver:any={toString(){calls++;return "abc";}};const value:any={};Object.defineProperty(value,Symbol.match,{get(){throw original;}});try{String.prototype.match.call(receiver,value);}catch(e){console.log(e===original,calls);}
            """, "true 0\n" };
        yield return new object[] { "primitive-receiver-types", """
            const value:any={[Symbol.match](s:any){return typeof s;}};const match:any=String.prototype.match;for(const receiver of [77,false,"abc",1n,Symbol("s")]){console.log(match.call(receiver,value));}
            """, "number\nboolean\nstring\nbigint\nsymbol\n" };
        yield return new object[] { "borrowed-value-forms", """
            const value:any={[Symbol.match](s:any){return typeof s+":"+s;}};const match:any=String.prototype.match;console.log(match.call(77,value),match.apply(77,[value]),match.bind(77,value)());
            """, "number:77 number:77 number:77\n" };
        yield return new object[] { "nullish-receiver-before-getter", """
            let calls=0;const value:any={};for(const key of [Symbol.match,Symbol.search,Symbol.matchAll,Symbol.split,Symbol.replace]){Object.defineProperty(value,key,{get(){calls++;return function(){return "hook";};}});}const p:any=String.prototype;for(const method of [p.match,p.search,p.matchAll,p.split,p.replaceAll]){for(const receiver of [null,undefined]){try{method.call(receiver,value);}catch(e){console.log(e.name,e instanceof TypeError,calls);}}}
            """, "TypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\nTypeError true 0\n" };
        yield return new object[] { "other-protocol-original-receivers", """
            let calls=0;const receiver:any={toString(){calls++;throw "unexpected";}};const value:any={[Symbol.search](s:any){return s===receiver;},[Symbol.matchAll](s:any){return s===receiver;},[Symbol.split](s:any,n:any){console.log(n);return s===receiver;},[Symbol.replace](s:any,r:any){console.log(r);return s===receiver;}};const p:any=String.prototype;console.log(p.search.call(receiver,value),p.matchAll.call(receiver,value),p.split.call(receiver,value,2),p.replaceAll.call(receiver,value,"X"),calls);
            """, "2\nX\ntrue true true true 0\n" };
        yield return new object[] { "boxed-string-direct-and-any", """
            const receiver=new String("abc");const alias:any=receiver;const value:any={flags:"g",[Symbol.match](s:any){console.log(s===receiver);return ["M"];},[Symbol.search](s:any){console.log(s===receiver);return 7;},[Symbol.matchAll](s:any){console.log(s===receiver);return "A";},[Symbol.split](s:any){console.log(s===receiver);return ["S"];}};const result:any=receiver.match(value);console.log(result[0],alias.match(value)[0]);console.log(receiver.search(value),alias.search(value));console.log(receiver.split(value)[0],alias.split(value)[0]);console.log((String.prototype as any).matchAll.call(receiver,value),alias.matchAll(value));
            """, "true\ntrue\nM M\ntrue\ntrue\n7 7\ntrue\ntrue\nS S\ntrue\ntrue\nA A\n" };
        yield return new object[] { "ordinary-fallback-and-omitted-pattern", """
            const match:any=String.prototype.match;const search:any=String.prototype.search;console.log(match.call(77,"7")[0],search.call(77,"7"));const empty:any=match.call("abc");console.log(empty[0]==="",empty.index,search.call("abc"));for(const method of [match,search]){try{method.call(Symbol("s"),undefined);}catch(e){console.log(e.name,e instanceof TypeError);}}
            """, "7 0\ntrue 0 0\nTypeError true\nTypeError true\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void BorrowedStringProtocolsDeferReceiverConversion(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
