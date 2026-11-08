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

    [Theory]
    [InlineData("class C { get x() { this.y; return 1; } y = this.x; }", "new C().y")]
    [InlineData("const C = class { get x() { this.y; return 1; } y = this.x; };", "new C().y")]
    [InlineData("class C { static get x() { C.y; return 1; } static y = C.x; }", "C.y")]
    [InlineData("class C { get x() { this.y; return 1; } y = this.x; z = this.y; }", "new C().z")]
    [InlineData("class C { a = this.x; z = () => this.y; y = this.x; get x() { this.z; return 1; } }", "new C().z()")]
    public void AGetterWithACompletedReturnTypeCanResolveCyclicFields(string declaration, string access)
    {
        new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const right: number = {access};"));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const wrong: string = {access};")));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
