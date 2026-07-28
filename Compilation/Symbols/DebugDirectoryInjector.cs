using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Injects a portable-PDB debug directory into an already-serialized PE image.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A debug directory is normally supplied to
/// <see cref="System.Reflection.Metadata.Ecma335.ManagedPEBuilder"/> at serialization time, but
/// SharpTS runs a post-pass — <c>PEPacker.AssemblyReferenceRewriter</c> — that rebuilds the final
/// PE from scratch to retarget <c>System.Private.CoreLib</c> references onto SDK reference
/// assemblies. That rewriter rebuilds the image <i>without</i> a debug directory and exposes no
/// hook to preserve one, so symbols must be re-attached to the final bytes afterwards.</para>
///
/// <para><b>What is safe to assume.</b> The rewriter preserves <c>MethodDef</c> row ids and their
/// ordering, so a portable PDB remains valid across the rewrite (<see cref="PdbEmitter"/> still
/// rebuilds the PDB against the <i>final</i> row counts, and
/// <see cref="PdbEmitter.VerifyMethodMappingPreserved"/> proves the assumption per build rather
/// than trusting it).</para>
///
/// <para><b>How the injection works.</b> The payload — the debug directory table plus its CodeView
/// and PDB-checksum blobs — is appended to the end of an existing section's content, which changes
/// no RVA, no section count, and no PE header size:</para>
/// <list type="number">
/// <item>Prefer the section holding the CLI header (<c>.text</c>), matching where Roslyn and
/// <c>ManagedPEBuilder</c> place a debug directory. It is used when the payload fits between the
/// section's current <c>VirtualSize</c> and the next section's RVA.</item>
/// <item>Otherwise fall back to the last section, whose virtual size can always grow because
/// nothing follows it. Its <c>MemDiscardable</c> flag is cleared so the loader keeps the debug
/// directory mapped.</item>
/// </list>
/// <para>Growing the section's raw data beyond its file-alignment padding shifts the raw data of
/// every later section; their <c>PointerToRawData</c> fields are patched to match. Virtual
/// addresses never move.</para>
/// </remarks>
internal static class DebugDirectoryInjector
{
    private const int DebugDirectoryEntrySize = 28;

    /// <summary>Entries written: a CodeView pointer to the PDB, and the PDB's checksum.</summary>
    private const int EntryCount = 2;
    private const int CoffHeaderSize = 20;
    private const int SectionHeaderSize = 40;

    /// <summary>Offset of <c>SizeOfImage</c> within the optional header (same for PE32 and PE32+).</summary>
    private const int SizeOfImageOffset = 56;

    /// <summary>Offset of the data-directory array within the optional header.</summary>
    private const int Pe32DataDirectoriesOffset = 96;
    private const int Pe32PlusDataDirectoriesOffset = 112;

    /// <summary>Index of the debug data directory in the optional header's data-directory array.</summary>
    private const int DebugDataDirectoryIndex = 6;

    private const uint CodeViewSignatureRsds = 0x53445352; // 'RSDS'

    /// <summary>
    /// The version DWORD stamped into a CodeView entry that points at a portable PDB:
    /// the low word is the portable-PDB format version, the high word the portable marker 'PM'.
    /// </summary>
    private const ushort PortableCodeViewVersionMagic = 0x504d;

    /// <summary>PDB-checksum debug directory entries are version 1.</summary>
    private const uint PdbChecksumEntryVersion = 1;

    private const int SectionCharacteristicsMemDiscardable = 0x02000000;

