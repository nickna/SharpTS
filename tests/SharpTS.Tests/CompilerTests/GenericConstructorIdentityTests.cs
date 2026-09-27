using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericConstructorIdentityTests
{
    [Fact]
    public void SameNamedGenericInstancesKeepDistinctPrivateBrandsAfterSelfComparison()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const Left = class Same<T> { private value: number = 1; };
                const Right = class Same<T> { private value: number = 1; };
                let left = new Left<number>();
                const right = new Right<number>();
                left = left;
                left = right;
                """
        };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory]
    [InlineData("class Box<T> {}")]
    [InlineData("class Box<T extends { name: string }> { private value: number = 1; }")]
    [InlineData("class Box<T, U> {}")]
    [InlineData("const Box = class<T, U> {};")]
    [InlineData("const Box = class Named<T> { private value: number = 1; };")]
    public void ConstructorAliasesRemainAssignableToTheirOwnType(string declaration)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = declaration + """

                let Alias: typeof Box = Box;
                Alias = Box;
                function identity(value: typeof Box): typeof Box { return value; }
                Alias = identity(Alias);
                console.log(Alias === Box);
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("true\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Theory]
    [InlineData("class Box<T> { static value: number = 1; }", "class Other<T> { static value: string = 'x'; }")]
    [InlineData("class Box<T> { private value: number = 1; }", "class Other<T> { private value: number = 1; }")]
    [InlineData("class Box<T> {}", "class Other<T extends string> { constructor(value: T) {} }")]
    public void DifferentConstructorDeclarationsDoNotGainIdentityCompatibility(string target, string source)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = target + "\n" + source + "\nlet Alias: typeof Box = Other;"
        };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory]
    [InlineData("private value: number = 1;", "private value: number = 1;")]
    [InlineData("static value: number = 1;", "static value: string = 'x';")]
    public void SameNamedExpressionsDoNotShareSuccessfulCompatibilityCacheEntries(string left, string right)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                const Left = class Same<T> { {{left}} };
                const Right = class Same<T> { {{right}} };
                let alias: typeof Left = Left;
                alias = Left;
                alias = Right;
                """
        };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void ForwardSignatureDoesNotSuppressInvalidClassBodyDiagnostics()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const Box = class<T> { read(): number { return "invalid"; } };
                function identity(value: typeof Box): typeof Box { return value; }
                const Alias = identity(Box);
                """
        };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void ForwardSignaturePreservesInferredMethodReturnTypes()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const Box = class<T> { read() { return 42; } };
                function identity(value: typeof Box): typeof Box { return value; }
                const Alias = identity(Box);
                const value: number = new Alias<string>().read();
                console.log(value);
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("42\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }
}
