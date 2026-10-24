using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ForwardGenericTypeQueryTests
{
    [Theory]
    [InlineData("U", false)]
    [InlineData("string", true)]
    public void HoistedTypeOfParametersRetainGenericConstructorIdentity(string sourceArgument, bool rejected)
    {
        string source = $$"""
            class Base<T> {
                value: T;
                constructor(value: T) { this.value = value; }
            }
            class Box<T> extends Base<T> {}
            class Other<U> extends Base<{{sourceArgument}}> {}
            function identity(value: typeof Box): typeof Box { return value; }
            const Alias: typeof Box = identity(Other);
            const value: number = new Alias<number>(1).value;
            """;
        void Check() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
