using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class TypedArrayPrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["object-prototype-override", "const v:any=new Uint8Array(3);Object.setPrototypeOf(v,Object.prototype);console.log(Reflect.get(v,'length')===undefined);", "true\n"];
        yield return ["same-explicit-after-prevention", "const v:any=new Uint8Array(3),p={length:7};Object.setPrototypeOf(v,p);Object.preventExtensions(v);console.log(Object.setPrototypeOf(v,p)===v,Reflect.get(v,'length'));", "true 7\n"];
        yield return ["different-prototype-after-prevention", "const v:any=new Uint8Array(3);Object.preventExtensions(v);let caught=false;try{Object.setPrototypeOf(v,Object.prototype)}catch(e){caught=e instanceof TypeError}console.log(caught,Reflect.get(v,'length'));", "true 3\n"];
        yield return ["actual-object-prototype-link", "const v:any=new Uint8Array(3);console.log(Object.setPrototypeOf(v,Object.prototype)===v,Object.getPrototypeOf(v)===Object.prototype,v.length===undefined);", "true true true\n"];
        yield return ["reflect-nonextensible-refusal", "const v:any=new Uint8Array(3);Object.preventExtensions(v);console.log(Reflect.setPrototypeOf(v,Object.prototype),Reflect.get(v,\"length\"));", "false 3\n"];
        yield return ["same-object-link-after-prevention", "const v:any=new Uint8Array(3);Object.setPrototypeOf(v,Object.prototype);Object.preventExtensions(v);console.log(Object.setPrototypeOf(v,Object.prototype)===v,Reflect.setPrototypeOf(v,Object.prototype),v.length===undefined);", "true true true\n"];
        yield return ["same-null-link-after-prevention", "const v:any=new Uint8Array(3);Object.setPrototypeOf(v,null);Object.preventExtensions(v);console.log(Reflect.setPrototypeOf(v,null),Object.setPrototypeOf(v,null)===v,v.length===undefined);", "true true true\n"];
        yield return ["own-length-and-elements-retained", "const v:any=new Uint8Array(3);Object.defineProperty(v,\"length\",{value:7});Object.setPrototypeOf(v,Object.prototype);v[0]=9;console.log(v.length,Reflect.get(v,\"length\"),v[0]);", "7 7 9\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PrototypeChangesRequireAnActualEqualLinkForNoOp(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunCompiled(source));
    }
}
