using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ConcreteInheritedFieldWriteTests
{
    [Theory]
    [InlineData("class Base<T>{value:T=undefined as any}class Derived extends Base<string>{}declare const value:Derived;value.value='next';")]
    [InlineData("class Base<T>{value:T=undefined as any}class Middle<U> extends Base<U[]>{}class Derived extends Middle<string>{}declare const value:Derived;value.value=['next'];")]
    [InlineData("class Base<T>{value:T=undefined as any}class Middle<T> extends Base<T[]>{}class Derived extends Middle<number>{}declare const value:Derived;value.value=[1];")]
    public void ConcreteReceiversComposeAncestorFieldArguments(string source) => Check(source);

    [Theory]
    [InlineData("class Base<T>{value:T=undefined as any}class Derived extends Base<string>{}declare const value:Derived;value.value=1;", "TS2322")]
    [InlineData("class Base<T>{value:T=undefined as any}class Middle<U> extends Base<U[]>{}class Derived extends Middle<string>{}declare const value:Derived;value.value=[1];", "TS2322")]
    [InlineData("class Base<T>{value:T=undefined as any}class Derived extends Base<string>{}function write<T>(value:Derived,input:T){value.value=input}", "TS2322")]
    [InlineData("class Base<T>{readonly value:T=undefined as any}class Derived extends Base<string>{}declare const value:Derived;value.value='next';", "TS2540")]
    [InlineData("class Base<T>{private value:T=undefined as any}class Derived extends Base<string>{}declare const value:Derived;value.value='next';", "TS2341")]
    public void InvalidWritesKeepConcreteTypeAndMemberRestrictions(string source, string code)
    {
        Assert.Equal(code, Assert.ThrowsAny<TypeCheckException>(() => Check(source)).Diagnostic.TsCode);
    }

    private static void Check(string source) =>
        new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}
