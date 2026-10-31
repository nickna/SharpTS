using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalObjectEnumeration3236Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const target:any=globalThis;target.__p2_enumerable__=7;console.log(Object.keys(target).includes('__p2_enumerable__'),Object.values(target).includes(7),Object.entries(target).some(entry=>entry[0]==='__p2_enumerable__'&&entry[1]===7));delete target.__p2_enumerable__;", "true true true\n"];
        yield return ["getter-hidden-and-delete", "const g:any=globalThis;let reads=0;Object.defineProperty(g,'__p2_accessor__',{get(){reads++;return 7;},enumerable:true,configurable:true});Object.defineProperty(g,'__p2_hidden__',{value:9,enumerable:false,configurable:true});const keys=Object.keys(g);console.log(keys.includes('__p2_accessor__'),keys.includes('__p2_hidden__'),reads);const entry=Object.entries(g).find(entry=>entry[0]==='__p2_accessor__');console.log(entry![1],reads);delete g.__p2_accessor__;delete g.__p2_hidden__;console.log(Object.keys(g).includes('__p2_accessor__'));", "true false 0\n7 1\nfalse\n"];
        yield return ["live-records-with-one-key-snapshot", "const g:any=globalThis;let reads=0;Object.defineProperty(g,'__p2_first__',{get(){reads++;g.__p2_late__=9;delete g.__p2_removed__;return 7;},enumerable:true,configurable:true});g.__p2_removed__=8;const entries=Object.entries(g).filter(entry=>entry[0].startsWith('__p2_'));console.log(entries.map(entry=>entry[0]+':'+entry[1]).join(','),reads);console.log(Object.keys(g).filter(key=>key.startsWith('__p2_')).sort().join(','));delete g.__p2_first__;delete g.__p2_late__;", "__p2_first__:7 1\n__p2_first__,__p2_late__\n"];
        yield return ["numeric-and-string-key-order", "const g:any=globalThis;g.__p2_b__=3;g[2]=2;g[1]=1;g.__p2_a__=4;console.log(Object.keys(g).filter(key=>key==='1'||key==='2'||key.startsWith('__p2_')).join(','));delete g[1];delete g[2];delete g.__p2_b__;delete g.__p2_a__;", "1,2,__p2_b__,__p2_a__\n"];
        yield return ["ordinary-object-control", "const target:any={a:1};Object.defineProperty(target,'hidden',{value:9});console.log(Object.keys(target).join(','),Object.values(target).join(','),Object.entries(target).map(entry=>entry.join(':')).join(','));", "a 1 a:1\n"];
        yield return ["later-key-becomes-enumerable", "const g:any=globalThis;Object.defineProperty(g,'__p2_first__',{get(){Object.defineProperty(g,'__p2_later__',{enumerable:true});return 1;},enumerable:true,configurable:true});Object.defineProperty(g,'__p2_later__',{value:2,enumerable:false,configurable:true});console.log(Object.entries(g).filter(entry=>entry[0].startsWith('__p2_')).map(entry=>entry[0]+':'+entry[1]).join(','));delete g.__p2_first__;delete g.__p2_later__;", "__p2_first__:1,__p2_later__:2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterEnumeratesActualGlobalRecords(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
