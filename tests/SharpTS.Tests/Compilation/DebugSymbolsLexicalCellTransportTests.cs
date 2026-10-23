using SharpTS.Compilation;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Fact]
    public void GeneratorExpressionTransportKeepsProjectedCellLiveInBothDebugModes()
    {
        string source = """
            let value = 100;
            async function run() {
                let factory: any;
                let read: any;
                {
                    let value = 1;
                    read = () => value;
                    factory = function* () {
                        yield value;
                        value += 2;
                        yield value;
                    };
                    const iterator = factory();
                    console.log("first", iterator.next().value, read());
                    await Promise.resolve(0);
                    value = 5;
                    console.log("second", iterator.next().value, read());
                }
                console.log("outside", value);
                const later = factory();
                console.log("retained", later.next().value, read());
            }
            run().then(
                () => console.log("done"),
                (error: any) => console.log("rejected", error.name)
            );
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        const string expected = "first 1 1\nsecond 7 7\noutside 100\nretained 7 7\ndone\n";

        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        HoistedStateMachineScopes owner = Assert.Single(ReadAndValidateHoistedScopes(artifacts),
            machine => metadata.GetString(metadata.GetMethodDefinition(machine.Kickoff).Name) == "run");
        HoistedFieldScope payload = Assert.Single(ReadHoistedFieldScopes(artifacts, owner),
            field => field.SourceName == "value" && field.DeclaringType != owner.StateMachineType);
        Assert.Contains("DisplayClass_LexicalCell",
            metadata.GetString(metadata.GetTypeDefinition(payload.DeclaringType).Name));

        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Fact]
    public void ClassDefinitionTransportKeepsProjectedCellLiveInBothDebugModes()
    {
        string source = """
            let value = 100;
            function readOuter() { return value; }
            async function run() {
                let instance: any;
                let read: any;
                for (let value of [1]) {
                    read = () => value;
                    const Holder = class {
                        read() { return value; }
                        write(next: number) { value = next; }
                    };
                    instance = new Holder();
                    console.log("first", instance.read(), read());
                    await Promise.resolve(0);
                    value = 5;
                    instance.write(instance.read() + 2);
                    console.log("second", instance.read(), read());
                }
                console.log("outside", readOuter());
                instance.write(9);
                console.log("retained", instance.read(), read());
            }
            run().then(
                () => console.log("done"),
                (error: any) => console.log("rejected", error.name)
            );
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        const string expected = "first 1 1\nsecond 7 7\noutside 100\nretained 9 9\ndone\n";

        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        HoistedStateMachineScopes owner = Assert.Single(ReadAndValidateHoistedScopes(artifacts),
            machine => metadata.GetString(metadata.GetMethodDefinition(machine.Kickoff).Name) == "run");
        HoistedFieldScope payload = Assert.Single(ReadHoistedFieldScopes(artifacts, owner),
            field => field.SourceName == "value" && field.DeclaringType != owner.StateMachineType);
        Assert.Contains("DisplayClass_LexicalCell",
            metadata.GetString(metadata.GetTypeDefinition(payload.DeclaringType).Name));

        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }
}
