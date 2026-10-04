using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IteratorFromArrayTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const a:any=[1,2];const wrapped:any=Iterator.from(a);console.log(wrapped===a,Iterator.from(wrapped)===wrapped,wrapped.next().value);
            """, "false true 1\n" };
        yield return new object[] { "original-identity", """
            const a:any=[1,2];const wrapped:any=Iterator.from(a);console.log(wrapped===a,Iterator.from(wrapped)===wrapped);
            """, "false true\n" };
        yield return new object[] { "original-stage", """
            const a:any=[1,2];const wrapped:any=Iterator.from(a);console.log("before-next");try{const result=wrapped.next();console.log("next",result.value);}catch(e){console.log("caught",e.name,e.message);}
            """, "before-next\nnext 1\n" };
        yield return new object[] { "values-and-exhaustion", """
            const a:any=[1,2];const wrapped=Iterator.from(a);console.log(wrapped.next().value,wrapped.next().value,wrapped.next().done);a.push(3);const result=wrapped.next();console.log(result.done,result.value===undefined);console.log(Iterator.from(a).toArray().join(","));
            """, "1 2 true\ntrue true\n1,2,3\n" };
        yield return new object[] { "live-growth", """
            const a:any=[1];const wrapped=Iterator.from(a);console.log(wrapped.next().value);a.push(2,3);console.log(wrapped.next().value,wrapped.toArray().join(","));
            """, "1\n2 3\n" };
        yield return new object[] { "live-shrink", """
            const a:any=[1,2,3];const wrapped=Iterator.from(a);console.log(wrapped.next().value);a.length=1;console.log(wrapped.next().done);a.push(4);console.log(wrapped.next().done,Iterator.from(a).toArray().join(","));
            """, "1\ntrue\ntrue 1,4\n" };
        yield return new object[] { "indexed-getters", """
            const a:any=[1,2];let reads=0;Object.defineProperty(a,"0",{get(){reads++;a.push(3);return 7;},configurable:true});const wrapped=Iterator.from(a);console.log(reads);console.log(wrapped.toArray().join(","),reads);
            """, "0\n7,2,3 1\n" };
        yield return new object[] { "getter-throws", """
            const marker={tag:7};const a:any=[1];Object.defineProperty(a,"0",{get(){throw marker;}});const wrapped=Iterator.from(a);try{wrapped.next();}catch(e:any){console.log(e===marker);}
            """, "true\n" };
        yield return new object[] { "custom-iterator", """
            const a:any=[1,2];let calls=0;a[Symbol.iterator]=function(){console.log(this===a);calls++;let n=0;return {next(){n++;return {value:n*7,done:n>2};}};};const wrapped=Iterator.from(a);console.log(calls,wrapped===a,Iterator.from(wrapped)===wrapped);console.log(wrapped.toArray().join(","),calls);
            """, "true\n1 false true\n7,14 1\n" };
        yield return new object[] { "iterator-getter", """
            const a:any=[1,2];let gets=0;let calls=0;Object.defineProperty(a,Symbol.iterator,{get(){gets++;return function(){calls++;return [8,9].values();};}});const wrapped=Iterator.from(a);console.log(gets,calls);console.log(wrapped.toArray().join(","),gets,calls);
            """, "1 1\n8,9 1 1\n" };
        yield return new object[] { "generator-identity", """
            function* values(){yield 3;yield 4;}const source:any=values();const wrapped=Iterator.from(source);console.log(wrapped===source,Iterator.from(wrapped)===wrapped,wrapped.next().value,wrapped.toArray().join(","));
            """, "true true 3 4\n" };
        yield return new object[] { "helper-identity", """
            const source:any=Iterator.from([1,2]).map((n:any)=>n*3);const wrapped=Iterator.from(source);console.log(wrapped===source,wrapped.next().value,wrapped.toArray().join(","));
            """, "true 3 6\n" };
        yield return new object[] { "captured-next", """
            const source:any={n:0,next(){this.n++;return {value:this.n,done:this.n>2};}};const wrapped=Iterator.from(source);source.next=()=>({value:99,done:false});console.log(wrapped===source,Iterator.from(wrapped)===wrapped,wrapped.next().value,wrapped.toArray().join(","));
            """, "false true 1 2\n" };
    }

    // A separate dynamic helper-dispatch gap is retained outside passing data.
    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "any-toarray-independent", """
            const wrapped:any=Iterator.from([1,2]);console.log(wrapped.toArray().join(","));
            """, "1,2\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledFromAdaptsArraysAndPreservesCompatibleIteratorIdentity(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
