using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeReflectDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-existing-nonextensible", "const p:any=Object.prototype;let calls=0;const get=function(this:any){calls++;return this===p?7:99};Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'toString',{get}));const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.get===get,d.enumerable,d.configurable,calls,p.toString,calls);", "true\ntrue false true 0 7 1\n"];
        yield return ["fresh-data", "const p:any=Object.prototype;console.log(Reflect.defineProperty(p,'__p2_define',{value:7,writable:true,enumerable:false,configurable:true}));const d:any=Object.getOwnPropertyDescriptor(p,'__p2_define');console.log(p.__p2_define,d.value,d.writable,d.enumerable,d.configurable);delete p.__p2_define;", "true\n7 7 true false true\n"];
        yield return ["getter-setter-receiver", "const p:any=Object.prototype;let value=1;const log:string[]=[];console.log(Reflect.defineProperty(p,'__p2_define',{get(){log.push('get:'+String(this===p));return value},set(v:any){log.push('set:'+String(this===p));value=v},configurable:true}));console.log(p.__p2_define,p.__p2_define=7,value,log.join(','));delete p.__p2_define;", "true\n1 7 7 get:true,set:true\n"];
        yield return ["descriptor-conversion-once", "const p:any=Object.prototype,log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_define'}};const d:any={get enumerable(){log.push('enumerable');return true},get value(){log.push('value');return 7},configurable:true};console.log(Reflect.defineProperty(p,key,d),p.__p2_define,log.join(','));delete p.__p2_define;", "true 7 string,enumerable,value\n"];
        yield return ["descriptor-abrupt", "const p:any=Object.prototype,marker={};const d:any={get value(){throw marker}};try{Reflect.defineProperty(p,'__p2_define',d)}catch(e){console.log(e===marker,Object.hasOwn(p,'__p2_define'))}", "true false\n"];
        yield return ["key-abrupt-before-descriptor", "const p:any=Object.prototype,marker={},log:string[]=[];const key:any={[Symbol.toPrimitive](){log.push('key');throw marker}};const d:any={get value(){log.push('value');return 7}};try{Reflect.defineProperty(p,key,d)}catch(e){console.log(e===marker,log.join(','))}", "true key\n"];
        yield return ["readonly-refusal", "const p:any=Object.prototype;Object.defineProperty(p,'__p2_define',{value:1,writable:false,configurable:false});console.log(Reflect.defineProperty(p,'__p2_define',{value:2}),p.__p2_define);", "false 1\n"];
        yield return ["new-slot-nonextensible-refusal", "const p:any=Object.prototype;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'__p2_define',{value:7}),Object.hasOwn(p,'__p2_define'),p.__p2_define===undefined);", "false false true\n"];
        yield return ["existing-physical-nonextensible", "const p:any=Object.prototype;Object.defineProperty(p,'__p2_define',{value:1,writable:true,configurable:false});Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'__p2_define',{value:7}));const d:any=Object.getOwnPropertyDescriptor(p,'__p2_define');console.log(p.__p2_define,d.writable,d.enumerable,d.configurable);", "true\n7 true false false\n"];
        yield return ["null-root-converted-descriptor", "const p:any=Object.prototype,marker={};let poison=0;Object.defineProperty(p,'get',{get(){poison++;throw marker},configurable:true});try{const descriptor:any=Object.create(null);descriptor.value=7;descriptor.configurable=true;console.log(Reflect.defineProperty(p,'__p2_define',descriptor),p.__p2_define,poison);delete p.__p2_define}finally{delete p.get}", "true 7 0\n"];
        yield return ["other-receiver-control", "class C{}const p:any=C.prototype;console.log(Reflect.defineProperty(p,'field',{value:7,configurable:true}),Reflect.get(p,'field'));delete p.field;", "true 7\n"];
        yield return ["frozen-existing-method-definition", "const p:any=Object.prototype,old=p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:()=>7}),p.toString===old);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.writable,d.configurable);", "false true\nfalse false\n"];
        yield return ["sealed-existing-method-accessor", "const p:any=Object.prototype,old=p.toString;Object.seal(p);console.log(Reflect.defineProperty(p,'toString',{get(){return 7}}),p.toString===old);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.writable,d.configurable);", "false true\ntrue false\n"];
        yield return ["frozen-identical-definition", "const p:any=Object.prototype,old=p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:old}),p.toString===old);", "true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RootDefinitionsUseExistingDescriptorProtocol(string name, string source, string expected, ExecutionMode mode)
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

    // The separately tracked compiled root read counterpart is #3323.
    [Fact]
    public void RefusedDefinitionKeepsDeletedRootAbsent()
    {
        const string source = "const p:any=Object.prototype;console.log(delete p.constructor);Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}));console.log(Object.hasOwn(p,'constructor'),p.constructor===undefined,Object.isExtensible(p));";
        const string expected = "true\nfalse\nfalse true false\n";
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
