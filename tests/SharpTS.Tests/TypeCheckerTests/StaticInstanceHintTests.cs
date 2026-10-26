using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticInstanceHintTests
{
    [Theory]
    [InlineData("class C { static value = 1; }\nnew C().value;", 2)]
    [InlineData("class C { static value() {} }\nnew C().value;", 2)]
    [InlineData("class C { static get value() { return 1; } }\nnew C().value;", 2)]
    [InlineData("const C = class { static value = 1; copy = this.value; };", 1)]
    [InlineData("class C<T> { static value = 1; }\nnew C<number>().value;", 2)]
    [InlineData("class C { static value = 1; } class D extends C {}\nnew D().value;", 2)]
    public void KnownStaticMemberSuggestsConstructorAccess(string source, int line)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2576", error.Diagnostic.TsCode);
        Assert.Equal(line, error.Diagnostic.Location!.Line);
        Assert.Contains("static member", error.Message);
        Assert.Contains(".value", error.Message);
    }

    [Fact]
    public void MissingMemberKeepsMissingPropertyDiagnostic()
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow("class C { static value = 1; } new C().missing;")));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void InstanceMemberWinsOverStaticMemberWithSameName(ExecutionMode mode)
    {
        Assert.Equal("2\n", TestHarness.Run("class C { static value = 1; value = 2; } console.log(new C().value);", mode));
    }
}
