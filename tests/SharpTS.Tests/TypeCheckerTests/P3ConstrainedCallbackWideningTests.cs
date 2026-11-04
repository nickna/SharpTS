using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ConstrainedCallbackWideningTests
{
    [Fact]
    public void ConstrainedNamedCallbackAndIndexViewsHaveTheSameBooleanReturnDomain()
    {
        const string source = """
            function accept<T extends Record<1 | 7, () => any>>(value: T): T { return value; }
            const object = accept({1: () => true, 7: () => 'text'});
            const result = object[1]();
            const value: true = result;
            """;
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Equal("TS2322", Assert.Single(diagnostics).TsCode);
    }

    [Theory]
    [InlineData("function identity<T>(value:T):T{return value;} const object=identity({one:()=>true});const exact:true=object.one();")]
    [InlineData("function accept<T extends Record<1|7,()=>any>>(value:T):T{return value;}const object=accept({1:():true=>true,7:()=> 'text'});const exact:true=object[1]();")]
    public void UnconstrainedAndExplicitLiteralReturnContextsRetainTheirPrecision(string source)
        => Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics);
}
