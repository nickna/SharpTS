using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArraySpreadDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-array", """
            const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,"1",{get(){reads++;return 8;}});const b=[...a];console.log(b.join(","),reads);
            """, "1,8,3 1\n" };
        yield return new object[] { "original-arguments", """
            const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,"1",{get(){reads++;return 8;}});function collect(...xs:any[]){console.log(xs.join(","),reads);}collect(0,...a,4);
            """, "0,1,8,3,4 1\n" };
        yield return new object[] { "original-direct", """
            const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,"1",{get(){reads++;return 8;}});console.log(a[1],reads);
            """, "8 1\n" };
        yield return new object[] { "original-values", """
            const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,"1",{get(){reads++;return 8;}});console.log(Array.from(a.values()).join(","),reads);
            """, "1,8,3 1\n" };
        yield return new object[] { "dense-getter-order", """
            const a:any[]=[1,2,3];let order="";Object.defineProperty(a,"0",{get(){order+="a";return 4;}});Object.defineProperty(a,"1",{get(){order+="b";return 5;}});console.log([...a].join(","),order);order="";function collect(...xs:any[]){console.log(xs.join(","),order);}collect(...a);
            """, "4,5,3 ab\n4,5,3 ab\n" };
        yield return new object[] { "grow-live-length", """
            const a:any[]=[1,2];Object.defineProperty(a,"0",{get(){a.push(3);return 7;}});console.log([...a].join(","));const b:any[]=[1,2];Object.defineProperty(b,"0",{get(){b.push(4);return 8;}});function collect(...xs:any[]){console.log(xs.join(","));}collect(...b);
            """, "7,2,3\n8,2,4\n" };
        yield return new object[] { "shrink-live-length", """
            const a:any[]=[1,2,3];Object.defineProperty(a,"0",{get(){a.length=1;return 7;}});console.log([...a].join(","));const b:any[]=[1,2,3];Object.defineProperty(b,"0",{get(){b.length=1;return 8;}});function collect(...xs:any[]){console.log(xs.join(","));}collect(...b);
            """, "7\n8\n" };
        yield return new object[] { "getter-throws", """
            const marker={tag:7};let reads=0;let calls=0;const a:any[]=[1,2];Object.defineProperty(a,"0",{get(){reads++;throw marker;}});try{const b=[...a];console.log("accepted");}catch(e:any){console.log(e===marker,reads);}function collect(...xs:any[]){calls++;}try{collect(...a);}catch(e:any){console.log(e===marker,reads,calls);}
            """, "true 1\ntrue 2 0\n" };
        yield return new object[] { "iterator-override", """
            const a:any=[1,2];let reads=0;Object.defineProperty(a,"0",{get(){reads++;return 9;}});a[Symbol.iterator]=function*(){yield 7;yield 8;};console.log([...a].join(","),reads);function collect(...xs:any[]){console.log(xs.join(","),reads);}collect(0,...a,3);
            """, "7,8 0\n0,7,8,3 0\n" };
        yield return new object[] { "sparse-and-numeric", """
            const a:any[]=[1,,3];const b=[...a];console.log(b.length,1 in b,b[1]===undefined);function collect(...xs:any[]){console.log(xs.length,1 in xs,xs[1]===undefined);}collect(...a);const dense=[1,2,3];console.log([...dense].join(","));function sum(...xs:number[]){let total=0;for(const n of xs)total+=n;console.log(total);}sum(0,...dense,4);
            """, "3 true true\n3 true true\n1,2,3\n10\n" };
        yield return new object[] { "repeated-spread", """
            const a:any[]=[1,2];let reads=0;Object.defineProperty(a,"1",{get(){return ++reads;}});console.log([...a].join(","),[...a].join(","),reads);function collect(...xs:any[]){console.log(xs.join(","),reads);}collect(...a,...a);
            """, "1,1 1,2 2\n1,3,1,4 4\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledSpreadReadsDescriptorsInIteratorOrder(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
