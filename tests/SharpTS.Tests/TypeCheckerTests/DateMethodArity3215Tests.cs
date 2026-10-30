using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DateMethodArity3215Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const value=new Date(0);value.getTime(1);", false];
        yield return ["alias", "const value=new Date(0);const read=value.getTime;read(1);", false];
        yield return ["prototype", "Date.prototype.getTime(1);", false];
        yield return ["computed", "const value=new Date(0);value['getTime'](1);", false];
        yield return ["iso", "const value=new Date(0);value.toISOString(1);", false];
        yield return ["locale-surplus", "const value=new Date(0);value.toLocaleString('en-US',{},'extra');", false];
        yield return ["zero", "const value=new Date(0);value.getTime();", true];
        yield return ["optional-json-key", "const value=new Date(0);value.toJSON('key');", true];
        yield return ["open-constructor", "const value=new Date(0);const ctor=value.constructor;ctor(1,2);", true];
        yield return ["locale-options", "const value=new Date(0);value.toLocaleString('en-US',{});", true];
    }

    [Theory, MemberData(nameof(Cases))]
    public void UsesDeclaredDateMethodArity(string name, string source, bool valid)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (valid) Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2554");
    }
}
