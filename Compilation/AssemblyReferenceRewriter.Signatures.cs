using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    #region Signature Rewriting

    private byte[] RewriteTypeSignature(BlobReader reader)
    {
        var builder = new BlobBuilder();
        RewriteTypeSignatureCore(ref reader, builder);
        return builder.ToArray();
    }

    private void RewriteTypeSignatureCore(ref BlobReader reader, BlobBuilder builder)
    {
        var typeCode = reader.ReadCompressedInteger();
        builder.WriteCompressedInteger(typeCode);

        switch (typeCode)
        {
            case 0x00: // ELEMENT_TYPE_END
                break;

            case 0x01: // ELEMENT_TYPE_VOID
            case 0x02: // ELEMENT_TYPE_BOOLEAN
            case 0x03: // ELEMENT_TYPE_CHAR
            case 0x04: // ELEMENT_TYPE_I1
            case 0x05: // ELEMENT_TYPE_U1
            case 0x06: // ELEMENT_TYPE_I2
            case 0x07: // ELEMENT_TYPE_U2
            case 0x08: // ELEMENT_TYPE_I4
            case 0x09: // ELEMENT_TYPE_U4
            case 0x0A: // ELEMENT_TYPE_I8
            case 0x0B: // ELEMENT_TYPE_U8
            case 0x0C: // ELEMENT_TYPE_R4
            case 0x0D: // ELEMENT_TYPE_R8
            case 0x0E: // ELEMENT_TYPE_STRING
            case 0x16: // ELEMENT_TYPE_TYPEDBYREF
            case 0x18: // ELEMENT_TYPE_I
            case 0x19: // ELEMENT_TYPE_U
            case 0x1C: // ELEMENT_TYPE_OBJECT
                // Primitive types - no additional data
                break;

            case 0x0F: // ELEMENT_TYPE_PTR
            case 0x10: // ELEMENT_TYPE_BYREF
            case 0x1D: // ELEMENT_TYPE_SZARRAY
            case 0x45: // ELEMENT_TYPE_PINNED
                // Single type follows
                RewriteTypeSignatureCore(ref reader, builder);
                break;

            case 0x11: // ELEMENT_TYPE_VALUETYPE
            case 0x12: // ELEMENT_TYPE_CLASS
                {
                    // TypeDefOrRefOrSpec encoded
                    var codedIndex = reader.ReadCompressedInteger();
                    var newIndex = MapTypeDefOrRefOrSpec(codedIndex);
                    builder.WriteCompressedInteger(newIndex);
                    break;
                }

            case 0x13: // ELEMENT_TYPE_VAR
            case 0x1E: // ELEMENT_TYPE_MVAR
                {
                    // Generic parameter number
                    var paramNum = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(paramNum);
                    break;
                }

            case 0x14: // ELEMENT_TYPE_ARRAY
                {
                    // Element type
                    RewriteTypeSignatureCore(ref reader, builder);
                    // Rank
                    var rank = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(rank);
                    // NumSizes
                    var numSizes = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(numSizes);
                    for (int i = 0; i < numSizes; i++)
                    {
                        var size = reader.ReadCompressedInteger();
                        builder.WriteCompressedInteger(size);
                    }
                    // NumLoBounds
                    var numLoBounds = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(numLoBounds);
                    for (int i = 0; i < numLoBounds; i++)
                    {
                        var loBound = reader.ReadCompressedSignedInteger();
                        builder.WriteCompressedSignedInteger(loBound);
                    }
                    break;
                }

            case 0x15: // ELEMENT_TYPE_GENERICINST
                {
                    // Generic type (CLASS or VALUETYPE)
                    var elemType = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(elemType);
                    // TypeDefOrRefOrSpec
                    var codedIndex = reader.ReadCompressedInteger();
                    var newIndex = MapTypeDefOrRefOrSpec(codedIndex);
                    builder.WriteCompressedInteger(newIndex);
                    // Argument count
                    var argCount = reader.ReadCompressedInteger();
                    builder.WriteCompressedInteger(argCount);
                    // Arguments
                    for (int i = 0; i < argCount; i++)
                    {
                        RewriteTypeSignatureCore(ref reader, builder);
                    }
                    break;
                }

            case 0x1B: // ELEMENT_TYPE_FNPTR
                {
                    // Method signature follows
                    RewriteMethodSignatureCore(ref reader, builder);
                    break;
                }

            case 0x41: // ELEMENT_TYPE_SENTINEL
                // Sentinel for varargs - no data
                break;

            case 0x1F: // ELEMENT_TYPE_CMOD_REQD
            case 0x20: // ELEMENT_TYPE_CMOD_OPT
                {
                    // TypeDefOrRefOrSpec
                    var codedIndex = reader.ReadCompressedInteger();
                    var newIndex = MapTypeDefOrRefOrSpec(codedIndex);
                    builder.WriteCompressedInteger(newIndex);
                    // Type follows
                    RewriteTypeSignatureCore(ref reader, builder);
                    break;
                }

            default:
                // Unknown type code - try to copy remaining bytes
                while (reader.RemainingBytes > 0)
                {
                    builder.WriteByte(reader.ReadByte());
                }
                break;
        }
    }

    private int MapTypeDefOrRefOrSpec(int codedIndex)
    {
        // Decode TypeDefOrRefOrSpec coded index
        var tag = codedIndex & 0x3;
        var row = codedIndex >> 2;

        EntityHandle newHandle = tag switch
        {
            0 => _typeDefMap.GetValueOrDefault(
                MetadataTokens.TypeDefinitionHandle(row),
                MetadataTokens.TypeDefinitionHandle(row)),
            1 => _typeRefMap.GetValueOrDefault(
                MetadataTokens.TypeReferenceHandle(row),
                MetadataTokens.TypeReferenceHandle(row)),
            2 => _typeSpecMap.GetValueOrDefault(
                MetadataTokens.TypeSpecificationHandle(row),
                MetadataTokens.TypeSpecificationHandle(row)),
            _ => default
        };

        // Re-encode as coded index
        var newRow = MetadataTokens.GetRowNumber(newHandle);
        var newTag = newHandle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => tag
        };

        return (newRow << 2) | newTag;
    }

    private byte[] RewriteMethodOrFieldSignature(BlobReader reader)
    {
        if (reader.Length == 0)
            return [];

        var builder = new BlobBuilder();

        var header = reader.ReadByte();
        builder.WriteByte(header);

        // Check if it's a field signature
        if ((header & 0x0F) == 0x06) // FIELD
        {
            // Rewrite field type
            RewriteTypeSignatureCore(ref reader, builder);
        }
        else
        {
            // Method signature
            RewriteMethodSignatureAfterHeader(ref reader, builder, header);
        }

        return builder.ToArray();
    }

    private byte[] RewriteMethodSignature(BlobReader reader)
    {
        if (reader.Length == 0)
            return [];

        var builder = new BlobBuilder();
        RewriteMethodSignatureCore(ref reader, builder);
        return builder.ToArray();
    }

    private void RewriteMethodSignatureCore(ref BlobReader reader, BlobBuilder builder)
    {
        var header = reader.ReadByte();
        builder.WriteByte(header);
        RewriteMethodSignatureAfterHeader(ref reader, builder, header);
    }

    private void RewriteMethodSignatureAfterHeader(ref BlobReader reader, BlobBuilder builder, byte header)
    {
        // Generic parameter count
        if ((header & 0x10) != 0) // GENERIC
        {
            var genParamCount = reader.ReadCompressedInteger();
            builder.WriteCompressedInteger(genParamCount);
        }

        // Parameter count
        var paramCount = reader.ReadCompressedInteger();
        builder.WriteCompressedInteger(paramCount);

        // Return type
        RewriteTypeSignatureCore(ref reader, builder);

        // Parameters
        for (int i = 0; i < paramCount; i++)
        {
            RewriteTypeSignatureCore(ref reader, builder);
        }
    }

    private byte[] RewriteFieldSignature(BlobReader reader)
    {
        if (reader.Length == 0)
            return [];

        var builder = new BlobBuilder();

        var header = reader.ReadByte();
        builder.WriteByte(header);

        // Rewrite field type
        RewriteTypeSignatureCore(ref reader, builder);

        return builder.ToArray();
    }

    private byte[] RewriteMethodSpecSignature(BlobReader reader)
    {
        if (reader.Length == 0)
            return [];

        var builder = new BlobBuilder();

        var header = reader.ReadByte();
        builder.WriteByte(header);

        // Argument count
        var argCount = reader.ReadCompressedInteger();
        builder.WriteCompressedInteger(argCount);

        // Type arguments
        for (int i = 0; i < argCount; i++)
        {
            RewriteTypeSignatureCore(ref reader, builder);
        }

        return builder.ToArray();
    }

    private byte[] RewriteLocalVarsSignature(BlobReader reader)
    {
        if (reader.Length == 0)
            return [];

        var builder = new BlobBuilder();

        var header = reader.ReadByte();
        builder.WriteByte(header);

        if (header != 0x07) // LOCAL_SIG
        {
            // Not a local variables signature
            while (reader.RemainingBytes > 0)
            {
                builder.WriteByte(reader.ReadByte());
            }
            return builder.ToArray();
        }

        // Local count
        var localCount = reader.ReadCompressedInteger();
        builder.WriteCompressedInteger(localCount);

        // Local types
        for (int i = 0; i < localCount; i++)
        {
            RewriteTypeSignatureCore(ref reader, builder);
        }

        return builder.ToArray();
    }

    #endregion
}
