using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class StaticPrivateReceiverTests
{
    [Theory]
    [InlineData("return other.#x;")]
    [InlineData("other.#x = 2;")]
    [InlineData("return other.#method();")]
    [InlineData("return other.#method;")]
    public void StaticPrivateAccessRejectsUnrelatedConstructors(string body)
    {
        string source = "class Other {} class C { static #x = 1; " +
            "static #method() { return 1; } static test(other: typeof Other) { " + body + " } }";
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("typeof C")]
    [InlineData("any")]
    public void StaticPrivateAccessAcceptsOwnerAndDynamicReceivers(string type)
    {
        string source = "class C { static #x = 1; static #method() { return 1; } " +
            "static test(other: " + type + ") { other.#x = 2; return other.#x + other.#method(); } }";
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    [Fact]
    public void StaticPrivateAccessDoesNotInheritThePrivateBrand()
    {
        const string source = "class C { static #x = 1; static test() { class D extends C {} return D.#x; } }";
        Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
    }
}
