using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromisePrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["own-and-borrowed","const p:any=Promise.resolve(7),proto:any={tag:11};console.log(Object.setPrototypeOf(p,proto)===p,Object.getPrototypeOf(p)===proto,p.tag,typeof p.then);Promise.prototype.then.call(p,(v:any)=>console.log(v));","true true 11 undefined\n7\n"];
        yield return ["subclass-explicit","class P extends (Promise as any){}const p:any=P.resolve(7),proto:any={tag:9};console.log(Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===proto,p.tag,typeof p.then);Promise.prototype.then.call(p,(v:any)=>console.log(v));","true true 9 undefined\n7\n"];
        yield return ["nonextensible","const p:any=Promise.resolve(7),proto=Object.getPrototypeOf(p);Object.preventExtensions(p);console.log(Reflect.setPrototypeOf(p,proto),Reflect.setPrototypeOf(p,{}));try{Object.setPrototypeOf(p,{});}catch(e){console.log(e instanceof TypeError);}","true false\ntrue\n"];
        yield return ["cycle","const p:any=Promise.resolve(7),proto=Object.getPrototypeOf(p);console.log(Reflect.setPrototypeOf(p,p),Object.getPrototypeOf(p)===proto);try{Object.setPrototypeOf(p,p);}catch(e){console.log(e instanceof TypeError);}const next:any={};Object.setPrototypeOf(next,p);console.log(Reflect.setPrototypeOf(p,next),Object.getPrototypeOf(p)===proto);","false true\ntrue\nfalse true\n"];
        yield return ["proxy-parent","let calls=0;const p:any=Promise.resolve(7),proto:any=new Proxy({},{getPrototypeOf(){calls++;return null;}});console.log(Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===proto,calls);","true true 0\n"];
        yield return ["inherited-descriptors","let seen=0;const p:any=Promise.resolve(7),receiver:any={};const proto:any={get tag(){return this===p?11:this===receiver?22:99;},set value(v:any){seen=this===p?v:99;}};Object.setPrototypeOf(p,proto);p.value=9;console.log(p.tag,seen,Reflect.get(p,\"tag\",receiver),Object.hasOwn(p,\"value\"));","11 9 22 false\n"];
        yield return ["null-read","const p:any=Promise.resolve(7);console.log(Object.setPrototypeOf(p,null)===p,Object.getPrototypeOf(p)===null,typeof p.then);Promise.prototype.then.call(p,(v:any)=>console.log(v));","true true undefined\n7\n"];
        yield return ["symbol-accessor","const key=Symbol(\"key\");let writes=0;const p:any=Promise.resolve(7),receiver:any={};const proto:any={get [key](){return this===p?11:this===receiver?22:99;},set [key](v:any){writes=this===p?v:99;}};Object.setPrototypeOf(p,proto);p[key]=9;console.log(p[key],writes,Reflect.get(p,key,receiver));","11 9 22\n"];
        yield return ["inherited-readonly","const p:any=Promise.resolve(7),proto:any={};Object.defineProperty(proto,\"value\",{value:7,writable:false});Object.setPrototypeOf(p,proto);console.log(Reflect.set(p,\"value\",9),p.value);try{(()=>{\"use strict\";p.value=9;})();}catch(e){console.log(e instanceof TypeError);}console.log(Object.hasOwn(p,\"value\"));","false 7\ntrue\nfalse\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void MutatesOnlyExtensibleAcyclicPromisePrototypes(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
