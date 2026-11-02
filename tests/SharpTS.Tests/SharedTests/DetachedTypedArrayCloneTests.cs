using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DetachedTypedArrayCloneTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["detached-view-original", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});try{structuredClone(a);console.log(\"returned\")}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["empty-detached", "const a:any=new Uint8Array(0);structuredClone(a.buffer,{transfer:[a.buffer]});try{structuredClone(a);console.log(\"returned\")}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["offset-detached", "const b:any=new ArrayBuffer(8),a:any=new Uint16Array(b,2,2);structuredClone(b,{transfer:[b]});try{structuredClone(a);console.log(\"returned\")}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["subarray-detached", "const a:any=new Uint8Array([7,8]),v:any=a.subarray(1);structuredClone(a.buffer,{transfer:[a.buffer]});try{structuredClone(v);console.log(\"returned\")}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["nested-detached", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});try{structuredClone({value:a});console.log(\"returned\")}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["owner-not-shadow-getter", "const a:any=new Uint8Array([7]),b:any=a.buffer;let calls=0;Object.defineProperty(a,\"buffer\",{get(){calls++;return new ArrayBuffer(1)}});structuredClone(b,{transfer:[b]});try{structuredClone(a);console.log(\"returned\")}catch(e:any){console.log(e.name,calls)}", "DataCloneError 0\n"];
        yield return ["metadata-shadow", "const a:any=new Uint8Array([7]),b:any=a.buffer;Object.defineProperty(a,\"length\",{value:9});structuredClone(b,{transfer:[b]});try{structuredClone(a);console.log(\"returned\")}catch(e:any){console.log(e.name,a.length)}", "DataCloneError 9\n"];
        yield return ["attached-copy", "const a:any=new Uint8Array([7]),c:any=structuredClone(a);c[0]=9;console.log(a[0],c[0],a!==c);", "7 9 true\n"];
        yield return ["attached-empty", "const a:any=new Uint8Array(0),c:any=structuredClone(a);console.log(a.length,c.length,a!==c);", "0 0 true\n"];
        yield return ["shared-owner", "const a:any=new Uint8Array(new SharedArrayBuffer(2));a[0]=7;const c:any=structuredClone(a);c[0]=9;console.log(a[0],c[0],a!==c);", "9 9 true\n"];
        yield return ["all-element-kinds", "const views:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];let n=0;for(const a of views){structuredClone(a.buffer,{transfer:[a.buffer]});try{structuredClone(a)}catch(e:any){if(e.name===\"DataCloneError\")n++}}console.log(n);", "11\n"];
        yield return ["attached-transfer", "const a:any=new Uint8Array([7]),c:any=structuredClone(a,{transfer:[a.buffer]});console.log(a.length,c.length,c[0]);", "0 1 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void DetachedOwnerRejectsCloneBeforeCopy(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