    /// <summary>
    /// Returns a copy of <paramref name="peImage"/> carrying a debug directory with a CodeView
    /// entry pointing at <paramref name="pdbPath"/> and a matching PDB-checksum entry.
    /// </summary>
    /// <param name="peImage">The serialized PE to augment. Not modified.</param>
    /// <param name="pdbContentId">Content id returned when the portable PDB was serialized.</param>
    /// <param name="portablePdbVersion">
    /// <c>PortablePdbBuilder.FormatVersion</c> for the emitted PDB.
    /// </param>
    /// <param name="pdbPath">Path recorded in the CodeView entry so debuggers can find the PDB.</param>
    /// <param name="pdbChecksum">Cryptographic hash of the serialized PDB bytes.</param>
    /// <param name="checksumAlgorithmName">Name of the hash algorithm used for <paramref name="pdbChecksum"/>.</param>
    internal static byte[] Inject(
        byte[] peImage,
        BlobContentId pdbContentId,
        ushort portablePdbVersion,
        string pdbPath,
        ImmutableArray<byte> pdbChecksum,
        string checksumAlgorithmName = "SHA256")
    {
        ArgumentNullException.ThrowIfNull(peImage);
        ArgumentException.ThrowIfNullOrEmpty(pdbPath);

        PEHeaders headers;
        using (var probe = new PEReader(new MemoryStream(peImage, writable: false)))
        {
            headers = probe.PEHeaders;
            if (headers.PEHeader is null)
                throw new InvalidOperationException("Cannot inject debug symbols: image has no PE optional header.");
        }

        byte[] codeViewData = BuildCodeViewData(pdbContentId, pdbPath);
        byte[] checksumData = BuildChecksumData(checksumAlgorithmName, pdbChecksum);

        // Payload layout: [table][CodeView blob][4-byte aligned][checksum blob]
        int tableSize = EntryCount * DebugDirectoryEntrySize;
        int codeViewOffset = tableSize;
        int checksumOffset = Align(codeViewOffset + codeViewData.Length, 4);
        int payloadSize = checksumOffset + checksumData.Length;

        var placement = ChoosePlacement(headers, payloadSize);
        var section = headers.SectionHeaders[placement.SectionIndex];

        int payloadRva = section.VirtualAddress + placement.OffsetInSection;
        int payloadFilePos = section.PointerToRawData + placement.OffsetInSection;

        byte[] payload = new byte[payloadSize];
        WriteDebugDirectoryEntry(
            payload.AsSpan(0, DebugDirectoryEntrySize),
            type: (uint)DebugDirectoryEntryType.CodeView,
            stamp: pdbContentId.Stamp,
            version: ((uint)PortableCodeViewVersionMagic << 16) | portablePdbVersion,
            dataSize: codeViewData.Length,
            dataRva: payloadRva + codeViewOffset,
            dataFilePos: payloadFilePos + codeViewOffset);
        WriteDebugDirectoryEntry(
            payload.AsSpan(DebugDirectoryEntrySize, DebugDirectoryEntrySize),
            type: (uint)DebugDirectoryEntryType.PdbChecksum,
            stamp: 0,
            version: PdbChecksumEntryVersion,
            dataSize: checksumData.Length,
            dataRva: payloadRva + checksumOffset,
            dataFilePos: payloadFilePos + checksumOffset);
        codeViewData.CopyTo(payload, codeViewOffset);
        checksumData.CopyTo(payload, checksumOffset);

        return WritePatchedImage(peImage, headers, placement, payload, payloadRva, payloadSize);
    }

    /// <summary>Where the payload will live: which section, and at what offset inside it.</summary>
    private readonly record struct Placement(int SectionIndex, int OffsetInSection, bool ClearDiscardable);

    private static Placement ChoosePlacement(PEHeaders headers, int payloadSize)
    {
        var sections = headers.SectionHeaders;

        // Preferred: the section holding the CLI header — `.text` for anything ManagedPEBuilder
        // produces, which is also where Roslyn puts its debug directory.
        int preferred = FindSectionContaining(headers, headers.CorHeaderStartOffset);
        if (preferred >= 0)
        {
            int offset = Align(sections[preferred].VirtualSize, 4);
            int headroom = preferred + 1 < sections.Length
                ? sections[preferred + 1].VirtualAddress - sections[preferred].VirtualAddress
                : int.MaxValue;
            if (offset + payloadSize <= headroom)
                return new Placement(preferred, offset, ClearDiscardable: false);
        }

        // Fallback: the last section can always grow virtually because nothing follows it. Keep it
        // mapped at run time so an attached debugger can still read the directory from the image.
        int last = sections.Length - 1;
        if (last < 0)
            throw new InvalidOperationException("Cannot inject debug symbols: image has no sections.");

        return new Placement(
            last,
            Align(sections[last].VirtualSize, 4),
            ClearDiscardable: (sections[last].SectionCharacteristics & SectionCharacteristics.MemDiscardable) != 0);
    }

