using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeVirtualOwnKeysTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-virtual-constructor-ownkeys", "const p:any=Number.prototype,d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(Object.hasOwn(p,'constructor'),d.value===Number,Reflect.ownKeys(p).includes('constructor'));\n", "true true true\n"];
        yield return ["boolean-virtual-constructor-ownkeys", "const p:any=Boolean.prototype,d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(Object.hasOwn(p,'constructor'),d.value===Boolean,Reflect.ownKeys(p).includes('constructor'));\n", "true true true\n"];
        yield return ["number-complete-intrinsic-order", "const p:any=Number.prototype;console.log(Reflect.ownKeys(p).join(','),Object.keys(p).length);\n", "constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString 0\n"];
        yield return ["number-integer-string-symbol-order", "const p:any=Number.prototype;const a=Symbol('a'),b=Symbol('b');p.b=1;p[10]=2;p.a=3;p[2]=4;p[a]=5;p[b]=6;let result='';for(const k of Reflect.ownKeys(p)){result+=(typeof k==='symbol'?(k===a?'s1':'s2'):k)+',';}console.log(result);\n", "2,10,constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString,b,a,s1,s2,\n"];
        yield return ["number-deletion-and-recreation-order", "const p:any=Number.prototype;delete p.constructor;delete p.toString;p.constructor=9;p.toString=7;console.log(Reflect.ownKeys(p).join(','),Object.getOwnPropertyDescriptor(p,'constructor')!.enumerable,Object.getOwnPropertyDescriptor(p,'toString')!.enumerable);\n", "toExponential,toFixed,toPrecision,valueOf,toLocaleString,constructor,toString true true\n"];
        yield return ["number-undefined-override-retains-position", "const p:any=Number.prototype;p.constructor=undefined;const keys=Reflect.ownKeys(p),d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(keys[0],keys.includes('constructor'),d.value===undefined,d.enumerable);\n", "constructor true true false\n"];
        yield return ["number-getter-free-overridden-virtual-slot", "const p:any=Number.prototype;let calls=0;Object.defineProperty(p,'toString',{get(){calls++;return 7},configurable:true});const keys=Reflect.ownKeys(p),d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(calls,keys.includes('toString'),typeof d.get,d.enumerable,d.configurable);\n", "0 true function false true\n"];
        yield return ["number-integrity-keeps-virtual-own-slot", "const p:any=Number.prototype;const object:any=Object;for(const operation of ['preventExtensions','seal','freeze']){object[operation](p);const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(Object.isExtensible(p),d.writable,d.enumerable,d.configurable,Reflect.ownKeys(p).includes('constructor'),Object.keys(p).length);}\n", "false true false true true 0\nfalse true false false true 0\nfalse false false false true 0\n"];
        yield return ["number-inherited-excluded", "const p:any=Number.prototype;const inherited=Symbol('parent'),base:any={inherited:9};base[inherited]=8;Object.setPrototypeOf(p,base);const keys=Reflect.ownKeys(p);console.log(keys.includes('inherited'),keys.includes(inherited),keys.includes('constructor'),Object.keys(p).length);\n", "false false true 0\n"];
        yield return ["number-snapshot-and-method-identity", "const p:any=Number.prototype;const ctor=Object.getOwnPropertyDescriptor(p,'constructor')!.value,method=Object.getOwnPropertyDescriptor(p,'toString')!.value;const before=Reflect.ownKeys(p);before.pop();p.extra=7;const after=Reflect.ownKeys(p);console.log(before.length,after.length,Object.getOwnPropertyDescriptor(p,'constructor')!.value===ctor,Object.getOwnPropertyDescriptor(p,'toString')!.value===method,after.includes('extra'));\n", "6 8 true true true\n"];
        yield return ["boolean-complete-intrinsic-order", "const p:any=Boolean.prototype;console.log(Reflect.ownKeys(p).join(','),Object.keys(p).length);\n", "constructor,toString,valueOf 0\n"];
        yield return ["boolean-integer-string-symbol-order", "const p:any=Boolean.prototype;const a=Symbol('a'),b=Symbol('b');p.b=1;p[10]=2;p.a=3;p[2]=4;p[a]=5;p[b]=6;let result='';for(const k of Reflect.ownKeys(p)){result+=(typeof k==='symbol'?(k===a?'s1':'s2'):k)+',';}console.log(result);\n", "2,10,constructor,toString,valueOf,b,a,s1,s2,\n"];
        yield return ["boolean-deletion-and-recreation-order", "const p:any=Boolean.prototype;delete p.constructor;delete p.toString;p.constructor=9;p.toString=7;console.log(Reflect.ownKeys(p).join(','),Object.getOwnPropertyDescriptor(p,'constructor')!.enumerable,Object.getOwnPropertyDescriptor(p,'toString')!.enumerable);\n", "valueOf,constructor,toString true true\n"];
        yield return ["boolean-undefined-override-retains-position", "const p:any=Boolean.prototype;p.constructor=undefined;const keys=Reflect.ownKeys(p),d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(keys[0],keys.includes('constructor'),d.value===undefined,d.enumerable);\n", "constructor true true false\n"];
        yield return ["boolean-getter-free-overridden-virtual-slot", "const p:any=Boolean.prototype;let calls=0;Object.defineProperty(p,'toString',{get(){calls++;return 7},configurable:true});const keys=Reflect.ownKeys(p),d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(calls,keys.includes('toString'),typeof d.get,d.enumerable,d.configurable);\n", "0 true function false true\n"];
        yield return ["boolean-integrity-keeps-virtual-own-slot", "const p:any=Boolean.prototype;const object:any=Object;for(const operation of ['preventExtensions','seal','freeze']){object[operation](p);const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(Object.isExtensible(p),d.writable,d.enumerable,d.configurable,Reflect.ownKeys(p).includes('constructor'),Object.keys(p).length);}\n", "false true false true true 0\nfalse true false false true 0\nfalse false false false true 0\n"];
        yield return ["boolean-inherited-excluded", "const p:any=Boolean.prototype;const inherited=Symbol('parent'),base:any={inherited:9};base[inherited]=8;Object.setPrototypeOf(p,base);const keys=Reflect.ownKeys(p);console.log(keys.includes('inherited'),keys.includes(inherited),keys.includes('constructor'),Object.keys(p).length);\n", "false false true 0\n"];
        yield return ["boolean-snapshot-and-method-identity", "const p:any=Boolean.prototype;const ctor=Object.getOwnPropertyDescriptor(p,'constructor')!.value,method=Object.getOwnPropertyDescriptor(p,'toString')!.value;const before=Reflect.ownKeys(p);before.pop();p.extra=7;const after=Reflect.ownKeys(p);console.log(before.length,after.length,Object.getOwnPropertyDescriptor(p,'constructor')!.value===ctor,Object.getOwnPropertyDescriptor(p,'toString')!.value===method,after.includes('extra'));\n", "2 4 true true true\n"];
        yield return ["ordinary-accessor-control", "const s=Symbol('s');let calls=0;const p:any={a:1};Object.defineProperty(p,'held',{get(){calls++;return 7},configurable:true});p[s]=2;const keys=Reflect.ownKeys(p);console.log(keys[0],keys[1],keys[2]===s,calls);\n", "a held true 0\n"];
        yield return ["null-prototype-ordinary-control", "const p:any=Object.create(null),s=Symbol('s');p.a=1;p[2]=2;p[s]=3;const keys=Reflect.ownKeys(p);console.log(keys[0],keys[1],keys[2]===s,Object.keys(p).join(','));\n", "2 a true 2,a\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OwnInventoryRetainsLiveVirtualNamesWithoutReadingValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
