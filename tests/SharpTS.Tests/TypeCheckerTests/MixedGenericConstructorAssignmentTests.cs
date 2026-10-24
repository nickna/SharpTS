using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class MixedGenericConstructorAssignmentTests
{
    [Theory, ModeData]
    public void UnusedDefaultedParameterPreservesCompatibleConstructor(ExecutionMode mode)
    {
        const string source = """
            class Target { value = 'target'; }
            class Source<T = string> { value = 'source'; }
            const Held: typeof Target = Source;
            const value = new Held();
            console.log(value.value, value instanceof Source);
            """;
        Assert.Equal("source true\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("value: number = 1;")]
    [InlineData("value = 'source'; constructor(required: number) {}")]
    [InlineData("value = 'source'; private static marker = 1;")]
    public void IncompatibleGenericConstructorRemainsRejected(string members)
    {
        string target = members.Contains("marker") ? "value = 'target'; private static marker = 1;" : "value = 'target';";
        var source = $"class Target {{ {target} }} class Source<T = string> {{ {members} }} const Held: typeof Target = Source;";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }
}
