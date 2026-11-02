using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

// Native metadata ownership and final-PE mutation controls have no interpreter
// counterpart. The unchanged source's ordinary semantics remain shared elsewhere.
public sealed class NativeTaskSourceBufferProofTests
{
    private const string Original = "const a:any=new Uint8Array(2);let gets=0;Object.defineProperty(a,'probe',{get(){gets++;return 9},enumerable:true,configurable:true});Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});console.log(Object.getOwnPropertyNames(a).join(','),Reflect.ownKeys(a).join(','),Object.keys(a).join(','),gets);";
    private const string Expected = "0,1,probe,length 0,1,probe,length 0,1,probe 0\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ActualDefinitionSourceKeepsItsThreeGlobalReceiptsAndCertifiesFinalArtifacts(bool references, bool symbols)
    {
        var compiler = Compile(references); compiler.EmitDebugSymbols = symbols;
        var artifacts = compiler.SaveArtifacts("task-proof.pdb");
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var image = new MemoryStream(artifacts.Assembly);
        Assert.Empty(verifier.Verify(image));
        var saved = Assembly.Load(artifacts.Assembly);
        Assert.Equal(Expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        Assert.False(compiler.NativeTypedArrayBuffers!.UsesLegacy);
        Assert.Equal(3, compiler.NativePrototypeFields!.CompletedUnprovedResults.Producers.Count);
        Assert.True(compiler.NativePrototypeFields.UsesLegacy);
        Assert.All(compiler.NativePrototypeFields.CompletedUnprovedResults.Producers,
            result => Assert.Equal("get_Task", result.Producer.Name));
        Assert.DoesNotContain(compiler.NativeTypedArrayBuffers.Owners, type => typeof(Task).IsAssignableFrom(type));
        if (symbols) Assert.NotEmpty(artifacts.Pdb!);
    }

    [Theory]
    [InlineData("unrecorded-store")]
    [InlineData("wrong-local")]
    [InlineData("local-address")]
    [InlineData("branch-into-store")]
    [InlineData("uninitialized-path")]
    [InlineData("getter-name-row")]
    [InlineData("getter-signature-row")]
    [InlineData("tcs-type-name-row")]
    [InlineData("tcs-assembly-scope-row")]
    public void FinalImageRejectsUnprovedLocalFlowAndSameTokenMetadataChanges(string mutation)
    {
        var compiler = Compile(true); byte[] image = compiler.SaveToBytes();
        var saved = Assembly.Load(image);
        var method = saved.GetType("$Runtime")!.GetMethod("PromiseResolveValueWithRoot", BindingFlags.Public | BindingFlags.Static)!;
        int local = method.GetMethodBody()!.LocalVariables.Single(row => row.LocalType == typeof(TaskCompletionSource<object>)).LocalIndex;
        using var pe = new PEReader(new MemoryStream(image)); var metadata = pe.GetMetadataReader();
        var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken));
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
        var steps = Read(body).ToArray();
        var load = steps.First(row => Local(row) == (1, local));
        var store = steps.First(row => Local(row) == (2, local));
        var getter = steps.First(row => row.Code == OpCodes.Callvirt && method.Module.ResolveMember(row.Operand) is MethodInfo member &&
            member.Name == "get_Task" && member.DeclaringType == typeof(TaskCompletionSource<object>));
        int start = FileOffset(definition.RelativeVirtualAddress);
        start += (image[start] & 3) == 2 ? 1 : (image[start + 1] >> 4) * 4;
        if (mutation == "unrecorded-store") image[start + load.Offset] = checked((byte)(load.Code.Value + 4));
        else if (mutation == "wrong-local") image[start + load.Offset] = unchecked((byte)OpCodes.Ldloc_1.Value);
        else if (mutation == "local-address")
        {
            Assert.Equal(OpCodes.Ldarg_0, steps.Single(row => row.Offset == load.Offset + load.Size).Code);
            image[start + load.Offset] = unchecked((byte)OpCodes.Ldloca_S.Value); image[start + load.Offset + 1] = (byte)local;
        }
        else if (mutation is "branch-into-store" or "uninitialized-path")
        {
            var branch = steps.First(row => row.Code.OperandType == OperandType.InlineBrTarget);
            int target = mutation == "branch-into-store" ? store.Offset : steps.Single(row => row.Offset + row.Size == getter.Offset).Offset;
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(start + branch.Offset + branch.Code.Size, 4), target - branch.Offset - branch.Size);
        }
        else
        {
            var handle = (MemberReferenceHandle)MetadataTokens.EntityHandle(getter.Operand);
            var member = metadata.GetMemberReference(handle);
            int memberRow = Row(TableIndex.MemberRef, MetadataTokens.GetRowNumber(handle));
            int parentSize = CodedSize(3, TableIndex.TypeDef, TableIndex.TypeRef, TableIndex.ModuleRef, TableIndex.MethodDef, TableIndex.TypeSpec);
            int strings = metadata.GetHeapSize(HeapIndex.String) > ushort.MaxValue ? 4 : 2;
            int blobs = metadata.GetHeapSize(HeapIndex.Blob) > ushort.MaxValue ? 4 : 2;
            if (mutation == "getter-name-row")
                Index(memberRow + parentSize, strings, MetadataTokens.GetHeapOffset(metadata.MemberReferences.Select(h => metadata.GetMemberReference(h)).First(m => metadata.GetString(m.Name) == "TrySetResult").Name));
            else if (mutation == "getter-signature-row")
                Index(memberRow + parentSize + strings, blobs, MetadataTokens.GetHeapOffset(metadata.MemberReferences.Select(h => metadata.GetMemberReference(h)).First(m => m.Parent == member.Parent && metadata.GetString(m.Name) == ".ctor").Signature));
            else
            {
                var spec = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
                Assert.Equal(0x15, spec.ReadByte()); Assert.Equal(0x12, spec.ReadByte());
                var type = (TypeReferenceHandle)spec.ReadTypeHandle();
                int row = Row(TableIndex.TypeRef, MetadataTokens.GetRowNumber(type));
                int scopeSize = CodedSize(2, TableIndex.Module, TableIndex.ModuleRef, TableIndex.AssemblyRef, TableIndex.TypeRef);
                if (mutation == "tcs-type-name-row")
                    Index(row + scopeSize, strings, MetadataTokens.GetHeapOffset(metadata.TypeReferences.Select(h => metadata.GetTypeReference(h)).First(t => metadata.GetString(t.Name) == "Task`1").Name));
                else
                {
                    var assembly = metadata.AssemblyReferences.First(h => metadata.GetString(metadata.GetAssemblyReference(h).Name) == "System.Console");
                    Index(row, scopeSize, (MetadataTokens.GetRowNumber(assembly) << 2) | 2);
                }
            }
        }
        var failure = Assert.Throws<CompileException>(() => compiler.NativeTypedArrayBuffers!.ValidateImage(image));
        Assert.Contains("certificate refused", failure.Message);

        int Row(TableIndex table, int rid) => pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(table) + (rid - 1) * metadata.GetTableRowSize(table);
        int CodedSize(int tags, params TableIndex[] tables) => tables.Max(metadata.GetTableRowCount) < (1 << (16 - tags)) ? 2 : 4;
        void Index(int offset, int width, int value)
        {
            if (width == 2) BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset, 2), checked((ushort)value));
            else BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset, 4), value);
        }
        int FileOffset(int rva)
        {
            var section = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData));
            return section.PointerToRawData + rva - section.VirtualAddress;
        }
    }

    [Fact]
    public void AnotherCompletedOutputCannotRedeemTheOperationRecords()
    {
        var first = Compile(true); first.SaveToBytes();
        var second = Compile(true); var image = second.SaveToBytes();
        Assert.Throws<CompileException>(() => first.NativeTypedArrayBuffers!.ValidateImage(image));
    }

    [Fact]
    public void ForeignSameIndexBodyAndForgedResultOffsetsDoNotRedeemARealReceipt()
    {
        var compiler = Compile(true); compiler.SaveToBytes();
        var proof = (NativeTaskSourceBufferProof)typeof(NativeTypedArrayBufferLookup)
            .GetField("_taskSourceProof", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compiler.NativeTypedArrayBuffers)!;
        var actual = compiler.NativePrototypeFields!.CompletedUnprovedResults.Producers[0];
        Assert.True(proof.Claims(actual));
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("foreign_task_body"), typeof(object).Assembly);
        var owner = assembly.DefineDynamicModule("main").DefineType("Foreign");
        var body = owner.DefineMethod("Get", MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
        body.DeclareLocal(typeof(object)); body.DeclareLocal(typeof(object));
        var sameIndexLocal = body.DeclareLocal(typeof(TaskCompletionSource<object>));
        Assert.Equal(2, sameIndexLocal.LocalIndex);
        Assert.False(proof.Claims(actual with { Body = body }));
        Assert.False(proof.Claims(actual with { CallOffset = actual.CallOffset + 1 }));
        Assert.False(proof.Claims(actual with { CompletedBodyLength = actual.CompletedBodyLength + 1 }));
    }

    [Fact]
    public void ForeignMethodAndIncompleteOrUnrecordedConstructionCannotMintTheProof()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("task_proof_scope"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var lookup = new NativeTypedArrayBufferLookup(module, TypeProvider.Runtime);
        var foreignAssembly = new PersistedAssemblyBuilder(new AssemblyName("foreign_task_scope"), typeof(object).Assembly);
        var foreignOwner = foreignAssembly.DefineDynamicModule("main").DefineType("Foreign");
        var foreignMethod = foreignOwner.DefineMethod("Get", MethodAttributes.Static, typeof(Task<object>), Type.EmptyTypes);
        Assert.Throws<InvalidOperationException>(() => lookup.BeginTaskSourceProof(foreignMethod));
        var owner = module.DefineType("Own"); var method = owner.DefineMethod("Get", MethodAttributes.Static, typeof(Task<object>), Type.EmptyTypes);
        var proof = lookup.BeginTaskSourceProof(method);
        Assert.Throws<InvalidOperationException>(() => lookup.BeginTaskSourceProof(method));
        Assert.Throws<InvalidOperationException>(() => proof.RecordAllocation(typeof(TaskCompletionSource<object>).GetConstructor([typeof(object)])!, 0, 5, null));
        Assert.Throws<InvalidOperationException>(() => proof.RecordLoad(0));
        Assert.Throws<InvalidOperationException>(() => proof.Claims(new(typeof(TaskCompletionSource<object>).GetProperty("Task")!.GetMethod!, method.GetILGenerator(), 0, 1)));
    }

    private static ILCompiler Compile(bool references)
    {
        var statements = new Parser(new Lexer(Original).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var map = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), row => row.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("task_source_" + Guid.NewGuid().ToString("N"), false, references, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, map); return compiler;
    }
    private readonly record struct Step(int Offset, OpCode Code, int Operand, int Size);
    private static IEnumerable<Step> Read(byte[] bytes)
    {
        for (int offset = 0; offset < bytes.Length;)
        {
            int start = offset; int value = bytes[offset++]; if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
            var code = Codes[(short)value];
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
            int operand = size switch { 0 => 0, 1 => bytes[offset], 2 => BitConverter.ToUInt16(bytes, offset), _ => BitConverter.ToInt32(bytes, offset) };
            offset += size; yield return new(start, code, operand, offset - start);
        }
    }
    private static (int Kind, int Index) Local(Step row) => row.Code.Value switch
    {
        0x06 => (1, 0), 0x07 => (1, 1), 0x08 => (1, 2), 0x09 => (1, 3),
        0x0a => (2, 0), 0x0b => (2, 1), 0x0c => (2, 2), 0x0d => (2, 3),
        0x11 => (1, row.Operand), 0x13 => (2, row.Operand), _ => (0, -1)
    };
    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
}
