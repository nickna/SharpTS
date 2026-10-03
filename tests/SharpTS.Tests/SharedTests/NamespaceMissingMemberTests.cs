using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceMissingMemberTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            namespace Values {export const value=3;}const values:any=Values;console.log(values.missing===undefined,typeof values["missing"],values.value);
            """, "true undefined 3\n" };
        yield return new object[] { "dot-and-index", """
            namespace Values {export const value=3;}const values:any=Values;console.log(typeof values.missing,values["missing"]===undefined,values.missing===values["missing"],values["value"]);
            """, "undefined true true 3\n" };
        yield return new object[] { "present-null", """
            namespace Values {export const nil:any=null;}const values:any=Values;console.log(values.nil===null,typeof values.nil,values.nil===undefined,values.missing===undefined);
            """, "true object false true\n" };
        yield return new object[] { "assigned-null", """
            namespace Values {export const value=3;}const values:any=Values;values.missing=null;console.log(values.missing===null,values["missing"]===undefined,typeof values["missing"]);
            """, "true false object\n" };
        yield return new object[] { "bound-undefined", """
            namespace Values {export let value:any=2;}const values:any=Values;values.value=undefined;console.log(values.value===undefined,Values.value===undefined,typeof values.value);
            """, "true true undefined\n" };
        yield return new object[] { "generator", """
            namespace Values {export const value=3;}function* run(){const values:any=Values;yield values.missing;yield values["missing"];}for(const value of run())console.log(value===undefined,typeof value);
            """, "true undefined\ntrue undefined\n" };
        yield return new object[] { "private-function", """
            namespace Values {function read(){return 5;}export const result=read();}const values:any=Values;console.log(Values.result,typeof values.read);
            """, "5 undefined\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void MissingNamespaceMembersReturnUndefinedWhilePresentNullStaysNull(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
