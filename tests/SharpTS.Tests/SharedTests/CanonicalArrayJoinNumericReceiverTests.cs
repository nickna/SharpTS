using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CanonicalArrayJoinNumericReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["contents-before-join", "function pack(prefix:number,...values:number[]):number[]{return values}\nconst log:string[]=[];const values:any={[Symbol.iterator](){log.push(\"iterator\");let index=0;return{next(){log.push(\"next\");return index++===0?{value:2,done:false}:{done:true}}}}};function tail(){log.push(\"tail\");return 4}const result:any=pack(0,...values,tail());console.log(result.length,result[0],result[1],result.join(\",\"),log.join(\",\"));", "2 2 4 2,4 iterator,next,next,tail\n"];
        yield return ["held-prototype-join", "function pack(prefix:number,...values:number[]):number[]{return values}const held=Array.prototype.join;const result:any=pack(0,2,4);console.log(result.join(\",\"),typeof held);", "2,4 function\n"];
        yield return ["plain-rest-join", "function pack(prefix:number,...values:number[]):number[]{return values}function take(values:number[]){return pack(0,...values,4)}const source:any=[2];const result:any=take(source);console.log(result.join(\",\"));", "2,4\n"];
        yield return ["canonical-call", "function pack(prefix:number,...values:number[]):number[]{return values}const join:any=Array.prototype.join;const result:any=pack(0,2,4);console.log(join.call(result,\"|\"));", "2|4\n"];
        yield return ["reflect-apply", "function pack(prefix:number,...values:number[]):number[]{return values}const join:any=Reflect.get(Array.prototype,\"join\");const result:any=pack(0,2,4);console.log(Reflect.apply(join,result,[\"|\"]));", "2|4\n"];
        yield return ["generic-receiver", "const join:any=Array.prototype.join;const result:any={0:\"a\",1:\"b\",length:2};console.log(join.call(result,\"|\"),result.length);", "a|b 2\n"];
        yield return ["static-inline-control", "function pack(prefix:number,...values:number[]):number[]{return values}const result:number[]=pack(0,2,4);console.log(result.join(\",\"));", "2,4\n"];
        yield return ["separator-default-null", "function pack(prefix:number,...values:number[]):number[]{return values}const join:any=Array.prototype.join;const result:any=pack(0,2,4);console.log(join.call(result),join.call(result,undefined),join.call(result,null));", "2,4 2,4 2null4\n"];
        yield return ["separator-order", "function pack(prefix:number,...values:number[]):number[]{return values}const join:any=Array.prototype.join;const result:any=pack(0,2,4);const events:string[]=[];function target(){events.push(\"target\");return result}function separator(){events.push(\"arg\");return{[Symbol.toPrimitive](hint:string){events.push(hint);return \"|\"}}}console.log(target().join(separator()),events.join(\",\"));", "2|4 target,arg,string\n"];
        yield return ["separator-abrupt", "function pack(prefix:number,...values:number[]):number[]{return values}const join:any=Array.prototype.join;const result:any=pack(0,2,4);const marker={};let calls=0;const separator:any={[Symbol.toPrimitive](){calls++;throw marker}};try{join.call(result,separator)}catch(error){console.log(error===marker,calls,result[0],result[1]);}", "true 1 2 4\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void CanonicalJoinConsumesActualNumericElements(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
