using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoxedStringOwnMethodDispatchTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["boxed-own-method-original", "const value:any=new String(\"abc\");value.includes=function(s:string){return s===\"q\"};console.log(value.includes(\"q\"));", "true\n"];
        yield return ["own-trim", "const value:any=new String(\" abc \");value.trim=function(){return \"own\"};console.log(value.trim());", "own\n"];
        yield return ["own-char-at", "const value:any=new String(\"abc\");value.charAt=function(i:number){return \"own\"+i};console.log(value.charAt(1));", "own1\n"];
        yield return ["own-undefined", "const value:any=new String(\"abc\");value.includes=undefined;try{value.includes(\"a\")}catch(e:any){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["noncallable-own-argument-order", "let count=0;const value:any=new String(\"abc\");value.includes=7;function arg(){count++;return \"a\"}try{value.includes(arg())}catch(e:any){console.log(e instanceof TypeError,count)}", "true 1\n"];
        yield return ["own-getter-receiver-and-order", "let log=\"\";const value:any=new String(\"abc\");Object.defineProperty(value,\"includes\",{get(){log+=\"g\";return function(this:any,s:string){return this===value&&s===\"q\"}}});function arg(){log+=\"a\";return \"q\"}console.log(value.includes(arg()),log);", "true ga\n"];
        yield return ["own-getter-abrupt", "let count=0;const marker={};const value:any=new String(\"abc\");Object.defineProperty(value,\"includes\",{get(){throw marker}});function arg(){count++;return \"a\"}try{value.includes(arg())}catch(e){console.log(e===marker,count)}", "true 0\n"];
        yield return ["argument-redefines-method-after-capture", "const value:any=new String(\"abc\");value.includes=function(s:string){return \"old\"};function arg(){value.includes=function(){return \"new\"};return \"a\"}console.log(value.includes(arg()));", "old\n"];
        yield return ["getter-deletes-own-slot", "let count=0;const value:any=new String(\"abc\");Object.defineProperty(value,\"includes\",{configurable:true,get(){count++;delete value.includes;return function(){return true}}});console.log(value.includes(\"q\"),count,value.includes(\"a\",1));", "true 1 false\n"];
        yield return ["delete-restores-native-argument-path", "const value:any=new String(\"abc\");value.includes=function(){return true};delete value.includes;console.log(value.includes(\"a\",1),value.indexOf(\"b\",2));", "false -1\n"];
        yield return ["descriptor-defined-own-method", "const value:any=new String(\"abc\");Object.defineProperty(value,\"includes\",{value:function(s:string){return s===\"q\"},writable:false});console.log(value.includes(\"q\"));", "true\n"];
        yield return ["own-nonboolean-result", "const value:any=new String(\"abc\");value.includes=function(){return 7};console.log(value.includes(\"q\"));", "7\n"];
        yield return ["async-own-method", "const value:any=new String(\"abc\");value.includes=function(this:any,s:string){return this===value&&s===\"q\"};async function arg(){return \"q\"}async function run(){console.log(value.includes(await arg()))}run();", "true\n"];
        yield return ["primitive-hot-control", "const value=\"banana\";console.log(value.includes(\"a\",2),value.indexOf(\"na\",1),value.slice(1,3));", "true 2 an\n"];
        yield return ["borrowed-string-coercion-control", "const value:any=new Error(\"clone\");console.log(String.prototype.includes.call(value,\"clone\"));", "true\n"];
        yield return ["ordinary-own-method-control", "const value:any={includes(this:any,s:string){return this===value&&s===\"q\"}};console.log(value.includes(\"q\"));", "true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OwnMethodsPrecedeBoxedStringIntrinsics(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
