using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UpdateToNumericTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const abortMarker=new Error('stop');const obj:any={count:{valueOf(){throw abortMarker;}}};try{obj.count++;console.log('returned');}catch(error){console.log(error===abortMarker);}", "true\n"];
        yield return ["indexed-abrupt-get-convert-no-write", "const events:string[]=[],marker={};const obj:any={get count(){events.push('get');return {valueOf(){events.push('convert');throw marker;}};},set count(value:any){events.push('set');}};try{++obj['count'];}catch(error){console.log(error===marker,events.join(','));}", "true get,convert\n"];
        yield return ["number-conversion-once-before-set", "const events:string[]=[];let stored:any={valueOf(){events.push('convert');return '4';}};const obj:any={get count(){events.push('get');return stored;},set count(value:any){events.push('set:'+value);stored=value;}};console.log(obj.count++,stored,events.join(','));", "4 5 get,convert,set:5\n"];
        yield return ["number-hint", "const events:string[]=[];const obj:any={count:{[Symbol.toPrimitive](hint:string){events.push(hint);return '7';},valueOf(){throw 'wrong';}}};console.log(--obj.count,obj.count,events.join(','));", "6 6 number\n"];
        yield return ["bigint-result", "const obj:any={count:{valueOf(){return 4n;}}};const before=obj['count']++;const after=--obj.count;console.log(String(before),typeof before,String(after),typeof after,String(obj.count));", "4 bigint 4 bigint 4\n"];
        yield return ["symbol-result-refuses-before-write", "let writes=0;const obj:any={get count(){return {[Symbol.toPrimitive](){return Symbol('value');}};},set count(value:any){writes++;}};try{obj.count++;}catch(error){console.log(error instanceof TypeError,writes);}", "true 0\n"];
        yield return ["nonprimitive-hooks", "const events:string[]=[];const obj:any={count:{valueOf(){events.push('value');return {};},toString(){events.push('string');return {};}}};try{obj.count++;}catch(error){console.log(error instanceof TypeError,events.join(','),typeof obj.count);}", "true value,string object\n"];
        yield return ["receiver-retained-during-conversion", "const first:any={count:{valueOf(){target=second;return 3;}}},second:any={count:20};let target:any=first;console.log(target.count++,first.count,second.count,target===second);", "3 4 20 true\n"];
        yield return ["key-conversion-get-and-put", "const events:string[]=[];let calls=0;const obj:any={a:{valueOf(){events.push('value');return 3;}},b:20},key:any={[Symbol.toPrimitive](hint:string){events.push('key:'+hint);return ++calls===1?'a':'b';}};console.log(obj[key]++,typeof obj.a,obj.b,events.join(','));", "3 object 4 key:string,value,key:string\n"];
        yield return ["actual-async-receiver", "async function main(){const events:string[]=[],obj:any={count:{valueOf(){events.push('convert');return 3;}}};async function target(){await new Promise<void>(resolve=>setTimeout(resolve,0));events.push('receiver');return obj;}console.log((await target()).count++,obj.count,events.join(','));}main();", "3 4 receiver,convert\n"];
        yield return ["variable-conversion", "let calls=0,value:any={valueOf(){calls++;return '3';}};console.log(value++,value,calls);let big:any={valueOf(){return 7n;}};const before=big++;console.log(String(before),typeof before,String(big));", "3 4 1\n7 bigint 8\n"];
        yield return ["variable-abrupt", "const marker={};let value:any={valueOf(){throw marker;}};try{value++;}catch(error){console.log(error===marker,typeof value);}", "true object\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ConvertsTheCurrentValueBeforeUpdatingTheRetainedReference(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted) Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
