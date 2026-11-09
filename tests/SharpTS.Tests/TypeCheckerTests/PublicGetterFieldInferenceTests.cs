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
    [InlineData("class C { a = this.x; z = () => this.y; y = this.x; get x() { this.z; return 1; } }", "new C().a")]
    [InlineData("class C { a = this['y'] ?? 0; y = this.x; get x() { this.y; return 1; } }", "new C().a")]
    [InlineData("class C { a = this['b']; b = this['y']; y = this.x; get x() { this.y; return 1; } }", "new C().a")]
    public void AGetterWithACompletedReturnTypeCanResolveCyclicFields(string declaration, string access)
    {
        new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const right: number = {access};"));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow($"{declaration} const wrong: string = {access};")));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C", "", "this", "new C()")]
    [InlineData("const C = class", "", "this", "new C()")]
    [InlineData("class C", "static ", "C", "C")]
    [InlineData("const C = class Named", "static ", "Named", "C")]
    public void MethodsUseFieldTypesResolvedAfterCyclicGetterInference(
        string declaration, string modifier, string receiver, string use)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            {{declaration}} {
                {{modifier}}get x() { {{receiver}}.y; return 1; }
                {{modifier}}y = {{receiver}}.x;
                {{modifier}}read() { return {{receiver}}.y; }
            }
            const right: number = {{use}}.read();
            const wrong: string = {{use}}.read();
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal(7, diagnostic.Line);
        Assert.Equal("TS2322", diagnostic.TsCode);
    }

    [Fact]
    public void RecheckingInferredMethodsDoesNotDuplicateDiagnostics()
    {
        var statements = TestHarness.ParseOrThrow("""
            class C {
                get x() { this.y; return 1; }
                y = this.x;
                read() {
                    const wrong: number = "wrong";
                    return this.y;
                }
            }
            const wrong: string = new C().read();
            """);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics;
        Assert.Equal(new[] { 5, 9 }, diagnostics.Select(diagnostic => diagnostic.Line));
        Assert.All(diagnostics, diagnostic => Assert.Equal("TS2322", diagnostic.TsCode));
    }

    [Theory]
    [InlineData("class C")]
    [InlineData("const C = class")]
    public void InterfaceValidationUsesTheRetriedFieldType(string declaration)
    {
        string implementation = $$"""
            {{declaration}} implements I {
                get x() { this.y; return 1; }
                y = this.x;
            }
            """;
        new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow("interface I { y: number; }\n" + implementation));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker(strictNullChecks: true).Check(
            TestHarness.ParseOrThrow("interface I { y: string; }\n" + implementation)));
        Assert.Equal("TS2416", error.Diagnostic.TsCode);
    }
}
