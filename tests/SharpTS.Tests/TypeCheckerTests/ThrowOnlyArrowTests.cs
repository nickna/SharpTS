using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ThrowOnlyArrowTests
{
    public static IEnumerable<object[]> ThrowOnlyCases()
        => WithModes([
            "() => { throw 'stop'; }",
            "(flag: boolean) => { if (flag) throw 1; else throw 2; }",
            "() => { try { throw 1; } catch { throw 2; } }"
        ]);

    public static IEnumerable<object[]> CompletingCases()
        => WithModes([
            "() => {}",
            "() => { return; }",
            "(flag: boolean) => { if (flag) throw 1; }"
        ]);

    private static IEnumerable<object[]> WithModes(string[] expressions)
        => expressions.SelectMany(expression => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { mode, expression }));

    [Theory, MemberData(nameof(ThrowOnlyCases))]
    public void ThrowOnlyBlockArrowsInferNever(ExecutionMode mode, string expression)
        => TestHarness.Run("const inferred = " + expression + "; const fail: (...args: any[]) => never = inferred;", mode);

    [Theory, MemberData(nameof(CompletingCases))]
    public void ArrowsThatCanCompleteDoNotInferNever(ExecutionMode mode, string expression)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(
            "const inferred = " + expression + "; const fail: (...args: any[]) => never = inferred;", mode));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
