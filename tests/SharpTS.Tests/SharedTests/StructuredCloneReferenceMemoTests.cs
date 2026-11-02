using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneReferenceMemoTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["shared-plain-record-original", "const child:any={n:1};const value:any=structuredClone({left:child,right:child});console.log(value.left===value.right,value.left.n);", "true 1\n"];
        yield return ["shared-array-child", "const child:any={n:2};const c:any=structuredClone([child,child]);console.log(c[0]===c[1],c[0]!==child,c[0].n);", "true true 2\n"];
        yield return ["map-key-value-identity", "const child:any={n:3};const m=new Map<any,any>();m.set(child,child);const c:any=structuredClone({child,m});console.log(c.m.get(c.child)===c.child,c.child!==child);", "true true\n"];
        yield return ["set-object-identity", "const child:any={n:4};const c:any=structuredClone({child,set:new Set<any>([child])});console.log(c.set.has(c.child),c.child!==child);", "true true\n"];
        yield return ["object-self-cycle", "const a:any={n:5};a.self=a;const c:any=structuredClone(a);console.log(c!==a,c.self===c,c.n);", "true true 5\n"];
        yield return ["mutual-object-cycle", "const a:any={},b:any={};a.next=b;b.prev=a;const c:any=structuredClone(a);console.log(c.next!==b,c.next.prev===c);", "true true\n"];
        yield return ["array-self-cycle", "const a:any[]=[];a.push(a);const c:any=structuredClone(a);console.log(c!==a,c[0]===c);", "true true\n"];
        yield return ["map-self-cycle", "const a=new Map<any,any>();a.set(\"self\",a);const c:any=structuredClone(a);console.log(c!==a,c.get(\"self\")===c);", "true true\n"];
        yield return ["set-self-cycle", "const a=new Set<any>();a.add(a);const c:any=structuredClone(a);console.log(c!==a,c.has(c));", "true true\n"];
        yield return ["per-invocation-memo", "const a:any={n:6};const x:any=structuredClone(a),y:any=structuredClone(a);console.log(x!==y,x!==a,y!==a);", "true true true\n"];
        yield return ["date-alias", "const a:any=new Date(123);const c:any=structuredClone({a,b:a});console.log(c.a===c.b,c.a!==a,c.a.getTime());", "true true 123\n"];
        yield return ["regexp-alias", "const a:any=/ab/gi;const c:any=structuredClone({a,b:a});console.log(c.a===c.b,c.a!==a,c.a.source,c.a.flags);", "true true ab gi\n"];
        yield return ["error-alias", "const a:any=new TypeError(\"kept\");const c:any=structuredClone({a,b:a});console.log(c.a===c.b,c.a!==a,c.a.name,c.a.message);", "true true TypeError kept\n"];
        yield return ["array-buffer-alias", "const a=new ArrayBuffer(4);const c:any=structuredClone({a,b:a});console.log(c.a===c.b,c.a!==a,c.a.byteLength);", "true true 4\n"];
        yield return ["typed-view-alias", "const a:any=new Uint8Array([7]);const c:any=structuredClone({a,b:a});console.log(c.a===c.b,c.a!==a,c.a[0]);", "true true 7\n"];
        yield return ["rejection-after-cycle", "const a:any={};a.self=a;a.fn=()=>{};try{structuredClone(a)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OneCallPreservesReferenceIdentity(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
