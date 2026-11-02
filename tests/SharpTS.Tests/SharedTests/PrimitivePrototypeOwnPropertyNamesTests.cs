using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeOwnPropertyNamesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-stored-name-original", "const p:any=Number.prototype;p.extra=7;console.log(Object.getOwnPropertyDescriptor(p,'extra')!.value,Object.getOwnPropertyNames(p).includes('extra'));\n", "7 true\n"];
        yield return ["boolean-stored-name-original", "const p:any=Boolean.prototype;p.extra=7;console.log(Object.getOwnPropertyDescriptor(p,'extra')!.value,Object.getOwnPropertyNames(p).includes('extra'));\n", "7 true\n"];
        yield return ["number-intrinsic-order", "const p:any=Number.prototype;console.log(Object.getOwnPropertyNames(p).join(','));\n", "constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString\n"];
        yield return ["number-indices-strings-and-nonenumerable", "const p:any=Number.prototype;p.b=1;p[10]=2;p.a=3;p[2]=4;Object.defineProperty(p,'hidden',{value:7});const names=Object.getOwnPropertyNames(p);console.log(names.join(','));console.log(names.includes('hidden'),Object.keys(p).includes('hidden'));\n", "2,10,constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString,b,a,hidden\ntrue false\n"];
        yield return ["number-symbols-excluded-string-collision-kept", "const p:any=Number.prototype;const s=Symbol('same');p[s]=7;p[String(s)]=9;const names=Object.getOwnPropertyNames(p);console.log(names.includes(s as any),names.includes(String(s)),names.length,Object.getOwnPropertyDescriptor(p,s)!.value);\n", "false true 8 7\n"];
        yield return ["number-accessor-no-getter-read", "const p:any=Number.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;throw 7},configurable:true});const names=Object.getOwnPropertyNames(p);console.log(calls,names.includes('held'),typeof Object.getOwnPropertyDescriptor(p,'held')!.get);\n", "0 true function\n"];
        yield return ["number-inherited-and-deleted-excluded", "const p:any=Number.prototype;p.gone=7;delete p.gone;Object.setPrototypeOf(p,{inherited:9});const names=Object.getOwnPropertyNames(p);console.log(names.includes('inherited'),names.includes('gone'),names.includes('constructor'));\n", "false false true\n"];
        yield return ["number-snapshot-and-undefined-presence", "const p:any=Number.prototype;p.constructor=undefined;const first=Object.getOwnPropertyNames(p);first.pop();p.extra=undefined;const next=Object.getOwnPropertyNames(p);console.log(first.length,next.length,next[0],next.includes('extra'),Object.getOwnPropertyDescriptor(p,'extra')!.value===undefined);\n", "6 8 constructor true true\n"];
        yield return ["number-deleted-intrinsic-recreated-last", "const p:any=Number.prototype;delete p.constructor;p.constructor=7;console.log(Object.getOwnPropertyNames(p).join(','),Object.getOwnPropertyDescriptor(p,'constructor')!.enumerable);\n", "toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString,constructor true\n"];
        yield return ["number-integrity-retains-owned-names", "const p:any=Number.prototype;p.probe=7;const object:any=Object;for(const operation of ['preventExtensions','seal','freeze']){object[operation](p);const names=Object.getOwnPropertyNames(p),d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(names.includes('constructor'),names.includes('probe'),d.writable,d.configurable);}\n", "true true true true\ntrue true true false\ntrue true false false\n"];
        yield return ["boolean-intrinsic-order", "const p:any=Boolean.prototype;console.log(Object.getOwnPropertyNames(p).join(','));\n", "constructor,toString,valueOf\n"];
        yield return ["boolean-indices-strings-and-nonenumerable", "const p:any=Boolean.prototype;p.b=1;p[10]=2;p.a=3;p[2]=4;Object.defineProperty(p,'hidden',{value:7});const names=Object.getOwnPropertyNames(p);console.log(names.join(','));console.log(names.includes('hidden'),Object.keys(p).includes('hidden'));\n", "2,10,constructor,toString,valueOf,b,a,hidden\ntrue false\n"];
        yield return ["boolean-symbols-excluded-string-collision-kept", "const p:any=Boolean.prototype;const s=Symbol('same');p[s]=7;p[String(s)]=9;const names=Object.getOwnPropertyNames(p);console.log(names.includes(s as any),names.includes(String(s)),names.length,Object.getOwnPropertyDescriptor(p,s)!.value);\n", "false true 4 7\n"];
        yield return ["boolean-accessor-no-getter-read", "const p:any=Boolean.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;throw 7},configurable:true});const names=Object.getOwnPropertyNames(p);console.log(calls,names.includes('held'),typeof Object.getOwnPropertyDescriptor(p,'held')!.get);\n", "0 true function\n"];
        yield return ["boolean-inherited-and-deleted-excluded", "const p:any=Boolean.prototype;p.gone=7;delete p.gone;Object.setPrototypeOf(p,{inherited:9});const names=Object.getOwnPropertyNames(p);console.log(names.includes('inherited'),names.includes('gone'),names.includes('constructor'));\n", "false false true\n"];
        yield return ["boolean-snapshot-and-undefined-presence", "const p:any=Boolean.prototype;p.constructor=undefined;const first=Object.getOwnPropertyNames(p);first.pop();p.extra=undefined;const next=Object.getOwnPropertyNames(p);console.log(first.length,next.length,next[0],next.includes('extra'),Object.getOwnPropertyDescriptor(p,'extra')!.value===undefined);\n", "2 4 constructor true true\n"];
        yield return ["boolean-deleted-intrinsic-recreated-last", "const p:any=Boolean.prototype;delete p.constructor;p.constructor=7;console.log(Object.getOwnPropertyNames(p).join(','),Object.getOwnPropertyDescriptor(p,'constructor')!.enumerable);\n", "toString,valueOf,constructor true\n"];
        yield return ["boolean-integrity-retains-owned-names", "const p:any=Boolean.prototype;p.probe=7;const object:any=Object;for(const operation of ['preventExtensions','seal','freeze']){object[operation](p);const names=Object.getOwnPropertyNames(p),d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(names.includes('constructor'),names.includes('probe'),d.writable,d.configurable);}\n", "true true true true\ntrue true true false\ntrue true false false\n"];
        yield return ["ordinary-object-control", "const p:any={a:1},s=Symbol('s');p[s]=3;Object.defineProperty(p,'hidden',{value:7});Object.setPrototypeOf(p,{inherited:9});console.log(Object.getOwnPropertyNames(p).join(','));\n", "a,hidden\n"];
        yield return ["nullish-target-control", "for(const p of [null,undefined]){try{Object.getOwnPropertyNames(p as any)}catch(e){console.log(e instanceof TypeError)}}\n", "true\ntrue\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReturnsOwnedStringNamesWithoutReadingValuesOrIncludingSymbols(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
