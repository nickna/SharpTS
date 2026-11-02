using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BuiltInPrototypePartialDataDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-public-partial", "const p:any=String.prototype,old=p.constructor;Object.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true false false true\n"];
        yield return ["string-reflect-partial", "const p:any=String.prototype,old=p.constructor;const ok=Reflect.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(ok,p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false true\n"];
        yield return ["string-method-partial", "const p:any=String.prototype,old=p.toString;Object.defineProperty(p,\"toString\",{configurable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"toString\");console.log(p.toString===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false\n"];
        yield return ["string-explicit-undefined", "const p:any=String.prototype;Object.defineProperty(p,\"constructor\",{value:undefined});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===undefined,d.value===undefined,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["number-public-partial", "const p:any=Number.prototype,old=p.constructor;Object.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true false false true\n"];
        yield return ["number-reflect-partial", "const p:any=Number.prototype,old=p.constructor;const ok=Reflect.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(ok,p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false true\n"];
        yield return ["number-method-partial", "const p:any=Number.prototype,old=p.toString;Object.defineProperty(p,\"toString\",{configurable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"toString\");console.log(p.toString===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false\n"];
        yield return ["number-explicit-undefined", "const p:any=Number.prototype;Object.defineProperty(p,\"constructor\",{value:undefined});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===undefined,d.value===undefined,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["boolean-public-partial", "const p:any=Boolean.prototype,old=p.constructor;Object.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true false false true\n"];
        yield return ["boolean-reflect-partial", "const p:any=Boolean.prototype,old=p.constructor;const ok=Reflect.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(ok,p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false true\n"];
        yield return ["boolean-method-partial", "const p:any=Boolean.prototype,old=p.toString;Object.defineProperty(p,\"toString\",{configurable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"toString\");console.log(p.toString===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false\n"];
        yield return ["boolean-explicit-undefined", "const p:any=Boolean.prototype;Object.defineProperty(p,\"constructor\",{value:undefined});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===undefined,d.value===undefined,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["array-public-partial", "const p:any=Array.prototype,old=p.constructor;Object.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true false false true\n"];
        yield return ["array-reflect-partial", "const p:any=Array.prototype,old=p.constructor;const ok=Reflect.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(ok,p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false true\n"];
        yield return ["array-method-partial", "const p:any=Array.prototype,old=p.toString;Object.defineProperty(p,\"toString\",{configurable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"toString\");console.log(p.toString===old,d.value===old,d.writable,d.enumerable,d.configurable);", "true true true false false\n"];
        yield return ["array-explicit-undefined", "const p:any=Array.prototype;Object.defineProperty(p,\"constructor\",{value:undefined});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===undefined,d.value===undefined,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["array-length-partial", "const p:any=Array.prototype;Object.defineProperty(p,\"length\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"length\");console.log(p.length,d.value,d.writable,d.enumerable,d.configurable);", "0 0 false false false\n"];
        yield return ["deleted-slot-fresh-defaults", "const p:any=Number.prototype;delete p.constructor;Object.defineProperty(p,\"constructor\",{value:7});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor,d.value,d.writable,d.enumerable,d.configurable);", "7 7 false false false\n"];
        yield return ["physical-accessor-partial-no-eager-read", "const p:any=Number.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,\"probe\",{get,configurable:true});Object.defineProperty(p,\"probe\",{enumerable:true});const d:any=Object.getOwnPropertyDescriptor(p,\"probe\");console.log(calls,d.get===get,d.set===undefined,d.enumerable,d.configurable,p.probe,calls);", "0 true true true true 7 1\n"];
        yield return ["virtual-data-to-accessor", "const p:any=Boolean.prototype;let calls=0;const get=()=>{calls++;return 7};const ok=Reflect.defineProperty(p,\"constructor\",{get});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(ok,calls,d.get===get,d.set===undefined,d.enumerable,d.configurable,p.constructor,calls);", "true 0 true true false true 7 1\n"];
        yield return ["nonextensible-live-partial", "const p:any=String.prototype,old=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,\"constructor\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true false false true false\n"];
        yield return ["readonly-refusal-preserves-value", "const p:any=Number.prototype,old=p.constructor;Object.defineProperty(p,\"constructor\",{writable:false,configurable:false});console.log(Reflect.defineProperty(p,\"constructor\",{value:9}),p.constructor===old);", "false true\n"];
        yield return ["descriptor-getter-once", "const p:any=Number.prototype,old=p.constructor;let count=0;Object.defineProperty(p,\"constructor\",{get writable(){count++;return false}});const d:any=Object.getOwnPropertyDescriptor(p,\"constructor\");console.log(count,p.constructor===old,d.value===old,d.writable,d.enumerable,d.configurable);", "1 true true false false true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PartialDefinitionsRetainCurrentVirtualData(string name, string source, string expected, ExecutionMode mode)
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
