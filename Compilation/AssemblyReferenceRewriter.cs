using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SharpTS.Compilation;

/// <summary>
/// Post-processes a compiled assembly to rewrite System.Private.CoreLib references
/// to SDK reference assembly references (System.Runtime, System.Collections, etc.).
/// This enables --ref-asm output for assemblies containing async/generator code.
/// </summary>
/// <remarks>
/// The problem this solves: MetadataLoadContext types are inspection-only and cannot
/// be passed to TypeBuilder.DefineType() for interface implementation. So we compile
/// with runtime types (which works), then post-process to rewrite the references.
/// </remarks>
public class AssemblyReferenceRewriter : IDisposable
{
    // Source assembly reading
    private readonly PEReader _peReader;
    private readonly MetadataReader _reader;
    private readonly Stream _sourceStream;

    // Reference assembly resolution
    private readonly string _refAssemblyPath;
    private readonly Dictionary<string, string> _typeToAssembly = new(); // FullTypeName -> AssemblyName
    private readonly Dictionary<string, AssemblyName> _assemblyInfoCache = new(); // AssemblyName -> AssemblyName object

    // Target assembly building
    private readonly MetadataBuilder _metadata = new();
    private readonly BlobBuilder _ilStream = new();
    private readonly BlobBuilder _mappedFieldData = new();

    // Handle mappings (source -> target)
    private readonly Dictionary<AssemblyReferenceHandle, AssemblyReferenceHandle> _assemblyRefMap = new();
    private readonly Dictionary<TypeReferenceHandle, TypeReferenceHandle> _typeRefMap = new();
    private readonly Dictionary<TypeSpecificationHandle, TypeSpecificationHandle> _typeSpecMap = new();
    private readonly Dictionary<MemberReferenceHandle, MemberReferenceHandle> _memberRefMap = new();
    private readonly Dictionary<MethodSpecificationHandle, MethodSpecificationHandle> _methodSpecMap = new();
    private readonly Dictionary<TypeDefinitionHandle, TypeDefinitionHandle> _typeDefMap = new();
    private readonly Dictionary<MethodDefinitionHandle, MethodDefinitionHandle> _methodDefMap = new();
    private readonly Dictionary<FieldDefinitionHandle, FieldDefinitionHandle> _fieldDefMap = new();
    private readonly Dictionary<StandaloneSignatureHandle, StandaloneSignatureHandle> _standAloneSigMap = new();
    private readonly Dictionary<UserStringHandle, UserStringHandle> _userStringMap = new();
    private readonly Dictionary<StringHandle, StringHandle> _stringHandleMap = new();
    private readonly Dictionary<GuidHandle, GuidHandle> _guidHandleMap = new();
    private readonly Dictionary<BlobHandle, BlobHandle> _blobHandleMap = new();

    // New assembly references we create
    private readonly Dictionary<string, AssemblyReferenceHandle> _newAssemblyRefs = new();

    // Method body offset tracking
    private readonly Dictionary<MethodDefinitionHandle, int> _methodBodyOffsets = new();

    // Entry point from source
    private MethodDefinitionHandle _sourceEntryPoint;
    private MethodDefinitionHandle _targetEntryPoint;

    private bool _disposed;

    /// <summary>
    /// Creates a new assembly reference rewriter.
    /// </summary>
    /// <param name="sourceAssembly">Stream containing the compiled assembly to rewrite.</param>
    /// <param name="refAssemblyPath">Path to SDK reference assemblies directory.</param>
    public AssemblyReferenceRewriter(Stream sourceAssembly, string refAssemblyPath)
    {
        _sourceStream = sourceAssembly;
        _refAssemblyPath = refAssemblyPath;

        _peReader = new PEReader(sourceAssembly);
        _reader = _peReader.GetMetadataReader();

        // Get entry point from PE header
        var corHeader = _peReader.PEHeaders.CorHeader;
        if (corHeader != null && corHeader.EntryPointTokenOrRelativeVirtualAddress != 0)
        {
            _sourceEntryPoint = MetadataTokens.MethodDefinitionHandle(
                corHeader.EntryPointTokenOrRelativeVirtualAddress);
        }

        BuildTypeToAssemblyMapping();
    }

