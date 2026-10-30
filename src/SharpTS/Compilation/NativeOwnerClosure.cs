using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal enum NativeOwnerNormalization { ConstructorBase, PromiseConstructor }

/// <summary>Late native call-edge proof for source static storage and symbol owner closure.</summary>
internal sealed class NativeOwnerClosure
{
    internal const string ForeignOwnerMessage = "This open Type is outside this native guest's recorded owner-closure domain. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly List<string> _blockers = [];
    private readonly List<string> _storageBlockers = [];
    private readonly HashSet<TypeBuilder> _storagePolicies = new(ReferenceEqualityComparer.Instance);
    internal sealed class SymbolEmission(TypeBuilder owner, MethodBuilder registered, ILGenerator body, int offset)
    {
        internal TypeBuilder Owner { get; } = owner;
        internal MethodBuilder Registered { get; } = registered;
        internal ILGenerator Body { get; } = body;
        internal int TokenOffset { get; } = offset;
        internal int MethodTokenOffset { get; set; } = -1;
        internal MethodBuilder? Register { get; set; }
        internal int CallOffset { get; set; }
    }
    private readonly List<SymbolEmission> _symbols = [];
    private NativeCompletedSymbolRegistrations? _completedSymbols;
    internal NativeCompletedSymbolRegistrations CompletedSymbols => IsComplete ? _completedSymbols! :
        throw new InvalidOperationException("Actual symbol method registrations have not completed.");
    private readonly List<(NativeOwnerNormalization Kind, MethodBuilder Method, ILGenerator Body, int Offset)> _normalizers = [];
    private bool _attached;
    private bool _userClasses = true;

    internal NativeOwnerClosure(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = EmitTypeDefinitions.DefineType(module, "<>NativeOwnerClosure",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, types.Object);
        StaticMemberOwner = Declare("StaticMemberOwner");
        StaticStorageOwner = Declare("StaticStorageOwner");
        SymbolOwner = Declare("SymbolOwner");
        AdmitType = _owner.DefineMethod("AdmitType", MethodAttributes.Assembly | MethodAttributes.Static, types.Void, [types.Type]);
    }

