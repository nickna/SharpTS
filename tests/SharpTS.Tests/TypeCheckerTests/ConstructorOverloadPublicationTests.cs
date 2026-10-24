using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ConstructorOverloadPublicationTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ConstructorBodyPublication_PreservesPublicOverloads(string declaration, string ending)
    {
        var source = declaration + " { constructor(value: number); constructor(value: string); constructor(value: any) {} }" + ending;
        foreach (string value in new[] { "1", "'ok'", "true" })
        {
            var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source + " new C(" + value + ");").ScanTokens()).ParseOrThrow());
            if (value == "true") Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2769");
            else Assert.Empty(result.Diagnostics);
        }
    }
}
