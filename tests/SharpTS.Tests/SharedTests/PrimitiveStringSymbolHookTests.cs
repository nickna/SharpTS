using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveStringSymbolHookTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "primitive_prototype", """
            (Number.prototype as any)[Symbol.search]=function(s:string){return s.length+4;};console.log("abc".search(1 as any));
            """, "7\n" };
        yield return new object[] { "five-primitive-prototypes", """
            function hook(s:any){"use strict";console.log(typeof this,this===1,this===false,this==="x",typeof s);return 9;}(Number.prototype as any)[Symbol.search]=hook;(Boolean.prototype as any)[Symbol.search]=hook;(String.prototype as any)[Symbol.search]=hook;(BigInt.prototype as any)[Symbol.search]=hook;(Symbol.prototype as any)[Symbol.search]=hook;for(const value of [1,false,"x",1n,Symbol("s")]){console.log("abc".search(value as any));}
            """, "number true false false string\n9\nboolean false true false string\n9\nstring false false true string\n9\nbigint false false false string\n9\nsymbol false false false string\n9\n" };
        yield return new object[] { "strict-accessor-receiver", """
            let order="";Object.defineProperty(Number.prototype,Symbol.search,{get(){"use strict";order+="get:"+typeof this+":"+(this===2)+">";return function(s:any){"use strict";order+="call:"+typeof this+":"+(this===2)+">";return s.length;};}});console.log("abc".search(2 as any),order);
            """, "3 get:number:true>call:number:true>\n" };
        yield return new object[] { "strict-string-accessor-receiver", """
            Object.defineProperty(String.prototype,Symbol.match,{get(){"use strict";console.log(typeof this,this==="b");return function(s:any){"use strict";console.log(typeof this,this==="b");return [s];};}});console.log("abc".match("b")![0]);
            """, "string true\nstring true\nabc\n" };
        yield return new object[] { "abrupt-accessor-and-call", """
            const original=new Error("original");Object.defineProperty(Boolean.prototype,Symbol.search,{get(){throw original;},configurable:true});try{"abc".search(false as any);}catch(e){console.log(e===original);}delete (Boolean.prototype as any)[Symbol.search];(Boolean.prototype as any)[Symbol.search]=function(){throw original;};try{"abc".search(false as any);}catch(e){console.log(e===original);}
            """, "true\ntrue\n" };
        yield return new object[] { "noncallable-and-nullish-hooks", """
            const p:any=Number.prototype;for(const value of [false,0,"x",{}]){p[Symbol.search]=value;try{"abc".search(1 as any);}catch(e){console.log(e.name,e instanceof TypeError);}}p[Symbol.search]=null;console.log("a1b".search(1 as any));p[Symbol.search]=undefined;console.log("a1b".search(1 as any));delete p[Symbol.search];console.log("a1b".search(1 as any));
            """, "TypeError true\nTypeError true\nTypeError true\nTypeError true\n1\n1\n1\n" };
        yield return new object[] { "all-shared-string-protocols", """
            const p:any=Number.prototype;p[Symbol.match]=function(s:any){return ["M",s];};p[Symbol.search]=function(s:any){return s.length+4;};p[Symbol.replace]=function(s:any,r:any){return s+":"+r;};p[Symbol.split]=function(s:any,n:any){return [s,String(n)];};console.log("abc".match(1 as any)!.join("|"),"abc".search(1 as any),"abc".replace(1 as any,"X"),"abc".replaceAll(1 as any,"Y"),"abc".split(1 as any,2).join("|"));
            """, "M|abc 7 abc:X abc:Y abc|2\n" };
        yield return new object[] { "matchall-primitive-hook", """
            Object.defineProperty(Number.prototype,Symbol.match,{get(){throw "IsRegExp must ignore primitives";}});(Number.prototype as any)[Symbol.matchAll]=function(s:any){"use strict";console.log(typeof this,this===1,typeof s);return "hook:"+s;};console.log("abc".matchAll(1 as any));
            """, "number true string\nhook:abc\n" };
        yield return new object[] { "boxed-and-custom-controls", """
            const box:any=new Number(1);(Number.prototype as any)[Symbol.search]=function(s:any){"use strict";console.log(this===box,typeof this);return s.length+4;};console.log("abc".search(box));const value:any={[Symbol.match](s:any){console.log(this===value);return [s];}};console.log("abc".match(value)![0]);
            """, "true object\n7\ntrue\nabc\n" };
        yield return new object[] { "nullish-candidates-skip-prototypes", """
            let calls=0;(Object.prototype as any)[Symbol.search]=function(){calls++;return 99;};console.log("abc".search(undefined),"null".search(null),calls);
            """, "0 0 0\n" };
        yield return new object[] { "inherited-object-prototype-hook", """
            (Object.prototype as any)[Symbol.search]=function(s:any){"use strict";console.log(typeof this);return s.length+5;};console.log("abc".search(1 as any),"abc".search(false as any),"abc".search("x"));
            """, "number\nboolean\nstring\n8 8 8\n" };
        yield return new object[] { "borrowed-value-calls", """
            (Number.prototype as any)[Symbol.search]=function(s:any){"use strict";console.log(typeof this,this===1);return s.length+4;};const search:any=String.prototype.search;console.log(search.call("abc",1),search.apply("abc",[1]),search.bind("abc",1)());
            """, "number true\nnumber true\nnumber true\n7 7 7\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void StringProtocolsObservePrimitivePrototypeHooks(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
