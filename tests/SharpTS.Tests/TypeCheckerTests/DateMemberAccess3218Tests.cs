using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DateMemberAccess3218Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["computed-original", "const value=new Date(0);console.log(value['getTime']());\n", ""];
        yield return ["constant-key", "const key='getTime';const value=new Date(0);console.log(value[key]());", ""];
        yield return ["unknown-key", "const value=new Date(0);console.log(value['missing']);", "TS7053"];
        yield return ["prototype", "Date.prototype.getTime();", ""];
        yield return ["prototype-arity-original", "Date.prototype.getTime(1);\n", "TS2554"];
        yield return ["prototype-unknown", "Date.prototype.noSuchDateMember;", "TS2339"];
        yield return ["shadow", "const Date={prototype:{getTime(value:number){return value;}}};console.log(Date.prototype.getTime(1));", ""];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ResolvesTheActualKnownDateMember(string name, string source, string expectedCode)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (expectedCode.Length == 0) Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == expectedCode);
    }
}
