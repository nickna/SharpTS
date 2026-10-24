using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReadonlyObjectTypePropertyTests
{
    private static void Check(string source) => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Theory]
    [InlineData("object.value = 2;")]
    [InlineData("object['value'] = 2;")]
    [InlineData("object.value += 1;")]
    [InlineData("object['value'] += 1;")]
    [InlineData("object.value++;")]
    [InlineData("++object.value;")]
    [InlineData("object['value']++;")]
    [InlineData("object.value ||= 2;")]
    [InlineData("object['value'] ||= 2;")]
    public void ExplicitReadonlyObjectPropertyRejectsWrites(string write)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => Check("const object: { readonly value: number; mutable: number } = { value: 1, mutable: 1 };" + write));
        Assert.Equal("TS2540", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("object[0] += 1;")]
    [InlineData("object[0] -= 1;")]
    [InlineData("object[0] ||= 2;")]
    [InlineData("object[0] &&= 2;")]
    [InlineData("object[0] ??= 2;")]
    [InlineData("function update(key: 0 | 'mutable') { object[key] += 1; }")]
    public void NumericReadonlyObjectPropertyRejectsUpdates(string write)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => Check(
            "const object: { readonly 0: number; mutable: number } = { 0: 1, mutable: 1 };" + write));
        Assert.Equal("TS2540", error.Diagnostic.TsCode);
    }

    [Fact]
    public void MutableNumericPropertyStillAcceptsUpdates()
        => Check("const object: { 0: number; readonly other: number } = { 0: 1, other: 1 }; object[0] += 1; object[0] ||= 2;");

    [Fact]
    public void MutableSiblingAndGenericReadonlyReadRetainTheirFacets()
    {
        Check("type Box<T> = { readonly value: T; mutable: T }; const object: Box<number> = { value: 1, mutable: 1 }; object.mutable = 2; const read: number = object.value;");
        Assert.ThrowsAny<TypeCheckException>(() => Check("type Box<T> = { readonly value: T }; const object: Box<number> = { value: 1 }; object.value = 2;"));
    }
}
