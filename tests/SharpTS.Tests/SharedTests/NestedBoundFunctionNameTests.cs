using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedBoundFunctionNameTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            function sample(a:number,b:number,c:number){return a+b+c;}const a:any=sample.bind(null,1);const b:any=a.bind(null,2);console.log(a.name,a.length,b.name,b.length);a.tag=7;console.log(a.tag,b.tag===undefined,a.missing===undefined,b(3));
            """, "bound sample 2 bound bound sample 1\n7 true true 6\n" };
        yield return new object[] { "repeated", """
            function sample(a:number,b:number,c:number){return a+b+c;}
            const first:any=sample.bind(null,1);
            const second:any=first.bind({ignored:1},2);
            const third:any=second.bind({ignored:2},3);
            console.log(first.name,second.name,third.name);
            console.log(first.length,second.length,third.length,third());
            first.tag=7;second.tag=8;
            console.log(first.tag,second.tag,third.tag===undefined,first.missing===undefined);
            const fourth:any=third.bind(null,4);
            console.log(fourth.name,fourth.length,fourth(),fourth.tag===undefined);
            """, "bound sample bound bound sample bound bound bound sample\n2 1 0 6\n7 8 true true\nbound bound bound bound sample 0 6 true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void RepeatedBindingRetainsNamesLengthsAndIndependentProperties(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