    /// <summary>
    /// Builds a mapping from type full names to their SDK reference assembly.
    /// </summary>
    private void BuildTypeToAssemblyMapping()
    {
        // Scan all reference assemblies to find where types are defined
        var assemblies = Directory.GetFiles(_refAssemblyPath, "*.dll");
        var resolver = new PathAssemblyResolver(assemblies);

        using var mlc = new MetadataLoadContext(resolver, "System.Runtime");

        foreach (var asmPath in assemblies)
        {
            try
            {
                var asm = mlc.LoadFromAssemblyPath(asmPath);
                var asmName = asm.GetName();
                var name = asmName.Name!;

                // Skip implementation assemblies
                if (name == "System.Private.CoreLib")
                    continue;

                // Cache assembly info for later
                _assemblyInfoCache[name] = asmName;

                // Handle forwarded types
                try
                {
                    foreach (var forwardedType in asm.GetForwardedTypes())
                    {
                        if (forwardedType.FullName != null)
                        {
                            _typeToAssembly[forwardedType.FullName] = name;
                        }
                    }
                }
                catch
                {
                    // Some assemblies may not support GetForwardedTypes
                }

                // Map all public types
                foreach (var type in asm.GetTypes())
                {
                    if ((type.IsPublic || type.IsNestedPublic) && type.FullName != null)
                    {
                        _typeToAssembly[type.FullName] = name;
                    }
                }
            }
            catch
            {
                // Skip assemblies that fail to load
            }
        }
    }

    /// <summary>
    /// Rewrites the assembly references and saves to the output stream.
    /// </summary>
    public void Rewrite()
    {
        // Phase 1: Copy assembly definition
        CopyAssemblyDefinition();

        // Phase 2: Copy module definition
        CopyModuleDefinition();

        // Phase 3: Create needed assembly references
        CreateAssemblyReferences();

        // Phase 4: Copy type references with rewritten scopes
        CopyTypeReferences();

        // Phase 5: Create type definition handles (but not members yet)
        // This populates _typeDefMap so that signatures can reference TypeDefs
        CreateTypeDefinitionHandles();

        // Phase 6: Create field and method definition handles (but not bodies yet)
        // This populates _fieldDefMap and _methodDefMap so that MethodSpecs
        // can reference MethodDefs
        CreateFieldAndMethodHandles();

        // Phase 7: Copy type specifications (generic instantiations)
        // Now has valid _typeDefMap entries
        CopyTypeSpecifications();

        // Phase 8: Copy member references
        CopyMemberReferences();

        // Phase 9: Copy method specifications
        // Now has valid _typeDefMap and _methodDefMap entries
        CopyMethodSpecifications();

        // Phase 10: Copy method bodies and finish type definition members
        // Now has valid _methodSpecMap for IL token patching
        CopyMethodBodiesAndFinishTypes();

        // Phase 11: Copy standalone signatures
        CopyStandaloneSignatures();

        // Phase 12: Copy custom attributes
        CopyCustomAttributes();
    }

