using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypeofSymbolConditionalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [ExecutionMode.Interpreted, "function f(k:string|symbol):string{return typeof k==='symbol'?'S':k}console.log(f('x'),f(Symbol('x')));", "x S\n"];
        yield return [ExecutionMode.Compiled, "function f(k:string|symbol):string{return typeof k==='symbol'?'S':k}console.log(f('x'),f(Symbol('x')));", "x S\n"];
        yield return [ExecutionMode.Interpreted, "const s=Symbol();const keys:(string|symbol)[]=['a',s,'b'];let out='';for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s||typeof k==='string')out+=typeof k==='symbol'?'S':k}console.log(out);", "aSb\n"];
        yield return [ExecutionMode.Compiled, "const s=Symbol();const keys:(string|symbol)[]=['a',s,'b'];let out='';for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s||typeof k==='string')out+=typeof k==='symbol'?'S':k}console.log(out);", "aSb\n"];
        yield return [ExecutionMode.Interpreted, "const key=Symbol('k');function f(k:typeof key|string):string{return typeof k!=='symbol'?k:'S'}console.log(f(key),f('x'));", "S x\n"];
        yield return [ExecutionMode.Compiled, "const key=Symbol('k');function f(k:typeof key|string):string{return typeof k!=='symbol'?k:'S'}console.log(f(key),f('x'));", "S x\n"];
        yield return [ExecutionMode.Interpreted, "function f(o:{key:string|symbol}):string{return typeof o.key==='symbol'?'S':o.key}console.log(f({key:'x'}),f({key:Symbol()}));", "x S\n"];
        yield return [ExecutionMode.Compiled, "function f(o:{key:string|symbol}):string{return typeof o.key==='symbol'?'S':o.key}console.log(f({key:'x'}),f({key:Symbol()}));", "x S\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void MapsSymbolValuesToStringsWithoutChangingRuntimeValues(ExecutionMode mode, string source, string expected)
        => Assert.Equal(expected, TestHarness.Run(source, mode));
}
