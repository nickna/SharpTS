using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ClassExpressionAutoAccessorTypeTests
{
    [Theory]
    [InlineData("", "new C()")]
    [InlineData("static ", "C")]
    public void AutoAccessors_PublishReadAndWriteTypes(string modifier, string receiver)
    {
        var declaration = "const C = class { " + modifier + "accessor value: string = 'x'; };";
        foreach (var write in new[] { "'ok'", "7" })
        {
            var source = declaration + " const read: string = " + receiver + ".value; " + receiver + ".value = " + write + ";";
            var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(write == "7", result.Diagnostics.Any(d => d.TsCode == "TS2322"));
        }
    }
}
