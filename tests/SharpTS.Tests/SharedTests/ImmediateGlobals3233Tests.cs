using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ImmediateGlobals3233Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-six-values", "console.log(typeof setTimeout,typeof clearTimeout,typeof setInterval,typeof clearInterval,typeof setImmediate,typeof clearImmediate);", "function function function function function function\n"];
        yield return ["original-four-own-slot-coherence", "const g:any=globalThis;console.log(setTimeout===g.setTimeout,clearTimeout===g.clearTimeout);const d:any=Object.getOwnPropertyDescriptor(g,\"setTimeout\");console.log(d.value===setTimeout,d.writable,d.enumerable,d.configurable);", "true true\ntrue true true true\n"];
        yield return ["six-own-data-slots", "const names=['setTimeout','clearTimeout','setInterval','clearInterval','setImmediate','clearImmediate'];const g:any=globalThis;console.log(names.every(name=>typeof g[name]==='function'),names.every(name=>Object.prototype.hasOwnProperty.call(g,name)));console.log(names.every(name=>{const d=Object.getOwnPropertyDescriptor(g,name);return !!d&&d.value===g[name]&&d.writable&&d.enumerable&&d.configurable;}));console.log(names.every(name=>Object.keys(g).includes(name)));", "true true\ntrue\ntrue\n"];
        yield return ["immediate-arguments", "setImmediate((first:any,second:any)=>console.log(first,second),7,9);console.log('done');", "done\n7 9\n"];
        yield return ["aliased-immediate", "const start:any=setImmediate;start(()=>console.log(7));console.log('done');", "done\n7\n"];
        yield return ["cancel-immediate", "const handle:any=setImmediate(()=>console.log('unreachable'));clearImmediate(handle);console.log('done');", "done\n"];
        yield return ["holder-immediate", "const holder:any={start:globalThis.setImmediate,stop:globalThis.clearImmediate};holder.start((value:any)=>console.log(value),7);console.log('done');", "done\n7\n"];
        yield return ["clear-optional-and-extra-argument", "let count=0;const clear:any=clearImmediate;console.log(typeof clear(),typeof clear(undefined,++count),count);", "undefined undefined 1\n"];
        yield return ["replacement-and-deletion", "const g:any=globalThis;const original:any=setImmediate;g.setImmediate=()=>7;const changed:any=setImmediate;console.log(changed===g.setImmediate,changed());console.log(delete g.setImmediate,typeof setImmediate);g.setImmediate=original;console.log(setImmediate===original);", "true 7\ntrue undefined\ntrue\n"];
        yield return ["lexical-shadow", "function run(){const setImmediate=(value:number)=>value+1;const clearImmediate=()=>9;console.log(setImmediate(6),clearImmediate());}run();", "7 9\n"];
        yield return ["invalid-callback-argument-evaluation", "let count=0;const start:any=setImmediate;try{start(1,++count);}catch(error){console.log(error instanceof TypeError,count);}", "true 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterExposesActualMutableTimerGlobals(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
