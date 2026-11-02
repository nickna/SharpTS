using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public sealed class TypedArrayPropertyPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-undefined-data", "const a:any=new Uint8Array(1),p:any={tag:undefined};Object.setPrototypeOf(a,p);console.log(a.tag===undefined,\"tag\" in a,Object.hasOwn(a,\"tag\"));", "true true false\n"];
        yield return ["own-undefined", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"tag\",{value:undefined});console.log(\"tag\" in a,Object.hasOwn(a,\"tag\"));\n", "true true\n"];
        yield return ["own-null", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"tag\",{value:null});console.log(\"tag\" in a,Object.hasOwn(a,\"tag\"));\n", "true true\n"];
        yield return ["own-getter-no-invoke", "const a:any=new Uint8Array(1);let calls=0;Object.defineProperty(a,\"tag\",{get(){calls++;throw 9}});console.log(\"tag\" in a,calls);\n", "true 0\n"];
        yield return ["own-setter-only", "const a:any=new Uint8Array(1);let calls=0;Object.defineProperty(a,\"tag\",{set(v:any){calls++}});console.log(\"tag\" in a,calls);\n", "true 0\n"];
        yield return ["inherited-getter-no-invoke", "const a:any=new Uint8Array(1),p:any={};let calls=0;Object.defineProperty(p,\"tag\",{get(){calls++;throw 9}});Object.setPrototypeOf(a,p);console.log(\"tag\" in a,calls);\n", "true 0\n"];
        yield return ["inherited-setter-only", "const a:any=new Uint8Array(1),p:any={};let calls=0;Object.defineProperty(p,\"tag\",{set(v:any){calls++}});Object.setPrototypeOf(a,p);console.log(\"tag\" in a,calls);\n", "true 0\n"];
        yield return ["inherited-null", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{tag:null});console.log(\"tag\" in a,Object.hasOwn(a,\"tag\"));\n", "true false\n"];
        yield return ["ancestor-undefined", "const a:any=new Uint8Array(1),q:any={tag:undefined},p:any=Object.create(q);Object.setPrototypeOf(a,p);console.log(\"tag\" in a,Object.hasOwn(a,\"tag\"));\n", "true false\n"];
        yield return ["live-parent-delete", "const a:any=new Uint8Array(1),p:any={tag:undefined};Object.setPrototypeOf(a,p);console.log(\"tag\" in a);delete p.tag;console.log(\"tag\" in a);\n", "true\nfalse\n"];
        yield return ["live-parent-replace", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{tag:undefined});console.log(\"tag\" in a);Object.setPrototypeOf(a,{});console.log(\"tag\" in a);\n", "true\nfalse\n"];
        yield return ["null-parent-and-physical", "const a:any=new Uint8Array([7]);Object.setPrototypeOf(a,null);console.log(\"tag\" in a,\"length\" in a,\"slice\" in a,\"0\" in a);\n", "false false false true\n"];
        yield return ["invalid-canonical-parent-is-ignored", "const a:any=new Uint8Array([7]),p:any={\"1\":undefined,\"-0\":undefined,\"0.5\":undefined,NaN:undefined,Infinity:undefined};Object.setPrototypeOf(a,p);console.log(\"0\" in a,\"1\" in a,\"-0\" in a,\"0.5\" in a,\"NaN\" in a,\"Infinity\" in a);\n", "true false false false false false\n"];
        yield return ["ordinary-numeric-spellings-parent", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{\"01\":undefined,\"1.0\":undefined});console.log(\"01\" in a,\"1.0\" in a,Object.hasOwn(a,\"01\"));\n", "true true false\n"];
        yield return ["default-intrinsics", "const a:any=new Uint16Array(1);console.log(\"length\" in a,\"byteLength\" in a,\"byteOffset\" in a,\"buffer\" in a,\"BYTES_PER_ELEMENT\" in a,\"slice\" in a);\n", "true true true true true true\n"];
        yield return ["symbol-presence-parent-control", "const a:any=new Uint8Array(1),s=Symbol(\"s\");Object.setPrototypeOf(a,{[s]:undefined});console.log(s in a,Object.hasOwn(a,s));\n", "true false\n"];
        yield return ["proxy-parent-has-only", "const a:any=new Uint8Array(1);let has=0,gets=0;const p:any=new Proxy({},{has(t:any,k:any){has++;return k===\"tag\"},get(){gets++;throw 9}});Object.setPrototypeOf(a,p);console.log(\"tag\" in a,has,gets);\n", "true 1 0\n"];
        yield return ["prevented-own-undefined", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"tag\",{value:undefined});Object.preventExtensions(a);console.log(\"tag\" in a,Object.hasOwn(a,\"tag\"));\n", "true true\n"];
        yield return ["bigint-physical-parent", "const a:any=new BigInt64Array([7n]);Object.setPrototypeOf(a,{tag:undefined,\"1\":undefined});console.log(\"tag\" in a,\"0\" in a,\"1\" in a);\n", "true true false\n"];
        yield return ["ordinary-object-control", "const o:any={tag:undefined},a:any=[];Object.defineProperty(a,\"tag\",{set(v:any){}});console.log(\"tag\" in o,\"tag\" in a,\"missing\" in o);\n", "true true false\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PresenceUsesDescriptorsWithoutReadingValues(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
