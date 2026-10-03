using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceInitializerFunctionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-ordinary", """
            namespace Values {const base=4;function read(){return base+1;}export const result=read();}console.log(Values.result);
            """, "5\n" };
        yield return new object[] { "original-generator", """
            namespace Values {const base=4;function* read(){yield base;yield base+1;}export const result=[...read()].join(",");}console.log(Values.result);
            """, "4,5\n" };
        yield return new object[] { "original-async", """
            namespace Values {const base=6;async function read(){await Promise.resolve(0);return base+1;}export const result=read();}Values.result.then(value=>console.log(value));
            """, "7\n" };
        yield return new object[] { "nested", """
            function read(){return 99;}namespace Outer {export namespace Inner {const base=4;function read(){return base+1;}export const result=read();}}console.log(Outer.Inner.result,read());
            """, "5 99\n" };
        yield return new object[] { "merged", """
            namespace Joined {function left(){return 3;}export const first=left();}namespace Joined {function right(){return 5;}export const second=right();}console.log(Joined.first,Joined.second);
            """, "3 5\n" };
        yield return new object[] { "private-shape", """
            namespace Values {function read(){return 5;}function* sequence(){yield 6;}async function later(){return 7;}export const result=read();export function publicRead(){return read();}}const values:any=Values;console.log(Values.result,values.publicRead(),values.read==null,values.sequence==null,values.later==null,values.publicRead===Values.publicRead);
            """, "5 5 true true true true\n" };
        yield return new object[] { "private-identity", """
            namespace Values {function read(){return 5;}export const first=read;export const second=read;}console.log(Values.first===Values.second,Values.first());
            """, "true 5\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void NamespaceInitializersResolveOwnPrivateFunctions(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
