using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassFieldInitializationOrderTests
{
    [Theory]
    [InlineData("class C", "z = this.y;", "y = 1;")]
    [InlineData("const C = class", "z = (this).y;", "y: number = 1;")]
    [InlineData("class C", "static z = this.y;", "static y = 1;")]
    [InlineData("class C", "static z = C.y;", "static y = 1;")]
    [InlineData("class C", "z = this.#y;", "#y = 1;")]
    public void DirectReadOfLaterFieldReportsAnInitializationError(string declaration, string initializer, string field)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            {{declaration}} {
                {{initializer}}
                {{field}}
            }
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2729", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }

    [Fact]
    public void EarlierOptionalAndDeferredFieldsRemainReadable()
    {
        var statements = TestHarness.ParseOrThrow("""
            class C {
                first = 1;
                earlier = this.first;
                deferred = () => this.later;
                optional = this.maybe;
                nested = class { read() { return C.staticLater; } };
                later = 2;
                maybe?: number;
                static staticLater = 3;
            }
            """);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }
}