    private MethodBuilder Declare(string name) => _owner.DefineMethod(name,
        MethodAttributes.Assembly | MethodAttributes.Static, _types.Type, [_types.Type]);
    internal MethodBuilder StaticMemberOwner { get; }
    internal MethodBuilder StaticStorageOwner { get; }
    internal MethodBuilder SymbolOwner { get; }
    internal MethodBuilder AdmitType { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0 || _storageBlockers.Count != 0;
    internal bool StaticMemberUsesLegacy => _blockers.Count != 0;
    internal bool StaticStorageUsesLegacy => UsesLegacy;
    internal bool SymbolUsesLegacy => _blockers.Count != 0;
    internal IReadOnlyList<string> Blockers => Array.AsReadOnly(_blockers.Concat(_storageBlockers).ToArray());

    internal void ValidateForEmitter(ModuleBuilder module, bool native, bool userClasses)
    {
        EnsureMutable();
        if (!native || _attached || !ReferenceEquals(module, _owner.Module))
            throw new InvalidOperationException("Native owner closure requires its exact native output module once.");
        _attached = true; _userClasses = userClasses;
    }

    // These APIs perform the exact operations they record. Declaration counts,
    // annotations, constructor ABI and initializer/property roles are not evidence.
    internal static void EmitNormalization(NativeOwnerClosure? proof, TypeProvider types,
        ILGenerator il, MethodBuilder method, NativeOwnerNormalization kind)
    {
        proof?.EnsureMutable();
        il.Emit(OpCodes.Callvirt, types.GetMethodNoParams(types.Type, "GetGenericTypeDefinition"));
        if (proof is null) return;
        if (!ReferenceEquals(method.Module, proof._owner.Module) || !ReferenceEquals(method.GetILGenerator(), il))
            throw new InvalidOperationException("Owner normalization requires its actual emitting method.");
        proof._normalizers.Add((kind, method, il, il.ILOffset));
    }

    internal static void EmitStoragePolicy(NativeOwnerClosure? proof, TypeBuilder owner, ConstructorBuilder attribute)
    {
        proof?.EnsureMutable();
        owner.SetCustomAttribute(attribute, CustomAttributeEncoder.EmptyBlob);
        if (proof is null) return;
        if (!ReferenceEquals(owner.Module, proof._owner.Module) || !owner.IsGenericTypeDefinition ||
            !ReferenceEquals(attribute.Module, proof._owner.Module) || !proof._storagePolicies.Add(owner))
            throw new InvalidOperationException("Canonical storage requires its actual owned attribute emission.");
    }

    internal static SymbolEmission? EmitSymbolOwner(NativeOwnerClosure? proof, TypeProvider types,
        TypeBuilder owner, MethodBuilder registered, ILGenerator body)
    {
        proof?.EnsureMutable();
        int offset = body.ILOffset;
        body.Emit(OpCodes.Ldtoken, owner); body.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
        if (proof is null) return null;
        if (!ReferenceEquals(owner.Module, proof._owner.Module) || !ReferenceEquals(registered.DeclaringType, owner))
            throw new InvalidOperationException("Symbol registration requires its actual source owner and method.");
        var emission = new SymbolEmission(owner, registered, body, offset);
        proof._symbols.Add(emission); return emission;
    }

    internal static void RecordSymbolMethod(NativeOwnerClosure? proof, ILGenerator body,
        SymbolEmission? emission, MethodBuilder method, int offset)
    {
        if (proof is null) return;
        proof.EnsureMutable();
        if (emission is null || !proof._symbols.Contains(emission) || emission.MethodTokenOffset >= 0 ||
            !ReferenceEquals(emission.Body, body) || !ReferenceEquals(emission.Registered, method) ||
            offset <= emission.TokenOffset || body.ILOffset <= offset)
            throw new InvalidOperationException("Symbol parameter metadata requires its actual registered method token.");
        emission.MethodTokenOffset = offset;
    }

    internal static void EmitSymbolRegistration(NativeOwnerClosure? proof, ILGenerator il,
        SymbolEmission? emission, MethodBuilder register)
    {
        proof?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Call, register);
        if (proof is null) return;
        if (emission is null || !proof._symbols.Contains(emission) || emission.Register is not null ||
            !ReferenceEquals(emission.Body, il) || !ReferenceEquals(register.Module, proof._owner.Module) ||
            offset <= emission.TokenOffset)
            throw new InvalidOperationException("A symbol owner token must reach its actual registration once.");
        emission.Register = register; emission.CallOffset = offset;
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, NativeConstructorValueProducerSnapshot values,
        NativeClrAdmission.CompletedIngressProof? ingress, EmittedRuntime runtime)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(values.Module, _owner.Module) || !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module) ||
            ingress is not null && !ReferenceEquals(ingress.Module, _owner.Module))
            throw new InvalidOperationException("Owner closure requires completed evidence from this exact output.");
        if (!_attached) _blockers.Add("No actual native runtime call-edge attachment");
        if (ingress is null) _blockers.Add("CLR ingress/escape admission has not completed for this output");
        if (runtime.NativeConstructorActivation?.IsComplete != true)
            _blockers.Add("The actual emitted constructor-value producer ledger has not completed");
        else if (!ReferenceEquals(runtime.NativeConstructorActivation.CompletedSnapshot, values))
            throw new InvalidOperationException("Owner closure must consume the runtime's exact completed Type-value producer ledger.");
        if (_userClasses && !_normalizers.Any(normalization => normalization.Kind == NativeOwnerNormalization.ConstructorBase))
            _blockers.Add("The actual constructor-base normalization operation was not recorded");
        if (_userClasses && runtime.Promise is not null && !_normalizers.Any(normalization => normalization.Kind == NativeOwnerNormalization.PromiseConstructor))
            _blockers.Add("The actual Promise-constructor normalization operation was not recorded");
        _blockers.AddRange(sources.Blockers);
        foreach (var value in values.Owners)
        {
            if (value.Producers.Any(p => p.ExactTokenOwner != value.ExactTokenOwner ||
                p.Method is { } method && !ReferenceEquals(method.Module, _owner.Module)))
                throw new InvalidOperationException("Owner closure requires actual constructor-value records from this output.");
            if (value.Producers.Count == 0 || value.Producers.Any(p => p.ValueOffset <= 0 || p.Body.ILOffset <= p.ValueOffset))
                _blockers.Add($"Incomplete actual Type-value producer body: {value.ExactTokenOwner.FullName}");
            if (value.ClosureKind == NativeConstructorValueClosureKind.UnprovedValue)
                _blockers.Add($"Unproved Type-value producer: {value.ExactTokenOwner.FullName}");
            else if (value.ClosureKind == NativeConstructorValueClosureKind.UnprovedSourceGenericFamily)
            {
                if (value.SourceGenericDefinition is not { } definition ||
                    !sources.CanonicalMappings.Any(mapping => mapping.Definition == definition))
                    _blockers.Add($"No completed canonical mapping for constructor family: {value.ExactTokenOwner.FullName}");
            }
            else if (FunctionGenericParameterRegistry.ContainsUnboundType(value.ExactTokenOwner))
                _blockers.Add($"Unbound literal Type-value producer: {value.ExactTokenOwner.FullName}");
        }
        // A constructor's exposed base can normalize to an open definition even
        // when the declaring source class is non-generic. Validate every actual
        // source-contract implementer's finalized chain, including generic bases.
        foreach (var owner in sources.Implementers)
        {
            if (!owner.IsCreated()) throw new InvalidOperationException("Owner closure requires finalized source implementers.");
            ProveSourceBases(owner, sources);
        }
        foreach (var normalization in _normalizers)
            if (normalization.Body.ILOffset <= normalization.Offset ||
                !ReferenceEquals(normalization.Method.GetILGenerator(), normalization.Body))
                _blockers.Add($"Incomplete actual constructor normalization body: {normalization.Method.Name}");
        foreach (var symbol in _symbols)
            if (!symbol.Owner.IsCreated() || !sources.Implementers.Contains(symbol.Owner) ||
                symbol.Register != runtime.SymbolAccessors.RegisterMethod || symbol.CallOffset <= symbol.TokenOffset ||
                symbol.Body.ILOffset <= symbol.CallOffset || symbol.Registered.GetILGenerator().ILOffset == 0)
                _blockers.Add($"Incomplete actual symbol registration or method body: {symbol.Owner.FullName}");
        foreach (var policy in _storagePolicies)
            if (!policy.IsCreated() || !sources.CanonicalMappings.Any(mapping => mapping.Definition == policy))
                _storageBlockers.Add($"Unproved canonical storage policy owner: {policy.FullName}");

        _completedSymbols = new(_owner.Module, SymbolUsesLegacy, Array.AsReadOnly(_symbols.Select(row =>
            new NativeCompletedSymbolRegistration(row.Owner, row.Registered, row.Register, row.TokenOffset, row.MethodTokenOffset,
                row.CallOffset, row.Body.ILOffset, row.Registered.GetILGenerator().ILOffset)).ToArray()));
        EmitAdmission(runtime.Errors);
        EmitOwnerMethod(StaticMemberOwner, sources, runtime, storage: false);
        EmitOwnerMethod(StaticStorageOwner, sources, runtime, storage: true);
        EmitSymbolMethod(sources, runtime);
        _owner.CreateType(); IsComplete = true;
    }

    private void ProveSourceBases(Type owner, NativeSourceOwnerCatalog sources)
    {
        var seen = new HashSet<Type>();
        for (Type? current = owner; current is not null;)
        {
            if (!seen.Add(current)) { _blockers.Add($"Cyclic source owner base context: {owner.FullName}"); return; }
            try
            {
                if (current.IsGenericType)
                {
                    Type definition = current.GetGenericTypeDefinition();
                    if (!sources.CanonicalMappings.Any(mapping => mapping.Definition == definition))
                    { _blockers.Add($"Unproved generic constructor base normalization: {current.FullName}"); return; }
                }
                current = current.BaseType;
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            { _blockers.Add($"Unavailable constructor base context: {owner.FullName}"); return; }
        }
    }

    private void EmitAdmission(EmittedErrorRuntime errors)
    {
        var il = AdmitType.GetILGenerator(); var refused = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, refused);
        NativeTypeIdentityEmitter.EmitRuntimeTypeFence(il, _types, () => il.Emit(OpCodes.Ldarg_0), refused);
        il.Emit(OpCodes.Ret); il.MarkLabel(refused); Refuse(il, errors);
    }

    private void EmitOwnerMethod(MethodBuilder method, NativeSourceOwnerCatalog sources, EmittedRuntime runtime, bool storage)
    {
        var il = method.GetILGenerator(); var identity = il.DefineLabel();
        if (!_userClasses) { Identity(il); return; }
        if (storage ? StaticStorageUsesLegacy : StaticMemberUsesLegacy)
        { RuntimeEmitter.EmitLegacyOwnerClosureOperation(il, _types, sources, runtime, storage); return; }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AdmitType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, storage ? "IsGenericType" : "ContainsGenericParameters"));
        il.Emit(OpCodes.Brfalse, identity);
        if (!storage)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
            var generic = il.DefineLabel(); il.Emit(OpCodes.Brtrue, generic); Refuse(il, runtime.Errors); il.MarkLabel(generic);
        }
        var definition = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Stloc, definition);
        EmitCanonicalCases(il, sources, definition, storage, identity);
        if (storage)
        {
            // A foreign closed owner that does not implement this output's
            // contract was always an identity operation. An unrecorded contract
            // implementer must not be mistaken for canonical static storage.
            Literal(il, sources.Contract); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
            var noContract = il.DefineLabel(); il.Emit(OpCodes.Brfalse, noContract); Refuse(il, runtime.Errors); il.MarkLabel(noContract);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "ContainsGenericParameters"));
            il.Emit(OpCodes.Brfalse, identity);
        }
        Refuse(il, runtime.Errors);
        il.MarkLabel(identity); Identity(il);
    }

    private void EmitSymbolMethod(NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        var il = SymbolOwner.GetILGenerator(); var identity = il.DefineLabel();
        if (!_userClasses) { Identity(il); return; }
        if (SymbolUsesLegacy) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, runtime.SymbolAccessors.ClosedOwner); il.Emit(OpCodes.Ret); return; }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AdmitType);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "ContainsGenericParameters"));
        il.Emit(OpCodes.Brfalse, identity);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
        var generic = il.DefineLabel(); il.Emit(OpCodes.Brtrue, generic); Refuse(il, runtime.Errors); il.MarkLabel(generic);
        var definition = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition")); il.Emit(OpCodes.Stloc, definition);
        EmitCanonicalCases(il, sources, definition, storage: false, identity);
        Refuse(il, runtime.Errors); il.MarkLabel(identity); Identity(il);
    }

    private void EmitCanonicalCases(ILGenerator il, NativeSourceOwnerCatalog sources,
        LocalBuilder definition, bool storage, Label identity)
    {
        foreach (var mapping in sources.CanonicalMappings)
        {
            var next = il.DefineLabel(); Literal(il, mapping.Definition); il.Emit(OpCodes.Ldloc, definition);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object)); il.Emit(OpCodes.Brfalse, next);
            if (storage && !_storagePolicies.Contains((TypeBuilder)mapping.Definition)) il.Emit(OpCodes.Br, identity);
            else { Literal(il, mapping.Canonical); il.Emit(OpCodes.Ret); }
            il.MarkLabel(next);
        }
    }

    private void Literal(ILGenerator il, Type type) { il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); }
    private static void Identity(ILGenerator il) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret); }
    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignOwnerMessage);
    private void EnsureMutable() { if (IsComplete) throw new InvalidOperationException("Native owner closure is complete."); }
}

internal sealed record NativeCompletedSymbolRegistration(TypeBuilder Owner, MethodBuilder Method, MethodBuilder? Register,
    int OwnerTokenOffset, int MethodTokenOffset, int CallOffset, int CompletedBodyLength, int RegisteredBodyLength);
internal sealed record NativeCompletedSymbolRegistrations(Module Module, bool UsesLegacy, IReadOnlyList<NativeCompletedSymbolRegistration> Rows);
