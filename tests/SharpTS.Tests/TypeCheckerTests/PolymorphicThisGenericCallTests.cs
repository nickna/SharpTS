using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PolymorphicThisGenericCallTests
{
    [Theory]
    [InlineData("x", null)]
    [InlineData("missing", "TS2345")]
    public void InferredReceiverRelatesToItsOwnGenericMethodSurface(string key, string? code)
    {
        string source = $$"""
            function getProperty<T, K extends keyof T>(object: T, key: K) { return object[key]; }
            class C1 {
                x: number = 1;
                get<K extends keyof this>(key: K) { return this[key]; }
                foo() { const value = getProperty(this, '{{key}}'); }
            }
            """;
        var result = new TypeChecker(strictNullChecks: true)
            .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (code is null) Assert.Empty(result.Diagnostics);
        else Assert.Equal(code, Assert.Single(result.Diagnostics).TsCode);
    }
}
