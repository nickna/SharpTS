using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DynamicArrayDestructureOverrideTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const source:any=[1,2];source[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "original-alias", """
            const source=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "typed", """
            const source=[1,2];source[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "key-alias", """
            const source:any=[1,2];const key=Symbol.iterator;source[key]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "tuple-alias", """
            const source:[number,number]=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const dynamic:any=source;const [a,...rest]=dynamic;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "captured-next", """
            const source:any=[1,2];let gets=0;source[Symbol.iterator]=()=>{let n=0;return {get next(){gets++;return ()=>({value:++n*4,done:n>2});}};};const [a,...rest]=source;console.log(a,rest.join(","),gets);
            """, "4 8 1\n" };
        yield return new object[] { "empty-override", """
            const source:any=[1,2];source[Symbol.iterator]=function*(){};const [a=7,...rest]=source;console.log(a,rest.length);
            """, "7 0\n" };
        yield return new object[] { "fresh-factory", """
            const source:any=[1,2];let calls=0;source[Symbol.iterator]=function*(){calls++;yield calls;yield calls+4;};const [...a]=source;const [...b]=source;console.log(a.join(","),b.join(","),calls);
            """, "1,5 2,6 2\n" };
        yield return new object[] { "factory-throws", """
            const source:any=[1,2];const marker={};source[Symbol.iterator]=()=>{throw marker;};try{const [...values]=source;console.log("accepted");}catch(e){console.log(e===marker);}
            """, "true\n" };
        yield return new object[] { "invalid-methods", """
            for(const method of [null,4,false]){const source:any=[1,2];source[Symbol.iterator]=method;try{const [...values]=source;console.log("accepted");}catch(e){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "ordinary-array", """
            const source:any=[1,2,3];const [a,...rest]=source;console.log(a,rest.join(","));const [...copy]=source;console.log(copy===source,copy.join(","));
            """, "1 2,3\nfalse 1,2,3\n" };
        yield return new object[] { "unrelated-array", """
            const other:any=[5,6];other[Symbol.iterator]=function*(){yield 9;};const source:any=[1,2,3];const [a,...rest]=source;console.log(a,rest.join(","));
            """, "1 2,3\n" };
    }

    public static IEnumerable<object[]> CompiledOnlyCases()
    {
        yield return new object[] { "getter-receiver", """
            const source:any=[1,2];let getters=0;let factories=0;Object.defineProperty(source,Symbol.iterator,{get(){getters++;return function*(){factories++;yield this===source?8:0;yield 9;};}});const [a,...rest]=source;console.log(a,rest.join(","),getters,factories);
            """, "8 9 1 1\n" };
    }

    public static IEnumerable<object[]> AllCases() => Cases().Concat(CompiledOnlyCases());
    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> CompiledOnlySourceCases() => CompiledOnlyCases().Select(row => row[1..]);

    [Theory, MemberData(nameof(CompiledOnlySourceCases))]
    public void CompiledDynamicArrayDestructuringSelectsIteratorGetterOnce(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void DynamicArrayDestructuringHonorsObservableIteratorOverride(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
