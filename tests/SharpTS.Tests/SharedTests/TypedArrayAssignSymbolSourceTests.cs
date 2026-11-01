using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayAssignSymbolSourceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["source-original", "const s=Symbol(\"s\"); const v:any=new Uint8Array([1]); Object.defineProperty(v,s,{value:3,enumerable:true}); const o:any=Object.assign({},v); console.log(o[0],o[s]);\n", "1 3\n"];
        yield return ["nonenumerable-symbol", "const a:any=new Uint8Array([7]);const key=Symbol(\"hidden\");let calls=0;Object.defineProperty(a,key,{get(){calls++;return 9},enumerable:false});const o:any=Object.assign({},a);console.log(o[0],Object.hasOwn(o,key),calls);", "7 false 0\n"];
        yield return ["getter-receiver-order", "const a:any=new Uint8Array([4]);const s=Symbol(\"s\");const events:string[]=[];Object.defineProperty(a,\"named\",{get(){events.push(\"name:\"+(this===a));return 5},enumerable:true});Object.defineProperty(a,s,{get(){events.push(\"symbol:\"+(this===a));return 6},enumerable:true});const o:any=Object.assign({},a);console.log(o[0],o.named,o[s],events.join(\",\"));", "4 5 6 name:true,symbol:true\n"];
        yield return ["named-getter-adds-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"late\");Object.defineProperty(a,\"first\",{get(){Object.defineProperty(a,s,{value:9,enumerable:true});return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o.first,Object.hasOwn(o,s),a[s]);", "7 false 9\n"];
        yield return ["named-getter-deletes-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"removed\");Object.defineProperty(a,s,{value:9,enumerable:true,configurable:true});Object.defineProperty(a,\"first\",{get(){delete a[s];return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o.first,Object.hasOwn(o,s));", "7 false\n"];
        yield return ["named-getter-hides-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"hidden\");Object.defineProperty(a,s,{value:9,enumerable:true,configurable:true});Object.defineProperty(a,\"first\",{get(){Object.defineProperty(a,s,{enumerable:false});return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o.first,Object.hasOwn(o,s));", "7 false\n"];
        yield return ["named-getter-reveals-symbol", "const a:any=new Uint8Array([1]);const s=Symbol(\"reveal\");Object.defineProperty(a,s,{value:9,enumerable:false,configurable:true});Object.defineProperty(a,\"first\",{get(){Object.defineProperty(a,s,{enumerable:true});return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o.first,o[s]);", "7 9\n"];
        yield return ["named-getter-adds-string", "const a:any=new Uint8Array([1]);Object.defineProperty(a,\"first\",{get(){Object.defineProperty(a,\"late\",{value:9,enumerable:true});return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o.first,Object.hasOwn(o,\"late\"),a.late);", "7 false 9\n"];
        yield return ["symbol-getter-deletes-next", "const a:any=new Uint8Array(0);const s=Symbol(\"first\"),t=Symbol(\"next\");Object.defineProperty(a,s,{get(){delete a[t];return 7},enumerable:true});Object.defineProperty(a,t,{value:9,enumerable:true,configurable:true});const o:any=Object.assign({},a);console.log(o[s],Object.hasOwn(o,t));", "7 false\n"];
        yield return ["symbol-getter-adds-next", "const a:any=new Uint8Array(0);const s=Symbol(\"first\"),t=Symbol(\"late\");Object.defineProperty(a,s,{get(){Object.defineProperty(a,t,{value:9,enumerable:true});return 7},enumerable:true});const o:any=Object.assign({},a);console.log(o[s],Object.hasOwn(o,t),a[t]);", "7 false 9\n"];
        yield return ["symbol-getter-reveals-next", "const a:any=new Uint8Array(0);const s=Symbol(\"first\"),t=Symbol(\"next\");Object.defineProperty(a,s,{get(){Object.defineProperty(a,t,{enumerable:true});return 7},enumerable:true});Object.defineProperty(a,t,{value:9,enumerable:false,configurable:true});const o:any=Object.assign({},a);console.log(o[s],o[t]);", "7 9\n"];
        yield return ["symbol-order-and-identity", "const a:any=new Uint8Array([2,3]);const s=Symbol(\"same\"),t=Symbol(\"same\");Object.defineProperty(a,\"b\",{value:4,enumerable:true});Object.defineProperty(a,s,{value:5,enumerable:true});Object.defineProperty(a,t,{value:6,enumerable:true});const o:any=Object.assign({},a);const keys=Reflect.ownKeys(o);console.log(keys[0],keys[1],keys[2],keys[3]===s,keys[4]===t,o[s],o[t]);", "0 1 b true true 5 6\n"];
        yield return ["ordinary-target-symbol-setter", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:7,enumerable:true});let seen=0;const target:any={};Object.defineProperty(target,s,{set(value){console.log(this===target,value);seen=value},enumerable:false});console.log(Object.assign(target,a)===target,target[0],seen);", "true 7\ntrue 1 7\n"];
        yield return ["readonly-target-refusal", "const a:any=new Uint8Array([1]);const s=Symbol(\"s\");Object.defineProperty(a,s,{value:7,enumerable:true});const target:any={};Object.defineProperty(target,s,{value:3,writable:false});try{Object.assign(target,a)}catch(e){console.log(e instanceof TypeError,target[s],target[0])}", "true 3 1\n"];
        yield return ["abrupt-source-symbol-getter", "const a:any=new Uint8Array([1]);const s=Symbol(\"first\"),t=Symbol(\"next\"),reason={};let calls=0;Object.defineProperty(a,s,{get(){throw reason},enumerable:true});Object.defineProperty(a,t,{get(){calls++;return 8},enumerable:true});const target:any={};try{Object.assign(target,a)}catch(e){console.log(e===reason,target[0],Object.hasOwn(target,s),calls)}", "true 1 false 0\n"];
        yield return ["abrupt-target-symbol-setter", "const a:any=new Uint8Array(0);const s=Symbol(\"first\"),t=Symbol(\"next\"),reason={};let calls=0;Object.defineProperty(a,s,{value:7,enumerable:true});Object.defineProperty(a,t,{get(){calls++;return 8},enumerable:true});const target:any={};Object.defineProperty(target,s,{set(){throw reason}});try{Object.assign(target,a)}catch(e){console.log(e===reason,calls)}", "true 0\n"];
        yield return ["multiple-sources-same-symbol", "const s=Symbol(\"s\"),a:any=new Uint8Array([1]),b:any=new Uint8Array([2]);Object.defineProperty(a,s,{value:3,enumerable:true});Object.defineProperty(b,s,{value:4,enumerable:true});const o:any=Object.assign({},a,b);console.log(o[0],o[s]);", "2 4\n"];
        yield return ["prevented-source-and-bigint-values", "const s=Symbol(\"s\"),a:any=new BigInt64Array([7n]);Object.defineProperty(a,s,{value:9n,enumerable:true});Object.preventExtensions(a);const o:any=Object.assign({},a);console.log(String(o[0]),String(o[s]),Object.isExtensible(a));", "7 9 false\n"];
        yield return ["inherited-symbol-excluded", "const a:any=new Uint8Array([1]);const s=Symbol(\"inherited\");let calls=0;const p:any={};Object.defineProperty(p,s,{get(){calls++;return 8},enumerable:true});Object.setPrototypeOf(a,p);const o:any=Object.assign({},a);console.log(o[0],Object.hasOwn(o,s),calls);", "1 false 0\n"];
        yield return ["empty-source-symbol-only", "const a:any=new Float64Array(0),s=Symbol(\"s\");Object.defineProperty(a,s,{value:NaN,enumerable:true});const target:any={};console.log(Object.assign(target,a)===target,Number.isNaN(target[s]),Object.keys(target).length);", "true true 0\n"];
    }

    // Ordinary named GetIndex reads in the other seven authored compiler cases
    // retain existing P3 #3054 ownership; all twenty sources run interpreted.
    private static readonly HashSet<string> SupportedCompiledCases =
    [
        "source-original",
        "nonenumerable-symbol",
        "symbol-getter-deletes-next",
        "symbol-getter-adds-next",
        "symbol-getter-reveals-next",
        "ordinary-target-symbol-setter",
        "readonly-target-refusal",
        "abrupt-source-symbol-getter",
        "abrupt-target-symbol-setter",
        "multiple-sources-same-symbol",
        "prevented-source-and-bigint-values",
        "inherited-symbol-excluded",
        "empty-source-symbol-only"
    ];

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        (SupportedCompiledCases.Contains((string)row[0])
            ? new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            : new[] { ExecutionMode.Interpreted }).Select(mode =>
                new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AssignSnapshotsTypedArrayStringsAndSymbols(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
