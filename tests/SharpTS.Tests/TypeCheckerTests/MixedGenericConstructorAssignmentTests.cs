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
    [InlineData("value = 'source'; private constructor() {}")]
    [InlineData("value = 'source'; protected constructor() {}")]
    public void IncompatibleGenericConstructorRemainsRejected(string members)
    {
        string target = members.Contains("marker") ? "value = 'target'; private static marker = 1;" : "value = 'target';";
        var source = $"class Target {{ {target} }} class Source<T = string> {{ {members} }} const Held: typeof Target = Source;";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }

    [Theory]
    [InlineData("constructor(value: Animal) {}", "constructor(value: Dog) {}", false)]
    [InlineData("accept(value: Animal): void {}", "accept(value: Dog): void {}", false)]
    [InlineData("callback = (value: Animal): void => {};", "callback = (value: Dog): void => {};", true)]
    public void ConstructorAndMethodBivarianceKeepCallbackFieldsStrict(string target, string source, bool rejected)
    {
        string text = $"class Animal {{ animal = 1; }} class Dog extends Animal {{ dog = 1; }} class Target {{ {target} }} class Source<T = string> {{ {source} }} const Held: typeof Target = Source;";
        void Check() => new TypeChecker(new TypeCheckerOptions { StrictFunctionTypes = true })
            .Check(new Parser(new Lexer(text).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }

    [Fact]
    public void AbstractGenericSourceCannotSatisfyConcreteConstructor()
    {
        const string source = "class Target { value = 'target'; } abstract class Source<T = string> { value = 'source'; } const Held: typeof Target = Source;";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }

    [Theory]
    [InlineData("static #brand = 1;", false)]
    [InlineData("#brand = 1;", true)]
    [InlineData("private static brand = 1;", true)]
    public void ConstructorSurfaceOmitsStaticEsPrivateBrands(string targetMember, bool rejected)
    {
        string source = $"class Target {{ {targetMember} value = 'target'; }} class Source<T = string> {{ value = 'source'; }} const Held: typeof Target = Source;";
        void Check() => new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        if (rejected) Assert.ThrowsAny<TypeCheckException>(Check);
        else Check();
    }
}
