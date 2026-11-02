using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypePartialDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-value-preserved", "const p:any=Object.prototype,original=p.constructor;Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor===original,d.value===original,d.writable,d.enumerable,d.configurable);", "true true false false true\n"];
        yield return ["method-value-preserved", "const p:any=Object.prototype,original=p.toString;Object.defineProperty(p,'toString',{configurable:false});const d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(p.toString===original,d.value===original,d.writable,d.enumerable,d.configurable);", "true true true false false\n"];
        yield return ["constructor-attributes-preserved", "const p:any=Object.prototype;Object.defineProperty(p,'constructor',{value:null});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor===null,d.value===null,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["data-accessor-transition", "const p:any=Object.prototype;let count=0;const getter=()=>{count++;return 7};Object.defineProperty(p,'constructor',{get:getter});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(count,d.get===getter,d.set===undefined,d.enumerable,d.configurable,p.constructor,count);", "0 true true false true 7 1\n"];
        yield return ["reflect-existing-control", "const p:any=Object.prototype,original=p.constructor;console.log(Reflect.defineProperty(p,'constructor',{writable:false}));const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor===original,d.value===original,d.writable,d.enumerable,d.configurable);", "true\ntrue true false false true\n"];
        yield return ["physical-existing-control", "const p:any=Object.prototype,original={};Object.defineProperty(p,'custom',{value:original,writable:true,enumerable:true,configurable:true});Object.defineProperty(p,'custom',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'custom')!;console.log(p.custom===original,d.value===original,d.writable,d.enumerable,d.configurable);", "true true false true true\n"];
        yield return ["ordinary-existing-control", "const p:any={custom:7};Object.defineProperty(p,'custom',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'custom')!;console.log(p.custom,d.value,d.writable,d.enumerable,d.configurable);", "7 7 false true true\n"];
        yield return ["explicit-undefined-value", "const p:any=Object.prototype;Object.defineProperty(p,'constructor',{value:undefined});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor===undefined,d.value===undefined,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["descriptor-getter-once", "const p:any=Object.prototype,original=p.constructor;let calls=0;Object.defineProperty(p,'constructor',{get writable(){calls++;return false}});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(calls,p.constructor===original,d.value===original,d.writable,d.enumerable,d.configurable);", "1 true true false false true\n"];
        yield return ["deleted-slot-new-defaults", "const p:any=Object.prototype;delete p.constructor;Object.defineProperty(p,'constructor',{value:7});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor,d.value,d.writable,d.enumerable,d.configurable);", "7 7 false false false\n"];
        yield return ["nonextensible-existing-partial", "const p:any=Object.prototype,original=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(p.constructor===original,d.value===original,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true false false true false\n"];
        yield return ["root-live-refused-definition", "const p:any=Object.prototype;const original=p.constructor;Object.defineProperty(p,'constructor',{configurable:false,writable:false});let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(caught,p.constructor===original,d.value===original,d.writable,d.configurable);", "true true true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PartialDefinitionsPreserveEffectiveOwnDescriptor(string name, string source, string expected, ExecutionMode mode)
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
