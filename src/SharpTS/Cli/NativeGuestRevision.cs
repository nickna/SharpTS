using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Cli;

/// <summary>A guest revision ignores only emission-generated PE timestamp and module identity.</summary>
internal sealed record NativeGuestRevision(string RevisionSha256, string ImageSha256, string? PdbSha256)
{
    internal static NativeGuestRevision Compute(byte[] image, byte[]? pdb = null)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null || pe.PEHeaders.PEHeader is null ||
            (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) == 0)
            throw new BadImageFormatException("Native guest revision requires a managed PE DLL.");
        var metadata = pe.GetMetadataReader();
        var module = metadata.GetModuleDefinition();
        if (module.Mvid.IsNil) throw new BadImageFormatException("Native guest module identity is missing.");
        int guidIndex = MetadataTokens.GetHeapOffset(module.Mvid);
        int guidHeapSize = metadata.GetHeapSize(HeapIndex.Guid);
        int guidSlot = checked((guidIndex - 1) * 16);
        if (guidIndex < 1 || guidSlot > guidHeapSize - 16)
            throw new BadImageFormatException("Native guest module identity is outside the GUID heap.");
        int mvidOffset = checked(pe.PEHeaders.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.Guid) + guidSlot);
        int timestampOffset = checked(pe.PEHeaders.CoffHeaderStartOffset + 4);
        if (timestampOffset < 0 || timestampOffset > image.Length - 4 || mvidOffset < 0 || mvidOffset > image.Length - 16 ||
            !image.AsSpan(mvidOffset, 16).SequenceEqual(metadata.GetGuid(module.Mvid).ToByteArray()))
            throw new BadImageFormatException("Native guest revision fields do not match the serialized image.");
        byte[] normalized = (byte[])image.Clone();
        normalized.AsSpan(timestampOffset, 4).Clear();
        normalized.AsSpan(mvidOffset, 16).Clear();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("SharpTS.NativeGuestRevision.v1\0"));
        hash.AppendData(BitConverter.GetBytes(normalized.LongLength));
        hash.AppendData(normalized);
        hash.AppendData([pdb is null ? (byte)0 : (byte)1]);
        if (pdb is not null) hash.AppendData(pdb);
        return new(Convert.ToHexStringLower(hash.GetHashAndReset()),
            Convert.ToHexStringLower(SHA256.HashData(image)), pdb is null ? null : Convert.ToHexStringLower(SHA256.HashData(pdb)));
    }
}
