using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticLiteralIndexWriteTests
{
    [Theory]
    [InlineData("class C { static value = 0; } C['value'] = 1;", null)]
    [InlineData("class C { static value = 0; static { this['value'] = 1; } }", null)]
    [InlineData("const C = class { static value = 0; static { this['value'] = 1; } }; C['value'] = 2;", null)]
    [InlineData("class Base<T> { static value = 0; } class C extends Base<string> {} C['value'] = 1;", null)]
    [InlineData("class C<T> { static value = 0; } const CT = C<string>; CT['value'] = 1;", null)]
    [InlineData("class C<T> { static value = 0; } const CT = C<string>; CT['value'] = 'wrong';", "TS2322")]
    [InlineData("class C { static get value(): number { return 1; } static set value(v: string | number) {} } C['value'] = 'ok';", null)]
    [InlineData("class C { static value = 0; } C['value'] = 'wrong';", "TS2322")]
    [InlineData("class C { static readonly value = 0; } C['value'] = 1;", "TS2540")]
    [InlineData("class C { static get value(): number { return 1; } } C['value'] = 1;", "TS2540")]
    [InlineData("class C { private static value = 0; } C['value'] = 1;", null)]
    [InlineData("class C { private static value = 0; } C['value'] = 'wrong';", "TS2322")]
    public void LiteralStaticWritesUseTheDottedPropertyRules(string source, string? code)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (code is null) Assert.Empty(diagnostics);
        else Assert.Equal(code, Assert.Single(diagnostics).TsCode);
    }
}
