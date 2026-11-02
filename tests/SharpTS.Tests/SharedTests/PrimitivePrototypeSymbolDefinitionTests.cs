using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeSymbolDefinitionTests
{
    // Necessary descriptor-definition prerequisite for #3400; Refs #3077 (P3 remains open).
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-data", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(p[s],d.value,d.writable,d.enumerable,d.configurable,Object.getOwnPropertySymbols(p).includes(s),Reflect.ownKeys(p).includes(s));\n", "7 7 true true true true true\n"];
        yield return ["number-accessor-receiver", "const p:any=Number.prototype,s=Symbol('probe');let gets=0,sets=0,seen:any;const get=function(this:any){gets++;seen=this;return 7},set=function(this:any,v:number){sets=v;seen=this};Object.defineProperty(p,s,{get,set,configurable:true});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(gets,d.get===get,d.set===set);console.log(p[s],seen===p);p[s]=9;console.log(sets,seen===p,gets);\n", "0 true true\n7 true\n9 true 1\n"];
        yield return ["number-partial", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});Object.defineProperty(p,s,{value:8});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(p[s],d.writable,d.enumerable,d.configurable);\n", "8 true true true\n"];
        yield return ["number-symbol-identity", "const p:any=Number.prototype,s=Symbol('probe');const other=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.defineProperty(p,other,{value:9,configurable:true});const keys=Object.getOwnPropertySymbols(p);console.log(p[s],p[other],keys.length,keys[0]===s,keys[1]===other,p[String(s)]);\n", "7 9 2 true true undefined\n"];
        yield return ["number-readonly", "'use strict';const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:false,configurable:false});let write=false,redefine=false;try{p[s]=8}catch(e){write=e instanceof TypeError}try{Object.defineProperty(p,s,{value:8})}catch(e){redefine=e instanceof TypeError}console.log(Reflect.defineProperty(p,s,{value:8}),write,redefine,p[s]);\n", "false true true 7\n"];
        yield return ["number-prevent", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});Object.preventExtensions(p);console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "true false 8 true\n"];
        yield return ["number-seal", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,configurable:true});Object.seal(p);console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,s,{configurable:true}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "true false false 8 false\n"];
        yield return ["number-freeze", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,configurable:true});Object.freeze(p);const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],d.writable,d.configurable);\n", "false false 7 false false\n"];
        yield return ["number-explicit-parent", "const p:any=Number.prototype,s=Symbol('probe');const proto:any={[s]:9},receiver:any={};Object.setPrototypeOf(p,proto);Object.defineProperty(p,s,{value:7,writable:true,configurable:true});console.log(Reflect.get(p,s,receiver));Object.defineProperty(p,s,{get(){return this===receiver},configurable:true});console.log(Reflect.get(p,s,receiver),p[s]);\n", "7\ntrue false\n"];
        yield return ["boolean-data", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(p[s],d.value,d.writable,d.enumerable,d.configurable,Object.getOwnPropertySymbols(p).includes(s),Reflect.ownKeys(p).includes(s));\n", "7 7 true true true true true\n"];
        yield return ["boolean-accessor-receiver", "const p:any=Boolean.prototype,s=Symbol('probe');let gets=0,sets=0,seen:any;const get=function(this:any){gets++;seen=this;return 7},set=function(this:any,v:number){sets=v;seen=this};Object.defineProperty(p,s,{get,set,configurable:true});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(gets,d.get===get,d.set===set);console.log(p[s],seen===p);p[s]=9;console.log(sets,seen===p,gets);\n", "0 true true\n7 true\n9 true 1\n"];
        yield return ["boolean-partial", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});Object.defineProperty(p,s,{value:8});const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(p[s],d.writable,d.enumerable,d.configurable);\n", "8 true true true\n"];
        yield return ["boolean-symbol-identity", "const p:any=Boolean.prototype,s=Symbol('probe');const other=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.defineProperty(p,other,{value:9,configurable:true});const keys=Object.getOwnPropertySymbols(p);console.log(p[s],p[other],keys.length,keys[0]===s,keys[1]===other,p[String(s)]);\n", "7 9 2 true true undefined\n"];
        yield return ["boolean-readonly", "'use strict';const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:false,configurable:false});let write=false,redefine=false;try{p[s]=8}catch(e){write=e instanceof TypeError}try{Object.defineProperty(p,s,{value:8})}catch(e){redefine=e instanceof TypeError}console.log(Reflect.defineProperty(p,s,{value:8}),write,redefine,p[s]);\n", "false true true 7\n"];
        yield return ["boolean-prevent", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});Object.preventExtensions(p);console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "true false 8 true\n"];
        yield return ["boolean-seal", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,configurable:true});Object.seal(p);console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,s,{configurable:true}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "true false false 8 false\n"];
        yield return ["boolean-freeze", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,configurable:true});Object.freeze(p);const d=Object.getOwnPropertyDescriptor(p,s)!;console.log(Reflect.defineProperty(p,s,{value:8}),Reflect.defineProperty(p,Symbol('new'),{value:9}),p[s],d.writable,d.configurable);\n", "false false 7 false false\n"];
        yield return ["boolean-explicit-parent", "const p:any=Boolean.prototype,s=Symbol('probe');const proto:any={[s]:9},receiver:any={};Object.setPrototypeOf(p,proto);Object.defineProperty(p,s,{value:7,writable:true,configurable:true});console.log(Reflect.get(p,s,receiver));Object.defineProperty(p,s,{get(){return this===receiver},configurable:true});console.log(Reflect.get(p,s,receiver),p[s]);\n", "7\ntrue false\n"];
        yield return ["string-defined-symbol", "const p:any=String.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,writable:true,enumerable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(p,s);console.log(p[s],d?.value,Object.getOwnPropertySymbols(p).includes(s),Reflect.ownKeys(p).includes(s));\n", "7 7 true true\n"];
        yield return ["number-string-alias", "const p:any=Number.prototype,s=Symbol('probe'),text=String(s);Object.defineProperty(p,s,{value:7,writable:true,configurable:true});console.log(p[s],p[text],Object.getOwnPropertyDescriptor(p,text)?.value,Object.getOwnPropertyDescriptor(p,s)?.value);\n", "7 undefined undefined 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void DefinedSymbolUsesActualOwnedStorageAndDescriptor(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
