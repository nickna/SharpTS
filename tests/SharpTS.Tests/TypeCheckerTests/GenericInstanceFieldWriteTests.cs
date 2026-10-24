using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericInstanceFieldWriteTests
{
    [Theory]
    [InlineData("class Base<T> { value: T = undefined as any; } class Derived<U> extends Base<U> {}", "new Derived<number>().value = 'bad';", "TS2322")]
    [InlineData("class Base<T> { readonly code: number = 1; } class Derived<U> extends Base<U> {}", "new Derived<number>().code = 2;", "TS2540")]
    [InlineData("class Box<T> { readonly code: number = 1; }", "new Box<number>().code = 2;", "TS2540")]
    public void GenericWritesEnforceInheritedTypeAndReadonly(string declarations, string write, string code)
    {
        var statements = new Parser(new Lexer(declarations + write).ScanTokens()).ParseOrThrow();
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void MutableInheritedFieldAcceptsComposedType()
    {
        const string source = "class Base<T> { value: T = undefined as any; } class Middle<U> extends Base<U[]> {} class Derived<V> extends Middle<V> {} new Derived<number>().value = [1];";
        new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    }
}
