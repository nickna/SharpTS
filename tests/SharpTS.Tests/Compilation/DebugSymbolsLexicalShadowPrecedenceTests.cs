using System.Buffers.Binary;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        ProjectedCellAlias alias = Assert.Single(ReadProjectedCellAliases(artifacts, machine));
        AssertAliasReloadsRetainedCell(artifacts, machine, alias);
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedAsyncShadowUsesScopedLocalForDebuggerDiscovery(bool captureOuter)
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

        // Locals are discovered before receiver fields. The alias must be the only discovery
        // path to its cell type or Roslyn's type deduplication discards that lexical priority.
        Assert.NotEqual(machine.StateMachineType, inner.DeclaringType);
        ProjectedCellAlias alias = Assert.Single(ReadProjectedCellAliases(artifacts, machine),
            alias => alias.CellType == inner.DeclaringType);
        Assert.True(alias.Scope.Contains(inside));
        Assert.False(alias.Scope.Contains(before));
        Assert.False(alias.Scope.Contains(after));
        AssertAliasReloadsRetainedCell(artifacts, machine, alias);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        Assert.Equal(-1, HolderIndex(inner.DeclaringType));

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

    [Fact]
    public void CapturedInnerCellShadowsAParameterWithoutChangingParameterStorage()
    {
        string source = TimerDelaySource + """
            async function work(value: number) {
                console.log(value); // before-parameter-cell
                {
                    let value = 98;
                    const reader = () => value;
                    value++;
                    await delay();
                    console.log(reader(), value); // inside-parameter-cell
                }
                console.log(value); // after-parameter-cell
            }
            work(11);
            """;
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        int inside = SourceMarkerOffset(artifacts, machine, source, "inside-parameter-cell");
        int before = SourceMarkerOffset(artifacts, machine, source, "before-parameter-cell");
        int after = SourceMarkerOffset(artifacts, machine, source, "after-parameter-cell");
        ProjectedCellAlias alias = Assert.Single(ReadProjectedCellAliases(artifacts, machine));
        Assert.True(alias.Scope.Contains(inside));
        Assert.False(alias.Scope.Contains(before));
        Assert.False(alias.Scope.Contains(after));
        AssertAliasReloadsRetainedCell(artifacts, machine, alias);
        Assert.Equal("11\n99 99\n11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal("11\n99 99\n11\n", CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    private static void AssertAliasReloadsRetainedCell(
        CompilationArtifacts artifacts, HoistedStateMachineScopes machine, ProjectedCellAlias alias)
    {
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        FieldDefinitionHandle backing = Assert.Single(metadata.GetTypeDefinition(machine.StateMachineType).GetFields(),
            handle =>
            {
                FieldDefinition field = metadata.GetFieldDefinition(handle);
                BlobReader signature = metadata.GetBlobReader(field.Signature);
                signature.ReadSignatureHeader();
                return signature.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle
                    && signature.ReadTypeHandle() == alias.CellType;
            });
        Assert.StartsWith("<>7__", metadata.GetString(metadata.GetFieldDefinition(backing).Name));
        byte[] il = pe.GetMethodBody(metadata.GetMethodDefinition(machine.MoveNext).RelativeVirtualAddress).GetILBytes()!;
        int[] boundaries = ReadInstructionBoundaries(il).Order().ToArray();
        int reloads = 0;
        foreach (int offset in boundaries)
        {
            if (offset == il.Length || il[offset] != OpCodes.Ldfld.Value) continue;
            if (MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset + 1, 4))) != backing) continue;
            int store = offset + 5;
            int slot = il[store] switch
            {
                >= 0x0a and <= 0x0d => il[store] - 0x0a,
                0x13 => il[store + 1],
                0xfe when il[store + 1] == 0x0e => BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(store + 2, 2)),
                _ => -1,
            };
            if (slot == alias.LocalSlot) reloads++;
        }
        Assert.True(reloads >= 2, "The alias must reload its backing field on entry and after cell replacement.");
        // The initial reload precedes state dispatch, so every resumed MoveNext refreshes it too.
        Assert.Equal(OpCodes.Ldarg_0.Value, il[0]);
        Assert.Equal(OpCodes.Ldfld.Value, il[1]);
        Assert.Equal((EntityHandle)backing, MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(2, 4))));
    }
}
