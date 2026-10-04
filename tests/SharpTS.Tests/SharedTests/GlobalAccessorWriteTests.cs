using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalAccessorWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const root:any=globalThis;const box:any={value:2};Object.defineProperty(root,"__sharptsGlobalAccessor",{get(){return box.value;},set(value:number){box.value=value;},configurable:true});root["__sharptsGlobalAccessor"]=8;console.log(globalThis.__sharptsGlobalAccessor,box.value);delete root.__sharptsGlobalAccessor;console.log(root.__sharptsGlobalAccessor===undefined);
            """, "8 8\ntrue\n" };
        yield return new object[] { "direct-dot-index-receiver", """
            const root:any=globalThis;let stored=2;let calls=0;let receiver:any;Object.defineProperty(root,"__ga",{get(){return stored;},set(v:number){calls++;stored=v;receiver=this;},configurable:true});globalThis.__ga=4;root.__ga=6;root["__ga"]=7;console.log(root.__ga,calls,receiver===root);
            """, "7 3 true\n" };
        yield return new object[] { "computed-and-global-alias", """
            const root:any=globalThis;const alias:any=root.global;const key="__ga";let stored=2;let calls=0;Object.defineProperty(root,key,{get(){return stored;},set(v:number){calls++;stored=v;},configurable:true});alias[key]=4;globalThis[key]=8;console.log(alias[key],calls,alias===root);
            """, "8 2 true\n" };
        yield return new object[] { "setter-only", """
            const root:any=globalThis;let stored=0;let calls=0;let receiver:any;Object.defineProperty(root,"__ga",{set(v:number){stored=v;calls++;receiver=this;},configurable:true});root.__ga=8;console.log(root.__ga===undefined,stored,calls,receiver===root);delete root.__ga;console.log(root.__ga===undefined);
            """, "true 8 1 true\ntrue\n" };
        yield return new object[] { "setter-throw", """
            const root:any=globalThis;const original=new Error("setter");let calls=0;Object.defineProperty(root,"__ga",{set(v:number){calls++;throw original;},configurable:true});try{root["__ga"]=8;}catch(e){console.log(e===original,e.message);}console.log(calls,root.__ga===undefined);
            """, "true setter\n1 true\n" };
        yield return new object[] { "reentrant-global-write", """
            const root:any=globalThis;let calls=0;Object.defineProperty(root,"__ga",{get(){return root.__side;},set(v:number){calls++;this.__side=v+1;},configurable:true});root.__ga=8;console.log(root.__ga,root.__side,calls);
            """, "9 9 1\n" };
        yield return new object[] { "read-modify-write", """
            const root:any=globalThis;let stored=2;let calls=0;Object.defineProperty(root,"__ga",{get(){return stored;},set(v:number){stored=v;calls++;},configurable:true});root.__ga++;root.__ga+=3;console.log(root.__ga,calls);
            """, "6 2\n" };
        yield return new object[] { "data-and-getter-only-values", """
            const root:any=globalThis;let reads=0;Object.defineProperty(root,"__data",{value:4,writable:false,configurable:true});Object.defineProperty(root,"__getter",{get(){reads++;return 2;},configurable:true});try{root.__data=9;}catch(e){}try{root.__getter=9;}catch(e){}console.log(root.__data,root.__getter,reads);delete root.__data;delete root.__getter;console.log(root.__data===undefined,root.__getter===undefined);
            """, "4 2 1\ntrue true\n" };
        yield return new object[] { "strict-setter", """
            const root:any=globalThis;let stored=2;Object.defineProperty(root,"__ga",{get(){return stored;},set(v:number){stored=v;},configurable:true});function assign(){"use strict";root.__ga=8;}assign();console.log(root.__ga);
            """, "8\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void GlobalWritesInvokeInstalledSettersWithTheGlobalReceiver(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
