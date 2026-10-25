using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OptionalStaticMemberReadTests
{
    [Theory]
    [InlineData("class C { static value?: number; } const invalid: number = C.value;")]
    [InlineData("class C { static value?: number = 1; } const invalid: number = C['value'];")]
    [InlineData("class C { static '0'?: number; } const invalid: number = C[0];")]
    [InlineData("class Base<T> { static value?: number; } class C<U> extends Base<U> {} const invalid: number = C.value;")]
    [InlineData("class Base<T> { static '0'?: number; } class C<U> extends Base<U> {} const invalid: number = C[0];")]
    [InlineData("class C { static value?: number; } class D { static value = 1; } declare const constructor: typeof C | typeof D; const invalid: number = constructor['value'];")]
    [InlineData("class C { static '0'?: number; static value = 1; } declare const key: 0 | 'value'; const invalid: number = C[key];")]
    [InlineData("declare class C { static read?(): number; } const invalid: () => number = C.read;")]
    public void OptionalStaticReadsIncludeUndefined(string source)
    {
        Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source))).Diagnostic.TsCode);
        Assert.Equal("TS2322", Assert.Single(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics).TsCode);
    }

    [Theory]
    [InlineData("class C { static value?: number; } const valid: number | undefined = C.value;")]
    [InlineData("class C { static value?: number; } if (C.value !== undefined) { const valid: number = C.value; }")]
    [InlineData("class C { static value?: number; } const valid: number = C.value ?? 1;")]
    [InlineData("class Base { static value?: number; } class C extends Base { static value: number = 1; } const valid: number = C.value;")]
    [InlineData("declare class C { static read?(): number; } const valid: (() => number) | undefined = C['read'];")]
    [InlineData("class C { static value?: number; } const valid: number = C.value!;")]
    public void OptionalStaticReadsPreserveGuardsAndRequiredShadows(string source)
        => new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalityFollowsStrictNullChecks(bool strictNullChecks)
    {
        const string source = "class C { static value?: number; } const value: number = C.value;";
        var diagnostics = new TypeChecker(strictNullChecks: strictNullChecks)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (strictNullChecks)
            Assert.Equal("TS2322", Assert.Single(diagnostics).TsCode);
        else
            Assert.Empty(diagnostics);
    }

    [Fact]
    public void PossiblyMissingStaticMethodCannotBeInvokedDirectly()
    {
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow("declare class C { static read?(): number; } C.read();")).Diagnostics);
        Assert.Equal("TS2722", diagnostic.TsCode);
    }
}
