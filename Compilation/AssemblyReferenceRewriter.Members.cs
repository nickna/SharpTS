using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpTS.Compilation;

public partial class AssemblyReferenceRewriter
{
    private void CopyMemberReferences()
    {
        foreach (var memberRefHandle in _reader.MemberReferences)
        {
            var memberRef = _reader.GetMemberReference(memberRefHandle);
            var name = _reader.GetString(memberRef.Name);
            var reader = _reader.GetBlobReader(memberRef.Signature);

            // Map the parent
            var newParent = MapEntityHandle(memberRef.Parent);

            // Rewrite the signature
            var newSignature = RewriteMethodOrFieldSignature(reader);

            var newHandle = _metadata.AddMemberReference(
                newParent,
                GetOrAddString(name),
                _metadata.GetOrAddBlob(newSignature));

            _memberRefMap[memberRefHandle] = newHandle;
        }
    }

    private void CopyMethodSpecifications()
    {
        // Iterate through MethodSpec table
        int methodSpecCount = _reader.GetTableRowCount(TableIndex.MethodSpec);
        for (int row = 1; row <= methodSpecCount; row++)
        {
            var methodSpecHandle = MetadataTokens.MethodSpecificationHandle(row);
            var methodSpec = _reader.GetMethodSpecification(methodSpecHandle);
            var reader = _reader.GetBlobReader(methodSpec.Signature);

            // Map the method
            var newMethod = MapEntityHandle(methodSpec.Method);

            // Rewrite the instantiation signature
            var newSignature = RewriteMethodSpecSignature(reader);

            var newHandle = _metadata.AddMethodSpecification(
                newMethod,
                _metadata.GetOrAddBlob(newSignature));

            _methodSpecMap[methodSpecHandle] = newHandle;
        }
    }

    /// <summary>
    /// Third phase of type definition copying: copy all members (fields, methods with bodies, etc.).
    /// This must run after CopyMethodSpecifications so that IL tokens can be mapped.
    /// </summary>
    private void CopyMethodBodiesAndFinishTypes()
    {
        var typeDefHandles = _reader.TypeDefinitions.ToList();

        // Copy fields
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            foreach (var fieldHandle in typeDef.GetFields())
            {
                CopyFieldDefinition(fieldHandle);
            }
        }

