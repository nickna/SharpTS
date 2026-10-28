using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late exact-owner property lookup; owns no runtime reflection fallback policy outside this operation.</summary>
internal sealed partial class NativePropertyLookupCatalog
{
    internal const string ForeignOwnerMessage = "Property lookup on an unproved CLR receiver is not supported in this native guest profile. Use --target dll or --target exe.";
    private const BindingFlags PropertyFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly HashSet<Type> _owners = [];
    private IReadOnlyList<OwnerInventory> _inventory = [];
    internal sealed record PropertyInventory(string Name, MethodInfo? Getter, bool UsesLiteralSelection);
    internal sealed record OwnerInventory(Type Owner, IReadOnlyList<PropertyInventory> Properties, bool UsesLiteralLookup = false);

    internal NativePropertyLookupCatalog(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativePropertyLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FindGetter = _owner.DefineMethod("FindGetter", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type, types.String]);
    }

    internal MethodBuilder FindGetter { get; }
    internal bool IsComplete { get; private set; }
    internal IReadOnlyList<OwnerInventory> Inventory => IsComplete ? _inventory :
        throw new InvalidOperationException("Native property inventory is incomplete.");

    internal void RegisterOutputOwner(Type type)
    {
        EnsureOpen();
        if (type is not TypeBuilder || type.Module.ModuleVersionId != _owner.Module.ModuleVersionId)
            throw new InvalidOperationException("Native property owners must be declarations in this exact output.");
        _owners.Add(type);
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sourceOwners, EmittedRuntime runtime)
    {
        EnsureOpen();
        if (!sourceOwners.IsComplete || !ReferenceEquals(sourceOwners.Module, _owner.Module) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native properties require completed source metadata from this exact output.");
        foreach (var source in sourceOwners.Implementers) RegisterOutputOwner(source);
        RegisterRuntimeOwners(runtime);
        foreach (var type in FrameworkOwners()) _owners.Add(type);
        var inventory = new List<OwnerInventory>();
        foreach (var type in _owners.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type is TypeBuilder builder && !builder.IsCreated())
                throw new InvalidOperationException("Native property owners must be finalized before lookup completion.");
            // This slice does not pretend an open definition proves every actual closed receiver.
            if (FunctionGenericParameterRegistry.ContainsUnboundType(type)) continue;
            // Builder visibility/hiding is not identical to the saved runtime binder.
            // A literal query on this exact positive owner preserves that operation.
            bool literal;
            PropertyInfo[] visible;
            try
            {
                literal = HasNonPublicAccessor(type);
                visible = literal ? [] : _types.GetProperties(type, PropertyFlags);
            }
            catch (NotSupportedException) { literal = true; visible = []; }
            var properties = new List<PropertyInventory>();
            foreach (var group in visible.GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
            {
                var candidates = group.ToArray();
                properties.Add(candidates.Length == 1
                    ? new(group.First().Name, candidates[0].GetGetMethod(nonPublic: true), false)
                    : new(group.First().Name, null, true));
            }
            inventory.Add(new(type, Array.AsReadOnly(properties.ToArray()), literal));
        }
        EmitLookup(inventory, runtime.Errors);
        _owner.CreateType();
        _inventory = Array.AsReadOnly(inventory.ToArray());
        IsComplete = true;
    }

    private void EmitLookup(IEnumerable<OwnerInventory> inventory, EmittedErrorRuntime errors)
    {
        var il = FindGetter.GetILGenerator();
        foreach (var entry in inventory)
        {
            var nextOwner = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, entry.Owner);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, nextOwner);
            if (entry.UsesLiteralLookup)
            {
                EmitLiteralGetter(il, entry.Owner, errors); il.MarkLabel(nextOwner); continue;
            }
            foreach (var property in entry.Properties)
            {
                var nextProperty = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, property.Name);
                il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String, _types.StringComparison));
                il.Emit(OpCodes.Brfalse, nextProperty);
                if (property.UsesLiteralSelection)
                {
                    EmitLiteralGetter(il, entry.Owner, errors);
                }
                else if (property.Getter is { } getter)
                {
                    _types.EmitLoadMethodInfo(il, getter); il.Emit(OpCodes.Ret);
                }
                else GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
                    "A setter-only CLR property is not supported in this native guest profile. Use --target dll or --target exe.");
                il.MarkLabel(nextProperty);
            }
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
            il.MarkLabel(nextOwner);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignOwnerMessage);
    }

    private bool HasNonPublicAccessor(Type owner)
    {
        for (Type? type = owner; type is not null; type = type.BaseType)
            foreach (var property in _types.GetProperties(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (property.GetGetMethod(true) is { IsPublic: false } || property.GetSetMethod(true) is { IsPublic: false }) return true;
        return false;
    }

    private void EmitLiteralGetter(ILGenerator il, Type owner, EmittedErrorRuntime errors)
    {
        il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)PropertyFlags);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
        var found = il.DefineLabel(); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brtrue, found);
        il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); il.MarkLabel(found);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetGetMethod", _types.Boolean));
        var hasGetter = il.DefineLabel(); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brtrue, hasGetter); il.Emit(OpCodes.Pop);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
            "A setter-only CLR property is not supported in this native guest profile. Use --target dll or --target exe.");
        il.MarkLabel(hasGetter); il.Emit(OpCodes.Ret);
    }

    private void EnsureOpen()
    {
        if (IsComplete) throw new InvalidOperationException("Native property metadata emission is already complete.");
    }
}
