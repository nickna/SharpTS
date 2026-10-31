using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.CompilerTests;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalComputedKey3246Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var row in GlobalComputedKey3245Tests.Cases()) yield return row;
        yield return ["object-key-read", "const g:any=globalThis;g.__p2_key__=7;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_key__';}};console.log(g[key],log.join(','));", "7 string\n"];
        yield return ["ordinary-string-hint-fallback", "const g:any=globalThis;const log:string[]=[];const key:any={toString(){log.push('toString');return '__p2_key__';},valueOf(){log.push('valueOf');return 7;}};g[key]=8;console.log(g[key],log.join(','));", "8 toString,toString\n"];
        yield return ["accessor-receiver", "const g:any=globalThis;let value=7;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_key__';}};Object.defineProperty(g,'__p2_key__',{get(){log.push('get:'+String(this===g));return value;},set(next:number){log.push('set:'+String(this===g));value=next;},configurable:true});g[key]=8;console.log(g[key],log.join(','));", "8 string,set:true,string,get:true\n"];
        yield return ["compound-converts-at-get-and-put", "const g:any=globalThis;g.__p2_first__=7;g.__p2_second__=100;const log:string[]=[];let count=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++count===1?'__p2_first__':'__p2_second__';}};console.log(g[key]+=3,g.__p2_first__,g.__p2_second__,log.join(','));", "10 7 10 string,string\n"];
        yield return ["postfix-converts-at-get-and-put", "const g:any=globalThis;g.__p2_first__=7;g.__p2_second__=100;const log:string[]=[];let count=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++count===1?'__p2_first__':'__p2_second__';}};console.log(g[key]++,g.__p2_first__,g.__p2_second__,log.join(','));", "7 7 8 string,string\n"];
        yield return ["logical-write-converts-at-get-and-put", "const g:any=globalThis;g.__p2_first__=0;g.__p2_second__=100;const log:string[]=[];let count=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++count===1?'__p2_first__':'__p2_second__';}};console.log(g[key]||=8,g.__p2_first__,g.__p2_second__,log.join(','));", "8 0 8 string,string\n"];
        yield return ["logical-skip-converts-once", "const g:any=globalThis;g.__p2_first__=7;g.__p2_second__=100;const log:string[]=[];let count=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++count===1?'__p2_first__':'__p2_second__';}};console.log(g[key]||=8,g.__p2_first__,g.__p2_second__,log.join(','));", "7 7 100 string\n"];
        yield return ["getter-only-strict", "const g:any=globalThis;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_key__';}};Object.defineProperty(g,'__p2_key__',{get(){return 7;},configurable:true});function write(){'use strict';g[key]=8;}try{write();}catch(error){console.log(error instanceof TypeError);}console.log(g.__p2_key__,log.join(','));", "true\n7 string\n"];
        yield return ["async-compound-retains-raw-key", "const g:any=globalThis;g.__p2_first__=7;g.__p2_second__=100;const log:string[]=[];let count=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++count===1?'__p2_first__':'__p2_second__';}};async function rhs(){log.push('rhs');return 3;}async function run(){console.log(g[key]+=await rhs(),g.__p2_first__,g.__p2_second__,log.join(','));}run();", "10 7 10 string,rhs,string\n"];
        yield return ["other-primitive-string-keys", "const g:any=globalThis;g[true as any]=7;g[null as any]=8;g[1n as any]=9;console.log(g['true'],g['null'],g[1]);", "7 8 9\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterConvertsGlobalKeysAtEachActualAccess(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
