using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PublicGetterFieldInferenceTests
{
    [Theory]
    [InlineData("class C { value = this.x; stored = 1; get x() { return this.stored; } }", "new C().value")]
    [InlineData("const C = class { value = this.x; stored = 1; get x() { return this.stored; } };", "new C().value")]
    [InlineData("class C { static value = C.x; static stored = 1; static get x() { return C.stored; } }", "C.value")]
    public void AnInferredGetterCanReadALaterInferredField(string declaration, string access)
    {
        new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const right: number = {access};"));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const wrong: string = {access};")));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
