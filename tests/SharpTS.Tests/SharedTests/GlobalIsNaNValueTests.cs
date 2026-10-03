using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalIsNaNValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const root:any=globalThis;console.log(root.parseInt===parseInt,root.parseFloat===parseFloat,root.isNaN===isNaN,root.isFinite===isFinite);console.log(root.parseInt("21"),root.parseFloat("2.5"),root.isNaN("x"),root.isFinite(4));console.log(root.encodeURIComponent===encodeURIComponent,root.decodeURIComponent===decodeURIComponent,root.eval===eval);
            """, "true true true true\n21 2.5 true true\ntrue true true\n" };
        yield return new object[] { "original-predicate-controls", """
            const root:any=globalThis;console.log(isNaN("x" as any),Number.isNaN("x" as any),root.isNaN(NaN),root.isNaN(4));
            """, "true false true false\n" };
        yield return new object[] { "string-numeric-coercion", """
            const g:any=isNaN;for(const value of [""," ","0x10","0b10","0o10","NaN","Infinity","1x"]){console.log(g(value),isNaN(value as any));}
            """, "false false\nfalse false\nfalse false\nfalse false\nfalse false\ntrue true\nfalse false\ntrue true\n" };
        yield return new object[] { "omitted-and-primitive-values", """
            const g:any=isNaN;console.log(g(),g(undefined),g(null),g(NaN),g(-0),g(Infinity),g(true),g(false));
            """, "true true false true false false false false\n" };
        yield return new object[] { "object-coercion", """
            let log="";const bad:any={valueOf(){log+="v";return "bad";},toString(){log+="s";return "4";}};const g:any=isNaN;console.log(g(bad),log);log="";console.log(isNaN(bad),log);const good:any={valueOf(){return {};},toString(){return "4";}};console.log(g(good),Number.isNaN(good));
            """, "true v\ntrue v\nfalse false\n" };
        yield return new object[] { "abrupt-coercion", """
            const g:any=isNaN;const original=new Error("coerce");const bad:any={valueOf(){throw original;}};try{g(bad);}catch(e){console.log(e===original);}for(const value of [Symbol("s"),1n]){try{g(value);}catch(e){console.log(e.name,e instanceof TypeError);}console.log(Number.isNaN(value));}
            """, "true\nTypeError true\nfalse\nTypeError true\nfalse\n" };
        yield return new object[] { "borrowed-and-global-calls", """
            const root:any=globalThis;const g:any=root["isNaN"];console.log(g.call({},"x"),g.apply(null,["4"]),g.bind({},"x")());console.log(root.isNaN("x"),globalThis.isNaN("4" as any));
            """, "true false true\ntrue false\n" };
        yield return new object[] { "identity-and-metadata", """
            const root:any=globalThis;const g:any=isNaN;console.log(g===root.isNaN,g===root["isNaN"],g!==Number.isNaN);console.log(g.name,g.length,g.prototype===undefined);console.log(Number.isNaN(NaN),Number.isNaN("x" as any));
            """, "true true true\nisNaN 1 true\ntrue false\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void GlobalIsNaNValuesUseNumericCoercionAndKeepStrictPredicatesSeparate(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