    /// <summary>
    /// Saves the rewritten assembly to the output stream.
    /// </summary>
    public void Save(Stream output)
    {
        var metadataRootBuilder = new MetadataRootBuilder(_metadata);

        // Determine the entry point for the new assembly
        var entryPoint = _sourceEntryPoint.IsNil
            ? default
            : _methodDefMap.GetValueOrDefault(_sourceEntryPoint, default);

        var peHeaderBuilder = PEHeaderBuilder.CreateExecutableHeader();

        var peBuilder = new ManagedPEBuilder(
            peHeaderBuilder,
            metadataRootBuilder,
            _ilStream,
            mappedFieldData: _mappedFieldData.Count > 0 ? _mappedFieldData : null,
            entryPoint: entryPoint);

        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);
        peBlob.WriteContentTo(output);
    }

    private void CopyAssemblyDefinition()
    {
        var assemblyDef = _reader.GetAssemblyDefinition();

        _metadata.AddAssembly(
            GetOrAddString(_reader.GetString(assemblyDef.Name)),
            assemblyDef.Version,
            GetOrAddString(_reader.GetString(assemblyDef.Culture)),
            GetOrAddBlob(_reader.GetBlobBytes(assemblyDef.PublicKey)),
            assemblyDef.Flags,
            assemblyDef.HashAlgorithm);
    }

    private void CopyModuleDefinition()
    {
        var moduleDef = _reader.GetModuleDefinition();

        _metadata.AddModule(
            moduleDef.Generation,
            GetOrAddString(_reader.GetString(moduleDef.Name)),
            GetOrAddGuid(_reader.GetGuid(moduleDef.Mvid)),
            GetOrAddGuid(moduleDef.GenerationId.IsNil ? default : _reader.GetGuid(moduleDef.GenerationId)),
            GetOrAddGuid(moduleDef.BaseGenerationId.IsNil ? default : _reader.GetGuid(moduleDef.BaseGenerationId)));
    }

    private void CreateAssemblyReferences()
    {
        // Determine which target assemblies we need based on types used
        var neededAssemblies = new HashSet<string>();

        foreach (var typeRefHandle in _reader.TypeReferences)
        {
            var typeRef = _reader.GetTypeReference(typeRefHandle);
            var scope = typeRef.ResolutionScope;

            // Only rewrite references from System.Private.CoreLib
            if (scope.Kind == HandleKind.AssemblyReference)
            {
                var asmRef = _reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
                var asmName = _reader.GetString(asmRef.Name);

                if (asmName == "System.Private.CoreLib")
                {
                    var typeName = GetFullTypeName(typeRef);
                    if (_typeToAssembly.TryGetValue(typeName, out var targetAsm))
                    {
                        neededAssemblies.Add(targetAsm);
                    }
                    else
                    {
                        // Default to System.Runtime for unknown types
                        neededAssemblies.Add("System.Runtime");
                    }
                }
            }
        }

        // Always include System.Runtime as the core runtime assembly.
        // This ensures deterministic behavior across platforms since Directory.GetFiles()
        // returns files in different orders on Windows (alphabetical) vs Linux (inode order),
        // which affects type-to-assembly mapping when types exist in multiple assemblies.
        neededAssemblies.Add("System.Runtime");

        // Create references for all needed SDK assemblies
        foreach (var asmName in neededAssemblies)
        {
            if (_assemblyInfoCache.TryGetValue(asmName, out var asmInfo))
            {
                // Use public key token (not full public key) and clear the PublicKey flag.
                // The SDK reference assemblies have the PublicKey flag set with full public keys,
                // but GetPublicKeyToken() returns just the 8-byte token which is more compatible.
                var flags = (AssemblyFlags)asmInfo.Flags & ~AssemblyFlags.PublicKey;

                var handle = _metadata.AddAssemblyReference(
                    GetOrAddString(asmInfo.Name!),
                    asmInfo.Version!,
                    GetOrAddString(asmInfo.CultureName ?? string.Empty),
                    GetOrAddBlob(asmInfo.GetPublicKeyToken() ?? []),
                    flags,
                    default);

                _newAssemblyRefs[asmName] = handle;
            }
        }

        // Copy existing assembly references (except System.Private.CoreLib)
        foreach (var asmRefHandle in _reader.AssemblyReferences)
        {
            var asmRef = _reader.GetAssemblyReference(asmRefHandle);
            var name = _reader.GetString(asmRef.Name);

            if (name == "System.Private.CoreLib")
                continue;

            var newHandle = _metadata.AddAssemblyReference(
                GetOrAddString(name),
                asmRef.Version,
                GetOrAddString(_reader.GetString(asmRef.Culture)),
                GetOrAddBlob(_reader.GetBlobBytes(asmRef.PublicKeyOrToken)),
                asmRef.Flags,
                GetOrAddBlob(_reader.GetBlobBytes(asmRef.HashValue)));

            _assemblyRefMap[asmRefHandle] = newHandle;
        }
    }

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

    private void CopyMemberReferences()
    {
        foreach (var memberRefHandle in _reader.MemberReferences)
        {
            var memberRef = _reader.GetMemberReference(memberRefHandle);
            var name = _reader.GetString(memberRef.Name);
            var signature = _reader.GetBlobBytes(memberRef.Signature);

            // Map the parent
            var newParent = MapEntityHandle(memberRef.Parent);

            // Rewrite the signature
            var newSignature = RewriteMethodOrFieldSignature(signature);

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
            var signature = _reader.GetBlobBytes(methodSpec.Signature);

            // Map the method
            var newMethod = MapEntityHandle(methodSpec.Method);

            // Rewrite the instantiation signature
            var newSignature = RewriteMethodSpecSignature(signature);

            var newHandle = _metadata.AddMethodSpecification(
                newMethod,
                _metadata.GetOrAddBlob(newSignature));

            _methodSpecMap[methodSpecHandle] = newHandle;
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
        var typeFirstField = new Dictionary<TypeDefinitionHandle, int>();
        var typeFirstMethod = new Dictionary<TypeDefinitionHandle, int>();

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
        var signature = _reader.GetBlobBytes(field.Signature);
        var newSignature = RewriteFieldSignature(signature);

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
        var signature = _reader.GetBlobBytes(method.Signature);
        var newSignature = RewriteMethodSignature(signature);

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
                var sigBytes = _reader.GetBlobBytes(sig.Signature);
                var newSigBytes = RewriteLocalVarsSignature(sigBytes);
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
            var sigBytes = _reader.GetBlobBytes(sig.Signature);
            var newSigBytes = RewriteLocalVarsSignature(sigBytes);

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

    #region Signature Rewriting

    /// <summary>
    /// Simple signature reader for parsing metadata signatures.
    /// Avoids BlobReader which requires unsafe code.
    /// </summary>
    private ref struct SignatureReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public SignatureReader(byte[] data)
        {
            _data = data;
            _offset = 0;
        }

        public int RemainingBytes => _data.Length - _offset;

        public byte ReadByte()
        {
            return _data[_offset++];
        }

        public int ReadCompressedInteger()
        {
            byte b0 = _data[_offset++];
            if ((b0 & 0x80) == 0)
            {
                // 1-byte encoding
                return b0;
            }
            else if ((b0 & 0xC0) == 0x80)
            {
                // 2-byte encoding
                byte b1 = _data[_offset++];
                return ((b0 & 0x3F) << 8) | b1;
            }
            else if ((b0 & 0xE0) == 0xC0)
            {
                // 4-byte encoding
                byte b1 = _data[_offset++];
                byte b2 = _data[_offset++];
                byte b3 = _data[_offset++];
                return ((b0 & 0x1F) << 24) | (b1 << 16) | (b2 << 8) | b3;
            }
            else
            {
                throw new InvalidOperationException("Invalid compressed integer encoding");
            }
        }

        public int ReadCompressedSignedInteger()
        {
            int value = ReadCompressedInteger();
            // Rotate right by 1 and sign-extend
            bool isNegative = (value & 1) != 0;
            value >>= 1;
            if (isNegative)
            {
                value = -value - 1;
            }
            return value;
        }
    }

    private byte[] RewriteTypeSignature(byte[] signature)
    {
        var reader = new SignatureReader(signature);
        var builder = new BlobBuilder();
        RewriteTypeSignatureCore(ref reader, builder);
        return builder.ToArray();
    }

    private void RewriteTypeSignatureCore(ref SignatureReader reader, BlobBuilder builder)
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

    private byte[] RewriteMethodOrFieldSignature(byte[] signature)
    {
        if (signature.Length == 0)
            return signature;

        var reader = new SignatureReader(signature);
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

    private byte[] RewriteMethodSignature(byte[] signature)
    {
        if (signature.Length == 0)
            return signature;

        var reader = new SignatureReader(signature);
        var builder = new BlobBuilder();
        RewriteMethodSignatureCore(ref reader, builder);
        return builder.ToArray();
    }

    private void RewriteMethodSignatureCore(ref SignatureReader reader, BlobBuilder builder)
    {
        var header = reader.ReadByte();
        builder.WriteByte(header);
        RewriteMethodSignatureAfterHeader(ref reader, builder, header);
    }

    private void RewriteMethodSignatureAfterHeader(ref SignatureReader reader, BlobBuilder builder, byte header)
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

    private byte[] RewriteFieldSignature(byte[] signature)
    {
        if (signature.Length == 0)
            return signature;

        var reader = new SignatureReader(signature);
        var builder = new BlobBuilder();

        var header = reader.ReadByte();
        builder.WriteByte(header);

        // Rewrite field type
        RewriteTypeSignatureCore(ref reader, builder);

        return builder.ToArray();
    }

    private byte[] RewriteMethodSpecSignature(byte[] signature)
    {
        if (signature.Length == 0)
            return signature;

        var reader = new SignatureReader(signature);
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

    private byte[] RewriteLocalVarsSignature(byte[] signature)
    {
        if (signature.Length == 0)
            return signature;

        var reader = new SignatureReader(signature);
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _peReader.Dispose();
            _sourceStream.Dispose();
            _disposed = true;
        }
    }

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
