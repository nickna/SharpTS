using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingAcquisitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "accessor_capture", """
            const r:any={value:5};Object.defineProperty(r,Symbol.dispose,{get:function(){console.log("get");return function(){console.log(this.value);};}});{using x=r;console.log("body");}
            """, "get\nbody\n5\n" };
        yield return new object[] { "method_capture", """
            const r:any={ [Symbol.dispose](){console.log("first");} };{using x=r;r[Symbol.dispose]=function(){console.log("replacement");};console.log("body");}
            """, "body\nfirst\n" };
        yield return new object[] { "nullish-resources", """
            {using a:any=null;using b:any=undefined;console.log("body");}console.log("after");
            """, "body\nafter\n" };
        yield return new object[] { "getter-throws-before-body", """
            const original=new Error("original");const r:any={};Object.defineProperty(r,Symbol.dispose,{get(){console.log("get");throw original;}});try{{using x=r;console.log("unreachable");}}catch(e){console.log(e===original);}console.log("after");
            """, "get\ntrue\nafter\n" };
        yield return new object[] { "partial-registration-cleanup", """
            const original=new Error("original");const a:any={value:1,[Symbol.dispose](){console.log("dispose",this.value);}};const b:any={};Object.defineProperty(b,Symbol.dispose,{get(){console.log("get-b");throw original;}});try{{using x=a,y=b;console.log("unreachable");}}catch(e){console.log(e===original);}
            """, "get-b\ndispose 1\ntrue\n" };
        yield return new object[] { "multiple-getters-lifo", """
            function make(value:number):any{const r:any={value};Object.defineProperty(r,Symbol.dispose,{get(){console.log("get",value);return function(){console.log("dispose",this.value);};}});return r;}{using a=make(1),b=make(2);console.log("body");a.value=3;b.value=4;}
            """, "get 1\nget 2\nbody\ndispose 4\ndispose 3\n" };
        yield return new object[] { "deletion-after-registration", """
            const r:any={value:5,[Symbol.dispose](){console.log(this===r,this.value);}};{using x=r;delete r[Symbol.dispose];r.value=9;console.log("body");}
            """, "body\ntrue 9\n" };
        yield return new object[] { "function-body-capture", """
            const r:any={value:7};Object.defineProperty(r,Symbol.dispose,{configurable:true,get(){console.log("get");return function(){console.log(this===r,this.value);};}});function work():number{using x=r;delete r[Symbol.dispose];console.log("body");return 9;}console.log(work());
            """, "get\nbody\ntrue 7\n9\n" };
        yield return new object[] { "nested-capture-and-thrown-body", """
            const original=new Error("body");function make(value:number):any{return {value,[Symbol.dispose](){console.log(this.value);}};}try{{using a=make(1);{using b=make(2);b[Symbol.dispose]=function(){console.log("replacement");};throw original;}}}catch(e){console.log(e===original);}
            """, "2\n1\ntrue\n" };
        yield return new object[] { "noncallable-rejected-at-registration", """
            for(const value of [42,null,undefined]){const r:any={[Symbol.dispose]:value};try{{using x=r;console.log("unreachable");}}catch(e){console.log(e.name,e instanceof TypeError);}}
            """, "TypeError true\nTypeError true\nTypeError true\n" };
        yield return new object[] { "inherited-getter-receiver", """
            const p:any={};let calls=0;Object.defineProperty(p,Symbol.dispose,{get(){calls++;console.log(this.value);return function(){console.log(this===r,this.value,calls);};}});const r:any=Object.create(p);r.value=3;{using x=r;r.value=4;console.log("body");}
            """, "3\nbody\ntrue 4 1\n" };
        yield return new object[] { "captured-bound-disposer", """
            const target:any={value:8};function dispose(this:any){console.log(this===target,this.value);}const r:any={[Symbol.dispose]:dispose.bind(target)};{using x=r;r[Symbol.dispose]=function(){console.log("replacement");};console.log("body");}
            """, "body\ntrue 8\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledUsingCapturesDisposerAtRegistration(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
