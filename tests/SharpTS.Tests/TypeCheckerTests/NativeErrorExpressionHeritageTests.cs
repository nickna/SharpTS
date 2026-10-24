using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class NativeErrorExpressionHeritageTests
{
    [Theory]
    [InlineData("const E = class extends TypeError {};")]
    [InlineData("const E = class extends TypeError { constructor(message: string) { super(message); } };")]
    public void NativeErrorExpressions_InheritConstructorAndInstanceMembers(string declaration)
    {
        foreach (var type in new[] { "string", "number" })
        {
            var source = declaration + " const message: " + type + " = new E('message').message;";
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(type == "number", result.Diagnostics.Any(d => d.TsCode == "TS2322"));
        }
    }
}
