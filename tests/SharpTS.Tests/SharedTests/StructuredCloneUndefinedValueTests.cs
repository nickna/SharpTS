using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneUndefinedValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-undefined", "console.log(structuredClone(undefined)===undefined,structuredClone(null)===null);", "true true\n"];
        yield return ["object-undefined", "const a:any={u:undefined,n:null};const b:any=structuredClone(a);console.log(b.u===undefined,b.n===null,b===a,Object.keys(b).join(\",\"));", "true true false u,n\n"];
        yield return ["array-undefined", "const a:any=[undefined,null];const b:any=structuredClone(a);console.log(b[0]===undefined,b[1]===null,b.length,a===b);", "true true 2 false\n"];
        yield return ["nested-undefined", "const a:any={items:[{value:undefined}]};const b:any=structuredClone(a);console.log(b.items[0].value===undefined,b===a,b.items===a.items,b.items[0]===a.items[0]);", "true false false false\n"];
        yield return ["empty-transfer-undefined", "console.log(structuredClone(undefined,{transfer:[]})===undefined);", "true\n"];
        yield return ["transfer-outside-undefined", "const b:any=new ArrayBuffer(2);console.log(structuredClone(undefined,{transfer:[b]})===undefined,b.byteLength);", "true 0\n"];
        yield return ["function-still-rejected", "try{structuredClone(()=>1)}catch(e){console.log(String(e).includes(\"DataCloneError\"))}", "true\n"];
        yield return ["symbol-still-rejected", "try{structuredClone(Symbol(\"x\"))}catch(e){console.log(String(e).includes(\"DataCloneError\"))}", "true\n"];
        yield return ["other-primitives", "const a:any=structuredClone([undefined,null,true,\"x\",2,3n]);console.log(a[0]===undefined,a[1]===null,a[2],a[3],a[4],String(a[5]));", "true true true x 2 3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void UndefinedIsCloneable(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
