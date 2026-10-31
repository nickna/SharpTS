using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseConstructorIntegrity3174RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-freeze-overlay", "const p:any=Promise;p.x=1;Object.freeze(p);p.x=2;console.log(p.x,Object.getOwnPropertyDescriptor(p,\"x\")?.writable);", "1 false\n"];
        yield return ["original-nonextensible-write", "const p:any=Promise;Object.preventExtensions(p);p.new=1;console.log(Object.hasOwn(p,\"new\"),p.new);", "false undefined\n"];
        yield return ["nonextensible-define-control", "const p:any=Promise;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,\"new\",{value:1}),Object.hasOwn(p,\"new\"));", "false false\n"];
        yield return ["strict-nonextensible-write", "const p:any=Promise;Object.preventExtensions(p);function put(){\"use strict\";p.new=7;}try{put();console.log(\"accepted\");}catch(error){console.log(error instanceof TypeError);}console.log(Object.hasOwn(p,\"new\"));", "true\nfalse\n"];
        yield return ["seal-existing-writable", "const p:any=Promise;p.x=1;Object.seal(p);p.x=3;const d:any=Object.getOwnPropertyDescriptor(p,\"x\");console.log(p.x,d.writable,d.configurable,delete p.x,Object.isSealed(p));", "3 true false false true\n"];
        yield return ["frozen-accessor-setter", "const p:any=Promise;let reads=0,writes=0,value=1;const get=()=>{reads++;return value;},set=(next:number)=>{writes++;value=next;};Object.defineProperty(p,\"x\",{get,set,configurable:true});Object.freeze(p);console.log(reads,writes);p.x=7;const d:any=Object.getOwnPropertyDescriptor(p,\"x\");console.log(p.x,reads,writes,d.get===get,d.set===set,d.configurable);", "0 0\n7 1 1 true true false\n"];
        yield return ["inherited-setter-nonextensible", "const p:any=Promise;let calls=0;Object.defineProperty(Function.prototype,\"fresh\",{set:function(this:any,value:number){calls++;console.log(this===p,value);},configurable:true});Object.preventExtensions(p);p.fresh=7;console.log(calls,Object.hasOwn(p,\"fresh\"));", "true 7\n1 false\n"];
        yield return ["frozen-compatible-definitions", "const p:any=Promise;p.x=1;Object.freeze(p);console.log(Reflect.defineProperty(p,\"x\",{value:1}),Reflect.defineProperty(p,\"x\",{value:2}),p.x);", "true false 1\n"];
        yield return ["deleted-before-freeze", "const p:any=Promise;p.x=1;console.log(delete p.x);Object.freeze(p);p.x=2;console.log(Object.hasOwn(p,\"x\"),p.x===undefined);", "true\nfalse true\n"];
        yield return ["freeze-native-method", "const p:any=Promise,resolve=p.resolve;Object.freeze(p);p.resolve=()=>3;const d:any=Object.getOwnPropertyDescriptor(p,\"resolve\");console.log(p.resolve===resolve,d.value===resolve,d.writable,d.configurable,d.enumerable);", "true true false false false\n"];
        yield return ["deleted-native-method-before-freeze", "const p:any=Promise,resolve=p.resolve;console.log(delete p.resolve);Object.freeze(p);p.resolve=resolve;console.log(p.resolve===undefined,Object.hasOwn(p,\"resolve\"));", "true\ntrue false\n"];
        yield return ["sealed-compatible-value", "const p:any=Promise;p.x=1;Object.seal(p);console.log(Reflect.defineProperty(p,\"x\",{value:2}),Reflect.defineProperty(p,\"x\",{configurable:true}),p.x,Object.getOwnPropertyDescriptor(p,\"x\")!.writable);", "true false 2 true\n"];
        yield return ["frozen-reflect-receiver", "const p:any=Promise,receiver:any={};p.x=1;Object.freeze(p);console.log(Reflect.set(p,\"x\",2,receiver),p.x,Object.hasOwn(receiver,\"x\"));", "false 1 false\n"];
        yield return ["bigint-field-freeze", "const p:any=Promise;p.x=1n;Object.freeze(p);p.x=2n;const value=p.x;console.log(typeof value,value===1n,Object.getOwnPropertyDescriptor(p,\"x\")!.writable);", "bigint true false\n"];
        yield return ["noncanonical-owner-control", "const p:any=Promise,other:any=Date;other.x=1;Object.preventExtensions(p);other.x=2;console.log(other.x,Object.isExtensible(other));", "2 true\n"];
        yield return ["internal-descriptor-prototype-poison", "const p:any=Promise;p.x=1;Object.defineProperty(Object.prototype,\"get\",{value:function(){},configurable:true});try{p.x=2;console.log(p.x);}catch(error){console.log(error instanceof TypeError);}", "2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ActualIntrinsicStringPropertiesHonorIntegrity(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}