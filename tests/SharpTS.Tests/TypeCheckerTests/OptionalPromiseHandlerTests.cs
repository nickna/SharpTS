using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OptionalPromiseHandlerTests
{
    private static void Check(string source)
        => new TypeChecker(strictNullChecks: true).Check(new Parser(new Lexer(source).ScanTokens()).Parse().Statements);

    [Theory]
    [InlineData("")]
    [InlineData("undefined")]
    [InlineData("null")]
    public void NoLibHandlersMayBeOmittedOrNullish(string argument)
        => Check($$"""
            const promise = new Promise<number>(resolve => resolve(1));
            promise.catch({{argument}});
            promise.finally({{argument}});
            class Derived extends Promise<number> {}
            const derived = new Derived(resolve => resolve(1));
            derived.catch({{argument}});
            derived.finally({{argument}});
            """);

    [Theory]
    [InlineData("catch")]
    [InlineData("finally")]
    public void NoncallableNonNullishHandlersRemainInvalid(string method)
        => Assert.Equal("TS2345", Assert.ThrowsAny<TypeCheckException>(() => Check(
            $"const promise = new Promise<number>(resolve => resolve(1)); promise.{method}(42);")).Diagnostic.TsCode);
}
