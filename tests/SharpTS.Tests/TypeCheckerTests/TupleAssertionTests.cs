using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TupleAssertionTests
{
    [Theory]
    [InlineData("[1] as [number]")]
    [InlineData("[1] as [number, string?]")]
    [InlineData("[1, 'two'] as [number, string]")]
    [InlineData("[1, 'two'] as [...[number, string]]")]
    [InlineData("[[1, 'two']] as [[number, string]]")]
    public void CompatibleFreshLiteralsCanBeAssertedToTuples(string expression)
        => TestHarness.RunInterpreted("const value = " + expression + ";");

    [Theory]
    [InlineData("const value = [true] as [number, string?];", "TS2352")]
    [InlineData("const array: number[] = [1]; const value: [number] = array;", "TS2322")]
    public void UnrelatedAssertionsAndOrdinaryArrayAssignmentsRemainInvalid(string source, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
