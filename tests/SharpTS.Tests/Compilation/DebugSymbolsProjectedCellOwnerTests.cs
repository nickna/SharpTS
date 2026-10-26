using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Theory]
    [InlineData("generator")]
    [InlineData("async-generator")]
    public void GeneratorOwnersDoNotRequestOrdinaryAsyncLexicalCellAliases(string kind)
    {
        string source = StateMachineSource(kind, """
            let value = seed;
            {
                let value = 98;
                const reader = () => value;
                value++;
                SUSPEND
                console.log(reader(), value);
            }
            console.log(value);
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        Assert.Empty(ReadProjectedCellAliases(artifacts, machine));
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        Assert.DoesNotContain(metadata.TypeDefinitions, handle => metadata.GetString(
            metadata.GetTypeDefinition(handle).Name).StartsWith("<>c__DisplayClass_LexicalCell", StringComparison.Ordinal));
    }
}
