using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace SharpTS.Compilation;

/// <summary>The foreign shared-buffer query, closed only from completed actual allocation evidence.</summary>
internal sealed class NativeTypedArrayBufferLookup
{
    internal const string BufferOwnerName = "$SharedArrayBuffer";
    internal const string UnsupportedMessage = "This SharedArrayBuffer GetBuffer owner is outside this native guest's completed allocation domain. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly List<string> _blockers = [];
    private readonly List<(MethodBuilder Factory, ILGenerator Body, int Offset, ConstructorBuilder Constructor)> _factories = [];
    private readonly List<(ConstructorBuilder Constructor, ILGenerator Body, int Offset)> _queries = [];
    private (TypeBuilder Owner, ConstructorBuilder Constructor, MethodBuilder Getter, ILGenerator Body)? _declaration;
    private NativeTaskSourceBufferProof? _taskSourceProof;

    internal NativeTypedArrayBufferLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeTypedArrayBufferLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Find = _owner.DefineMethod("Find", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type]);
    }

    internal MethodBuilder Find { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();
    internal IReadOnlyList<Type> Owners { get; private set; } = [];

    internal NativeTaskSourceBufferProof BeginTaskSourceProof(MethodBuilder method)
    {
        EnsureMutable();
        if (_taskSourceProof is not null) throw new InvalidOperationException("The actual task-source body may be recorded only once.");
        return _taskSourceProof = new(_owner.Module as ModuleBuilder ?? throw new InvalidOperationException(), _types, method);
    }

    internal void ValidateImage(byte[] image)
    {
        if (!IsComplete) throw new InvalidOperationException("Typed-array buffer lookup emission is incomplete.");
        if (_taskSourceProof is null) return;
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(image, writable: false));
        _taskSourceProof.ValidateImage(pe, pe.GetMetadataReader());
    }

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        EnsureMutable();
        if (!native || !ReferenceEquals(module, _owner.Module))
            throw new InvalidOperationException("Typed-array buffer lookup requires this exact native output module.");
    }

    internal void RecordDeclaration(EmittedSharedArrayBufferRuntime buffer)
    {
        EnsureMutable();
        if (_declaration is not null || !ReferenceEquals(buffer.Type.Module, _owner.Module) || buffer.Type.IsCreated() ||
            !ReferenceEquals(buffer.Ctor.DeclaringType, buffer.Type) || !ReferenceEquals(buffer.GetBuffer.DeclaringType, buffer.Type) ||
            !ValidDeclaration(buffer.Type, buffer.Ctor, buffer.GetBuffer))
            throw new InvalidOperationException("Shared-buffer lookup requires its actual owned declaration and GetBuffer ABI.");
        _declaration = (buffer.Type, buffer.Ctor, buffer.GetBuffer, buffer.GetBuffer.GetILGenerator());
    }

    internal void RecordFactory(EmittedSharedArrayBufferRuntime buffer, MethodBuilder factory, ILGenerator body, int offset)
    {
        EnsureMutable();
        if (_declaration is not { } declaration || declaration.Owner != buffer.Type || declaration.Constructor != buffer.Ctor ||
            factory != buffer.Create || !ReferenceEquals(factory.Module, _owner.Module) || !ReferenceEquals(factory.GetILGenerator(), body) ||
            !factory.IsStatic || factory.ReturnType != _types.Object || factory.GetParameters() is not [{ ParameterType: var argument }] ||
            argument != _types.Double || offset < 0 || body.ILOffset <= offset || _factories.Count != 0)
            throw new InvalidOperationException("Shared-buffer factories require their actual completed constructor operation.");
        _factories.Add((factory, body, offset, buffer.Ctor));
    }

    internal void EmitQuery(ConstructorBuilder constructor, ILGenerator body)
    {
        EnsureMutable();
        if (!ReferenceEquals(constructor.Module, _owner.Module) || !ReferenceEquals(constructor.GetILGenerator(), body) ||
            _queries.Any(query => ReferenceEquals(query.Constructor, constructor)))
            throw new InvalidOperationException("Typed-array queries require their actual owned constructor body.");
        int offset = body.ILOffset;
        body.Emit(OpCodes.Call, Find);
        _queries.Add((constructor, body, offset));
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, NativeClrAdmission.CompletedIngressProof? ingress, EmittedRuntime runtime, NativeOwnedTypeDeclarations? declarations)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(runtime.NativeTypedArrayBuffers, this) || ingress is not null && !ReferenceEquals(ingress.Module, _owner.Module))
            throw new InvalidOperationException("Typed-array buffer lookup requires completed evidence from this exact output.");
        if (ingress is null) _blockers.Add("CLR ingress admission has not completed");
        _blockers.AddRange(sources.Blockers);
        if (runtime.Deployment.Requirements != SharpTSRuntimeRequirements.None)
            _blockers.Add("The output still requires a foreign managed runtime bridge");
        _taskSourceProof?.Complete(runtime);
        var owners = new HashSet<Type>();
        if (runtime.NativePrototypeFields is not { IsComplete: true } allocations)
            _blockers.Add("Actual allocation/activation producers have not completed");
        else
        {
            if (!ReferenceEquals(allocations.Find.Module, _owner.Module))
                throw new InvalidOperationException("Allocation evidence must belong to this exact output.");
            var unproved = allocations.CompletedUnprovedResults;
            if (!ReferenceEquals(unproved.Module, _owner.Module))
                throw new InvalidOperationException("Unproved result evidence must belong to this exact output.");
            foreach (var result in unproved.Producers)
            {
                if (_taskSourceProof?.Claims(result) == true) continue;
                _blockers.Add($"Unproved actual carrier result: {result.Producer.DeclaringType?.FullName}.{result.Producer.Name}");
            }
            foreach (var type in allocations.Owners)
            {
                // The unchanged caller rejects every other exact CLR Name before
                // this operation. This precondition grants no allocation authority.
                if (type.Name != BufferOwnerName) continue;
                if (FunctionGenericParameterRegistry.ContainsUnboundType(type))
                    _blockers.Add($"Unproved actual allocation family: {type.FullName}");
                else owners.Add(type);
            }
        }
        // Other prototype/callable roles' declaration obligations are not query
        // authority. Our own ledger below covers every matching instantiable
        // declaration, including those with no recorded allocation producer.
        if (runtime.SharedArrayBuffer is { } buffer)
        {
            if (!buffer.IsComplete || _declaration is not { } declaration || !declaration.Owner.IsCreated() ||
                declaration.Owner != buffer.Type || declaration.Constructor != buffer.Ctor || declaration.Getter != buffer.GetBuffer ||
                !ValidDeclaration(declaration.Owner, declaration.Constructor, declaration.Getter) || declaration.Body.ILOffset == 0 ||
                _factories.Count != 1 || _factories[0].Factory != buffer.Create || _factories[0].Constructor != buffer.Ctor ||
                _factories[0].Body.ILOffset <= _factories[0].Offset)
                _blockers.Add("Shared-buffer declaration/factory/constructor evidence has not completed");
            else owners.Add(declaration.Owner);
        }
        else if (_declaration is not null || _factories.Count != 0)
            throw new InvalidOperationException("Shared-buffer records require the exact runtime metadata bundle.");
        foreach (var query in _queries)
            if (query.Constructor.DeclaringType is not TypeBuilder { } owner || !owner.IsCreated() || query.Body.ILOffset <= query.Offset)
                throw new InvalidOperationException("Typed-array buffer queries require completed constructor bodies.");
        if (declarations is null) _blockers.Add("Own argument declaration coverage has not completed");
        else
        {
            if (!ReferenceEquals(declarations.Module, _owner.Module))
                throw new InvalidOperationException("Own argument declaration coverage must belong to this exact output.");
            _blockers.AddRange(declarations.CompleteNameCoverage(BufferOwnerName, owners));
        }
        Owners = Array.AsReadOnly(owners.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        EmitFind(runtime.Errors);
        _owner.CreateType(); IsComplete = true;
    }

    private bool ValidDeclaration(TypeBuilder owner, ConstructorBuilder constructor, MethodBuilder getter) =>
        owner.Name == BufferOwnerName && owner.IsSealed && !owner.IsGenericTypeDefinition && constructor.IsPublic && !constructor.IsStatic &&
        constructor.GetParameters() is [{ ParameterType: var length }] && length == _types.Int32 &&
        getter.IsPublic && !getter.IsStatic && !getter.IsGenericMethod && getter.Name == "GetBuffer" &&
        getter.ReturnType == typeof(byte[]) && getter.GetParameters().Length == 0 &&
        constructor.GetILGenerator().ILOffset != 0;

    private void EmitFind(EmittedErrorRuntime errors)
    {
        var il = Find.GetILGenerator();
        var query = _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(Type[]));
        if (UsesLegacy) { il.Emit(OpCodes.Ldarg_0); EmitLookup(); return; }
        var refused = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, refused);
        NativeTypeIdentityEmitter.EmitRuntimeTypeFence(il, _types, () => il.Emit(OpCodes.Ldarg_0), refused);
        foreach (var type in Owners)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); EmitLookup();
            il.MarkLabel(next);
        }
        il.MarkLabel(refused);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, UnsupportedMessage);

        void EmitLookup()
        {
            // Preserve the complete original public name/signature binder and its null/ambiguity behavior.
            il.Emit(OpCodes.Ldstr, "GetBuffer");
            il.Emit(OpCodes.Ldsfld, typeof(Type).GetField(nameof(Type.EmptyTypes))!);
            il.Emit(OpCodes.Callvirt, query); il.Emit(OpCodes.Ret);
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Typed-array buffer lookup emission has already completed.");
    }
}
