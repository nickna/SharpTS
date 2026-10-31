using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>The private prototype-store query, backed by actual prototype and carrier creation.</summary>
internal sealed class NativePrototypeFieldsLookup
{
    internal const string UnsupportedMessage = "This prototype carrier is outside this native guest's completed prototype-store producers. Use --target dll or --target exe.";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly Dictionary<TypeBuilder, MethodBuilder> _stores = new(ReferenceEqualityComparer.Instance);
    private readonly List<PrototypeEmission> _prototypes = [];
    private readonly List<(ConstructorInfo Constructor, ILGenerator Body, int Offset)> _carriers = [];
    private readonly List<(MethodInfo Method, ILGenerator Body, int Offset)> _unprovedResults = [];
    private readonly List<string> _blockers = [];
    private NativeUnprovedCarrierResultsSnapshot? _completedUnprovedResults;
    internal sealed class PrototypeEmission(Type owner, ILGenerator body, int offset)
    {
        internal Type Owner { get; } = owner;
        internal ILGenerator Body { get; } = body;
        internal int TokenOffset { get; } = offset;
        internal ConstructorInfo? Constructor { get; set; }
        internal int AllocationOffset { get; set; } = -1;
        internal MethodBuilder? Register { get; set; }
        internal int RegisterOffset { get; set; } = -1;
    }

