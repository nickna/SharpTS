using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    private int CopyMethodBody(MethodDefinition method)
    {
        var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
        var ilBytes = body.GetILBytes();

        if (ilBytes == null || ilBytes.Length == 0)
        {
            return -1;
        }

        // Patch metadata tokens in IL
        var patchedIL = PatchILTokens(ilBytes);

        // Get local variables signature
        StandaloneSignatureHandle localSig = default;
        if (!body.LocalSignature.IsNil)
        {
            localSig = _standAloneSigMap.GetValueOrDefault(body.LocalSignature, default);
            if (localSig.IsNil)
            {
                // Copy the standalone signature now
                var sig = _reader.GetStandaloneSignature(body.LocalSignature);
                var reader = _reader.GetBlobReader(sig.Signature);
                var newSigBytes = RewriteLocalVarsSignature(reader);
                localSig = _metadata.AddStandaloneSignature(_metadata.GetOrAddBlob(newSigBytes));
                _standAloneSigMap[body.LocalSignature] = localSig;
            }
        }

        // Build method body by writing directly to the IL stream
        var exceptionRegions = body.ExceptionRegions;
        bool initLocals = body.LocalVariablesInitialized;

        // Check if we can use tiny format (no exceptions, no locals, code < 64 bytes, max stack <= 8)
        bool canUseTinyFormat = exceptionRegions.Length == 0 &&
                                localSig.IsNil &&
                                patchedIL.Length < 64 &&
                                body.MaxStack <= 8;

        int methodBodyOffset;

        if (canUseTinyFormat)
        {
            // Tiny format: 1-byte header (format bits + code size)
            // Format: (CodeSize << 2) | 0x02
            methodBodyOffset = _ilStream.Count;
            byte header = (byte)((patchedIL.Length << 2) | 0x02);
            _ilStream.WriteByte(header);
            _ilStream.WriteBytes(patchedIL);
        }
        else
        {
            // Fat format: 12-byte header
            // Align to 4-byte boundary BEFORE recording offset
            int alignment = 4 - (_ilStream.Count % 4);
            if (alignment < 4)
            {
                for (int i = 0; i < alignment; i++)
                    _ilStream.WriteByte(0);
            }

            // Record offset AFTER alignment
            methodBodyOffset = _ilStream.Count;

            // Fat header (12 bytes)
            // Flags (2 bytes): 0x3 = fat format, 0x10 = init locals, 0x8 = more sections
            ushort flags = 0x3003; // Fat format, header size = 3 (dwords)
            if (initLocals)
                flags |= 0x0010;
            if (exceptionRegions.Length > 0)
                flags |= 0x0008; // More sections

            _ilStream.WriteUInt16(flags);
            _ilStream.WriteUInt16((ushort)body.MaxStack);
            _ilStream.WriteInt32(patchedIL.Length);
            _ilStream.WriteInt32(localSig.IsNil ? 0 : MetadataTokens.GetToken(localSig));

            // IL code
            _ilStream.WriteBytes(patchedIL);

            // Exception handlers (if any)
            if (exceptionRegions.Length > 0)
            {
                // Align to 4-byte boundary
                alignment = 4 - (_ilStream.Count % 4);
                if (alignment < 4)
                {
                    for (int i = 0; i < alignment; i++)
                        _ilStream.WriteByte(0);
                }

                // Determine if we need fat exception handlers
                bool needsFatHandlers = false;
                foreach (var region in exceptionRegions)
                {
                    if (region.TryOffset > 0xFFFF || region.TryLength > 0xFF ||
                        region.HandlerOffset > 0xFFFF || region.HandlerLength > 0xFF)
                    {
                        needsFatHandlers = true;
                        break;
                    }
                }

                if (needsFatHandlers)
                {
                    // Fat exception header
                    int dataSize = 4 + (exceptionRegions.Length * 24);
                    _ilStream.WriteByte(0x41); // Fat format, exception handling
                    _ilStream.WriteByte((byte)(dataSize & 0xFF));
                    _ilStream.WriteByte((byte)((dataSize >> 8) & 0xFF));
                    _ilStream.WriteByte((byte)((dataSize >> 16) & 0xFF));

                    foreach (var region in exceptionRegions)
                    {
                        int flags2 = region.Kind switch
                        {
                            ExceptionRegionKind.Catch => 0,
                            ExceptionRegionKind.Filter => 1,
                            ExceptionRegionKind.Finally => 2,
                            ExceptionRegionKind.Fault => 4,
                            _ => 0
                        };
                        _ilStream.WriteInt32(flags2);
                        _ilStream.WriteInt32(region.TryOffset);
                        _ilStream.WriteInt32(region.TryLength);
                        _ilStream.WriteInt32(region.HandlerOffset);
                        _ilStream.WriteInt32(region.HandlerLength);

                        if (region.Kind == ExceptionRegionKind.Catch)
                        {
                            var catchType = MapEntityHandle(region.CatchType);
                            _ilStream.WriteInt32(MetadataTokens.GetToken(catchType));
                        }
                        else if (region.Kind == ExceptionRegionKind.Filter)
                        {
                            _ilStream.WriteInt32(region.FilterOffset);
                        }
                        else
                        {
                            _ilStream.WriteInt32(0);
                        }
                    }
                }
                else
                {
                    // Small exception header
                    int dataSize = 4 + (exceptionRegions.Length * 12);
                    _ilStream.WriteByte(0x01); // Small format, exception handling
                    _ilStream.WriteByte((byte)dataSize);
                    _ilStream.WriteUInt16(0); // Reserved

                    foreach (var region in exceptionRegions)
                    {
                        ushort flags2 = region.Kind switch
                        {
                            ExceptionRegionKind.Catch => 0,
                            ExceptionRegionKind.Filter => 1,
                            ExceptionRegionKind.Finally => 2,
                            ExceptionRegionKind.Fault => 4,
                            _ => 0
                        };
                        _ilStream.WriteUInt16(flags2);
                        _ilStream.WriteUInt16((ushort)region.TryOffset);
                        _ilStream.WriteByte((byte)region.TryLength);
                        _ilStream.WriteUInt16((ushort)region.HandlerOffset);
                        _ilStream.WriteByte((byte)region.HandlerLength);

                        if (region.Kind == ExceptionRegionKind.Catch)
                        {
                            var catchType = MapEntityHandle(region.CatchType);
                            _ilStream.WriteInt32(MetadataTokens.GetToken(catchType));
                        }
                        else if (region.Kind == ExceptionRegionKind.Filter)
                        {
                            _ilStream.WriteInt32(region.FilterOffset);
                        }
                        else
                        {
                            _ilStream.WriteInt32(0);
                        }
                    }
                }
            }
        }

        return methodBodyOffset;
    }

    private byte[] PatchILTokens(byte[] ilBytes)
    {
        var result = new byte[ilBytes.Length];
        Buffer.BlockCopy(ilBytes, 0, result, 0, ilBytes.Length);

        int offset = 0;
        while (offset < ilBytes.Length)
        {
            byte opByte = ilBytes[offset++];
            ILOpCode opcode;

            if (opByte == 0xFE && offset < ilBytes.Length)
            {
                // Two-byte opcode
                opcode = (ILOpCode)(0xFE00 | ilBytes[offset++]);
            }
            else
            {
                opcode = (ILOpCode)opByte;
            }

            // Check if this opcode has a metadata token operand
            if (HasMetadataTokenOperand(opcode))
            {
                if (offset + 4 <= ilBytes.Length)
                {
                    int token = BitConverter.ToInt32(ilBytes, offset);
                    int newToken = MapMetadataToken(token);
                    BitConverter.TryWriteBytes(result.AsSpan(offset), newToken);
                }
                offset += 4;
            }
            else
            {
                offset += GetOperandSize(opcode);
            }
        }

        return result;
    }

    private static bool HasMetadataTokenOperand(ILOpCode opcode)
    {
        return opcode switch
        {
            // Method tokens
            ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or
            ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp => true,

            // Field tokens
            ILOpCode.Ldfld or ILOpCode.Stfld or ILOpCode.Ldsfld or
            ILOpCode.Stsfld or ILOpCode.Ldflda or ILOpCode.Ldsflda => true,

            // Type tokens
            ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Newarr or
            ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any or
            ILOpCode.Initobj or ILOpCode.Ldobj or ILOpCode.Stobj or
            ILOpCode.Cpobj or ILOpCode.Sizeof or ILOpCode.Mkrefany or
            ILOpCode.Refanyval or ILOpCode.Ldelema or ILOpCode.Constrained => true,

            // Token tokens
            ILOpCode.Ldtoken => true,

            // String tokens
            ILOpCode.Ldstr => true,

            // Calli (standalone signature)
            ILOpCode.Calli => true,

            _ => false
        };
    }

    private static int GetOperandSize(ILOpCode opcode)
    {
        return opcode switch
        {
            // No operand
            ILOpCode.Nop or ILOpCode.Break or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or
            ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or
            ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or
            ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Ldnull or ILOpCode.Ldc_i4_m1 or
            ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or
            ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or
            ILOpCode.Ldc_i4_8 or ILOpCode.Dup or ILOpCode.Pop or ILOpCode.Ret or
            ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2 or
            ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or ILOpCode.Ldind_i or
            ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref or ILOpCode.Stind_ref or
            ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or
            ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Add or ILOpCode.Sub or
            ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un or ILOpCode.Rem or ILOpCode.Rem_un or
            ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl or ILOpCode.Shr or
            ILOpCode.Shr_un or ILOpCode.Neg or ILOpCode.Not or ILOpCode.Conv_i1 or
            ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8 or ILOpCode.Conv_r4 or
            ILOpCode.Conv_r8 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8 or ILOpCode.Conv_r_un or
            ILOpCode.Throw or ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or
            ILOpCode.Conv_ovf_i4_un or ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u1_un or
            ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un or ILOpCode.Conv_ovf_u8_un or
            ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un or ILOpCode.Ldlen or
            ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or
            ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or
            ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or ILOpCode.Stelem_i or
            ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or
            ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8 or ILOpCode.Stelem_ref or ILOpCode.Conv_ovf_i1 or
            ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_u2 or
            ILOpCode.Conv_ovf_i4 or ILOpCode.Conv_ovf_u4 or ILOpCode.Conv_ovf_i8 or
            ILOpCode.Conv_ovf_u8 or ILOpCode.Ckfinite or ILOpCode.Conv_u2 or ILOpCode.Conv_u1 or
            ILOpCode.Conv_i or ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u or ILOpCode.Add_ovf or
            ILOpCode.Add_ovf_un or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un or ILOpCode.Sub_ovf or
            ILOpCode.Sub_ovf_un or ILOpCode.Endfinally or ILOpCode.Stind_i or ILOpCode.Conv_u or
            ILOpCode.Arglist or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or
            ILOpCode.Clt_un or ILOpCode.Localloc or ILOpCode.Endfilter or ILOpCode.Volatile or
            ILOpCode.Tail or ILOpCode.Cpblk or ILOpCode.Initblk or ILOpCode.Rethrow or
            ILOpCode.Refanytype or ILOpCode.Readonly => 0,

            // 1-byte operand
            ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or
            ILOpCode.Ldloca_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s or ILOpCode.Br_s or
            ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or
            ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or
            ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or
            ILOpCode.Leave_s or ILOpCode.Unaligned => 1,

            // 2-byte operand
            ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or
            ILOpCode.Ldloca or ILOpCode.Stloc => 2,

            // 4-byte operand (branch targets, integers, floats, tokens)
            ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or
            ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or
            ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un or ILOpCode.Blt_un or
            ILOpCode.Leave or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 => 4,

            // 8-byte operand
            ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,

            // Variable-length (switch)
            ILOpCode.Switch => 0, // Handled specially

            // All token-based opcodes are 4 bytes (handled by HasMetadataTokenOperand)
            _ => 0
        };
    }

    private int MapMetadataToken(int token)
    {
        var tableIndex = (token >> 24) & 0xFF;
        var rowNumber = token & 0x00FFFFFF;

        if (rowNumber == 0)
            return token;

        return tableIndex switch
        {
            0x01 => // TypeRef
                MetadataTokens.GetToken(_typeRefMap.GetValueOrDefault(
                    MetadataTokens.TypeReferenceHandle(rowNumber),
                    MetadataTokens.TypeReferenceHandle(rowNumber))),

            0x02 => // TypeDef
                MetadataTokens.GetToken(_typeDefMap.GetValueOrDefault(
                    MetadataTokens.TypeDefinitionHandle(rowNumber),
                    MetadataTokens.TypeDefinitionHandle(rowNumber))),

            0x04 => // FieldDef
                MetadataTokens.GetToken(_fieldDefMap.GetValueOrDefault(
                    MetadataTokens.FieldDefinitionHandle(rowNumber),
                    MetadataTokens.FieldDefinitionHandle(rowNumber))),

            0x06 => // MethodDef
                MetadataTokens.GetToken(_methodDefMap.GetValueOrDefault(
                    MetadataTokens.MethodDefinitionHandle(rowNumber),
                    MetadataTokens.MethodDefinitionHandle(rowNumber))),

            0x0A => // MemberRef
                MetadataTokens.GetToken(_memberRefMap.GetValueOrDefault(
                    MetadataTokens.MemberReferenceHandle(rowNumber),
                    MetadataTokens.MemberReferenceHandle(rowNumber))),

            0x11 => // StandAloneSig
                MetadataTokens.GetToken(_standAloneSigMap.GetValueOrDefault(
                    MetadataTokens.StandaloneSignatureHandle(rowNumber),
                    MetadataTokens.StandaloneSignatureHandle(rowNumber))),

            0x1B => // TypeSpec
                MetadataTokens.GetToken(_typeSpecMap.GetValueOrDefault(
                    MetadataTokens.TypeSpecificationHandle(rowNumber),
                    MetadataTokens.TypeSpecificationHandle(rowNumber))),

            0x2B => // MethodSpec
                MetadataTokens.GetToken(_methodSpecMap.GetValueOrDefault(
                    MetadataTokens.MethodSpecificationHandle(rowNumber),
                    MetadataTokens.MethodSpecificationHandle(rowNumber))),

            0x70 => // UserString
                MetadataTokens.GetToken(_userStringMap.GetValueOrDefault(
                    MetadataTokens.UserStringHandle(rowNumber),
                    AddUserString(MetadataTokens.UserStringHandle(rowNumber)))),

            _ => token
        };
    }

    private UserStringHandle AddUserString(UserStringHandle sourceHandle)
    {
        var str = _reader.GetUserString(sourceHandle);
        var newHandle = _metadata.GetOrAddUserString(str);
        _userStringMap[sourceHandle] = newHandle;
        return newHandle;
    }
}
