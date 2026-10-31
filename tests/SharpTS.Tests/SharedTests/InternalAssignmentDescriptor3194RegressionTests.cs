using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InternalAssignmentDescriptor3194RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-proxy-poison", "const target:any={},proxy:any=new Proxy(target,{});Object.defineProperty(Object.prototype,\"get\",{value:function(){},configurable:true});try{proxy.x=2;console.log(target.x);}catch(error){console.log(error instanceof TypeError);}", "2\n"];
        yield return ["reflect-explicit-receiver-poison", "const target:any={};let reads=0;const marker={};Object.defineProperty(Object.prototype,\"get\",{get(){reads++;throw marker;},configurable:true});try{console.log(Reflect.set(target,\"x\",3,target),target.x,reads);}catch(error){console.log(error===marker,reads);}", "true 3 0\n"];
        yield return ["public-new-descriptor-shape", "const target:any={},proxy:any=new Proxy(target,{defineProperty(target:any,key:PropertyKey,descriptor:PropertyDescriptor){console.log(Object.getPrototypeOf(descriptor)===Object.prototype,Object.keys(descriptor).join(\",\"),descriptor.value);return Reflect.defineProperty(target,key,descriptor);}});proxy.x=2;console.log(target.x);", "true value,writable,enumerable,configurable 2\n2\n"];
        yield return ["public-update-descriptor-shape", "const target:any={x:1},proxy:any=new Proxy(target,{defineProperty(target:any,key:PropertyKey,descriptor:PropertyDescriptor){console.log(Object.getPrototypeOf(descriptor)===Object.prototype,Object.keys(descriptor).join(\",\"),descriptor.value);return Reflect.defineProperty(target,key,descriptor);}});proxy.x=3;console.log(target.x);", "true value 3\n3\n"];
        yield return ["readonly-refusal-before-record", "const target:any={};Object.defineProperty(target,\"x\",{value:1,writable:false});let reads=0;Object.defineProperty(Object.prototype,\"get\",{get(){reads++;throw new Error(\"poison\");},configurable:true});console.log(Reflect.set(target,\"x\",2,target),target.x,reads);", "false 1 0\n"];
        yield return ["public-trap-copy-under-poison", "const target:any={},proxy:any=new Proxy(target,{defineProperty(_target:any,_key:PropertyKey,descriptor:PropertyDescriptor){console.log(Object.getPrototypeOf(descriptor)===Object.prototype,Object.keys(descriptor).join(\",\"));return true;}});Object.defineProperty(Object.prototype,\"get\",{value:function(){},configurable:true});proxy.x=2;console.log(Object.hasOwn(target,\"x\"));", "true value,writable,enumerable,configurable\nfalse\n"];
        yield return ["existing-value-partial-record-ready", "const target:any={x:1},proxy:any=new Proxy(target,{});let reads=0;console.log(\"ready\");Object.defineProperty(Object.prototype,\"writable\",{get(){reads++;return false;},configurable:true});proxy.x=2;const d:any=Object.getOwnPropertyDescriptor(target,\"x\");console.log(target.x,reads,d.writable,d.enumerable,d.configurable);", "ready\n2 0 true true true\n"];
        yield return ["symbol-key-null-handler-and-poison", "const symbol=Symbol(\"key\"),target:any={},proxy:any=new Proxy(target,Object.create(null));Object.defineProperty(Object.prototype,\"set\",{value:function(){},configurable:true});try{proxy[symbol]=7;console.log(target[symbol],Object.getOwnPropertySymbols(target)[0]===symbol);}catch(error){console.log(error instanceof TypeError);}", "7 true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PrivateRecordsAndPublicProxyCopies(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
