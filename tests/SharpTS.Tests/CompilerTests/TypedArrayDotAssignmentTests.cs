using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class TypedArrayDotAssignmentTests
{
    public static IEnumerable<object[]> AllCases()
    {
        yield return ["fresh-dot-own-presence", "const a:any=new Uint8Array([1]);a.foo=7;console.log(Object.hasOwn(a,\"foo\"),a[0]);\n", "true 1\n", true];
        yield return ["fresh-dot-name", "const v:any=new Uint8Array([1]); v.foo=7; console.log(v.foo,v[0]);\n", "7 1\n", true];
        yield return ["fresh-bracket-name", "const v:any=new Uint8Array([1]); v[\"01\"]=8; console.log(v[\"01\"],v[0],v[1]);\n", "8 1 undefined\n", false];
        yield return ["dot-assignment-result", "const a:any=new Uint8Array([1]),v={x:7};const result=a.tag=v;const d=Object.getOwnPropertyDescriptor(a,\"tag\")!;console.log(result===v,a.tag===v,d.writable,d.enumerable,d.configurable,a[0]);\n", "true true true true true 1\n", true];
        yield return ["ordinary-bracket-spellings", "const a:any=new Uint8Array([1]);a[\"\"]=3;a[\"1.0\"]=4;a[\"01\"]=5;console.log(a[\"\"],a[\"1.0\"],a[\"01\"],a[0],a[1]);\n", "3 4 5 1 undefined\n", false];
        yield return ["dot-evaluate-once", "const a:any=new Uint8Array([1]);let bases=0,rhs=0;function base(){bases++;return a}function value(){rhs++;return 9}base().tag=value();console.log(bases,rhs,a.tag,a[0]);\n", "1 1 9 1\n", true];
        yield return ["bracket-evaluation-order", "const a:any=new Uint8Array([1]);let order=\"\";function base(){order+=\"b\";return a}function key(){order+=\"k\";return \"tag\"}function value(){order+=\"v\";return 9}base()[key()]=value();console.log(order,Reflect.get(a,\"tag\"),a[0]);\n", "bkv 9 1\n", false];
        yield return ["prevented-sloppy-dot", "const a:any=new Uint8Array([1]);Object.preventExtensions(a);a.tag=7;console.log(Object.hasOwn(a,\"tag\"),a[0]);\n", "false 1\n", true];
        yield return ["prevented-strict-dot", "\"use strict\";const a:any=new Uint8Array([1]);Object.preventExtensions(a);try{a.tag=7}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,\"tag\"),a[0]);\n", "true\nfalse 1\n", true];
        yield return ["owned-readonly-sloppy", "const a:any=new Uint8Array([1]);Object.defineProperty(a,\"tag\",{value:3,writable:false});a.tag=7;console.log(a.tag,a[0]);\n", "3 1\n", true];
        yield return ["owned-getter-no-write-get", "const a:any=new Uint8Array([1]);let gets=0;Object.defineProperty(a,\"tag\",{get(){gets++;return 3}});a.tag=7;console.log(gets,Object.hasOwn(a,\"tag\"),a[0]);\n", "0 true 1\n", true];
        yield return ["inherited-setter-receiver", "const a:any=new Uint8Array([1]);let calls=0,seen:any;Object.setPrototypeOf(a,{set tag(v:any){calls++;seen=this;console.log(v)}});a.tag=7;console.log(calls,seen===a,Object.hasOwn(a,\"tag\"),a[0]);\n", "7\n1 true false 1\n", true];
        yield return ["inherited-readonly", "const a:any=new Uint8Array([1]),p:any={};Object.defineProperty(p,\"tag\",{value:3,writable:false});Object.setPrototypeOf(a,p);a.tag=7;console.log(a.tag,Object.hasOwn(a,\"tag\"),a[0]);\n", "3 false 1\n", true];
        yield return ["explicit-empty-parent", "const a:any=new Uint8Array([1]);Object.setPrototypeOf(a,{});a.tag=7;console.log(a.tag,Object.hasOwn(a,\"tag\"),a[0]);\n", "7 true 1\n", true];
        yield return ["null-parent-dot", "const a:any=new Uint8Array([1]);Object.setPrototypeOf(a,null);a.tag=7;console.log(a.tag,Object.hasOwn(a,\"tag\"),a[0]);\n", "7 true 1\n", true];
        yield return ["implicit-metadata-sloppy-dot", "const a:any=new Uint8Array([1]),buffer=a.buffer;a.length=9;a.byteLength=9;a.byteOffset=9;a.buffer=9;a.BYTES_PER_ELEMENT=9;console.log(a.length,a.byteLength,a.byteOffset,a.buffer===buffer,a.BYTES_PER_ELEMENT,Object.hasOwn(a,\"byteLength\"));\n", "1 1 0 true 1 false\n", true];
        yield return ["implicit-metadata-strict-dot", "const a:any=new Uint8Array([1]);function one(){\"use strict\";a.byteLength=9}try{one()}catch(e){console.log(e instanceof TypeError)}console.log(a.byteLength,Object.hasOwn(a,\"byteLength\"));\n", "true\n1 false\n", true];
        yield return ["owned-metadata-write", "const a:any=new Uint8Array([1]);Object.defineProperty(a,\"byteLength\",{value:7,writable:true,configurable:true});a.byteLength=9;console.log(a.byteLength,Object.hasOwn(a,\"byteLength\"),a[0]);\n", "9 true 1\n", true];
        yield return ["abrupt-inherited-setter", "const a:any=new Uint8Array([1]),marker={};Object.setPrototypeOf(a,{set tag(v:any){throw marker}});try{a.tag=7}catch(e){console.log(e===marker)}console.log(Object.hasOwn(a,\"tag\"),a[0]);\n", "true\nfalse 1\n", true];
        yield return ["physical-and-symbol-parent-controls", "const a:any=new Uint8Array([1]),s=Symbol(\"s\");a[\"0\"]=7;a[s]=9;console.log(a[0],a[\"0\"],a[s]);\n", "7 7 9\n", true];
        yield return ["ordinary-carrier-controls", "const o:any={},a:any=[];o.tag=7;a.tag=8;console.log(o.tag,a.tag,a.length);\n", "7 8 0\n", true];
    }

    public static IEnumerable<object[]> Cases() => AllCases().Where(row => (bool)row[3])
        .Select(row => new object[] { row[0], row[1], row[2] });

    [Theory, MemberData(nameof(Cases))]
    public void DotAssignmentsUseOrdinarySet(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
