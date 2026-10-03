using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExtractedTypedArrayMethodTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const a:any=new Uint8Array([1,2]);const fill:any=a.fill;fill.call(a,3);console.log(a[0],a[1]);
            """, "3 3\n" };
        yield return new object[] { "forwarding", """
            const original:any=new Uint8Array([1,2,3,4]);
            const other:any=new Uint8Array([5,6,7,8]);
            const fill:any=original.fill;
            console.log(fill.call(other,9,1,3)===other);
            console.log(original.join(','),other.join(','));
            console.log(fill.apply(other,[4,0,1])===other,other.join(','));
            const floating:any=new Float64Array([1.5,2.5]);
            console.log(fill.call(floating,3.5)===floating,floating[0],floating[1]);
            for(const receiver of [null,undefined,{},[]]){
                try{fill.call(receiver,0);console.log(false);}catch(error){console.log(error instanceof TypeError);}
            }
            """, "true\n1,2,3,4 5,9,9,8\ntrue 4,9,9,8\ntrue 3.5 3.5\ntrue\ntrue\ntrue\ntrue\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ExtractedFillRetainsSelectedReceiverArgumentsAndReturnIdentity(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
