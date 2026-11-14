using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class EnumAdditionTests
{
    [Theory, ModeData]
    public void EnumMembersUseTheirPrimitiveAdditionDomain(ExecutionMode mode)
    {
        const string source = """
            const enum Values { A = 4 }
            enum Regular { A = 8 }
            enum Mixed { A = 12, Word = 'word' }
            const alias = Mixed.A;
            const result: number = Values.A + 2;
            const other: number = 2 + Regular.A;
            function sum(value: Regular) { return value + 1; }
            console.log(result, other, Mixed.A + 2, alias + Regular.A, sum(Regular.A));
            console.log(Mixed.Word + 2, 2 + Mixed.Word, Values.A + 'x');
            """;
        Assert.Equal("6 10 14 20 9\nword2 2word 4x\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("enum E { A = 1 }\nconst result = E + 1;")]
    [InlineData("enum E { A = 1 }\nconst result = E.A + 1n;")]
    [InlineData("enum E { A = 1 }\nconst result = E.A + true;")]
    public void InvalidEnumAdditionStillReportsTS2365(string source)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2365");
    }
}
