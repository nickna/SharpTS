using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NeverNumericOperatorTypeTests
{
    private static readonly string[] NumericOperators = ["-", "*", "/", "%", "**", "<<", ">>", ">>>", "&", "|", "^"];
    private static readonly string[] RelationalOperators = ["<", "<=", ">", ">="];

    public static IEnumerable<object[]> AcceptedCases()
    {
        foreach (string op in NumericOperators.Concat(RelationalOperators))
        {
            bool comparison = RelationalOperators.Contains(op);
            yield return [$"bottom {op} bottom", comparison ? "boolean" : "number"];
            foreach (var (operand, domain) in new[] { ("numberValue", "number"), ("big", "bigint") })
            {
                if (op == ">>>" && domain == "bigint") continue;
                yield return [$"{operand} {op} bottom", comparison ? "boolean" : domain];
                yield return [$"bottom {op} {operand}", comparison ? "boolean" : domain];
            }
        }
    }

    private static string Source(string body) =>
        "function probe(bottom: never, numberValue: number, big: bigint, symbolValue: symbol) { " + body + " }";

    [Theory, MemberData(nameof(AcceptedCases))]
    public void NeverOperandsKeepTheirNumericOrRelationalResultType(string expression, string expected)
    {
        new TypeChecker().Check(TestHarness.ParseOrThrow(Source($"const result: {expected} = {expression};")));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow(Source($"const wrong: string = {expression};"))));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        foreach (string op in NumericOperators.Concat(RelationalOperators))
        foreach (string operand in new[] { "true", "({})", "'text'", "symbolValue" })
        {
            bool comparison = RelationalOperators.Contains(op);
            string leftCode = comparison ? operand == "symbolValue" ? "TS2469" : "TS2365" : "TS2362";
            string rightCode = comparison ? leftCode : "TS2363";
            yield return [$"{operand} {op} bottom", leftCode];
            yield return [$"bottom {op} {operand}", rightCode];
        }
        foreach (string op in NumericOperators)
        {
            yield return [$"numberValue {op} big", op == ">>>" ? "TS2791" : "TS2365"];
            yield return [$"big {op} numberValue", op == ">>>" ? "TS2791" : "TS2365"];
        }
        yield return ["big >>> bottom", "TS2791"];
        yield return ["bottom >>> big", "TS2791"];
        yield return ["big >>> big", "TS2791"];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void NeverAdmissionRetainsInvalidOperandAndMixedDomainDiagnostics(string expression, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow(Source($"const result = {expression};"))));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void NeverRightOperandDoesNotAdmitUnsignedBigIntCompoundAssignment()
    {
        new TypeChecker().Check(TestHarness.ParseOrThrow(Source("numberValue >>>= bottom;")));
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow(Source("big >>>= bottom;"))));
        Assert.Equal("TS2791", error.Diagnostic.TsCode);
    }
}