    internal NativePrototypeFieldsLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativePrototypeFieldsLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Find = _owner.DefineMethod("Find", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type]);
    }

    internal MethodBuilder Find { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();
    internal IReadOnlyList<Type> Owners { get; private set; } = [];
    internal NativeUnprovedCarrierResultsSnapshot CompletedUnprovedResults => IsComplete
        ? _completedUnprovedResults! : throw new InvalidOperationException("Unproved carrier result producers have not completed.");

    internal void RecordStore(TypeBuilder owner, MethodBuilder method)
    {
        EnsureMutable();
        if (!ReferenceEquals(owner.Module, _owner.Module) || owner.IsCreated() ||
            !ReferenceEquals(method.DeclaringType, owner) || !ValidStore(method) || !_stores.TryAdd(owner, method))
            throw new InvalidOperationException("Prototype stores require the actual same-output private accessor body.");
    }

    internal static PrototypeEmission? EmitOwner(NativePrototypeFieldsLookup? lookup, TypeProvider types,
        ILGenerator il, Type owner)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
        if (lookup is null) return null;
        var emission = new PrototypeEmission(owner, il, offset);
        lookup._prototypes.Add(emission);
        return emission;
    }

    internal static void EmitPrototypeCreate(NativePrototypeFieldsLookup? lookup, ILGenerator il,
        PrototypeEmission? emission, ConstructorInfo constructor, NativeClosedSourceFamilies? families = null, MethodBase? caller = null)
    {
        int offset = il.ILOffset;
        EmitCarrierCreate(lookup, il, constructor, families, caller);
        if (lookup is null) return;
        if (emission is null || !lookup._prototypes.Contains(emission) || emission.Constructor is not null ||
            !ReferenceEquals(emission.Body, il) || offset <= emission.TokenOffset)
            throw new InvalidOperationException("Prototype allocation must follow its actual owner token exactly once.");
        emission.Constructor = constructor; emission.AllocationOffset = offset;
    }

    internal static void EmitRegister(NativePrototypeFieldsLookup? lookup, ILGenerator il,
        PrototypeEmission? emission, MethodBuilder register)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Call, register);
        if (lookup is null) return;
        if (emission is null || !lookup._prototypes.Contains(emission) || emission.Register is not null ||
            !ReferenceEquals(emission.Body, il) || emission.Constructor is null || offset <= emission.AllocationOffset ||
            !ReferenceEquals(register.Module, lookup._owner.Module))
            throw new InvalidOperationException("Prototype allocation must reach its actual Register operation exactly once.");
        emission.Register = register; emission.RegisterOffset = offset;
    }

    internal static void EmitCarrierCreate(NativePrototypeFieldsLookup? lookup, ILGenerator il, ConstructorInfo constructor,
        NativeClosedSourceFamilies? families = null, MethodBase? caller = null)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        NativeClosedSourceFamilies.EmitAllocation(families, il, constructor, caller);
        lookup?._carriers.Add((constructor, il, offset));
    }

    internal static void EmitUnprovedCarrierResult(NativePrototypeFieldsLookup? lookup, ILGenerator il, MethodInfo producer)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Callvirt, producer);
        lookup?._unprovedResults.Add((producer, il, offset));
    }

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        EnsureMutable();
        if (!native || !ReferenceEquals(module, _owner.Module))
            throw new InvalidOperationException("Prototype lookup requires its exact native output module.");
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, NativeClrAdmission.CompletedIngressProof? ingress,
        EmittedRuntime runtime, Type runtimeType, NativeClosedSourceFamilySnapshot? families = null)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(runtime.NativePrototypeFields, this) ||
            ingress is not null && !ReferenceEquals(ingress.Module, _owner.Module))
            throw new InvalidOperationException("Prototype lookup requires completed metadata from this exact output.");
        if (families is not null && !ReferenceEquals(families.Module, _owner.Module))
            throw new InvalidOperationException("Closed prototype contexts must belong to this output.");
        if (ingress is null) _blockers.Add("CLR ingress admission has not completed");
        foreach (var result in _unprovedResults)
        {
            if (result.Body.ILOffset <= result.Offset || result.Method.IsStatic || result.Method.ReturnType.IsValueType)
                throw new InvalidOperationException("Unproved carrier results require their actual completed reference-result operation.");
            // A declared Task<T> return does not prove its exact runtime subclass.
            // Keep the entire operation rather than guessing a concrete query row.
            _blockers.Add($"Unproved actual carrier result: {result.Method.DeclaringType?.FullName}.{result.Method.Name}");
        }
        var owners = new HashSet<Type>();
        if (runtimeType != _types.Object.GetType() || runtimeType.Assembly != _types.CoreAssembly)
            throw new InvalidOperationException("Prototype Type-valued carriers require the existing exact runtime-kind and completed producer proof.");
        if (runtime.NativeConstructorActivation?.IsComplete != true)
            _blockers.Add("Actual constructor value/activation producers have not completed");
        else if (!ReferenceEquals(runtime.NativeConstructorActivation.CompletedSnapshot.Module, _owner.Module))
            throw new InvalidOperationException("Prototype constructor-value evidence must belong to this output.");
        else if (runtime.NativeConstructorActivation.CompletedSnapshot.Owners.Count != 0) owners.Add(runtimeType);
        foreach (var prototype in _prototypes)
        {
            var definition = prototype.Owner.IsGenericType ? prototype.Owner.GetGenericTypeDefinition() : prototype.Owner;
            if (definition is not TypeBuilder builder || !sources.Implementers.Contains(builder) || !builder.IsCreated() ||
                prototype.Register != runtime.ClassPrototypes.Register || prototype.Constructor is null ||
                prototype.AllocationOffset <= prototype.TokenOffset || prototype.RegisterOffset <= prototype.AllocationOffset ||
                prototype.Body.ILOffset <= prototype.RegisterOffset || !_stores.TryGetValue(builder, out var store) ||
                !ValidStore(store) || store.GetILGenerator().ILOffset == 0)
                throw new InvalidOperationException("Prototype records require completed owner, accessor and allocation/Register bodies.");
            Type allocated = prototype.Constructor.DeclaringType!;
            if ((builder.Attributes & TypeAttributes.Abstract) != 0 ? allocated != runtime.ObjectStorage.Type : allocated != prototype.Owner)
                throw new InvalidOperationException("Prototype allocation must construct its actual source owner or abstract ordinary carrier.");
            if (definition.IsGenericTypeDefinition && families?.ContextsFor(definition) is null)
                _blockers.Add($"Unproved owned generic prototype family: {definition.FullName}");
        }
        foreach (var store in _stores)
            if (!_prototypes.Any(prototype => (prototype.Owner.IsGenericType ? prototype.Owner.GetGenericTypeDefinition() : prototype.Owner) == store.Key))
                _blockers.Add($"No actual prototype allocation/Register producer for owned carrier: {store.Key.FullName}");
        foreach (var carrier in _carriers)
        {
            if (carrier.Body.ILOffset <= carrier.Offset || carrier.Constructor.DeclaringType is not { } type)
                throw new InvalidOperationException("Prototype carriers require their actual completed allocation operation.");
            if (FunctionGenericParameterRegistry.ContainsUnboundType(type))
            {
                Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (families?.ContextsFor(definition) is { } contexts)
                    foreach (var context in contexts) owners.Add(context);
                else _blockers.Add($"Unproved owned prototype carrier: {type.FullName}");
            }
            else if (type is TypeBuilder builder && !builder.IsCreated())
                throw new InvalidOperationException("Prototype carriers must be finalized before query emission.");
            else owners.Add(type);
        }
        foreach (var owner in runtime.NativeConstructorActivation?.IsComplete == true
            ? runtime.NativeConstructorActivation.LiteralActivationOwners : [])
        {
            if (FunctionGenericParameterRegistry.ContainsUnboundType(owner))
                _blockers.Add($"Unproved literal activation carrier: {owner.FullName}");
            else owners.Add(owner);
        }
        // Other operation registrations are obligations only. They never provide
        // a finite query row without an actual allocation/activation producer.
        if (runtime.NativeCallables is { IsComplete: true } callables)
        {
            foreach (var carrier in callables.Carriers)
                if (!(families?.ContextsFor(carrier) is { } contexts ? contexts.All(owners.Contains) : owners.Contains(carrier)))
                    _blockers.Add($"No completed allocation producer for admitted callable carrier: {carrier.FullName}");
        }
        else if (runtime.NativeCallables is not null)
            _blockers.Add("Native callable ingress declarations have not completed");
        Owners = Array.AsReadOnly(owners.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        EmitQuery(runtime.Errors);
        _owner.CreateType();
        _completedUnprovedResults = new(_owner.Module, _unprovedResults.Select(result =>
            new NativeUnprovedCarrierResultProducer(result.Method, result.Offset, result.Body.ILOffset)));
        IsComplete = true;
    }

    private bool ValidStore(MethodInfo method) => method.Name == "$EnsureFields" && method.IsPrivate && !method.IsStatic &&
        !method.IsGenericMethod && (method.CallingConvention & CallingConventions.VarArgs) == 0 &&
        method.ReturnType == _types.DictionaryStringObject && method.GetParameters().Length == 0;

    private void EmitQuery(EmittedErrorRuntime errors)
    {
        var il = Find.GetILGenerator();
        var lookup = _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags));
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); EmitLookup(); return;
        }
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
            // Retain the actual name-only binder, including absent/ambiguous results.
            il.Emit(OpCodes.Ldstr, "$EnsureFields"); il.Emit(OpCodes.Ldc_I4, (int)Flags);
            il.Emit(OpCodes.Callvirt, lookup); il.Emit(OpCodes.Ret);
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Prototype lookup emission has already completed.");
    }
}

/// <summary>Actual completed reference-result operations, independent of any consumer's query obligations.</summary>
internal sealed record NativeUnprovedCarrierResultProducer(MethodInfo Producer, int CallOffset, int CompletedBodyLength);

internal sealed class NativeUnprovedCarrierResultsSnapshot
{
    internal NativeUnprovedCarrierResultsSnapshot(Module module, IEnumerable<NativeUnprovedCarrierResultProducer> producers)
    {
        Module = module;
        Producers = Array.AsReadOnly(producers.ToArray());
    }

    internal Module Module { get; }
    internal IReadOnlyList<NativeUnprovedCarrierResultProducer> Producers { get; }
}
