using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ClassBodyInferenceTests
{
    [Theory]
    [InlineData("value.method()")]
    [InlineData("value.property")]
    public void MethodAndGetterInference_AreBothPublished(string expression)
    {
        // Both stages must run, even when the method stage already requests a
        // rebuild. A runtime-only assertion would miss an <inferred> placeholder.
        var source = """
            class Value {
                method() { return 1; }
                get property() { return 2; }
            }
            const value = new Value();
            """ + $"\nconst invalid: string = {expression};";

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess);
        var result = new TypeChecker().CheckWithRecovery(parsed.Statements);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(6, diagnostic.Line);
    }
}
