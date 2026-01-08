using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    #region Helper Methods

    private string GetFullTypeName(TypeReference typeRef)
    {
        var name = _reader.GetString(typeRef.Name);
        var ns = _reader.GetString(typeRef.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private EntityHandle MapEntityHandle(EntityHandle handle)
    {
        if (handle.IsNil)
            return handle;

        return handle.Kind switch
        {
            HandleKind.TypeReference => _typeRefMap.GetValueOrDefault(
                (TypeReferenceHandle)handle, (TypeReferenceHandle)handle),

            HandleKind.TypeDefinition => _typeDefMap.GetValueOrDefault(
                (TypeDefinitionHandle)handle, (TypeDefinitionHandle)handle),

            HandleKind.TypeSpecification => _typeSpecMap.GetValueOrDefault(
                (TypeSpecificationHandle)handle, (TypeSpecificationHandle)handle),

            HandleKind.MemberReference => _memberRefMap.GetValueOrDefault(
                (MemberReferenceHandle)handle, (MemberReferenceHandle)handle),

            HandleKind.MethodDefinition => _methodDefMap.GetValueOrDefault(
                (MethodDefinitionHandle)handle, (MethodDefinitionHandle)handle),

            HandleKind.FieldDefinition => _fieldDefMap.GetValueOrDefault(
                (FieldDefinitionHandle)handle, (FieldDefinitionHandle)handle),

            HandleKind.MethodSpecification => _methodSpecMap.GetValueOrDefault(
                (MethodSpecificationHandle)handle, (MethodSpecificationHandle)handle),

            HandleKind.AssemblyReference => _assemblyRefMap.GetValueOrDefault(
                (AssemblyReferenceHandle)handle, (AssemblyReferenceHandle)handle),

            _ => handle
        };
    }

    private StringHandle GetOrAddString(string value)
    {
        return _metadata.GetOrAddString(value);
    }

    private BlobHandle GetOrAddBlob(byte[] value)
    {
        return _metadata.GetOrAddBlob(value);
    }

    private GuidHandle GetOrAddGuid(Guid value)
    {
        return _metadata.GetOrAddGuid(value);
    }

    #endregion

    /// <summary>
    /// Helper to get field data size from signature.
    /// </summary>
    private class FieldDataSizeProvider : ISignatureTypeProvider<int, object?>
    {
        private readonly MetadataReader _reader;

        public FieldDataSizeProvider(MetadataReader reader) => _reader = reader;

        public int GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => 1,
            PrimitiveTypeCode.Byte => 1,
            PrimitiveTypeCode.SByte => 1,
            PrimitiveTypeCode.Char => 2,
            PrimitiveTypeCode.Int16 => 2,
            PrimitiveTypeCode.UInt16 => 2,
            PrimitiveTypeCode.Int32 => 4,
            PrimitiveTypeCode.UInt32 => 4,
            PrimitiveTypeCode.Int64 => 8,
            PrimitiveTypeCode.UInt64 => 8,
            PrimitiveTypeCode.Single => 4,
            PrimitiveTypeCode.Double => 8,
            PrimitiveTypeCode.IntPtr => 8,
            PrimitiveTypeCode.UIntPtr => 8,
            _ => 0
        };

        public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => 0;
        public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => 0;
        public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => 0;
        public int GetSZArrayType(int elementType) => 0;
        public int GetPointerType(int elementType) => 8;
        public int GetByReferenceType(int elementType) => 8;
        public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments) => 0;
        public int GetArrayType(int elementType, ArrayShape shape) => 0;
        public int GetFunctionPointerType(MethodSignature<int> signature) => 8;
        public int GetGenericMethodParameter(object? genericContext, int index) => 0;
        public int GetGenericTypeParameter(object? genericContext, int index) => 0;
        public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => unmodifiedType;
        public int GetPinnedType(int elementType) => elementType;
        public int GetTypeFromSerializedName(string name) => 0;
    }
}
