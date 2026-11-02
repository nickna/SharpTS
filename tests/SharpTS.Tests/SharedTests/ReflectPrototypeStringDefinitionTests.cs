using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectPrototypeStringDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-fresh", "const p:any=String.prototype;const ok=Reflect.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d!==undefined,d?.value,d?.writable,d?.enumerable,d?.configurable);", "true true 7 true true true\n"];
        yield return ["string-existing-physical", "const p:any=String.prototype;Object.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const ok=Reflect.defineProperty(p,\"probe\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d.value,d.writable,d.enumerable,d.configurable);", "true 7 false true true\n"];
        yield return ["string-nonextensible-refusal", "const p:any=String.prototype;Object.preventExtensions(p);const ok=Reflect.defineProperty(p,\"probe\",{value:7});console.log(ok,Object.getOwnPropertyDescriptor(p,\"probe\")===undefined,Object.isExtensible(p));", "false true false\n"];
        yield return ["number-fresh", "const p:any=Number.prototype;const ok=Reflect.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d!==undefined,d?.value,d?.writable,d?.enumerable,d?.configurable);", "true true 7 true true true\n"];
        yield return ["number-existing-physical", "const p:any=Number.prototype;Object.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const ok=Reflect.defineProperty(p,\"probe\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d.value,d.writable,d.enumerable,d.configurable);", "true 7 false true true\n"];
        yield return ["number-nonextensible-refusal", "const p:any=Number.prototype;Object.preventExtensions(p);const ok=Reflect.defineProperty(p,\"probe\",{value:7});console.log(ok,Object.getOwnPropertyDescriptor(p,\"probe\")===undefined,Object.isExtensible(p));", "false true false\n"];
        yield return ["boolean-fresh", "const p:any=Boolean.prototype;const ok=Reflect.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d!==undefined,d?.value,d?.writable,d?.enumerable,d?.configurable);", "true true 7 true true true\n"];
        yield return ["boolean-existing-physical", "const p:any=Boolean.prototype;Object.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const ok=Reflect.defineProperty(p,\"probe\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d.value,d.writable,d.enumerable,d.configurable);", "true 7 false true true\n"];
        yield return ["boolean-nonextensible-refusal", "const p:any=Boolean.prototype;Object.preventExtensions(p);const ok=Reflect.defineProperty(p,\"probe\",{value:7});console.log(ok,Object.getOwnPropertyDescriptor(p,\"probe\")===undefined,Object.isExtensible(p));", "false true false\n"];
        yield return ["array-fresh", "const p:any=Array.prototype;const ok=Reflect.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d!==undefined,d?.value,d?.writable,d?.enumerable,d?.configurable);", "true true 7 true true true\n"];
        yield return ["array-existing-physical", "const p:any=Array.prototype;Object.defineProperty(p,\"probe\",{value:7,writable:true,enumerable:true,configurable:true});const ok=Reflect.defineProperty(p,\"probe\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,d.value,d.writable,d.enumerable,d.configurable);", "true 7 false true true\n"];
        yield return ["array-nonextensible-refusal", "const p:any=Array.prototype;Object.preventExtensions(p);const ok=Reflect.defineProperty(p,\"probe\",{value:7});console.log(ok,Object.getOwnPropertyDescriptor(p,\"probe\")===undefined,Object.isExtensible(p));", "false true false\n"];
        yield return ["array-key-descriptor-once", "const p:any=Array.prototype;const events:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){events.push(\"key:\"+hint);return \"probe\"}};const desc:any={get value(){events.push(\"value\");return 9},get configurable(){events.push(\"config\");return true}};const ok=Reflect.defineProperty(p,key,desc);console.log(ok,events.join(\",\"),Object.getOwnPropertyDescriptor(p,\"probe\")?.value);", "true key:string,config,value 9\n"];
        yield return ["number-descriptor-abrupt", "const p:any=Number.prototype,marker={};let calls=0;const desc:any={get value(){calls++;throw marker}};try{Reflect.defineProperty(p,\"probe\",desc)}catch(error){console.log(error===marker,calls,Object.getOwnPropertyDescriptor(p,\"probe\")===undefined)}", "true 1 true\n"];
        yield return ["root-positive-control", "const p:any=Object.prototype;console.log(Reflect.defineProperty(p,\"probe\",{value:7,configurable:true}),Object.getOwnPropertyDescriptor(p,\"probe\")?.value);", "true 7\n"];
        yield return ["string-accessor-no-eager-read", "const p:any=String.prototype;let calls=0;const get=()=>{calls++;return 7};const ok=Reflect.defineProperty(p,\"probe\",{get,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,calls,d.get===get,d.set===undefined,p.probe,calls);", "true 0 true true 7 1\n"];
        yield return ["number-accessor-no-eager-read", "const p:any=Number.prototype;let calls=0;const get=()=>{calls++;return 7};const ok=Reflect.defineProperty(p,\"probe\",{get,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,calls,d.get===get,d.set===undefined,p.probe,calls);", "true 0 true true 7 1\n"];
        yield return ["boolean-accessor-no-eager-read", "const p:any=Boolean.prototype;let calls=0;const get=()=>{calls++;return 7};const ok=Reflect.defineProperty(p,\"probe\",{get,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,calls,d.get===get,d.set===undefined,p.probe,calls);", "true 0 true true 7 1\n"];
        yield return ["array-accessor-no-eager-read", "const p:any=Array.prototype;let calls=0;const get=()=>{calls++;return 7};const ok=Reflect.defineProperty(p,\"probe\",{get,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(ok,calls,d.get===get,d.set===undefined,p.probe,calls);", "true 0 true true 7 1\n"];
        yield return ["physical-incompatible-refusal", "const p:any=Number.prototype;Object.defineProperty(p,\"probe\",{value:7});console.log(Reflect.defineProperty(p,\"probe\",{value:9}),p.probe);", "false 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectDefinitionsUseActualPrototypeStores(string name, string source, string expected, ExecutionMode mode)
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
