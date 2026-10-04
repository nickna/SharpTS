using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArrayDestructureSourceValidationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-invalid", """
            for(const source of [null,undefined,4,{}]){try{const [a]=source as any;console.log("accepted",a);}catch(e){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "original-null", """
            const source:any=null;try{const [a]=source;console.log("accepted",a);}catch(e){console.log(e.name,e.message);}
            """, "TypeError source is not iterable\n" };
        yield return new object[] { "original-object", """
            const source:any={};try{const [a]=source;console.log("accepted",a);}catch(e){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "other-primitives", """
            for(const source of [false,1n,Symbol("value"),()=>1] as any[]){try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "object-storage", """
            for(const source of [{next(){return {done:true};}},{0:7,length:1},{value:9}] as any[]){try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "empty-pattern", """
            for(const source of [null,undefined,4,{}] as any[]){try{const []=source;console.log("accepted");}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "valid-collections", """
            const [a,...arrayRest]=[1,2,3];const [s,...stringRest]="AB";const [v,...setRest]=new Set([4,5]);const [entry,...mapRest]=new Map([["a",6],["b",7]]);console.log(a,arrayRest.join(","),s,stringRest.join(","),v,setRest.join(","),entry.join(","),mapRest.length);
            """, "1 2,3 A B 4 5 a,6 1\n" };
        yield return new object[] { "valid-custom", """
            const source:any={calls:0,reads:0,[Symbol.iterator](){this.calls++;return this;},next(){this.reads++;return {value:this.reads*7,done:this.reads>2};}};const [a,...rest]=source;console.log(a,rest.join(","),source.calls,source.reads);
            """, "7 14 1 3\n" };
        yield return new object[] { "iterator-getter", """
            const source:any={reads:0,next(){this.reads++;return {value:this.reads,done:this.reads>2};}};let gets=0;let calls=0;Object.defineProperty(source,Symbol.iterator,{get(){gets++;return function(){calls++;console.log(this===source);return source;};}});const [a,...rest]=source;console.log(a,rest.join(","),gets,calls,source.reads);
            """, "true\n1 2 1 1 3\n" };
        yield return new object[] { "factory-throws", """
            const marker={tag:7};const source:any={[Symbol.iterator](){throw marker;}};try{const [a]=source;console.log(a);}catch(e:any){console.log(e===marker);}
            """, "true\n" };
        yield return new object[] { "invalid-method", """
            for(const method of [null,undefined,4] as any[]){const source:any={};source[Symbol.iterator]=method;try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\n" };
        yield return new object[] { "primitive-iterator", """
            const source:any={[Symbol.iterator](){return 4;}};try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}
            """, "TypeError\n" };
        yield return new object[] { "non-iterator-result", """
            for(const result of [{},[]] as any[]){const source:any={[Symbol.iterator](){return result;}};try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\n" };
        yield return new object[] { "typed-array-control", """
            const source=new Uint8Array([1,2,3]);const [a,...rest]=source;console.log(a,Array.isArray(rest),rest.join(","));
            """, "1 true 2,3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledDestructuringRejectsNonIterableGuestSources(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
