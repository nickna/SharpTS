using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class QueueMicrotaskCallbackTransportTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-returned-callback", "export {};const events:string[]=[];function callback(){events.push('arg');return ()=>events.push('global');}queueMicrotask(callback());events.push('sync');setTimeout(()=>console.log(events.join(',')),0);", "arg,sync,global\n"];
        yield return ["direct-arrow", "export {};const events:string[]=[];queueMicrotask(()=>events.push('job'));events.push('sync');setTimeout(()=>console.log(events.join(',')),0);", "sync,job\n"];
        yield return ["held-alias-returned-callback", "export {};const events:string[]=[];const enqueue=queueMicrotask;function callback(){events.push('arg');return ()=>events.push('job');}console.log(enqueue(callback())===undefined);events.push('sync');setTimeout(()=>console.log(events.join(',')),0);", "true\narg,sync,job\n"];
        yield return ["invalid-values", "export {};for(const callback of [undefined,null,3,false,'text',{}] as any[]){try{queueMicrotask(callback);console.log('miss');}catch(error:any){console.log(error.name);}}console.log('sync');", "TypeError\nTypeError\nTypeError\nTypeError\nTypeError\nTypeError\nsync\n"];
        yield return ["bound-callback", "export {};const receiver={value:7};function callback(this:typeof receiver){console.log(this.value);}queueMicrotask(callback.bind(receiver));console.log('sync');", "sync\n7\n"];
        yield return ["callable-proxy", "export {};let calls=0;const callback=new Proxy(()=>console.log('body'),{apply(target,thisArg,args){calls++;return Reflect.apply(target,thisArg,args);}});queueMicrotask(callback);console.log('sync',calls);setTimeout(()=>console.log('calls',calls),0);", "sync 0\nbody\ncalls 1\n"];
        yield return ["noncallable-proxy", "export {};const callback:any=new Proxy({value:7},{});try{queueMicrotask(callback);console.log('miss');}catch(error:any){console.log(error.name);}console.log('sync');", "TypeError\nsync\n"];
        yield return ["argument-once-closure", "export {};let factories=0;function factory(){factories++;const value=factories;return ()=>console.log('job',value,factories);}queueMicrotask(factory());queueMicrotask(factory());console.log('sync',factories);", "sync 2\njob 1 2\njob 2 2\n"];
        yield return ["async-callback", "export {};const events:string[]=[];function factory(){return async()=>{events.push('first');await Promise.resolve();events.push('second');};}queueMicrotask(factory());events.push('sync');setTimeout(()=>console.log(events.join(',')),0);", "sync,first,second\n"];
        yield return ["generator-callback", "export {};function* callback(){console.log('body');yield 1;}queueMicrotask(callback);console.log('sync');setTimeout(()=>console.log('done'),0);", "sync\ndone\n"];
        yield return ["tuple-spread", "export {};const callbacks:[()=>void]=[()=>console.log('job')];queueMicrotask(...callbacks);console.log('sync');", "sync\njob\n"];
        yield return ["mutated-global-call", "export {};const original=queueMicrotask;const target:any=globalThis;target.queueMicrotask=(callback:any)=>{console.log('replacement');original(callback);};queueMicrotask(()=>console.log('job'));console.log('sync');", "replacement\nsync\njob\n"];
        yield return ["global-getter-before-argument", "export {};const events:string[]=[];const original=queueMicrotask;Object.defineProperty(globalThis,'queueMicrotask',{configurable:true,get(){events.push('get');return original;}});function factory(){events.push('arg');return ()=>events.push('job');}queueMicrotask(factory());events.push('sync');setTimeout(()=>console.log(events.join(',')),0);", "get,arg,sync,job\n"];
        yield return ["extra-argument-before-validation", "export {};const events:string[]=[];function value(){events.push('arg');return 3;}function extra(){events.push('extra');return 9;}try{(queueMicrotask as any)(value(),extra());}catch(error:any){events.push(error.name);}console.log(events.join(','));", "arg,extra,TypeError\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void CallbacksUseTheCanonicalGlobalFunction(string name, string source, string expected, ExecutionMode mode)
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
