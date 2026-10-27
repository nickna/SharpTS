using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ConsoleMainReturnAbiTests
{
    [Theory]
    [InlineData("function main(): number { return 13; }")]
    [InlineData("async function main(): Promise<number> { return 13; }")]
    public void NumericMainProducesVerifiableEntryPoint(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"console_main_{Guid.NewGuid():N}", false, true, null, null, null, OutputTarget.Exe);
        compiler.Compile(statements, typeMap);
        using var stream = new MemoryStream(compiler.SaveToBytes());
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
    }
}
