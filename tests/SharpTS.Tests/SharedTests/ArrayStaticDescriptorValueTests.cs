using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayStaticDescriptorValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-alias", """
            const A:any=Array;const O:any=Object;console.log(O.getOwnPropertyDescriptor(A,"isArray").value===A.isArray,A.isArray===Array.isArray);console.log(O.getOwnPropertyDescriptor(O,"assign").value===O.assign,O.assign.length);
            """, "true true\ntrue 2\n" };
        yield return new object[] { "original-direct", """
            console.log(Object.getOwnPropertyDescriptor(Array,"isArray")!.value===Array.isArray,Object.getOwnPropertyDescriptor(Object,"assign")!.value===Object.assign);
            """, "true true\n" };
        yield return new object[] { "computed-metadata", """
            const A:any=Array;
            const key="isArray";
            const first=Object.getOwnPropertyDescriptor(A,key)!;
            const second=Object.getOwnPropertyDescriptor(Array,key)!;
            const predicate=first.value;
            console.log(predicate===second.value,predicate===A[key],predicate===Array.isArray);
            console.log(typeof predicate,predicate.name,predicate.length);
            console.log(first.writable,first.enumerable,first.configurable);
            const nameDescriptor=Object.getOwnPropertyDescriptor(predicate,"name")!;
            const lengthDescriptor=Object.getOwnPropertyDescriptor(predicate,"length")!;
            console.log(nameDescriptor.value,nameDescriptor.writable,nameDescriptor.enumerable,nameDescriptor.configurable);
            console.log(lengthDescriptor.value,lengthDescriptor.writable,lengthDescriptor.enumerable,lengthDescriptor.configurable);
            console.log(predicate([]),predicate({}),predicate(null),predicate.call(null,[1]));
            console.log(Object.getOwnPropertyDescriptor(predicate,"prototype")===undefined);
            """, "true true true\nfunction isArray 1\ntrue false true\nisArray false false true\n1 false false true\ntrue false false true\ntrue\n" };
        yield return new object[] { "object-assign-control", """
            const O:any=Object;
            const key="assign";
            const first=Object.getOwnPropertyDescriptor(O,key)!;
            const second=Object.getOwnPropertyDescriptor(Object,key)!;
            console.log(first.value===second.value,first.value===O[key],first.value===Object.assign);
            console.log(first.value.name,first.value.length,first.writable,first.enumerable,first.configurable);
            const target:any={a:1};
            const result=first.value(target,{b:2});
            console.log(result===target,result.a,result.b);
            """, "true true true\nassign 2 true false true\ntrue 1 2\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void DescriptorValuesShareStaticFunctionIdentityAndMetadata(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
