using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NumericStaticMemberWriteTests
{
    [Fact]
    public void NumericWriteInvalidatesThePriorElementNarrowing()
    {
        const string source = """
            class C { static 1: number | string = 1; }
            if (typeof C[1] === 'number') {
                C[1] = 'text';
                const invalid: number = C[1];
            }
            """;
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2322" && diagnostic.Line == 4);
    }

    [Theory, ModeData]
    public void NumericWriteRhsCanUseThePriorElementNarrowing(ExecutionMode mode)
    {
        const string source = """
            class C { static 1: number | string = 1; }
            if (typeof C[1] === 'number') C[1] = C[1] + 1;
            console.log(C[1]);
            """;
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericKeysWriteStaticFieldsAndSetters(ExecutionMode mode)
    {
        const string source = """
            class C {
                static 1: number = 0;
                static 2: number;
                static { this[2] = 2; }
                static read = this[2];
                static set 3(value: number) { this[1] = value; }
            }
            C[1] = 1;
            console.log(C[1], C[2], C.read);
            C[3] = 3;
            console.log(C[1]);
            class Derived extends C {}
            Derived[1] = 4;
            console.log(C[1], Derived[1]);
            """;
        Assert.Equal("1 2 2\n3\n3 4\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("class C { static 1: number = 0; } C[1] = 'bad';", "TS2322")]
    [InlineData("class C { static readonly 1: number = 0; } C[1] = 2;", "TS2540")]
    [InlineData("class C { private static 1: number = 0; } C[1] = 'bad';", "TS2322")]
    [InlineData("class C { static get 1(): number { return 0; } } C[1] = 2;", "TS2540")]
    public void NumericWritesRetainMemberValidation(string source, string code)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == code);
    }
}
