using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class StaticAutoAccessorTypeTests
{
    [Theory]
    [InlineData("Config.version")]
    [InlineData("Derived.version")]
    public void StaticAutoAccessorRetainsItsDeclaredReadType(string expression)
    {
        string source = $$"""
            class Config { static accessor version: number = 1; }
            class Derived extends Config {}
            const incompatible: string = {{expression}};
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Fact]
    public void StaticAutoAccessorDoesNotBecomeAnInstanceMember()
    {
        const string source = """
            class Config { static accessor version: number = 1; }
            const incompatible = new Config().version;
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }
}
