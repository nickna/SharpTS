using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Compiler-owned canonical source owners; completed after source types, outside the frozen runtime.</summary>
internal sealed class NativeSourceOwnerCatalog
{
    private readonly TypeProvider _types;
    private readonly TypeBuilder _catalog;
    private readonly HashSet<TypeBuilder> _implementers = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _blockers = [];
    private readonly HashSet<TypeBuilder> _legacyOwners = new(ReferenceEqualityComparer.Instance);
    private Type? _contract;
    private IReadOnlyList<Type> _canonicalOwners = [];

    internal NativeSourceOwnerCatalog(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _catalog = EmitTypeDefinitions.DefineType(module, "<>NativeSourceOwnerCatalog",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, types.Object);
        Resolve = _catalog.DefineMethod("Resolve", MethodAttributes.Assembly | MethodAttributes.Static, types.Type, [types.Type]);
    }

    internal MethodBuilder Resolve { get; }
    internal Module Module => _catalog.Module;
    internal Type Contract => _contract ?? throw new InvalidOperationException("Source-owner interface is not declared.");
    internal bool IsComplete { get; private set; }
    internal int MappedOwnerCount { get; private set; }
    internal IReadOnlyList<string> Blockers => _blockers;
    internal IReadOnlyCollection<TypeBuilder> Implementers => _implementers;
    internal IReadOnlyCollection<TypeBuilder> LegacyOwners => _legacyOwners;
    internal IReadOnlyList<Type> CanonicalOwners => IsComplete ? _canonicalOwners :
        throw new InvalidOperationException("Canonical source owners are incomplete.");

    internal void DeclareContract(Type contract)
    {
        if (IsComplete || _contract != null || contract.Module.ModuleVersionId != _catalog.Module.ModuleVersionId)
            throw new InvalidOperationException("The source-owner interface must be declared once in this output.");
        _contract = contract;
    }

    internal static void Implement(TypeBuilder owner, Type contract, NativeSourceOwnerCatalog? catalog)
    {
        if (catalog != null && (catalog.IsComplete || catalog._contract != contract || owner.IsCreated()
            || owner.Module.ModuleVersionId != catalog._catalog.Module.ModuleVersionId || !catalog._implementers.Add(owner)))
            throw new InvalidOperationException("The source-owner interface implementation is invalid or already declared.");
        EmitTypeDefinitions.AddInterfaceImplementation(owner, contract);
    }

    internal void CompleteEmission(ClassGenericParameterRegistry parameters, IEnumerable<TypeBuilder> sourceOwners)
    {
        if (IsComplete || _contract == null || !parameters.IsComplete)
            throw new InvalidOperationException("Source-owner metadata is not ready for completion.");
        var expected = new HashSet<TypeBuilder>(sourceOwners, ReferenceEqualityComparer.Instance);
        foreach (var owner in expected.Where(owner => !_implementers.Contains(owner)))
            Block(owner, "Source interface implementation was not registered");
        var mappings = new List<(Type Definition, Type Canonical)>();
        foreach (var owner in _implementers)
        {
            if (!owner.IsCreated()) throw new InvalidOperationException("Source-owner types must be finalized before the catalog.");
            bool implementsContract;
            try { implementsContract = _types.GetInterfaces(owner).Contains(_contract); }
            catch (NotSupportedException)
            {
                Block(owner, "Created interface metadata is unavailable");
                continue;
            }
            if (!implementsContract)
            {
                Block(owner, "Registered owner does not implement this output's interface");
                continue;
            }
            if (!owner.IsGenericTypeDefinition) continue;
            if (!expected.Contains(owner) || !parameters.TryGetCanonicalArguments(owner, out var arguments)
                || arguments.Any(FunctionGenericParameterRegistry.ContainsUnboundType))
            {
                Block(owner, "Canonical generic owner is not proved");
                continue;
            }
            mappings.Add((owner, EmitGenerics.MakeGenericType(owner, arguments.ToArray())));
        }
        var il = Resolve.GetILGenerator();
        var ownerLocal = il.DeclareLocal(_types.Type);
        var definition = il.DeclareLocal(_types.Type);
        var returnOwner = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stloc, ownerLocal);
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
        il.Emit(OpCodes.Brfalse, returnOwner);
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Stloc, definition);
        foreach (var mapping in mappings)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, definition); il.Emit(OpCodes.Ldtoken, mapping.Definition);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldtoken, mapping.Canonical); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ret); il.MarkLabel(next);
        }
        // Unknown/unproved carriers retain old closure, never an unsupported runtime default.
        GenericTypeClosingEmitter.Emit(il, _types, ownerLocal);
        il.MarkLabel(returnOwner); il.Emit(OpCodes.Ldloc, ownerLocal); il.Emit(OpCodes.Ret);
        MappedOwnerCount = mappings.Count;
        _canonicalOwners = Array.AsReadOnly(mappings.Select(mapping => mapping.Canonical).ToArray());
        _catalog.CreateType();
        IsComplete = true;
    }

    private void Block(TypeBuilder owner, string reason)
    {
        _legacyOwners.Add(owner);
        _blockers.Add($"{reason}: {owner.FullName}");
    }
}
