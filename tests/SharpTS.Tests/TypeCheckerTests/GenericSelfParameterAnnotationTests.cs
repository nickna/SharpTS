using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericSelfParameterAnnotationTests
{
    [Theory]
    [InlineData("number", false)]
    [InlineData("string", true)]
    public void DirectCheckUsesCompletedGenericSelfParameter(string expected, bool rejected)
    {
        var source = $"class C<T> {{ value: number = 1; run(other: C<number>) {{ const value: {expected} = other.value; }} }}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        void Check() => new TypeChecker().Check(statements);
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
