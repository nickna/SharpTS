using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NumericSpreadElementValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["undefined-elements", "function pack(prefix:number,...values:number[]):number[]{return values}\nfunction take(values:number[]){return pack(0,...values,4)}const source:any=[undefined,2];const result:any=take(source);console.log(result.length,result[0]===undefined,result[1],result[2],source.length,source[0]===undefined);", "3 true 2 4 2 true\n"];
        yield return ["object-elements", "function pack(prefix:number,...values:number[]):number[]{return values}\nlet calls=0;const marker:any={[Symbol.toPrimitive](){calls++;return 9}};function take(values:number[]){return pack(0,...values,4)}const source:any=[marker,2];const result:any=take(source);result[1]=8;console.log(result[0]===marker,source[1],result[1],calls);", "true 2 8 0\n"];
        yield return ["bigint-elements", "function pack(prefix:number,...values:number[]):number[]{return values}\nfunction take(values:number[]){return pack(0,...values,4)}const source:any=[1n,2];const result:any=take(source);console.log(result.length,typeof result[0],result[0]===1n,result[1],result[2]);", "3 bigint true 2 4\n"];
        yield return ["iterator-order", "function pack(prefix:number,...values:number[]):number[]{return values}\nconst log:string[]=[];const values:any={[Symbol.iterator](){log.push(\"iterator\");let index=0;return{next(){log.push(\"next\");return index++===0?{value:2,done:false}:{done:true}}}}};function tail(){log.push(\"tail\");return 4}const result:any=pack(0,...values,tail());console.log(result.join(\",\"),log.join(\",\"));", "2,4 iterator,next,next,tail\n"];
        yield return ["iterator-abrupt", "function pack(prefix:number,...values:number[]):number[]{return values}\nconst marker={};const log:string[]=[];const values:any={[Symbol.iterator](){return{next(){log.push(\"next\");throw marker}}}};function tail(){log.push(\"tail\");return 4}try{pack(0,...values,tail())}catch(error){console.log(error===marker,log.join(\",\"))}", "true next\n"];
        yield return ["noniterable-abrupt", "function pack(prefix:number,...values:number[]):number[]{return values}\nconst values:any=undefined;let called=0;try{pack(0,...values,++called)}catch(error){console.log(error instanceof TypeError,called)}", "true 0\n"];
        yield return ["scalar-undefined-guard", "function pack(prefix:number,...values:number[]):number[]{return values}\nfunction source():number{return undefined as any}const result:any=pack(0,source(),4);console.log(result.length,result[0]===undefined,result[1]);", "2 true 4\n"];
        yield return ["spread-return-undefined", "function pack(prefix:number,...values:number[]):number[]{return values}\nfunction source():number[]{return undefined as any}let called=0;try{pack(0,...source(),++called)}catch(error){console.log(error instanceof TypeError,called)}", "true 0\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ExpansionPreservesActualValuesAndAbruptOrder(string name, string source, string expected, ExecutionMode mode)
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
