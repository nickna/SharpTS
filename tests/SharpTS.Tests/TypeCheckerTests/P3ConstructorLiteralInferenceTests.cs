using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ConstructorLiteralInferenceTests
{
    [Theory]
    [InlineData("T", "1", "2", false)]
    [InlineData("T", "'first'", "'next'", false)]
    [InlineData("T", "true", "false", false)]
    [InlineData("T", "1", "'wrong'", true)]
    [InlineData("const T", "1", "2", true)]
    [InlineData("T extends string", "'first'", "'next'", true)]
    public void MutableConstructorResultsWidenUnconstrainedPrimitiveCandidates(
        string parameter, string argument, string value, bool reject)
    {
        string source = $$"""
            interface Box<T> { value: T; }
            interface Factory { new<{{parameter}}>(value: T): Box<T>; }
            declare const Factory: Factory;
            const box = new Factory({{argument}});
            box.value = {{value}};
            """;
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics;
        if (reject) Assert.NotEmpty(diagnostics);
        else Assert.Empty(diagnostics);
    }
}
