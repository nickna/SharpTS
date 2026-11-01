using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArrayNamedPermissionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["strict-tag-original", "'use strict';const a:any=[9];Object.preventExtensions(a);try{a.tag=5;console.log('returned')}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,'tag'),a[0],a.length);", "true\nfalse 9 1\n"];
        yield return ["sloppy-tag-refuses", "const a:any=[9];Object.preventExtensions(a);a.tag=5;console.log(Object.hasOwn(a,\"tag\"),a[0],a.length);", "false 9 1\n"];
        yield return ["strict-bracket-tag-refuses", "\"use strict\";const a:any=[9];Object.preventExtensions(a);try{a[\"tag\"]=5;console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,\"tag\"),a[0]);", "true\nfalse 9\n"];
        yield return ["noncanonical-reflect-definition-refuses", "for(const key of [\"-0\",\"00\",\"01\",\"+0\"]){const a:any=[9,8];Object.preventExtensions(a);console.log(Reflect.defineProperty(a,key,{value:5,writable:true}),Object.getOwnPropertyDescriptor(a,key)===undefined,a[0],a[1]);}", "false true 9 8\nfalse true 9 8\nfalse true 9 8\nfalse true 9 8\n"];
        yield return ["noncanonical-object-definition-refuses", "const a:any=[9];Object.preventExtensions(a);try{Object.defineProperty(a,\"-0\",{value:5});console.log(\"returned\")}catch(e){console.log(e instanceof TypeError)}console.log(Object.getOwnPropertyDescriptor(a,\"-0\")===undefined,a[0]);", "true\ntrue 9\n"];
        yield return ["existing-named-writable-remains-writable", "\"use strict\";const a:any=[9];a.tag=1;Object.preventExtensions(a);a.tag=5;console.log(a.tag,a[0],Object.getOwnPropertyDescriptor(a,\"tag\")!.writable);", "5 9 true\n"];
        yield return ["sealed-existing-name-updates", "\"use strict\";const a:any=[9];a.tag=1;Object.seal(a);a.tag=5;const d=Object.getOwnPropertyDescriptor(a,\"tag\")!;console.log(d.value,d.writable,d.configurable,a[0]);", "5 true false 9\n"];
        yield return ["strict-own-setter-on-frozen-array", "\"use strict\";const a:any=[9];let value=0,receiver=false;Object.defineProperty(a,\"tag\",{set(v:any){value=v;receiver=this===a}});Object.freeze(a);a.tag=5;console.log(value,receiver,a[0]);", "5 true 9\n"];
        yield return ["strict-inherited-setter-on-nonextensible", "\"use strict\";const a:any=[9],p=Object.create(Array.prototype);let value=0,receiver=false;Object.defineProperty(p,\"tag\",{set(v:any){value=v;receiver=this===a}});Object.setPrototypeOf(a,p);Object.preventExtensions(a);a.tag=5;console.log(value,receiver,Object.hasOwn(a,\"tag\"),a[0]);", "5 true false 9\n"];
        yield return ["strict-inherited-getter-only-refuses", "\"use strict\";const a:any=[9],p=Object.create(Array.prototype);let calls=0;Object.defineProperty(p,\"tag\",{get(){calls++;return 1}});Object.setPrototypeOf(a,p);Object.preventExtensions(a);try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(calls,Object.hasOwn(a,\"tag\"),a[0]);", "true\n0 false 9\n"];
        yield return ["strict-inherited-readonly-refuses", "\"use strict\";const a:any=[9],p=Object.create(Array.prototype);Object.defineProperty(p,\"tag\",{value:1,writable:false});Object.setPrototypeOf(a,p);try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,\"tag\"),a[0]);", "true\nfalse 9\n"];
        yield return ["strict-inherited-writable-needs-own-permission", "\"use strict\";const a:any=[9],p=Object.create(Array.prototype);Object.defineProperty(p,\"tag\",{value:1,writable:true});Object.setPrototypeOf(a,p);Object.preventExtensions(a);try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,\"tag\"),a[0]);", "true\nfalse 9\n"];
        yield return ["strict-inherited-setter-on-frozen-array", "\"use strict\";const a:any=[9],p=Object.create(Array.prototype);let value=0;Object.defineProperty(p,\"tag\",{set(v:any){value=v}});Object.setPrototypeOf(a,p);Object.freeze(a);a.tag=5;console.log(value,Object.hasOwn(a,\"tag\"),a[0]);", "5 false 9\n"];
        yield return ["strict-own-getter-only-refuses-without-read", "\"use strict\";const a:any=[9];let calls=0;Object.defineProperty(a,\"tag\",{get(){calls++;return 1}});try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(calls,a[0]);", "true\n0 9\n"];
        yield return ["intrinsic-length-remains-owned", "\"use strict\";const a:any=[9];Object.preventExtensions(a);a.length=0;console.log(a.length,Object.hasOwn(a,\"tag\"),a[0]===undefined);", "0 false true\n"];
        yield return ["readonly-length-still-refuses", "\"use strict\";const a:any=[9];Object.defineProperty(a,\"length\",{writable:false});try{a.length=0}catch(e){console.log(e instanceof TypeError)}console.log(a.length,a[0]);", "true\n1 9\n"];
        yield return ["existing-physical-element-control", "\"use strict\";const a:any=[9];Object.preventExtensions(a);a[0]=5;console.log(a[0],a.length);", "5 1\n"];
        yield return ["ordinary-object-refusal-control", "\"use strict\";const a:any={};Object.preventExtensions(a);try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,\"tag\"));", "true\nfalse\n"];
        yield return ["inherited-proxy-set-retains-receiver", "\"use strict\";const a:any=[9];let calls=0,receiver=false;const p:any=new Proxy({},{set(t:any,k:any,v:any,r:any){calls++;receiver=r===a;return true}});Object.setPrototypeOf(a,p);Object.preventExtensions(a);a.tag=5;console.log(calls,receiver,Object.hasOwn(a,\"tag\"),a[0]);", "1 true false 9\n"];
        yield return ["descriptor-conversion-before-definition-refusal", "const a:any=[9];Object.preventExtensions(a);let calls=0;const d:any={get value(){calls++;return 5}};console.log(Reflect.defineProperty(a,\"-0\",d),calls,Object.getOwnPropertyDescriptor(a,\"-0\")===undefined,a[0]);", "false 1 true 9\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledNamedWritesRespectCreationPermission(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpretedNamedWritesRespectCreationPermission(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
