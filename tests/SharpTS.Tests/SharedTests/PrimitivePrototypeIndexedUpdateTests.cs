using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeIndexedUpdateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-postfix-increment", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']++,p['probe']);\n", "1 2\n"];
        yield return ["number-prefix-increment", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(++p['probe'],p['probe']);\n", "2 2\n"];
        yield return ["number-postfix-decrement", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']--,p['probe']);\n", "1 0\n"];
        yield return ["number-prefix-decrement", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(--p['probe'],p['probe']);\n", "0 0\n"];
        yield return ["number-accessor-receiver", "const p:any=Number.prototype;let value=1,gets=0,sets=0,same=false;Object.defineProperty(p,'probe',{get(){gets++;return value},set(v){sets++;same=this===p;value=v},configurable:true});console.log(p['probe']++,value,gets,sets,same);\n", "1 2 1 1 true\n"];
        yield return ["number-sloppy-readonly", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "1\n1\n"];
        yield return ["number-strict-readonly", "'use strict';const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "TypeError\n1\n"];
        yield return ["number-numeric-conversion-once", "const p:any=Number.prototype;let calls=0;const value:any={[Symbol.toPrimitive](hint:string){calls++;return '4'}};Object.defineProperty(p,'probe',{value,writable:true,configurable:true});console.log(p['probe']++,p['probe'],calls);\n", "4 5 1\n"];
        yield return ["string-postfix-increment", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']++,p['probe']);\n", "1 2\n"];
        yield return ["string-prefix-increment", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(++p['probe'],p['probe']);\n", "2 2\n"];
        yield return ["string-postfix-decrement", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']--,p['probe']);\n", "1 0\n"];
        yield return ["string-prefix-decrement", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(--p['probe'],p['probe']);\n", "0 0\n"];
        yield return ["string-accessor-receiver", "const p:any=String.prototype;let value=1,gets=0,sets=0,same=false;Object.defineProperty(p,'probe',{get(){gets++;return value},set(v){sets++;same=this===p;value=v},configurable:true});console.log(p['probe']++,value,gets,sets,same);\n", "1 2 1 1 true\n"];
        yield return ["string-sloppy-readonly", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "1\n1\n"];
        yield return ["string-strict-readonly", "'use strict';const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "TypeError\n1\n"];
        yield return ["string-numeric-conversion-once", "const p:any=String.prototype;let calls=0;const value:any={[Symbol.toPrimitive](hint:string){calls++;return '4'}};Object.defineProperty(p,'probe',{value,writable:true,configurable:true});console.log(p['probe']++,p['probe'],calls);\n", "4 5 1\n"];
        yield return ["boolean-postfix-increment", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']++,p['probe']);\n", "1 2\n"];
        yield return ["boolean-prefix-increment", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(++p['probe'],p['probe']);\n", "2 2\n"];
        yield return ["boolean-postfix-decrement", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(p['probe']--,p['probe']);\n", "1 0\n"];
        yield return ["boolean-prefix-decrement", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});console.log(--p['probe'],p['probe']);\n", "0 0\n"];
        yield return ["boolean-accessor-receiver", "const p:any=Boolean.prototype;let value=1,gets=0,sets=0,same=false;Object.defineProperty(p,'probe',{get(){gets++;return value},set(v){sets++;same=this===p;value=v},configurable:true});console.log(p['probe']++,value,gets,sets,same);\n", "1 2 1 1 true\n"];
        yield return ["boolean-sloppy-readonly", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "1\n1\n"];
        yield return ["boolean-strict-readonly", "'use strict';const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:false,configurable:true});try{console.log(p['probe']++)}catch(e:any){console.log(e.name)}console.log(p['probe']);\n", "TypeError\n1\n"];
        yield return ["boolean-numeric-conversion-once", "const p:any=Boolean.prototype;let calls=0;const value:any={[Symbol.toPrimitive](hint:string){calls++;return '4'}};Object.defineProperty(p,'probe',{value,writable:true,configurable:true});console.log(p['probe']++,p['probe'],calls);\n", "4 5 1\n"];
        yield return ["throwing-setter", "const p:any=Number.prototype,marker={};let gets=0,sets=0;Object.defineProperty(p,'probe',{get(){gets++;return 1},set(v){sets++;throw marker},configurable:true});try{++p['probe']}catch(e){console.log(e===marker,gets,sets)}\n", "true 1 1\n"];
        yield return ["throwing-conversion", "const p:any=Number.prototype,marker={};let calls=0;const value:any={[Symbol.toPrimitive](){calls++;throw marker}};Object.defineProperty(p,'probe',{value,writable:true,configurable:true});try{p['probe']++}catch(e){console.log(e===marker,p['probe']===value,calls)}\n", "true true 1\n"];
        yield return ["inherited-accessor", "const p:any=Number.prototype;let value=1,gets=0,sets=0,same=false;Object.defineProperty(Object.prototype,'probe',{get(){gets++;return value},set(v){sets++;same=this===p;value=v},configurable:true});console.log(p['probe']++,value,gets,sets,same,Object.hasOwn(p,'probe'));\n", "1 2 1 1 true false\n"];
        yield return ["nonextensible-existing", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});Object.preventExtensions(p);console.log(p['probe']++,p['probe']);\n", "1 2\n"];
        yield return ["explicit-parent", "const p:any=Boolean.prototype,base:any={};let value=1,same=false;Object.defineProperty(base,'probe',{get(){return value},set(v){same=this===p;value=v}});Object.setPrototypeOf(p,base);console.log(p['probe']++,value,same,Object.hasOwn(p,'probe'));\n", "1 2 true false\n"];
        yield return ["async-reference-once", "async function run(){const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});let bases=0,keys=0;const target=()=>{bases++;return p};const key=()=>{keys++;return 'probe'};console.log((await Promise.resolve(target()))[await Promise.resolve(key())]++,p['probe'],bases,keys)}void run();\n", "1 2 1 1\n"];
        yield return ["strict-getter-only", "'use strict';const p:any=String.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 1},configurable:true});try{p['probe']++}catch(e:any){console.log(e.name,gets)}\n", "TypeError 1\n"];
        yield return ["sloppy-getter-only", "const p:any=String.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 1},configurable:true});console.log(++p['probe'],gets,Object.getOwnPropertyDescriptor(p,'probe')!.set===undefined);\n", "2 1 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void IndexedUpdatesUseCurrentDescriptorsAndReferenceValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
