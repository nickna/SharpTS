using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneOwnDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["plain-nonenumerable-original", "const value:any={n:1};Object.defineProperty(value,\"hidden\",{value:7,enumerable:false});const copy:any=structuredClone(value);console.log(copy.n,Object.hasOwn(copy,\"hidden\"));", "1 false\n"];
        yield return ["plain-own-getter-original", "const value:any={n:1};let calls=0;Object.defineProperty(value,\"x\",{enumerable:true,get:function(this:any){calls++;return this.n+2}});const copy:any=structuredClone(value);console.log(copy.x,calls,Object.hasOwn(copy,\"x\"));", "3 1 true\n"];
        yield return ["literal-getter", "let calls=0;const value:any={n:2,get x(){calls++;return this.n+3}};const c:any=structuredClone(value);console.log(c.x,calls,Object.getOwnPropertyDescriptor(c,\"x\")!.get===undefined);", "5 1 true\n"];
        yield return ["hidden-getter-never-runs", "let calls=0;const a:any={n:2};Object.defineProperty(a,\"x\",{get(){calls++;return 7},enumerable:false});const c:any=structuredClone(a);console.log(c.n,calls,Object.hasOwn(c,\"x\"));", "2 0 false\n"];
        yield return ["receiver-and-once", "let calls=0,receiver=false;const a:any={n:4};Object.defineProperty(a,\"x\",{enumerable:true,get:function(this:any){calls++;receiver=this===a;return this.n+1}});const c:any=structuredClone(a);console.log(c.x,calls,receiver);", "5 1 true\n"];
        yield return ["data-flags-normalized", "const a:any={};Object.defineProperty(a,\"x\",{value:3,enumerable:true,writable:false,configurable:false});const c:any=structuredClone(a),d=Object.getOwnPropertyDescriptor(c,\"x\")!;console.log(c.x,d.writable,d.enumerable,d.configurable);", "3 true true true\n"];
        yield return ["setter-only-undefined", "let calls=0;const a:any={};Object.defineProperty(a,\"x\",{set(v:any){calls++},enumerable:true});const c:any=structuredClone(a);console.log(c.x===undefined,Object.hasOwn(c,\"x\"),calls);", "true true 0\n"];
        yield return ["getter-abrupt-identity", "const error:any={tag:7};const a:any={};Object.defineProperty(a,\"x\",{enumerable:true,get(){throw error}});try{structuredClone(a)}catch(e){console.log(e===error)}", "true\n"];
        yield return ["getter-function-refused", "let calls=0;const a:any={};Object.defineProperty(a,\"x\",{enumerable:true,get(){calls++;return ()=>{}}});try{structuredClone(a)}catch(e:any){console.log(e.name,calls)}", "DataCloneError 1\n"];
        yield return ["returned-child-memo", "const child:any={n:3};let calls=0;const a:any={first:child};Object.defineProperty(a,\"second\",{enumerable:true,get(){calls++;return child}});const c:any=structuredClone(a);console.log(c.first===c.second,c.first!==child,c.second.n,calls);", "true true 3 1\n"];
        yield return ["getter-cycle-memo", "let calls=0;const a:any={n:1};Object.defineProperty(a,\"self\",{enumerable:true,get(){calls++;return a}});const c:any=structuredClone(a);console.log(c!==a,c.self===c,c.n,calls);", "true true 1 1\n"];
        yield return ["hidden-function-skipped", "const a:any={n:1};Object.defineProperty(a,\"fn\",{value:()=>{},enumerable:false});const c:any=structuredClone(a);console.log(c.n,Object.hasOwn(c,\"fn\"));", "1 false\n"];
        yield return ["symbol-getter-skipped", "const s=Symbol(\"x\");let calls=0;const a:any={n:1};Object.defineProperty(a,s,{get(){calls++;return 3},enumerable:true});const c:any=structuredClone(a);console.log(c.n,calls,Object.getOwnPropertySymbols(c).length);", "1 0 0\n"];
        yield return ["inherited-getter-skipped", "let calls=0;const p:any={};Object.defineProperty(p,\"x\",{enumerable:true,get(){calls++;return 3}});const a:any=Object.create(p);a.n=1;const c:any=structuredClone(a);console.log(c.n,calls,Object.hasOwn(c,\"x\"),Object.getPrototypeOf(c)===Object.prototype);", "1 0 false true\n"];
        yield return ["enumerable-snapshot-before-get", "const a:any={};Object.defineProperty(a,\"first\",{get(){Object.defineProperty(a,\"later\",{enumerable:false});return 1},enumerable:true});Object.defineProperty(a,\"later\",{value:2,enumerable:true,configurable:true});const c:any=structuredClone(a);console.log(c.first,c.later,Object.hasOwn(c,\"later\"));", "1 2 true\n"];
        yield return ["added-key-not-in-snapshot", "const a:any={};Object.defineProperty(a,\"first\",{get(){a.later=2;return 1},enumerable:true});const c:any=structuredClone(a);console.log(c.first,Object.hasOwn(c,\"later\"));", "1 false\n"];
        yield return ["numeric-own-key-order", "const a:any={};a[10]=10;a[2]=2;Object.defineProperty(a,\"x\",{enumerable:true,get(){return 3}});const c:any=structuredClone(a);console.log(Object.keys(c).join(\",\"),c.x);", "2,10,x 3\n"];
        yield return ["abrupt-before-transfer-detach", "const b=new ArrayBuffer(4),error:any={tag:9};const a:any={};Object.defineProperty(a,\"x\",{enumerable:true,get(){throw error}});try{structuredClone(a,{transfer:[b]})}catch(e){console.log(e===error,b.byteLength)}", "true 4\n"];
        yield return ["callable-alias-context", "const clone=structuredClone;let calls=0;const a:any={};Object.defineProperty(a,\"x\",{get(){calls++;return 5},enumerable:true});const c:any=clone(a);console.log(c.x,calls);", "5 1\n"];
        yield return ["global-callable-context", "let calls=0;const a:any={};Object.defineProperty(a,\"x\",{get(){calls++;return 6},enumerable:true});const c:any=globalThis.structuredClone(a);console.log(c.x,calls);", "6 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void EnumerableOwnValuesAreReadOnce(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
