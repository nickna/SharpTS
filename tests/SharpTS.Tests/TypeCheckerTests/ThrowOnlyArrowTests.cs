using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ThrowOnlyArrowTests
{
    [Theory]
    [InlineData("() => { throw 'stop'; }")]
    [InlineData("(flag: boolean) => { if (flag) throw 1; else throw 2; }")]
    [InlineData("() => { try { throw 1; } catch { throw 2; } }")]
    public void ThrowOnlyBlockArrowsInferNever(string expression)
        => TestHarness.RunInterpreted("const inferred = " + expression + "; const fail: (...args: any[]) => never = inferred;");

    [Theory]
    [InlineData("() => {}")]
    [InlineData("() => { return; }")]
    [InlineData("(flag: boolean) => { if (flag) throw 1; }")]
    public void ArrowsThatCanCompleteDoNotInferNever(string expression)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(
            "const inferred = " + expression + "; const fail: (...args: any[]) => never = inferred;"));
        Assert.Equal("TS2322", error.Diagnostic.TsCode);
    }
}
