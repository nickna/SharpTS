using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Fact]
    public void CapturedAsyncShadowPreservesIndependentLoopCaptureStorage()
    {
        string source = TimerDelaySource + """
            async function work() {
                const readers: any[] = [];
                for (let index = 0; index < 2; index++) {
                    readers.push(() => index);
                    {
                        let index = 98;
                        const inner = () => index;
                        index++;
                        await delay();
                        console.log(inner(), index);
                    }
                }
                console.log(readers[0](), readers[1]());
            }
            work();
            """;
        const string expected = "99 99\n99 99\n0 1\n";
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedAsyncShadowPrecedesOuterStorageInDebuggerDiscovery(bool captureOuter)
    {
        string source = TimerDelaySource + """
            async function work(seed: number) {
                let value = seed;
                OUTER_READER
                console.log(value); // before-cell
                {
                    let value = 98;
                    const reader = () => value;
                    value++;
                    await delay();
                    console.log(reader(), value); // inside-cell
                }
                console.log(value); // after-cell
                return value;
            }
            work(11);
            """.Replace("OUTER_READER", captureOuter ? "const readOuter = () => value;" : "", StringComparison.Ordinal);

        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> fields = ReadHoistedFieldScopes(artifacts, machine);
        int before = SourceMarkerOffset(artifacts, machine, source, "before-cell");
        int inside = SourceMarkerOffset(artifacts, machine, source, "inside-cell");
        int after = SourceMarkerOffset(artifacts, machine, source, "after-cell");
        HoistedFieldScope outer = Assert.Single(fields,
            field => field.SourceName == "value" && field.Scope.Contains(before));
        HoistedFieldScope inner = Assert.Single(fields,
            field => field.SourceName == "value" && field.Field != outer.Field && field.Scope.Contains(inside));
        Assert.True(outer.Scope.Contains(after));
        Assert.False(inner.Scope.Contains(before));
        Assert.False(inner.Scope.Contains(after));

        // Roslyn traverses display classes breadth-first. An outer direct field wins before
        // any inner cell payload regardless of field ordering, even with correct PDB scopes.
        Assert.NotEqual(machine.StateMachineType, outer.DeclaringType);
        Assert.NotEqual(machine.StateMachineType, inner.DeclaringType);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        int innerHolder = HolderIndex(inner.DeclaringType);
        int outerHolder = HolderIndex(outer.DeclaringType);
        Assert.True(innerHolder >= 0, "The inner cell must have a state-machine holder.");
        Assert.True(outerHolder >= 0, "The outer display class must have a state-machine holder.");
        Assert.True(innerHolder < outerHolder);

        Assert.Equal("11\n99 99\n11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("11\n99 99\n11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));

        int HolderIndex(TypeDefinitionHandle target)
        {
            FieldDefinitionHandle[] machineFields = metadata.GetTypeDefinition(machine.StateMachineType).GetFields().ToArray();
            return Array.FindIndex(machineFields, handle =>
            {
                FieldDefinition field = metadata.GetFieldDefinition(handle);
                if (!metadata.GetString(field.Name).StartsWith("<>8__", StringComparison.Ordinal)) return false;
                BlobReader signature = metadata.GetBlobReader(field.Signature);
                signature.ReadSignatureHeader();
                return signature.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle
                    && signature.ReadTypeHandle() == target;
            });
        }
    }
}