        // Copy methods with bodies
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                CopyMethodDefinition(methodHandle);
            }
        }

        // Copy interface implementations
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            var newTypeDefHandle = _typeDefMap[typeDefHandle];

            foreach (var ifaceImplHandle in typeDef.GetInterfaceImplementations())
            {
                var ifaceImpl = _reader.GetInterfaceImplementation(ifaceImplHandle);
                var newInterface = MapEntityHandle(ifaceImpl.Interface);

                _metadata.AddInterfaceImplementation(newTypeDefHandle, newInterface);
            }
        }

        // Copy nested type relationships
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var newEnclosing = _typeDefMap[typeDefHandle];
                var newNested = _typeDefMap[nestedHandle];
                _metadata.AddNestedType(newNested, newEnclosing);
            }
        }

        // Copy method implementations (overrides)
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            var newTypeDefHandle = _typeDefMap[typeDefHandle];

            foreach (var methodImplHandle in typeDef.GetMethodImplementations())
            {
                var methodImpl = _reader.GetMethodImplementation(methodImplHandle);
                var newMethodBody = MapEntityHandle(methodImpl.MethodBody);
                var newMethodDecl = MapEntityHandle(methodImpl.MethodDeclaration);

                _metadata.AddMethodImplementation(newTypeDefHandle, newMethodBody, newMethodDecl);
            }
        }

        // Copy generic parameters for types
        foreach (var typeDefHandle in typeDefHandles)
        {
            var typeDef = _reader.GetTypeDefinition(typeDefHandle);
            var newTypeDefHandle = _typeDefMap[typeDefHandle];

            foreach (var genParamHandle in typeDef.GetGenericParameters())
            {
                CopyGenericParameter(genParamHandle, newTypeDefHandle);
            }
        }
    }

    private void CopyFieldDefinition(FieldDefinitionHandle fieldHandle)
    {
        var field = _reader.GetFieldDefinition(fieldHandle);
        var reader = _reader.GetBlobReader(field.Signature);
        var newSignature = RewriteFieldSignature(reader);

        var newHandle = _metadata.AddFieldDefinition(
            field.Attributes,
            GetOrAddString(_reader.GetString(field.Name)),
            _metadata.GetOrAddBlob(newSignature));

        _fieldDefMap[fieldHandle] = newHandle;

        // Copy field RVA if present (for field data)
        var rva = field.GetRelativeVirtualAddress();
        if (rva != 0)
        {
            // Get field data from the source PE
            var fieldData = GetFieldData(rva, field);
            if (fieldData.Length > 0)
            {
                _metadata.AddFieldRelativeVirtualAddress(newHandle, _mappedFieldData.Count);
                _mappedFieldData.WriteBytes(fieldData);
            }
        }

        // Copy default value if present
        var defaultValue = field.GetDefaultValue();
        if (!defaultValue.IsNil)
        {
            var constant = _reader.GetConstant(defaultValue);
            _metadata.AddConstant(newHandle, constant.Value);
        }

        // Copy marshal info if present
        var marshalInfo = field.GetMarshallingDescriptor();
        if (!marshalInfo.IsNil)
        {
            _metadata.AddMarshallingDescriptor(newHandle, GetOrAddBlob(_reader.GetBlobBytes(marshalInfo)));
        }

        // Copy field layout if present
        var offset = field.GetOffset();
        if (offset >= 0)
        {
            _metadata.AddFieldLayout(newHandle, offset);
        }
    }

    private byte[] GetFieldData(int rva, FieldDefinition field)
    {
        try
        {
            // Get the size from the signature
            var sig = field.DecodeSignature(new FieldDataSizeProvider(_reader), null);
            if (sig > 0)
            {
                var sectionData = _peReader.GetSectionData(rva);
                return sectionData.GetContent(0, sig).ToArray();
            }
        }
        catch
        {
            // Failed to get field data size
        }
        return [];
    }

    private void CopyMethodDefinition(MethodDefinitionHandle methodHandle)
    {
        var method = _reader.GetMethodDefinition(methodHandle);
        var reader = _reader.GetBlobReader(method.Signature);
        var newSignature = RewriteMethodSignature(reader);

        // Get IL body offset if present
        int bodyOffset = -1;
        if (method.RelativeVirtualAddress != 0)
        {
            bodyOffset = CopyMethodBody(method);
        }

        var newHandle = _metadata.AddMethodDefinition(
            method.Attributes,
            method.ImplAttributes,
            GetOrAddString(_reader.GetString(method.Name)),
            _metadata.GetOrAddBlob(newSignature),
            bodyOffset,
            default); // Parameters added separately

        _methodDefMap[methodHandle] = newHandle;

        // Track entry point
        if (methodHandle == _sourceEntryPoint)
        {
            _targetEntryPoint = newHandle;
        }

        // Copy parameters
        foreach (var paramHandle in method.GetParameters())
        {
            var param = _reader.GetParameter(paramHandle);
            _metadata.AddParameter(
                param.Attributes,
                GetOrAddString(_reader.GetString(param.Name)),
                param.SequenceNumber);
        }

        // Copy generic parameters
        foreach (var genParamHandle in method.GetGenericParameters())
        {
            CopyGenericParameter(genParamHandle, newHandle);
        }
    }

    private void CopyGenericParameter(GenericParameterHandle genParamHandle, EntityHandle parent)
    {
        var genParam = _reader.GetGenericParameter(genParamHandle);

        var newHandle = _metadata.AddGenericParameter(
            parent,
            genParam.Attributes,
            GetOrAddString(_reader.GetString(genParam.Name)),
            genParam.Index);

        // Copy constraints
        foreach (var constraintHandle in genParam.GetConstraints())
        {
            var constraint = _reader.GetGenericParameterConstraint(constraintHandle);
            var newConstraintType = MapEntityHandle(constraint.Type);
            _metadata.AddGenericParameterConstraint(newHandle, newConstraintType);
        }
    }

    private void CopyStandaloneSignatures()
    {
        // Iterate through StandAloneSig table
        int sigCount = _reader.GetTableRowCount(TableIndex.StandAloneSig);
        for (int row = 1; row <= sigCount; row++)
        {
            var sigHandle = MetadataTokens.StandaloneSignatureHandle(row);
            if (_standAloneSigMap.ContainsKey(sigHandle))
                continue;

            var sig = _reader.GetStandaloneSignature(sigHandle);
            var reader = _reader.GetBlobReader(sig.Signature);
            var newSigBytes = RewriteLocalVarsSignature(reader);

            var newHandle = _metadata.AddStandaloneSignature(_metadata.GetOrAddBlob(newSigBytes));
            _standAloneSigMap[sigHandle] = newHandle;
        }
    }

    private void CopyCustomAttributes()
    {
        foreach (var attrHandle in _reader.CustomAttributes)
        {
            var attr = _reader.GetCustomAttribute(attrHandle);
            var parent = MapEntityHandle(attr.Parent);
            var constructor = MapEntityHandle(attr.Constructor);
            var value = GetOrAddBlob(_reader.GetBlobBytes(attr.Value));

            _metadata.AddCustomAttribute(parent, constructor, value);
        }
    }
}
