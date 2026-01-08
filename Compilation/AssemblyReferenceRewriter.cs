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
public partial class AssemblyReferenceRewriter : IDisposable
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _peReader.Dispose();
            _sourceStream.Dispose();
            _disposed = true;
        }
    }
}
