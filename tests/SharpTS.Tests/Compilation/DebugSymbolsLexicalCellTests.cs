using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.Compilation;

public partial class DebugSymbolsTests
{
    [Theory, MemberData(nameof(AsyncBlockLexicalCaptureTests.Cases), MemberType = typeof(AsyncBlockLexicalCaptureTests))]
    public void LexicalCellsKeepIdentityAndTemporalDeadZonesInBothDebugModes(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        // Reuse the whole #2584 sources and expectations, including the synchronous control,
        // pre-declaration read/typeof, disjoint escaping cells and mutation across a timer await.
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    [Fact]
    public void ProjectedLexicalCellPayloadIsTheFieldUsedByItsOwnerAndMutatingClosure()
    {
        string source = StateMachineSource("async", """
            let value = seed;
            {
                let value = seed + 10;
                const mutate = () => { value += 1; return value; };
                SUSPEND
                console.log(mutate(), value); // inner-cell
            }
            console.log(value); // outer-cell
            return value;
            """);
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes machine = Assert.Single(ReadAndValidateHoistedScopes(artifacts));
        IReadOnlyList<HoistedFieldScope> projected = ReadHoistedFieldScopes(artifacts, machine);
        int inside = SourceMarkerOffset(artifacts, machine, source, "inner-cell");
        int outside = SourceMarkerOffset(artifacts, machine, source, "outer-cell");
        HoistedFieldScope payload = Assert.Single(projected, field => field.SourceName == "value"
            && field.DeclaringType != machine.StateMachineType
            && field.Scope.Contains(inside) && !field.Scope.Contains(outside));
        HoistedFieldScope outer = Assert.Single(projected,
            field => field.SourceName == "value" && field.Scope.Contains(outside));
        Assert.NotEqual(payload.Field, outer.Field);

        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinition cell = metadata.GetTypeDefinition(payload.DeclaringType);
        Assert.Contains("DisplayClass_LexicalCell", metadata.GetString(cell.Name));
        Assert.Single(cell.GetFields());
        FieldDefinition valueField = metadata.GetFieldDefinition(payload.Field);
        Assert.True((valueField.Attributes & FieldAttributes.Public) != 0);
        BlobReader signature = metadata.GetBlobReader(valueField.Signature);
        Assert.Equal(SignatureKind.Field, signature.ReadSignatureHeader().Kind);
        Assert.Equal(SignatureTypeCode.Object, signature.ReadSignatureTypeCode());
        Assert.Equal(0, signature.RemainingBytes);

        Assert.True(ReferencesLexicalPayload(pe, metadata, machine.MoveNext, OpCodes.Ldfld, payload.Field));
        Assert.True(ReferencesLexicalPayload(pe, metadata, machine.MoveNext, OpCodes.Stfld, payload.Field));
        Assert.Contains(metadata.MethodDefinitions, method => method != machine.MoveNext
            && metadata.GetString(metadata.GetMethodDefinition(method).Name) == "Invoke"
            && ReferencesLexicalPayload(pe, metadata, method, OpCodes.Ldfld, payload.Field)
            && ReferencesLexicalPayload(pe, metadata, method, OpCodes.Stfld, payload.Field));

        CompilationArtifacts repeated = CompileTypeScript(source, emitDebugSymbols: true);
        HoistedStateMachineScopes repeatedMachine = Assert.Single(ReadAndValidateHoistedScopes(repeated));
        Assert.Equal(machine.Scopes, repeatedMachine.Scopes);
        Assert.Equal(projected, ReadHoistedFieldScopes(repeated, repeatedMachine));
        Assert.Equal(GeneratedTypeNames(artifacts.Assembly), GeneratedTypeNames(repeated.Assembly));

        // The ordinary layout keeps its existing BCL box. No source-projection cell type or
        // holder is generated when symbols are disabled.
        CompilationArtifacts ordinary = CompileTypeScript(source, emitDebugSymbols: false);
        Assert.Null(ordinary.Pdb);
        using var ordinaryPe = new PEReader(new MemoryStream(ordinary.Assembly, writable: false));
        MetadataReader ordinaryMetadata = ordinaryPe.GetMetadataReader();
        Assert.DoesNotContain(ordinaryMetadata.TypeDefinitions,
            type => ordinaryMetadata.GetString(ordinaryMetadata.GetTypeDefinition(type).Name)
                .Contains("DisplayClass_LexicalCell", StringComparison.Ordinal));
        Assert.Contains(ordinaryMetadata.FieldDefinitions,
            field => ordinaryMetadata.GetString(ordinaryMetadata.GetFieldDefinition(field).Name)
                .StartsWith("<>lexicalCell", StringComparison.Ordinal));
    }

    [Fact]
    public void AsyncChildThroughSyncRelayProjectsTheSameCellWithoutOverlappingOwnSlots()
    {
        string source = AsyncCellRelaySource();
        const string expected = "13 7 13\n2\n";
        CompilationArtifacts artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        IReadOnlyList<HoistedStateMachineScopes> machines = ReadAndValidateHoistedScopes(artifacts);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        HoistedStateMachineScopes owner = Assert.Single(machines,
            machine => metadata.GetString(metadata.GetMethodDefinition(machine.Kickoff).Name) == "work");
        HoistedStateMachineScopes child = Assert.Single(machines, machine => machine != owner);
        HoistedFieldScope ownerPayload = Assert.Single(ReadHoistedFieldScopes(artifacts, owner),
            field => field.SourceName == "value" && field.DeclaringType != owner.StateMachineType);
        Assert.Contains("DisplayClass_LexicalCell",
            metadata.GetString(metadata.GetTypeDefinition(ownerPayload.DeclaringType).Name));
        IReadOnlyList<HoistedFieldScope> childFields = ReadHoistedFieldScopes(artifacts, child);
        HoistedFieldScope incoming = Assert.Single(childFields,
            field => field.Field == ownerPayload.Field && field.DeclaringType == ownerPayload.DeclaringType);
        HoistedFieldScope own = Assert.Single(childFields, field => field.SourceName == "own");
        Assert.NotEqual(incoming.Slot, own.Slot);
        Assert.True(incoming.Scope.Length > 0);
        Assert.True(own.Scope.Length > 0);
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: true, verifyIl: true));
        Assert.Equal(expected, CompileAndRunDebugArtifact(source, emitDebugSymbols: false, verifyIl: true));
    }

    private static string AsyncCellRelaySource() => """
        let value = 2;
        function readOuter() { return value; }
        async function work(seed: number) {
            for (let value of [seed + 10]) {
                const relay = () => async () => {
                    const own = 7;
                    await Promise.resolve(0);
                    value += 1;
                    return [value, own];
                };
                const child = relay();
                const result = await child();
                console.log(result[0], result[1], value);
            }
            console.log(readOuter());
        }
        work(2);
        """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static bool ReferencesLexicalPayload(
        PEReader pe, MetadataReader metadata, MethodDefinitionHandle methodHandle,
        OpCode operation, FieldDefinitionHandle payload)
    {
        MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
        if (method.RelativeVirtualAddress == 0) return false;
        byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        foreach (int offset in ReadInstructionBoundaries(il))
        {
            // ldfld/stfld are single-byte opcodes with a four-byte field token. Decode only at
            // actual instruction boundaries, so a matching byte inside an operand cannot pass.
            if (offset == il.Length || il[offset] != operation.Value) continue;
            int token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset + 1, sizeof(int)));
            EntityHandle operand = MetadataTokens.EntityHandle(token);
            if (operand.Kind == HandleKind.FieldDefinition && (FieldDefinitionHandle)operand == payload) return true;
        }
        return false;
    }
}
