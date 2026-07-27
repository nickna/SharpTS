using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Serializes the debug metadata produced by
/// <see cref="System.Reflection.Emit.PersistedAssemblyBuilder.GenerateMetadata(out BlobBuilder, out BlobBuilder, out MetadataBuilder)"/>
/// into a standalone portable PDB, built against the row counts of the <i>final</i> PE image.
/// </summary>
/// <remarks>
/// The PDB's <c>#Pdb</c> stream records the type-system table row counts of the assembly it
/// describes. SharpTS may rewrite assembly references after the initial serialization, which
/// changes <c>TypeRef</c>/<c>MemberRef</c>/<c>AssemblyRef</c> counts, so the counts are read back
/// from the finished image rather than taken from the pre-rewrite builder.
/// </remarks>
internal static class PdbEmitter
{
    internal readonly record struct Result(byte[] Bytes, BlobContentId ContentId, ushort FormatVersion, ImmutableArray<byte> Checksum);

    /// <summary>Hash algorithm recorded in the PE's PDB-checksum debug directory entry.</summary>
    internal const string ChecksumAlgorithmName = "SHA256";

    internal static Result Serialize(
        MetadataBuilder pdbMetadata,
        ImmutableArray<int> typeSystemRowCounts,
        MethodDefinitionHandle entryPoint)
    {
        var builder = new PortablePdbBuilder(pdbMetadata, typeSystemRowCounts, entryPoint, DeterministicIdProvider);
        var blob = new BlobBuilder();
        BlobContentId contentId = builder.Serialize(blob);

        byte[] bytes = blob.ToArray();
        return new Result(bytes, contentId, builder.FormatVersion, ImmutableArray.Create(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Reads the type-system table row counts out of a serialized PE, in the shape
    /// <see cref="PortablePdbBuilder"/> expects (debug tables left at zero).
    /// </summary>
    internal static ImmutableArray<int> ReadTypeSystemRowCounts(byte[] peImage)
    {
        using var reader = new PEReader(new MemoryStream(peImage, writable: false));
        var metadata = reader.GetMetadataReader();

        var counts = new int[MetadataTokens.TableCount];
        foreach (TableIndex table in Enum.GetValues<TableIndex>())
        {
            // Debug tables live in the PDB, never in the PE, and must stay zero here.
            if (table >= TableIndex.Document) continue;
            counts[(int)table] = metadata.GetTableRowCount(table);
        }
        return ImmutableArray.Create(counts);
    }

    /// <summary>
    /// Maps each <c>MethodDef</c> row id to the <c>StandAloneSig</c> row id of its local signature
    /// (0 when the method declares no locals or has no body).
    /// </summary>
    /// <remarks>
    /// Read from the finished image so the row ids match the ones a debugger will resolve, even
    /// though assembly-reference rewriting can renumber <c>StandAloneSig</c>.
    /// </remarks>
    internal static Func<int, int> ReadLocalSignatureRids(byte[] peImage)
    {
        using var reader = new PEReader(new MemoryStream(peImage, writable: false));
        var metadata = reader.GetMetadataReader();

        var byRid = new int[metadata.MethodDefinitions.Count + 1];
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;

            var localSignature = reader.GetMethodBody(method.RelativeVirtualAddress).LocalSignature;
            if (!localSignature.IsNil)
                byRid[MetadataTokens.GetRowNumber(handle)] = MetadataTokens.GetRowNumber(localSignature);
        }

        return rid => (uint)rid < (uint)byRid.Length ? byRid[rid] : 0;
    }

    /// <summary>
    /// Confirms that a post-processing pass preserved <c>MethodDef</c> identity row-for-row, which
    /// is what lets a PDB built from the pre-pass emit describe the final image.
    /// </summary>
    /// <exception cref="CompileException">
    /// Thrown when the mapping shifted, rather than shipping a PDB that silently points a debugger
    /// at the wrong methods.
    /// </exception>
    internal static void VerifyMethodMappingPreserved(byte[] before, byte[] after)
    {
        using var beforeReader = new PEReader(new MemoryStream(before, writable: false));
        using var afterReader = new PEReader(new MemoryStream(after, writable: false));
        var b = beforeReader.GetMetadataReader();
        var a = afterReader.GetMetadataReader();

        int beforeCount = b.MethodDefinitions.Count;
        int afterCount = a.MethodDefinitions.Count;
        if (beforeCount != afterCount)
            throw new CompileException(
                $"Debug symbols cannot be emitted: assembly post-processing changed the method count " +
                $"({beforeCount} -> {afterCount}), so the generated PDB would not match the output.");

        foreach (var handle in b.MethodDefinitions)
        {
            string expected = Describe(b, handle);
            string actual = Describe(a, handle);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new CompileException(
                    $"Debug symbols cannot be emitted: assembly post-processing reordered methods " +
                    $"(row {MetadataTokens.GetRowNumber(handle)} was '{expected}', is now '{actual}').");
        }

        static string Describe(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var method = reader.GetMethodDefinition(handle);
            var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
            return $"{reader.GetString(declaringType.Namespace)}.{reader.GetString(declaringType.Name)}::{reader.GetString(method.Name)}";
        }
    }

    /// <summary>
    /// Derives the PDB content id from the PDB's own content so repeated builds of unchanged input
    /// produce byte-identical symbols.
    /// </summary>
    private static BlobContentId DeterministicIdProvider(IEnumerable<Blob> content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var blob in content)
        {
            ArraySegment<byte> segment = blob.GetBytes();
            hash.AppendData(segment.Array!, segment.Offset, segment.Count);
        }
        return BlobContentId.FromHash(hash.GetHashAndReset());
    }

}
