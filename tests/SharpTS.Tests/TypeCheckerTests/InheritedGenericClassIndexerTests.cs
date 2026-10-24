using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InheritedGenericClassIndexerTests
{
    private static void Check(string source) => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Theory]
    [InlineData("declare class Base { [index: number]: string; } class Derived extends Base {}", "Derived")]
    [InlineData("declare class Base<T> { [index: number]: T; }", "Base<string>")]
    [InlineData("declare class Base<T> { [index: number]: T; } class Middle<U> extends Base<U[]> {} class Derived extends Middle<string> {}", "Derived")]
    public void InheritedIndexesPreserveSubstitutedReadAndWriteTypes(string declarations, string instance)
    {
        string expected = declarations.Contains("U[]") ? "string[]" : "string";
        string prefix = declarations + $" declare const value: {instance}; ";
        Check(prefix + $"const text: {expected} = value[0];");
        Assert.ThrowsAny<TypeCheckException>(() => Check(prefix + "const text: number = value[0];"));
        Assert.ThrowsAny<TypeCheckException>(() => Check(prefix + "value[0] = 42;"));
    }
}
