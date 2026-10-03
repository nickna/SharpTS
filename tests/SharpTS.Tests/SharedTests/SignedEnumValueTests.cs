using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SignedEnumValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "negative_fraction", """
            enum Values {Negative=-2,Half=0.5}let key:number=-2;console.log(Values[key]);key=0.5;console.log(Values[key]);
            """, "Negative\nHalf\n" };
        yield return new object[] { "negative_caught", """
            enum Values {Negative=-2,Half=0.5}let key:number=-2;try{console.log(Values[key]);}catch(e){console.log("negative-miss");}key=0.5;try{console.log(Values[key]);}catch(e){console.log("fraction-miss");}console.log(Values.Negative,Values.Half);
            """, "Negative\nHalf\n-2 0.5\n" };
        yield return new object[] { "signed-auto-increment", """
            enum E{A=-2,B,C,D}console.log(E.A,E.B,E.C,E.D);for(const key of [-2,-1,0,1]){console.log(E[key]);}
            """, "-2 -1 0 1\nA\nB\nC\nD\n" };
        yield return new object[] { "negative-fractions", """
            enum E{A=-0.5,B,C=-2.5,D}console.log(E.A,E.B,E.C,E.D);for(const key of [-0.5,0.5,-2.5,-1.5]){console.log(E[key]);}
            """, "-0.5 0.5 -2.5 -1.5\nA\nB\nC\nD\n" };
        yield return new object[] { "duplicate-signed-values", """
            enum E{A=-2,B=-2,C=0,D=-0,F=0.5,G=0.5}console.log(E.A,E.B,E.C,E.D,E.F,E.G);for(const key of [-2,0,0.5]){console.log(E[key]);}
            """, "-2 -2 0 0 0.5 0.5\nB\nD\nG\n" };
        yield return new object[] { "parenthesized-signed-literals", """
            enum E{A=(-2),B=+3,C=-(-4),D=-(0.5)}console.log(E.A,E.B,E.C,E.D);for(const key of [-2,3,4,-0.5]){console.log(E[key]);}
            """, "-2 3 4 -0.5\nA\nB\nC\nD\n" };
        yield return new object[] { "signed-namespace-values", """
            namespace N{export enum E{A=-2,B=-0.5,C}}console.log(N.E.A,N.E.B,N.E.C);for(const key of [-2,-0.5,0.5]){console.log(N.E[key]);}
            """, "-2 -0.5 0.5\nA\nB\nC\n" };
        yield return new object[] { "signed-const-enum-control", """
            const enum E{A=-2,B,C=-0.5,D}console.log(E.A,E.B,E.C,E.D);
            """, "-2 -1 -0.5 0.5\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledEnumsPreserveSignedLiteralValues(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
