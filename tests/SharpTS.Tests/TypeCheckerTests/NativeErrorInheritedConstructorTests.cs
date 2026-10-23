using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NativeErrorInheritedConstructorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["guarded-catch", """
            try {
                throw new TypeError('marker');
            } catch (error) {
                console.log(error instanceof TypeError && error.constructor === TypeError);
            }
            """.ReplaceLineEndings("\n") + "\n"];
        yield return ["direct-new", """
            const error = new TypeError('marker');
            console.log(error.constructor === TypeError);
            """.ReplaceLineEndings("\n") + "\n"];
        yield return ["own-message", """
            const error = new TypeError('marker');
            console.log(error.message);
            """.ReplaceLineEndings("\n") + "\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativeErrorReadsHaveNoStrictDiagnostics(string name, string source)
    {
        Assert.NotEmpty(name);
        var result = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("new TypeError('marker').definitelyMissing;", "TS2339")]
    [InlineData("const value: number = new TypeError('marker').message;", "TS2322")]
    [InlineData("const value: number = new TypeError('marker').constructor;", "TS2322")]
    public void NativeErrorReadsRetainMemberAndTypeDiagnostics(string source, string code)
    {
        var result = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.TsCode);
    }

    [Fact]
    public void DirectInheritedReadsDoNotEnlargeApparentMemberProjection()
    {
        foreach (var error in new[]
        {
            new TypeInfo.Error(), new TypeInfo.Error("TypeError"),
            new TypeInfo.Error("AggregateError")
        })
        {
            Assert.DoesNotContain("constructor", BuiltInTypes.GetInstanceMemberNames(error)!);
            Assert.Null(BuiltInTypes.GetInstanceMemberType(error, "constructor"));
        }
    }
}
