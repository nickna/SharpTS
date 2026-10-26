using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Theory]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void GeneratorOwnersDoNotRequestOrdinaryAsyncLexicalCellAliases(string kind)
    {
        const string body = """
            let value = seed;
            for (let value of [98]) {
                const reader = () => value;
                value++;
                SUSPEND
                console.log(reader(), value);
            }
            console.log(value);
            """;
        string source = StateMachineSource(kind, body);
        var analyzer = new LoopBodyCellAnalyzer();
        analyzer.Analyze(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        var cell = Assert.Single(analyzer.CellDeclarations);
        Assert.Equal("value", cell.Lexeme);
        var owner = Assert.IsType<Stmt.Function>(analyzer.GetCellOwner(cell));
        Assert.True(owner.IsGenerator);
        Assert.Equal(kind == "async-generator", owner.IsAsync);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        Assert.Empty(ReadProjectedCellAliases(artifacts, machine));
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        Assert.DoesNotContain(metadata.TypeDefinitions, handle => metadata.GetString(
            metadata.GetTypeDefinition(handle).Name).StartsWith("<>c__DisplayClass_LexicalCell", StringComparison.Ordinal));

        string runtimeSource = StateMachineRuntimeSource(kind, body);
        Assert.Equal("99 99\n2\n", CompileAndRunDebugArtifact(runtimeSource, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("99 99\n2\n", CompileAndRunDebugArtifact(runtimeSource, emitDebugSymbols: false, verifyIl: true));
    }
}
