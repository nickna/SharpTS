using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneCompactRecordTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-created-record-original", "class Value{left:any;constructor(){const child={n:8};this.left=child;}}const value=new Value();const copy:any=structuredClone(value.left);console.log(copy.n);", "8\n"];
        yield return ["record-reference-copy", "class Value{left:any;constructor(){const a={n:8};this.left=a;}}const a=new Value();const c:any=structuredClone(a.left);a.left.n=9;console.log(c!==a.left,c.n,a.left.n,Object.getPrototypeOf(c)===Object.prototype);", "true 8 9 true\n"];
        yield return ["record-shared-child", "class Value{a:any;b:any;constructor(){const c={n:3};this.a=c;this.b=c;}}const a=new Value();const c:any=structuredClone({a:a.a,b:a.b});console.log(c.a===c.b,c.a!==a.a,c.a.n);", "true true 3\n"];
        yield return ["record-two-fields", "class Value{a:any;constructor(){const c={n:1,text:\"kept\"};this.a=c;}}const c:any=structuredClone(new Value().a);console.log(c.n,c.text,Object.keys(c).join(\",\"));", "1 kept n,text\n"];
        yield return ["record-mixed-fields", "class Value{a:any;constructor(){const c={n:2,yes:true,text:\"x\",nothing:null};this.a=c;}}const c:any=structuredClone(new Value().a);console.log(c.n,c.yes,c.text,c.nothing,Object.keys(c).length);", "2 true x null 4\n"];
        yield return ["record-non-enumerable", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;Object.defineProperty(a,\"hidden\",{value:7,enumerable:false});const c:any=structuredClone(a);console.log(c.n,Object.hasOwn(c,\"hidden\"));", "1 false\n"];
        yield return ["record-added-property", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;a.extra=2;const c:any=structuredClone(a);console.log(c.n,c.extra,Object.keys(c).join(\",\"));", "1 2 n,extra\n"];
        yield return ["record-own-accessor", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;let calls=0;Object.defineProperty(a,\"x\",{enumerable:true,get:function(this:any){calls++;return this.n+2}});const c:any=structuredClone(a);console.log(c.x,calls,Object.getOwnPropertyDescriptor(c,\"x\")!.get===undefined);", "3 1 true\n"];
        yield return ["record-symbol-omitted", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;const s=Symbol(\"x\");a[s]=7;const c:any=structuredClone(a);console.log(c.n,Object.getOwnPropertySymbols(c).length);", "1 0\n"];
        yield return ["record-cycle", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;a.self=a;const c:any=structuredClone(a);console.log(c!==a,c.self===c,c.n);", "true true 1\n"];
        yield return ["record-map-set", "class Value{a:any;constructor(){const c={n:4};this.a=c;}}const a:any=new Value().a;const c:any=structuredClone({a,map:new Map<any,any>([[a,a]]),set:new Set<any>([a])});console.log(c.map.get(c.a)===c.a,c.set.has(c.a),c.a.n);", "true true 4\n"];
        yield return ["record-function-value-refused", "class Value{a:any;constructor(){const c={n:1};this.a=c;}}const a:any=new Value().a;a.fn=()=>{};try{structuredClone(a)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OwnedCompactObjectDataClones(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
