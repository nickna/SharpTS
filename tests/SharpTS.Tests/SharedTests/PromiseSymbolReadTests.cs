using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseSymbolReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["symbol-accessor", "const key=Symbol(\"key\");let writes=0;const p:any=Promise.resolve(7),receiver:any={};const proto:any={get [key](){return this===p?11:this===receiver?22:99;},set [key](v:any){writes=this===p?v:99;}};Object.setPrototypeOf(p,proto);p[key]=9;console.log(p[key],writes,Reflect.get(p,key,receiver));", "11 9 22\n"];
        yield return ["own-shadows-live-prototype", "const key=Symbol('key'),p:any=Promise.resolve(1),proto:any={get [key](){return this===p?7:99}};Object.setPrototypeOf(p,proto);Object.defineProperty(p,key,{value:9,configurable:true});console.log(p[key]);delete p[key];console.log(p[key]);", "9\n7\n"];
        yield return ["setter-only", "const key=Symbol('key'),p:any=Promise.resolve(1),proto:any={set [key](v:any){}};Object.setPrototypeOf(p,proto);console.log(p[key]===undefined);", "true\n"];
        yield return ["prototype-replacement", "const key=Symbol('key'),p:any=Promise.resolve(1),proto:any={[key]:7};Object.setPrototypeOf(p,proto);console.log(p[key]);Object.defineProperty(proto,key,{get(){return this===p?9:99}});console.log(p[key]);Object.setPrototypeOf(p,null);console.log(p[key]===undefined);", "7\n9\ntrue\n"];
        yield return ["getter-abrupt", "const key=Symbol('key'),marker:any={},p:any=Promise.resolve(1);Object.setPrototypeOf(p,{get [key](){throw marker}});try{console.log(p[key])}catch(e){console.log(e===marker)}", "true\n"];
        yield return ["ordinary-symbol-pair", "const key=Symbol('key');let writes=0;const value:any={get[key](){return 7},set[key](v:any){writes=v}};value[key]=9;console.log(value[key],writes);", "7 9\n"];
        yield return ["setter-before-getter", "const key=Symbol('key');let writes=0;const value:any={set[key](v:any){writes=v},get[key](){return 7}};value[key]=9;console.log(value[key],writes);", "7 9\n"];
        yield return ["duplicate-getter", "const key=Symbol('key');function next():any{return key}let writes=0;const value:any={get[next()](){return 1},set[next()](v:any){writes=v},get[next()](){return 7}};value[next()]=9;console.log(value[next()],writes);", "7 9\n"];
        yield return ["distinct-symbols", "const a=Symbol('key'),b=Symbol('key');const value:any={get[a](){return 7},set[b](v:any){}};console.log(value[a],value[b]===undefined);", "7 true\n"];
        yield return ["data-to-accessor", "const key=Symbol('key');function next():any{return key}const value:any={[next()]:1,set[next()](v:any){}};console.log(value[next()]===undefined);", "true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReadsUseLiveDescriptorsAndTheOriginalReceiver(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
