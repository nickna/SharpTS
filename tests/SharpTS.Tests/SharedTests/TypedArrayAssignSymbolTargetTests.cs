using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayAssignSymbolTargetTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["target-original", "const s=Symbol(\"s\");const a:any=new Uint8Array(1);const result=Object.assign(a,{[s]:3});console.log(result===a,a[s],a[0]);\n", "true 3 0\n"];
        yield return ["new-symbol-descriptor", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.assign(a,{[s]:7});const d=Object.getOwnPropertyDescriptor(a,s)!;console.log(a[s],d.value,d.writable,d.enumerable,d.configurable,a[0]);", "7 7 true true true 1\n"];
        yield return ["existing-writable-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:true,enumerable:false,configurable:true});Object.assign(a,{[s]:7});const d=Object.getOwnPropertyDescriptor(a,s)!;console.log(a[s],d.writable,d.enumerable,d.configurable);", "7 true false true\n"];
        yield return ["own-symbol-setter", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");let calls=0,seen=0;Object.defineProperty(a,s,{set(v){calls++;seen=v;console.log(this===a)}});console.log(Object.assign(a,{[s]:7})===a,calls,seen,a[0]);", "true\ntrue 1 7 1\n"];
        yield return ["inherited-symbol-setter", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");const p:any={};let seen=0;Object.defineProperty(p,s,{set(v){seen=v;console.log(this===a)}});Object.setPrototypeOf(a,p);Object.assign(a,{[s]:7});console.log(seen,Object.hasOwn(a,s),a[0]);", "true\n7 false 1\n"];
        yield return ["own-readonly-refusal", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:false});try{Object.assign(a,{[s]:7})}catch(e){console.log(e instanceof TypeError,a[s],a[0])}", "true 3 1\n"];
        yield return ["getter-only-no-eager-get", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");let calls=0;Object.defineProperty(a,s,{get(){calls++;return 3}});try{Object.assign(a,{[s]:7})}catch(e){console.log(e instanceof TypeError,calls,a[0])}", "true 0 1\n"];
        yield return ["inherited-readonly-refusal", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\"),p:any={};Object.defineProperty(p,s,{value:3,writable:false});Object.setPrototypeOf(a,p);try{Object.assign(a,{[s]:7})}catch(e){console.log(e instanceof TypeError,a[s],Object.hasOwn(a,s))}", "true 3 false\n"];
        yield return ["prevented-new-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.preventExtensions(a);try{Object.assign(a,{[s]:7})}catch(e){console.log(e instanceof TypeError,Object.hasOwn(a,s),a[0])}", "true false 1\n"];
        yield return ["prevented-existing-writable", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:true});Object.preventExtensions(a);Object.assign(a,{[s]:7});console.log(a[s],Object.isExtensible(a),a[0]);", "7 false 1\n"];
        yield return ["sealed-existing-symbol", "const a:any=new Uint8Array(0);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:true,configurable:true});Object.seal(a);Object.assign(a,{[s]:7});console.log(a[s],Object.getOwnPropertyDescriptor(a,s)!.configurable,Object.isExtensible(a));", "7 false false\n"];
        yield return ["frozen-empty-symbol-refusal", "const a:any=new Uint8Array(0);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:true,configurable:true});Object.freeze(a);try{Object.assign(a,{[s]:7})}catch(e){console.log(e instanceof TypeError,a[s])}", "true 3\n"];
        yield return ["all-element-kinds-symbol-target", "const views:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];const s=Symbol(\"s\");for(const a of views){console.log(Object.assign(a,{[s]:7})===a,a[s]);}", "true 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\ntrue 7\n"];
        yield return ["object-value-not-numeric-coercion", "const a:any=new BigInt64Array([1n]);const s=Symbol(\"s\");let calls=0;const value={valueOf(){calls++;return 9}};Object.assign(a,{[s]:value});console.log(a[s]===value,calls,String(a[0]));", "true 0 1\n"];
        yield return ["setter-abrupt-identity", "const a:any=new Uint8Array(0);const s=Symbol(\"s\"),reason={};Object.defineProperty(a,s,{set(){throw reason}});try{Object.assign(a,{[s]:7})}catch(e){console.log(e===reason)}", "true\n"];
        yield return ["source-getter-once-before-setter", "const a:any=new Uint8Array(0);const s=Symbol(\"s\");const events:string[]=[];const source:any={};Object.defineProperty(source,s,{get(){events.push(\"get:\"+(this===source));return 7},enumerable:true});Object.defineProperty(a,s,{set(v){events.push(\"set:\"+(this===a)+\":\"+v)}});Object.assign(a,source);console.log(events.join(\",\"));", "get:true,set:true:7\n"];
        yield return ["source-getter-prevents-target", "const a:any=new Uint8Array(0);const s=Symbol(\"s\"),source:any={};Object.defineProperty(source,s,{get(){Object.preventExtensions(a);return 7},enumerable:true});try{Object.assign(a,source)}catch(e){console.log(e instanceof TypeError,Object.hasOwn(a,s),Object.isExtensible(a))}", "true false false\n"];
        yield return ["multiple-sources-and-distinct-symbols", "const a:any=new Uint8Array(0);const s=Symbol(\"same\"),t=Symbol(\"same\");Object.assign(a,{[s]:3},{[s]:7,[t]:9});console.log(a[s],a[t],Reflect.ownKeys(a).length);", "7 9 2\n"];
        yield return ["source-abrupt-stops-later-set", "const a:any=new Uint8Array(0);const s=Symbol(\"first\"),t=Symbol(\"next\"),source:any={},reason={};let calls=0;Object.defineProperty(source,s,{get(){throw reason},enumerable:true});Object.defineProperty(a,t,{set(){calls++}});Object.defineProperty(source,t,{value:7,enumerable:true});try{Object.assign(a,source)}catch(e){console.log(e===reason,calls,Object.hasOwn(a,s))}", "true 0 false\n"];
        yield return ["ordinary-object-control", "const a:any={};const s=Symbol(\"s\");console.log(Object.assign(a,{[s]:7})===a,a[s]);", "true 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AssignWritesTypedArraySymbolsThroughOrdinarySet(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
