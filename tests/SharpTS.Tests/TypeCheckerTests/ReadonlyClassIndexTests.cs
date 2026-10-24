using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ReadonlyClassIndexTests
{
    [Theory]
    [InlineData("string", "'x'")]
    [InlineData("number", "0")]
    [InlineData("symbol", "Symbol.iterator")]
    public void ReadonlyIndex_RejectsWritesAndAllowsReads(string keyType, string key)
    {
        foreach (var modifier in new[] { "readonly ", "" })
        {
            var source = "class C { " + modifier + "[key: " + keyType + "]: number; } const c = new C(); c[" + key + "] = 1; const n: number = c[" + key + "];";
            var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(modifier.Length > 0, result.Diagnostics.Any(d => d.TsCode == "TS2542"));
        }
    }
    [Fact]
    public void NamedMutableField_IsWritableAlongsideReadonlyIndex()
    {
        var source = "class C { readonly [key: string]: number; value: number = 1; } new C()['value'] = 2;";
        Assert.Empty(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
    }
}
