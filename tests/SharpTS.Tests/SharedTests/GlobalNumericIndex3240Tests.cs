using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalNumericIndex3240Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const target:any=globalThis;target[2]=7;console.log(target[2]);delete target[2];\n", "7\n"];
        yield return ["negative-zero-fractional-negative", "const t:any=globalThis;t[-0]=1;t[1.5]=2;t[-2]=3;console.log(t[0],t['0'],t['1.5'],t[-2]);delete t[0];delete t[1.5];delete t[-2];", "1 1 2 3\n"];
        yield return ["compound-prefix-postfix", "const t:any=globalThis;t[2]=7;console.log(t[2]++,++t[2],t[2]+=3,t[2]);delete t[2];", "7 9 12 12\n"];
        yield return ["logical-update", "const t:any=globalThis;t[1.5]=0;console.log((t[1.5]||=7),(t[1.5]&&=9),(t[1.5]??=11),t[1.5]);delete t[1.5];", "7 9 9 9\n"];
        yield return ["accessor-receiver-and-postfix", "const t:any=globalThis;const events:string[]=[];let value=7;Object.defineProperty(t,'2',{get(){events.push('get:'+String(this===t));return value;},set(next:number){events.push('set:'+String(this===t)+':'+next);value=next;},configurable:true});console.log(t[2]++,t[2]);console.log(events.join(','));delete t[2];", "7 8\nget:true,set:true:8,get:true\n"];
        yield return ["getter-abrupt-before-rhs", "const t:any=globalThis;const events:string[]=[];const marker={};Object.defineProperty(t,'2',{get(){events.push('get');throw marker;},set(value:number){events.push('set');},configurable:true});function rhs(){events.push('rhs');return 8;}try{t[2]+=rhs();}catch(error){console.log(error===marker,events.join(','));}delete t[2];", "true get\n"];
        yield return ["setter-abrupt-after-rhs", "const t:any=globalThis;const events:string[]=[];const marker={};Object.defineProperty(t,'2',{get(){events.push('get');return 7;},set(value:number){events.push('set:'+value);throw marker;},configurable:true});function rhs(){events.push('rhs');return 8;}try{t[2]+=rhs();}catch(error){console.log(error===marker,events.join(','));}delete t[2];", "true get,rhs,set:15\n"];
        yield return ["readonly-sloppy", "const t:any=globalThis;Object.defineProperty(t,'2',{value:7,writable:false,configurable:true});console.log(t[2]=8,t[2]++,t[2]);delete t[2];", "8 7 7\n"];
        yield return ["readonly-strict", "'use strict';const t:any=globalThis;Object.defineProperty(t,'2',{value:7,writable:false,configurable:true});try{t[2]++;}catch(error){console.log(error instanceof TypeError);}console.log(t[2]);delete t[2];", "true\n7\n"];
        yield return ["getter-only-sloppy-and-strict", "const t:any=globalThis;Object.defineProperty(t,'2',{get(){return 7;},configurable:true});t[2]=8;function assign(){'use strict';t[2]=9;}try{assign();}catch(error){console.log(error instanceof TypeError);}console.log(t[2]);delete t[2];", "true\n7\n"];
        yield return ["target-key-rhs-once", "const t:any=globalThis;const events:string[]=[];t[0]=7;function target(){events.push('target');return t;}function key(){events.push('key');return -0;}function rhs(){events.push('rhs');return 3;}console.log(target()[key()]+=rhs(),events.join(','));delete t[0];", "10 target,key,rhs\n"];
        yield return ["string-accessor-control", "const t:any=globalThis;let value=7;Object.defineProperty(t,'__p2_numeric_control__',{get(){return value;},set(next:number){value=next;},configurable:true});console.log(t['__p2_numeric_control__']);t['__p2_numeric_control__']=8;console.log(t['__p2_numeric_control__']);delete t.__p2_numeric_control__;", "7\n8\n"];
        yield return ["negative-zero-descriptor", "const t:any=globalThis;Object.defineProperty(t,-0,{value:7,writable:true,configurable:true});console.log(t[-0],t['0']);t[-0]+=1;console.log(t[0]);delete t[0];", "7 7\n8\n"];
        yield return ["nonfinite-readonly-intrinsics", "const t:any=globalThis;console.log(Number.isNaN(t[NaN]),t[Infinity]===Infinity);t[NaN]=7;t[Infinity]=8;console.log(Number.isNaN(t[NaN]),t[Infinity]===Infinity);", "true true\ntrue true\n"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NumericGlobalKeysUseOrdinaryPropertySemantics(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}