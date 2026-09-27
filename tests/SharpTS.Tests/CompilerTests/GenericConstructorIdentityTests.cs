using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericConstructorIdentityTests
{
    [Theory]
    [InlineData("class Box<T> {}")]
    [InlineData("class Box<T extends { name: string }> { private value: number = 1; }")]
    [InlineData("class Box<T, U> {}")]
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
}
