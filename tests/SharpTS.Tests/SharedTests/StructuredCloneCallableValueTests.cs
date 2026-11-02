using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneCallableValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["aliased-clone-transfer-outside-value", "const f:any=structuredClone;const b:any=new ArrayBuffer(2);console.log(f(7,{transfer:[b]}),b.byteLength);", "7 0\n"];
        yield return ["global-escape-clone", "const f:any=(globalThis as any)['structuredClone'];const b:any=new ArrayBuffer(2);const c:any=f(b,{transfer:[b]});console.log(b.byteLength,c.byteLength);", "0 2\n"];
        yield return ["bare-alias-copy", "const f:any=structuredClone;const a:any={x:7};const b:any=f(a);console.log(b.x,a===b);", "7 false\n"];
        yield return ["global-dot-copy", "const root:any=globalThis;const a:any={x:8};const b:any=root.structuredClone(a);console.log(b.x,a===b);", "8 false\n"];
        yield return ["computed-name", "const root:any=globalThis;const key=\"structured\"+\"Clone\";const f:any=root[key];console.log(f(9));", "9\n"];
        yield return ["callable-metadata", "const f:any=structuredClone;const g:any=(globalThis as any).structuredClone;console.log(typeof f,typeof g,f===g,f.name,f.length,g===g);", "function function true structuredClone 2 true\n"];
        yield return ["returned-callable", "function pick(root:any){return root.structuredClone}const f:any=pick(globalThis);console.log(typeof f,f(11));", "function 11\n"];
        yield return ["array-held-callable", "const functions:any[]=[structuredClone,(globalThis as any).structuredClone];console.log(functions[0](3),functions[1](4),functions[0]===functions[1]);", "3 4 true\n"];
        yield return ["call-receiver", "const f:any=(globalThis as any).structuredClone;console.log(f.call({x:1},12));", "12\n"];
        yield return ["apply-receiver", "const f:any=(globalThis as any).structuredClone;console.log(f.apply(null,[13]));", "13\n"];
        yield return ["bound-receiver", "const f:any=(globalThis as any).structuredClone;const g:any=f.bind({x:1});console.log(g(14));", "14\n"];
        yield return ["argument-order", "let trace=\"\";const f:any=(globalThis as any).structuredClone;function value(){trace+=\"v\";return 15}function options(){trace+=\"o\";return {transfer:[]}}console.log(f(value(),options()),trace);", "15 vo\n"];
        yield return ["undefined-value", "const f:any=structuredClone;console.log(f(undefined)===undefined,f(null)===null);", "true true\n"];
        yield return ["global-override", "const root:any=globalThis;const saved:any=root.structuredClone;root.structuredClone=(x:any)=>x+1;console.log(root.structuredClone(7),saved(7));", "8 7\n"];
        yield return ["global-accessor", "const root:any=globalThis;const saved:any=root.structuredClone;let reads=0;Object.defineProperty(root,\"structuredClone\",{get(){reads++;return saved},configurable:true});console.log(root.structuredClone(16),reads);", "16 1\n"];
        yield return ["global-delete", "const root:any=globalThis;const saved:any=root.structuredClone;console.log(delete root.structuredClone,typeof root.structuredClone,saved(17));", "true undefined 17\n"];
        yield return ["ordinary-negative", "const obj:any={structuredClone:3};console.log(typeof obj.structuredClone);", "number\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void CloneIsAnOrdinaryGlobalCallable(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
