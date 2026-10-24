using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NeverAdditionTypeTests
{
    [Theory]
    [InlineData("numberValue + bottom", "number", "string")]
    [InlineData("bottom + numberValue", "number", "string")]
    [InlineData("1 + bottom", "number", "string")]
    [InlineData("bottom + 1", "number", "string")]
    [InlineData("big + bottom", "bigint", "number")]
    [InlineData("bottom + big", "bigint", "number")]
    [InlineData("text + bottom", "string", "number")]
    [InlineData("bottom + text", "string", "number")]
    [InlineData("bottom + bottom", "number", "string")]
    [InlineData("dynamic + bottom", "string", "number")]
    [InlineData("bottom + dynamic", "string", "number")]
    [InlineData("unknownValue + bottom", "string", "number")]
    [InlineData("bottom + unknownValue", "string", "number")]
    [InlineData("flag + bottom", "string", "number")]
    [InlineData("bottom + flag", "string", "number")]
    [InlineData("record + bottom", "string", "number")]
    [InlineData("bottom + record", "string", "number")]
    public void NeverUsesTheOtherOperandsPrimitiveResultDomain(string expression, string expected, string incompatible)
    {
        string Source(string resultType) =>
            "function probe(bottom: never, numberValue: number, big: bigint, text: string, " +
            "dynamic: any, unknownValue: unknown, flag: boolean, record: { x: number }) { " +
            $"const result: {resultType} = {expression}; }}";
        new TypeChecker().Check(TestHarness.ParseOrThrow(Source(expected)));
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(Source(incompatible))));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("symbolValue + bottom", "TS2469")]
    [InlineData("bottom + symbolValue", "TS2469")]
    [InlineData("symbolValue + dynamic", "TS2469")]
    [InlineData("dynamic + symbolValue", "TS2469")]
    [InlineData("numberValue + big", "TS2365")]
    [InlineData("big + numberValue", "TS2365")]
    public void NeverAdmissionPreservesSymbolAndMixedNumericDomainErrors(string expression, string code)
    {
        string source = "function probe(bottom: never, symbolValue: symbol, dynamic: any, numberValue: number, big: bigint) { " +
            $"const result = {expression}; }}";
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
