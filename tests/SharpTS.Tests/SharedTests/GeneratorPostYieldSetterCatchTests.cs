using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorPostYieldSetterCatchTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const target:any={set x(value){throw new TypeError('x');}};function* run(){try{target.x=yield 9;}catch(e){console.log(e instanceof TypeError);}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n"];
        yield return ["typed-original", "const target:any={set x(value){throw new TypeError('x');}};function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e instanceof TypeError);}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n"];
        yield return ["index-setter-marker", "const marker={};const target:any={set x(value:number){throw marker;}};function* run():Generator<number,void,number>{try{target['x']=yield 9;}catch(e){console.log(e===marker);}console.log('after');}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\nafter\n"];
        yield return ["null-dot-base", "const target:any=null;function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e instanceof TypeError);}console.log('after');}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\nafter\n"];
        yield return ["undefined-index-base", "const target:any=undefined;function* run():Generator<number,void,number>{try{target['x']=yield 9;}catch(e){console.log(e instanceof TypeError);}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n"];
        yield return ["target-key-once", "const events:string[]=[];const marker={};const value:any={set x(n:number){events.push('set:'+n);throw marker;}};function target(){events.push('target');return value;}function key(){events.push('key');return 'x';}function* run():Generator<number,void,number>{try{target()[key()]=yield 9;events.push('unreachable');}catch(e){events.push('catch:'+String(e===marker));}}const it=run();console.log(it.next().value,events.join(','));events.push('resume');it.next(8);console.log(events.join(','));", "9 target,key\ntarget,key,resume,set:8,catch:true\n"];
        yield return ["null-payload", "const target:any={set x(n:number){throw null;}};function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e===null);}finally{console.log('finally');}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\nfinally\n"];
        yield return ["undefined-payload", "const target:any={set x(n:number){throw undefined;}};function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e===undefined);}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n"];
        yield return ["nested-catch-finally", "const marker={};const target:any={set x(n:number){throw marker;}};function* run():Generator<number,void,number>{try{try{target.x=yield 9;}catch(e){console.log('inner',e===marker);throw e;}finally{console.log('finally');}}catch(e){console.log('outer',e===marker);}}const it=run();console.log(it.next().value);it.next(8);", "9\ninner true\nfinally\nouter true\n"];
        yield return ["successful-result", "const target:any={x:0};function* run():Generator<number,void,number>{try{console.log(target.x=yield 9,target.x);}catch(e){console.log('bad');}}const it=run();console.log(it.next().value);it.next(8);", "9\n8 8\n"];
        yield return ["throw-injected-before-store", "let writes=0;const marker={};const target:any={set x(n:number){writes++;}};function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e===marker,writes);}}const it=run();console.log(it.next().value);it.throw(marker);", "9\ntrue 0\n"];
        yield return ["ordinary-call-after-yield", "function fail(){throw new TypeError('x');}function* run():Generator<number,void,number>{try{yield 9;fail();}catch(e){console.log(e instanceof TypeError);}}const it=run();it.next();it.next(8);", "true\n"];
        yield return ["synchronous-store-control", "const target:any={set x(n:number){throw new TypeError('x');}};function* run():Generator<number,void,number>{try{target.x=8;}catch(e){console.log(e instanceof TypeError);}yield 9;}const it=run();console.log(it.next().value);", "true\n9\n"];
        yield return ["strict-typed-original", "const target:any={set x(value:any){throw new TypeError('x');}};function* run():Generator<number,void,number>{try{target.x=yield 9;}catch(e){console.log(e instanceof TypeError);}}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n"];
        yield return ["direct-global-setter", "declare var __p2_store__:any;const marker={};Object.defineProperty(globalThis,'__p2_store__',{set(value:number){throw marker;},configurable:true});function* run():Generator<number,void,number>{try{globalThis.__p2_store__=yield 9;}catch(e){console.log(e===marker);}}const it=run();console.log(it.next().value);it.next(8);delete globalThis.__p2_store__;", "9\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ResumedPropertyStoreRoutesToGuestCatch(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterPreservesTheSamePropertyStoreContract(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
