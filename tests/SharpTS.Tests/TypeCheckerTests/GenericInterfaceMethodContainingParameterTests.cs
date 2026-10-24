using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericInterfaceMethodContainingParameterTests
{
    [Theory]
    [InlineData("Facet<number>")]
    [InlineData("Surface<number>")]
    public void MethodSubstitutesContainingParameterAndDefault(string receiver)
    {
        string prefix = $"interface Facet<U> {{ inherited<V = U>(value: U): V; }} interface Surface<T> extends Facet<T> {{}} declare const value: {receiver}; ";
        void Check(string source) => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        Assert.ThrowsAny<TypeCheckException>(() => Check(prefix + "value.inherited('bad');"));
        Check(prefix + "const result: number = value.inherited(1);");
        Check(prefix + "const result: string = value.inherited<string>(1);");
        Assert.ThrowsAny<TypeCheckException>(() => Check(prefix + "const result = value.inherited(1); const text: string = result;"));
    }
}
