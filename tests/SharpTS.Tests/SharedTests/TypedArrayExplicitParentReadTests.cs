using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayExplicitParentReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-reflect-inherited-data", "const a:any=new Uint8Array([1]),p:any={tag:3};Object.setPrototypeOf(a,p);console.log(a.tag,Reflect.get(a,\"tag\"),Object.hasOwn(a,\"tag\"),a[0]);", "3 3 false 1\n"];
        yield return ["inherited-readonly-original", "const a:any=new Uint8Array([1]),p:any={};Object.defineProperty(p,\"tag\",{value:3,writable:false});Object.setPrototypeOf(a,p);a.tag=7;console.log(a.tag,Object.hasOwn(a,\"tag\"),a[0]);\n", "3 false 1\n"];
        yield return ["inherited-getter-receiver", "const a:any=new Uint8Array([1]),p:any={};Object.defineProperty(p,\"tag\",{get(){return this===a?7:9}});Object.setPrototypeOf(a,p);console.log(a.tag,Reflect.get(a,\"tag\"));", "7 7\n"];
        yield return ["inherited-getter-abrupt", "const a:any=new Uint8Array(1),e:any={};const p:any={};Object.defineProperty(p,\"tag\",{get(){throw e}});Object.setPrototypeOf(a,p);try{console.log(a.tag)}catch(x){console.log(x===e)}", "true\n"];
        yield return ["inherited-getterless", "const a:any=new Uint8Array(1),p:any={};Object.defineProperty(p,\"tag\",{set(v:any){}});Object.setPrototypeOf(a,p);console.log(a.tag===undefined,Object.hasOwn(a,\"tag\"));", "true false\n"];
        yield return ["inherited-undefined-data", "const a:any=new Uint8Array(1),p:any={tag:undefined};Object.setPrototypeOf(a,p);console.log(a.tag===undefined,\"tag\" in a,Object.hasOwn(a,\"tag\"));", "true true false\n"];
        yield return ["parent-null-terminates", "const a:any=new Uint8Array([7]);Object.setPrototypeOf(a,null);console.log(a.tag===undefined,a.length===undefined,a.slice===undefined,a[0]);", "true true true 7\n"];
        yield return ["ancestor-chain", "const a:any=new Uint8Array([7]),q:any={tag:3},p:any=Object.create(q);Object.setPrototypeOf(a,p);console.log(a.tag,a[0],Object.hasOwn(a,\"tag\"));", "3 7 false\n"];
        yield return ["live-parent-value", "const a:any=new Uint8Array(1),p:any={tag:3};Object.setPrototypeOf(a,p);console.log(a.tag);p.tag=8;console.log(a.tag);", "3\n8\n"];
        yield return ["live-parent-replacement", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{tag:3});console.log(a.tag);Object.setPrototypeOf(a,{tag:8});console.log(a.tag);", "3\n8\n"];
        yield return ["own-data-wins", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"tag\",{value:4});Object.setPrototypeOf(a,{tag:3});console.log(a.tag,Object.hasOwn(a,\"tag\"));", "4 true\n"];
        yield return ["own-undefined-wins", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"tag\",{value:undefined});Object.setPrototypeOf(a,{tag:3});console.log(a.tag===undefined,Object.hasOwn(a,\"tag\"));", "true true\n"];
        yield return ["explicit-metadata-getter-receiver", "const a:any=new Uint16Array(3),p:any={};Object.defineProperty(p,\"length\",{get(){return this===a?13:99}});Object.setPrototypeOf(a,p);console.log(a.length,a[0]);", "13 0\n"];
        yield return ["explicit-method-name-data", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{slice:3});console.log(a.slice);", "3\n"];
        yield return ["default-intrinsics-control", "const a:any=new Uint16Array(3);console.log(a.length,a.byteLength,a.byteOffset,a.BYTES_PER_ELEMENT,typeof a.slice);", "3 6 0 2 function\n"];
        yield return ["physical-before-parent", "const a:any=new Uint8Array([7]),p:any={};Object.defineProperty(p,\"0\",{get(){throw 99}});Object.setPrototypeOf(a,p);console.log(a[0],Reflect.get(a,\"0\"));", "7 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() =>
        Cases().SelectMany(c => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(m => new object[] { m, c[0], c[1], c[2] }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ExplicitParentsOwnInheritedReads(ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
