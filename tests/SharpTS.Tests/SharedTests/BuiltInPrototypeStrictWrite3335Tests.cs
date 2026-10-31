using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BuiltInPrototypeStrictWrite3335Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-strict", "const p:any=String.prototype;Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true false false\n"];
        yield return ["string-sloppy", "const p:any=String.prototype;Object.preventExtensions(p);let caught=false;try{(function(){p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "false false false\n"];
        yield return ["number-strict", "const p:any=Number.prototype;Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true false false\n"];
        yield return ["number-sloppy", "const p:any=Number.prototype;Object.preventExtensions(p);let caught=false;try{(function(){p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "false false false\n"];
        yield return ["boolean-strict", "const p:any=Boolean.prototype;Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true false false\n"];
        yield return ["boolean-sloppy", "const p:any=Boolean.prototype;Object.preventExtensions(p);let caught=false;try{(function(){p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "false false false\n"];
        yield return ["array-strict", "const p:any=Array.prototype;Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true false false\n"];
        yield return ["array-sloppy", "const p:any=Array.prototype;Object.preventExtensions(p);let caught=false;try{(function(){p.fresh=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "false false false\n"];
        yield return ["string-strict-bracket", "const p:any=String.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
        yield return ["array-strict-bracket", "const p:any=Array.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
        yield return ["number-strict-compound", "const p:any=Number.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]+=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
        yield return ["boolean-strict-compound", "const p:any=Boolean.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]+=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
        yield return ["array-strict-update", "const p:any=Array.prototype;Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.fresh++})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true false false\n"];
        yield return ["string-existing-writable-data", "const p:any=String.prototype;Object.defineProperty(p,'owned',{value:1,writable:true,configurable:true});Object.preventExtensions(p);(function(){'use strict';p.owned=7})();console.log(p.owned,Object.getOwnPropertyDescriptor(p,'owned')!.value,Object.isExtensible(p));", "7 7 false\n"];
        yield return ["boolean-existing-own-setter", "const p:any=Boolean.prototype;let calls=0,same=false,seen=0;Object.defineProperty(p,'setOnly',{set(v:number){calls++;same=this===p;seen=v},configurable:true});Object.preventExtensions(p);(function(){'use strict';p.setOnly=7})();console.log(calls,same,seen,Object.prototype.hasOwnProperty.call(p,'setOnly'));", "1 true 7 true\n"];
        yield return ["array-existing-inherited-setter", "const p:any=Array.prototype;let calls=0,same=false,seen=0;const key='receiver3335';Object.defineProperty(Object.prototype,key,{set(v:number){calls++;same=this===p;seen=v},configurable:true});try{Object.preventExtensions(p);(function(){'use strict';p[key]=7})();console.log(calls,same,seen,Object.prototype.hasOwnProperty.call(p,key))}finally{delete (Object.prototype as any)[key]}", "1 true 7 false\n"];
        yield return ["number-getter-only-rejection-order", "const p:any=Number.prototype;let gets=0;const log:string[]=[];Object.defineProperty(p,'blocked',{get(){gets++;return 1},configurable:true});Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.blocked=(log.push('rhs'),7)})()}catch(e){caught=e instanceof TypeError}console.log(log.join(','),caught,gets);", "rhs true 0\n"];
        yield return ["string-readonly-data-rejection-order", "const p:any=String.prototype;const log:string[]=[];Object.defineProperty(p,'locked',{value:1,writable:false,configurable:false});Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p.locked=(log.push('rhs'),7)})()}catch(e){caught=e instanceof TypeError}console.log(log.join(','),caught,p.locked);", "rhs true 1\n"];
        yield return ["string-existing-symbol-path-control", "const p:any=String.prototype;const key=Symbol('owned');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});Object.preventExtensions(p);(function(){'use strict';p[key]=7})();console.log(p[key],Object.getOwnPropertyDescriptor(p,key)!.value,Object.isExtensible(p));", "7 7 false\n"];
        yield return ["number-strict-logical-index", "const p:any=Number.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]??=7})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
        yield return ["array-strict-index-update", "const p:any=Array.prototype;const key:string='fresh';Object.preventExtensions(p);let caught=false;try{(function(){'use strict';p[key]++})()}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,key),Object.isExtensible(p));", "true false false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NonextensibleWritesUseTheActualOrdinarySetResult(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