    private static int FindSectionContaining(PEHeaders headers, int fileOffset)
    {
        if (fileOffset <= 0) return -1;
        var sections = headers.SectionHeaders;
        for (int i = 0; i < sections.Length; i++)
        {
            int start = sections[i].PointerToRawData;
            if (fileOffset >= start && fileOffset < start + sections[i].SizeOfRawData)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Copies the image, splicing in the payload (plus any file-alignment growth) and patching the
    /// headers that describe the new layout.
    /// </summary>
    private static byte[] WritePatchedImage(
        byte[] source,
        PEHeaders headers,
        Placement placement,
        byte[] payload,
        int payloadRva,
        int payloadSize)
    {
        var pe = headers.PEHeader!;
        var sections = headers.SectionHeaders;
        var target = sections[placement.SectionIndex];

        int newVirtualSize = placement.OffsetInSection + payloadSize;
        int newSizeOfRawData = Align(newVirtualSize, pe.FileAlignment);
        int rawGrowth = Math.Max(0, newSizeOfRawData - target.SizeOfRawData);
        int spliceAt = target.PointerToRawData + target.SizeOfRawData;

        byte[] result = new byte[Math.Max(source.Length, spliceAt) + rawGrowth];
        source.AsSpan(0, Math.Min(spliceAt, source.Length)).CopyTo(result);
        if (source.Length > spliceAt)
            source.AsSpan(spliceAt).CopyTo(result.AsSpan(spliceAt + rawGrowth));

        payload.CopyTo(result.AsSpan(target.PointerToRawData + placement.OffsetInSection));

        int sectionTableOffset = headers.CoffHeaderStartOffset + CoffHeaderSize + headers.CoffHeader.SizeOfOptionalHeader;
        int optionalHeader = headers.PEHeaderStartOffset;

        // Target section grows; sections physically after it slide by the same amount. RVAs are
        // untouched, so nothing inside the metadata or IL needs to change.
        var targetHeader = result.AsSpan(sectionTableOffset + placement.SectionIndex * SectionHeaderSize, SectionHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(targetHeader[8..], newVirtualSize);
        BinaryPrimitives.WriteInt32LittleEndian(targetHeader[16..], newSizeOfRawData);
        if (placement.ClearDiscardable)
        {
            int flags = BinaryPrimitives.ReadInt32LittleEndian(targetHeader[36..]);
            BinaryPrimitives.WriteInt32LittleEndian(targetHeader[36..], flags & ~SectionCharacteristicsMemDiscardable);
        }

        if (rawGrowth > 0)
        {
            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].PointerToRawData <= target.PointerToRawData) continue;
                var header = result.AsSpan(sectionTableOffset + i * SectionHeaderSize, SectionHeaderSize);
                BinaryPrimitives.WriteInt32LittleEndian(header[20..], sections[i].PointerToRawData + rawGrowth);
            }
        }

        // SizeOfImage must still cover every section's virtual extent.
        int virtualEnd = 0;
        for (int i = 0; i < sections.Length; i++)
        {
            int size = i == placement.SectionIndex ? newVirtualSize : sections[i].VirtualSize;
            virtualEnd = Math.Max(virtualEnd, sections[i].VirtualAddress + size);
        }
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(optionalHeader + SizeOfImageOffset), Align(virtualEnd, pe.SectionAlignment));

        int debugDirectoryField = optionalHeader
            + (pe.Magic == PEMagic.PE32Plus ? Pe32PlusDataDirectoriesOffset : Pe32DataDirectoriesOffset)
            + DebugDataDirectoryIndex * 8;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(debugDirectoryField), payloadRva);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(debugDirectoryField + 4), EntryCount * DebugDirectoryEntrySize);

        return result;
    }

    private static void WriteDebugDirectoryEntry(
        Span<byte> entry, uint type, uint stamp, uint version, int dataSize, int dataRva, int dataFilePos)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(entry, 0);              // Characteristics
        BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], stamp);     // TimeDateStamp
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], version);   // Major/MinorVersion
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], type);
        BinaryPrimitives.WriteInt32LittleEndian(entry[16..], dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(entry[20..], dataRva);
        BinaryPrimitives.WriteInt32LittleEndian(entry[24..], dataFilePos);
    }

    private static byte[] BuildCodeViewData(BlobContentId contentId, string pdbPath)
    {
        byte[] path = Encoding.UTF8.GetBytes(pdbPath);
        byte[] data = new byte[4 + 16 + 4 + path.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(data, CodeViewSignatureRsds);
        contentId.Guid.TryWriteBytes(data.AsSpan(4, 16));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 1); // Age is always 1 for portable PDBs
        path.CopyTo(data, 24);
        return data;
    }

    private static byte[] BuildChecksumData(string algorithmName, ImmutableArray<byte> checksum)
    {
        byte[] name = Encoding.UTF8.GetBytes(algorithmName);
        byte[] data = new byte[name.Length + 1 + checksum.Length];
        name.CopyTo(data, 0);
        checksum.CopyTo(data, name.Length + 1);
        return data;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
