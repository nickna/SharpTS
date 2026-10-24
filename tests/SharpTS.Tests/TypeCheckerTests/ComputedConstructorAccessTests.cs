using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedConstructorAccessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNamedForeignClassCannotReadThePrivateComputedMethod(bool generic)
    {
        string source = $$"""
            class C{{(generic ? "<T>" : "")}} {
                private ["constructor"](): number { return 1; }
            }
            const value = new C{{(generic ? "<number>" : "")}}();
            function make() {
                class C { read(): number { return value.constructor(); } }
            }
            """;
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2341");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaringClassCanReadItsOwnPrivateComputedMethod(bool generic)
    {
        string source = $$"""
            class C{{(generic ? "<T>" : "")}} {
                private ["constructor"](): number { return 1; }
                read(other: C{{(generic ? "<T>" : "")}}): number {
                    return this.constructor() + other.constructor();
                }
            }
            """;
        Assert.Empty(Check(source).Diagnostics);
    }

    [Fact]
    public void LiteralBracketAccessKeepsTypeScriptsPrivateMemberEscape()
    {
        Assert.Empty(Check("""
            class C { private ["constructor"](): number { return 1; } }
            const value: number = new C()["constructor"]();
            """).Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}
