using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GlobalStrictWrite3241Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["readonly-strict", "'use strict';const t:any=globalThis;Object.defineProperty(t,'2',{value:7,writable:false,configurable:true});try{t[2]++;}catch(error){console.log(error instanceof TypeError);}console.log(t[2]);delete t[2];", "true\n7\n"];
        yield return ["getter-only-sloppy-and-strict", "const t:any=globalThis;Object.defineProperty(t,'2',{get(){return 7;},configurable:true});t[2]=8;function assign(){'use strict';t[2]=9;}try{assign();}catch(error){console.log(error instanceof TypeError);}console.log(t[2]);delete t[2];", "true\n7\n"];
        yield return ["nonfinite-readonly-intrinsics", "const t:any=globalThis;console.log(Number.isNaN(t[NaN]),t[Infinity]===Infinity);t[NaN]=7;t[Infinity]=8;console.log(Number.isNaN(t[NaN]),t[Infinity]===Infinity);", "true true\ntrue true\n"];
        yield return ["direct-named-strict", "'use strict';declare var __p2_readonly__:any;Object.defineProperty(globalThis,'__p2_readonly__',{value:7,writable:false,configurable:true});try{globalThis.__p2_readonly__=8;}catch(e){console.log(e instanceof TypeError);}console.log(globalThis.__p2_readonly__);", "true\n7\n"];
        yield return ["direct-index-strict", "'use strict';declare var __p2_readonly__:any;Object.defineProperty(globalThis,'__p2_readonly__',{value:7,writable:false,configurable:true});try{globalThis['__p2_readonly__']=8;}catch(e){console.log(e instanceof TypeError);}console.log(globalThis['__p2_readonly__']);", "true\n7\n"];
        yield return ["sloppy-refusal", "const g:any=globalThis;Object.defineProperty(g,'__p2_readonly__',{value:7,writable:false,configurable:true});console.log(g.__p2_readonly__=8,g.__p2_readonly__);", "8 7\n"];
        yield return ["setter-once-receiver", "'use strict';const g:any=globalThis;let calls=0;Object.defineProperty(g,'__p2_setter__',{set(value){calls++;console.log(this===g,value);},configurable:true});console.log(g.__p2_setter__=9,calls);", "true 9\n9 1\n"];
        yield return ["setter-abrupt-identity", "'use strict';const g:any=globalThis;const marker={};let calls=0;Object.defineProperty(g,'__p2_setter__',{set(value){calls++;throw marker;},configurable:true});try{g.__p2_setter__=9;}catch(e){console.log(e===marker,calls);}", "true 1\n"];
        yield return ["undefined-accessor", "'use strict';const g:any=globalThis;Object.defineProperty(g,'__p2_getter__',{get:undefined,set:undefined,configurable:true});try{g.__p2_getter__=9;}catch(e){console.log(e instanceof TypeError);}console.log(g.__p2_getter__===undefined);", "true\ntrue\n"];
        yield return ["constants-strict", "'use strict';const g:any=globalThis;for(const key of ['NaN','Infinity','undefined']){try{g[key]=8;}catch(e){console.log(e instanceof TypeError);}}console.log(Number.isNaN(g.NaN),g.Infinity===Infinity,g.undefined===undefined);", "true\ntrue\ntrue\ntrue true true\n"];
        yield return ["reference-rhs-order", "'use strict';const g:any=globalThis;const order:string[]=[];Object.defineProperty(g,'__p2_readonly__',{value:7,writable:false,configurable:true});function target(){order.push('target');return g;}function key(){order.push('key');return '__p2_readonly__';}function value(){order.push('value');return 9;}try{target()[key()]=value();}catch(e){console.log(e instanceof TypeError,order.join(','));}", "true target,key,value\n"];
        yield return ["async-direct-strict", "'use strict';declare var __p2_readonly__:any;Object.defineProperty(globalThis,'__p2_readonly__',{value:7,writable:false,configurable:true});async function run(){try{globalThis['__p2_readonly__']=await Promise.resolve(9);}catch(e){console.log(e instanceof TypeError);}console.log(globalThis['__p2_readonly__']);}run();", "true\n7\n"];
        yield return ["generator-direct-strict", "'use strict';declare var __p2_readonly__:any;Object.defineProperty(globalThis,'__p2_readonly__',{value:7,writable:false,configurable:true});function* run():Generator<number,void,number>{try{globalThis.__p2_readonly__=yield 9;}catch(e){console.log(e instanceof TypeError);}console.log(globalThis.__p2_readonly__);}const it=run();console.log(it.next().value);it.next(8);", "9\ntrue\n7\n"];
        yield return ["ordinary-object-strict-control", "'use strict';const g:any={};Object.defineProperty(g,'x',{value:7});try{g.x=9;}catch(e){console.log(e instanceof TypeError);}console.log(g.x);", "true\n7\n"];
        yield return ["classic-script-var", "var __p2_classic__=15;const g:any=globalThis;console.log(g.__p2_classic__,Object.keys(g).includes('__p2_classic__'));", "15 true\n"];
        yield return ["classic-script-bare", "declare var __p2_bare__:any;__p2_bare__=7;const g:any=globalThis;console.log(g.__p2_bare__,Object.keys(g).includes('__p2_bare__'));", "7 true\n"];
        yield return ["nonextensible-strict", "'use strict';const g:any=globalThis;Object.preventExtensions(g);try{g.__p2_new__=7;}catch(e){console.log(e instanceof TypeError);}console.log(Object.getOwnPropertyDescriptor(g,'__p2_new__')===undefined);", "true\ntrue\n"];
        yield return ["nonextensible-sloppy", "const g:any=globalThis;Object.preventExtensions(g);console.log(g.__p2_new__=7,Object.getOwnPropertyDescriptor(g,'__p2_new__')===undefined);", "7 true\n"];
        yield return ["nonextensible-existing-setter", "'use strict';const g:any=globalThis;let calls=0;Object.defineProperty(g,'__p2_setter__',{set(value){calls++;console.log(this===g,value);},configurable:true});Object.preventExtensions(g);g.__p2_setter__=9;console.log(calls);", "true 9\n1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PreservesGlobalAssignmentStrictness(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
