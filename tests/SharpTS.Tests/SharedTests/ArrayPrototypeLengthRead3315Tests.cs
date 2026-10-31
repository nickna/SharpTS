using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayPrototypeLengthRead3315Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["borrowed-push-on-array-prototype","const push:any=Array.prototype.push;console.log(push.call(Array.prototype,7),Array.prototype.length);","1 1\n"];
        yield return ["borrowed-unshift-on-array-prototype","const unshift:any=Array.prototype.unshift;console.log(unshift.call(Array.prototype,7),Array.prototype.length,Array.prototype[0]);","1 1 7\n"];
        yield return ["helper-replaces-array-prototype-method","const prototype:any=Array.prototype;function replace(value:any){value.push=function(){return 17}}replace(prototype);const values:any=[];console.log(values.push(7),values.length);","17 0\n"];
        yield return ["strict-own-data-remains-live","'use strict';const target:any={x:1,get marker(){return 0}};target.x=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);","7 7\n"];
        yield return ["read-path-diagnostic","const P:any=Array.prototype;const push:any=P.push;console.log(push.call(P,7),P.length,Array.prototype.length,Reflect.get(P,'length'),Object.getOwnPropertyDescriptor(P,'length')!.value);","1 1 1 1 1\n"];
        yield return ["direct-slot-update","const P:any=Array.prototype;P.length=7;console.log(P.length,Array.prototype.length);","7 7\n"];
        yield return ["above-int32-length","const P:any=Array.prototype;P.length=2147483648;console.log(P.length,Array.prototype.length);","2147483648 2147483648\n"];
        yield return ["typed-parameter","const P:any=Array.prototype;P.length=7;function read(values:any[]){return values.length}console.log(read(P),read([1,2,3]));","7 3\n"];
        yield return ["typed-parameter-loop","const P:any=Array.prototype;P.length=2;function sum(values:any[]){let result=0;for(let i=0;i<2;i++)result+=values.length;return result}console.log(sum(P),sum([1,2,3]));","4 6\n"];
        yield return ["receiver-evaluated-once","const P:any=Array.prototype;P.length=7;let calls=0;function receiver():any[]{calls++;return Array.prototype}console.log(receiver().length,calls);","7 1\n"];
        yield return ["async-length-read","const P:any=Array.prototype;P.length=7;async function run(){await Promise.resolve();console.log(Array.prototype.length)}run();","7\n"];
        yield return ["generator-length-read","const P:any=Array.prototype;P.length=7;function* run(){yield 1;return Array.prototype.length}const iterator=run();console.log(iterator.next().value,iterator.next().value);","1 7\n"];
        yield return ["ordinary-array-and-string-controls","const numeric:number[]=[1,2],flags:boolean[]=[true,false,true],words:string[]=['x'];function count(a:any[]){return a.length}console.log(numeric.length,flags.length,words.length,count([1,2,3,4]),'abc'.length);","2 3 1 4 3\n"];
        yield return ["arguments-length-control","function count(a:number,b:number){return arguments.length}console.log(count(1,2));","2\n"];
        yield return ["source-Array-shadow-control","function run(){class Array{length=7}console.log(new Array().length)}run();","7\n"];
    }

    public static IEnumerable<object[]> LibraryModes() => Cases().SelectMany(row =>
        new[] { false, true }.Select(noLib => new object[] { row[0], row[1], row[2], noLib }));

    [Theory, MemberData(nameof(Cases))]
    public void DynamicLibraryProfileRetainsLiveReads(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach(var mode in new[] {ExecutionMode.Interpreted, ExecutionMode.Compiled})
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
