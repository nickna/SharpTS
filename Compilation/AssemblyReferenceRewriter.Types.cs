using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    private void CopyTypeReferences()
    {
        // Process in order to handle nested types correctly
        foreach (var typeRefHandle in _reader.TypeReferences)
        {
            var typeRef = _reader.GetTypeReference(typeRefHandle);
            var name = _reader.GetString(typeRef.Name);
            var ns = _reader.GetString(typeRef.Namespace);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            EntityHandle newResolutionScope;

            switch (typeRef.ResolutionScope.Kind)
            {
                case HandleKind.AssemblyReference:
                    {
                        var oldAsmRef = (AssemblyReferenceHandle)typeRef.ResolutionScope;
                        var oldAsmName = _reader.GetString(_reader.GetAssemblyReference(oldAsmRef).Name);

                        if (oldAsmName == "System.Private.CoreLib")
                        {
                            // Redirect to appropriate SDK assembly
                            var targetAsm = _typeToAssembly.GetValueOrDefault(fullName, "System.Runtime");
                            newResolutionScope = _newAssemblyRefs.GetValueOrDefault(targetAsm,
                                _newAssemblyRefs.GetValueOrDefault("System.Runtime", default));
                        }
                        else
                        {
                            newResolutionScope = _assemblyRefMap[oldAsmRef];
                        }
                        break;
                    }

                case HandleKind.TypeReference:
                    {
                        // Nested type - resolve through parent
                        newResolutionScope = _typeRefMap[(TypeReferenceHandle)typeRef.ResolutionScope];
                        break;
                    }

                case HandleKind.ModuleReference:
                case HandleKind.ModuleDefinition:
                default:
                    newResolutionScope = default;
                    break;
            }

            var newHandle = _metadata.AddTypeReference(
                newResolutionScope,
                GetOrAddString(ns),
                GetOrAddString(name));

            _typeRefMap[typeRefHandle] = newHandle;
        }
    }

    private void CopyTypeSpecifications()
    {
        // Iterate through TypeSpec table
        int typeSpecCount = _reader.GetTableRowCount(TableIndex.TypeSpec);
        for (int row = 1; row <= typeSpecCount; row++)
        {
            var typeSpecHandle = MetadataTokens.TypeSpecificationHandle(row);
            var typeSpec = _reader.GetTypeSpecification(typeSpecHandle);
            var signature = _reader.GetBlobBytes(typeSpec.Signature);

            // Rewrite the signature blob to use new type tokens
            var newSignature = RewriteTypeSignature(signature);

            var newHandle = _metadata.AddTypeSpecification(
                _metadata.GetOrAddBlob(newSignature));

            _typeSpecMap[typeSpecHandle] = newHandle;
        }
    }

    /// <summary>
    /// First phase: pre-calculate all handles and create TypeDef entries.
    /// This populates _typeDefMap, _fieldDefMap, and _methodDefMap.
    /// Must run before CopyTypeSpecifications and CopyMethodSpecifications.
    /// </summary>
    private void CreateTypeDefinitionHandles()
    {
        var typeDefHandles = _reader.TypeDefinitions.ToList();

        // First, pre-calculate field and method handles
        // This is needed to set correct FieldList and MethodList in TypeDefs
        int fieldRow = 1;
        int methodRow = 1;
        Dictionary<TypeDefinitionHandle, int> typeFirstField = [];
        Dictionary<TypeDefinitionHandle, int> typeFirstMethod = [];

        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);

            // Record first field/method for this type
            typeFirstField[typeDefHandle] = fieldRow;
            typeFirstMethod[typeDefHandle] = methodRow;

            // Pre-calculate field handles
            foreach (var fieldHandle in typeDef.GetFields())
            {
                _fieldDefMap[fieldHandle] = MetadataTokens.FieldDefinitionHandle(fieldRow++);
            }

            // Pre-calculate method handles
            foreach (var methodHandle in typeDef.GetMethods())
            {
                _methodDefMap[methodHandle] = MetadataTokens.MethodDefinitionHandle(methodRow++);
            }
        }

        // Now create TypeDef entries with correct FieldList and MethodList
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);

            var newHandle = _metadata.AddTypeDefinition(
                typeDef.Attributes,
                GetOrAddString(_reader.GetString(typeDef.Namespace)),
                GetOrAddString(_reader.GetString(typeDef.Name)),
                MapEntityHandle(typeDef.BaseType),
                MetadataTokens.FieldDefinitionHandle(typeFirstField[typeDefHandle]),
                MetadataTokens.MethodDefinitionHandle(typeFirstMethod[typeDefHandle]));

            _typeDefMap[typeDefHandle] = newHandle;
        }
    }

    /// <summary>
    /// Phase 6 is now a no-op since CreateTypeDefinitionHandles does all the work.
    /// Kept for clarity in the phase ordering.
    /// </summary>
    private void CreateFieldAndMethodHandles()
    {
        // Field and method handles are now pre-calculated in CreateTypeDefinitionHandles
        // to ensure correct FieldList and MethodList values in TypeDef entries.
    }
}
