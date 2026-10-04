using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NumberPredicateValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const N:any=Number;console.log(N.isNaN(NaN),N.isFinite(3),N.isInteger(3),N.isSafeInteger(3),N.isInteger(3.5));console.log(N.isNaN===Number.isNaN,N.isFinite.name,N.isFinite.length);
            """, "true true true true false\ntrue isFinite 1\n" };
        yield return new object[] { "call-control", """
            const N:any=Number;console.log(N.isNaN(NaN),N.isFinite(3),N.isInteger(3),N.isSafeInteger(3),N.isInteger(3.5));
            """, "true true true true false\n" };
        yield return new object[] { "method-values", """
            const N=Number;
            const nan:(value:unknown)=>boolean=Number.isNaN;
            const finite:(value:unknown)=>boolean=N.isFinite;
            const integer:(value:unknown)=>boolean=Number.isInteger;
            const safe:(value:unknown)=>boolean=N.isSafeInteger;
            console.log(nan===N.isNaN,finite===Number.isFinite,integer===N.isInteger,safe===Number.isSafeInteger);
            console.log(nan.name,nan.length,finite.name,finite.length,integer.name,integer.length,safe.name,safe.length);
            console.log(nan(NaN),finite(3),integer(3),safe(3));
            console.log(nan('NaN'),finite('3'),integer('3'),safe('3'));
            console.log(finite(Infinity),integer(3.5),safe(9007199254740992));
            """, "true true true true\nisNaN 1 isFinite 1 isInteger 1 isSafeInteger 1\ntrue true true true\nfalse false false false\nfalse false false\n" };
    }

    public static IEnumerable<object[]> LoadedLibraryCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return new object[] { "shadowed-interface-control", """
            namespace Inner {
                interface NumberConstructor { unrelated: string; }
                export function check():boolean {
                    const predicate: (value:unknown)=>boolean=Number.isNaN;
                    return predicate(NaN);
                }
            }
            console.log(Inner.check());
            """, "true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void StaticNumberPredicateValuesKeepIdentityMetadataAndNonCoercingResults(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
