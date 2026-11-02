using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeIntegrityDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["frozen-virtual-data", "const p:any=Object.prototype,f=p.toString,c=p.constructor;Object.freeze(p);const a:any=Object.getOwnPropertyDescriptor(p,'toString'),b:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(Object.isFrozen(p),Object.isSealed(p),Object.isExtensible(p),a.writable,a.configurable,b.writable,b.configurable,p.toString===f,p.constructor===c);", "true true false false false false false true true\n"];
        yield return ["sealed-virtual-data", "const p:any=Object.prototype,f=p.toString,c=p.constructor;Object.seal(p);const a:any=Object.getOwnPropertyDescriptor(p,'toString'),b:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(Object.isFrozen(p),Object.isSealed(p),Object.isExtensible(p),a.writable,a.configurable,b.writable,b.configurable,p.toString===f,p.constructor===c);", "false true false true false true false true true\n"];
        yield return ["freeze-accessor-without-invocation", "const p:any=Object.prototype;let gets=0,sets=0;function get(){gets++;return 7}function set(v:any){sets=v}Object.defineProperty(p,'custom',{get,set,enumerable:true,configurable:true});Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(gets,d.get===get,d.set===set,d.enumerable,d.configurable,'writable' in d);p.custom=9;console.log(sets,p.custom,gets);", "0 true true true false false\n9 7 1\n"];
        yield return ["frozen-data-refusal", "const p:any=Object.prototype,f=p.toString,c=p.constructor;Object.freeze(p);p.constructor=7;let threw=false;function put(){'use strict';p.toString=7}try{put()}catch(e){threw=e instanceof TypeError}console.log(p.constructor===c,p.toString===f,threw,delete p.toString);", "true true true false\n"];
        yield return ["sealed-data-remains-writable", "const p:any=Object.prototype;function replacement(){return 7}Object.seal(p);p.constructor=replacement;p.fresh=9;console.log(p.constructor===replacement,Object.hasOwn(p,'fresh'),delete p.constructor,Object.getOwnPropertyDescriptor(p,'constructor')!.writable);", "true false false true\n"];
        yield return ["prevent-extensions-preserves-existing-data", "const p:any=Object.prototype,names=Object.getOwnPropertyNames(p).join(',');Object.preventExtensions(p);const d:any=Object.getOwnPropertyDescriptor(p,'constructor');function replacement(){return 7}p.constructor=replacement;console.log(Object.isExtensible(p),Object.isSealed(p),Object.isFrozen(p),d.writable,d.configurable,p.constructor===replacement,Object.getOwnPropertyNames(p).join(',')===names);", "false false false true true true true\n"];
        yield return ["integrity-transitions", "const p:any=Object.prototype,names=Object.getOwnPropertyNames(p).join(','),f=p.toString;Object.preventExtensions(p);Object.seal(p);console.log(Object.getOwnPropertyDescriptor(p,'toString')!.writable,Object.getOwnPropertyDescriptor(p,'toString')!.configurable);Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.writable,d.configurable,p.toString===f,Object.getOwnPropertyNames(p).join(',')===names,Object.isFrozen(p));", "true false\nfalse false true true true\n"];
        yield return ["deleted-virtual-key-stays-absent", "const p:any=Object.prototype;delete p.toString;const names=Object.getOwnPropertyNames(p).join(',');Object.preventExtensions(p);Object.seal(p);Object.freeze(p);console.log(Object.hasOwn(p,'toString'),Object.getOwnPropertyNames(p).includes('toString'),Object.getOwnPropertyNames(p).join(',')===names);", "false false true\n"];
        yield return ["existing-readonly-descriptor", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{value:7,writable:false,enumerable:true,configurable:false});Object.seal(p);Object.freeze(p);p.custom=9;const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(p.custom,d.value,d.writable,d.enumerable,d.configurable);", "7 7 false true false\n"];
        yield return ["sealed-accessor-without-invocation", "const p:any=Object.prototype;let gets=0,value=1;function get(){gets++;return value}function set(v:any){value=v}Object.defineProperty(p,'custom',{get,set,configurable:true});Object.seal(p);const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(gets,d.get===get,d.set===set,d.configurable);p.custom=9;console.log(p.custom,gets,value);", "0 true true false\n9 1 9\n"];
        yield return ["frozen-same-value-definition", "const p:any=Object.prototype,f=p.toString;Object.freeze(p);console.log(Object.defineProperty(p,'toString',{value:f})===p);let threw=false;try{Object.defineProperty(p,'toString',{value:7})}catch(e){threw=e instanceof TypeError}console.log(threw,p.toString===f);", "true\ntrue true\n"];
        yield return ["nonextensible-existing-definition", "const p:any=Object.prototype;Object.preventExtensions(p);function replacement(){return 7}console.log(Object.defineProperty(p,'constructor',{value:replacement})===p);const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(p.constructor===replacement,d.writable,d.enumerable,d.configurable);", "true\ntrue true false true\n"];
        yield return ["nonextensible-fresh-slot-refusal", "const p:any=Object.prototype;Object.preventExtensions(p);p.fresh=7;let threw=false;try{Object.defineProperty(p,'other',{value:9})}catch(e){threw=e instanceof TypeError}console.log(Object.hasOwn(p,'fresh'),Object.hasOwn(p,'other'),threw,Object.isExtensible(p));", "false false true false\n"];
        yield return ["prevent-extensions-then-accessor-freeze", "const p:any=Object.prototype;let gets=0;function get(){gets++;return 7}Object.defineProperty(p,'custom',{get,configurable:true});Object.preventExtensions(p);Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(gets,d.get===get,d.configurable,'writable' in d,Object.isFrozen(p));", "0 true false false true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void IntegrityAppliesToCurrentOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Verify(source, expected, mode);
    }

    [Theory, ModeData]
    public void IntegrityRemainsLocalToTheRealm(ExecutionMode mode)
    {
        Verify("Object.freeze(Object.prototype);console.log(Object.isFrozen(Object.prototype));", "true\n", mode);
        Verify("const d:any=Object.getOwnPropertyDescriptor(Object.prototype,'toString');console.log(Object.isExtensible(Object.prototype),d.writable,d.configurable);", "true true true\n", mode);
    }

    private static void Verify(string source, string expected, ExecutionMode mode)
    {
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
