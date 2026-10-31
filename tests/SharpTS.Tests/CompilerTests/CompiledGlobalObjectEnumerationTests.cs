using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class CompiledGlobalObjectEnumerationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var row in GlobalObjectEnumeration3236Tests.Cases()) yield return row;
        yield return ["mixed-assignment-definition-order", @"const g:any=globalThis;g.__p2_a__=1;Object.defineProperty(g,""__p2_b__"",{value:2,enumerable:true,configurable:true});g.__p2_c__=3;console.log(Object.keys(g).filter(k=>k.startsWith(""__p2_"")).join("",""));", "__p2_a__,__p2_b__,__p2_c__\n"];
        yield return ["delete-recreate-order", @"const g:any=globalThis;g.__p2_a__=1;g.__p2_b__=2;delete g.__p2_a__;g.__p2_a__=3;console.log(Object.entries(g).filter(e=>e[0].startsWith(""__p2_"")).map(e=>e[0]+"":""+e[1]).join("",""));", "__p2_b__:2,__p2_a__:3\n"];
        yield return ["live-enumerability", @"const g:any=globalThis;Object.defineProperty(g,""__p2_first__"",{get(){Object.defineProperty(g,""__p2_later__"",{enumerable:true});return 1;},enumerable:true,configurable:true});Object.defineProperty(g,""__p2_later__"",{value:2,enumerable:false,configurable:true});console.log(Object.entries(g).filter(e=>e[0].startsWith(""__p2_"")).map(e=>e[0]+"":""+e[1]).join("",""));", "__p2_first__:1,__p2_later__:2\n"];
        yield return ["abrupt-getter", @"const g:any=globalThis;const marker={};let reads=0;Object.defineProperty(g,""__p2_first__"",{get(){throw marker;},enumerable:true,configurable:true});Object.defineProperty(g,""__p2_later__"",{get(){reads++;return 2;},enumerable:true,configurable:true});try{Object.values(g);}catch(e){console.log(e===marker,reads);}", "true 0\n"];
        yield return ["intrinsic-attributes-and-recreation", @"const g:any=globalThis;const original=g.parseInt;g.parseInt=()=>7;const first:any=Object.getOwnPropertyDescriptor(g,""parseInt"");console.log(first.writable,first.enumerable,first.configurable,first.value===g.parseInt);delete g.parseInt;g.parseInt=original;console.log(Object.getOwnPropertyDescriptor(g,""parseInt"")!.enumerable);", "true false true true\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EnumeratesActualGlobalRecords(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}