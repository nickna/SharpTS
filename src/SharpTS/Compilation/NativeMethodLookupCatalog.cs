using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Finite native method-query owners; selection stays with the runtime reflection binder.</summary>
internal sealed partial class NativeMethodLookupCatalog
{
    internal const string ForeignOwnerMessage = "Method lookup on an unproved CLR owner is not supported in this native guest profile. Use --target dll or --target exe.";
    internal const string FunctionBridgeMessage = "CLR function method lookup outside the fixed manual bridges is not supported in this native guest profile. Use --target dll or --target exe.";
    internal const string ArrayBridgeMessage = "CLR array method lookup outside the fixed manual bridges is not supported in this native guest profile. Use --target dll or --target exe.";
    internal static readonly IReadOnlyList<string> ManualBridgeNames = Array.AsReadOnly(new[] { "Call", "GetMember", "SetMember" });
    internal const string UnsupportedFlagsMessage = "These method lookup flags are not supported in this native guest profile. Use --target dll or --target exe.";
    internal static readonly IReadOnlyList<BindingFlags> SupportedFlags = Array.AsReadOnly(new[] {
        BindingFlags.Public | BindingFlags.Instance,
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
        BindingFlags.Public | BindingFlags.Static,
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
    });
    private static readonly IReadOnlyList<BindingFlags> FrameworkFlags = Array.AsReadOnly(new[] {
        BindingFlags.Public | BindingFlags.Instance,
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
    });
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _owners = [];
    private Type? _functionOwner;

    internal NativeMethodLookupCatalog(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeMethodLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        var select = _owner.DefineMethod("Select", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type, types.String, typeof(BindingFlags)]);
        var enumerate = _owner.DefineMethod("Enumerate", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MakeArrayType(types.MethodInfo), [types.Type, typeof(BindingFlags)]);
        Handles = new(module, select, enumerate);
    }

    internal EmittedNativeMethodLookupRuntime Handles { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal bool IsArrayBridgeOwner(Type owner) => owner == _types.ObjectArray;
    internal bool IsFunctionBridgeOwner(Type owner) => owner == _functionOwner;
    internal bool IsManualBridgeOwner(Type owner) => IsArrayBridgeOwner(owner) || IsFunctionBridgeOwner(owner);
    internal IReadOnlyList<BindingFlags> FlagsFor(Type owner) =>
        IsManualBridgeOwner(owner) ? Array.AsReadOnly(new[] { BindingFlags.Public | BindingFlags.Instance }) :
        owner.Module.ModuleVersionId == _owner.Module.ModuleVersionId ? SupportedFlags : FrameworkFlags;
    internal IReadOnlyList<Type> Owners => IsComplete ? _owners :
        throw new InvalidOperationException("Native method inventory is incomplete.");

    internal void CompleteEmission(NativePropertyLookupCatalog properties, NativeSourceOwnerCatalog sources, EmittedErrorRuntime errors, Type functionOwner)
    {
        if (IsComplete) throw new InvalidOperationException("Native method lookup emission is already complete.");
        if (!properties.IsComplete || !sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(properties.FindGetter.Module, _owner.Module) ||
            !ReferenceEquals(errors.Type.Module, _owner.Module) || !ReferenceEquals(functionOwner.Module, _owner.Module))
            throw new InvalidOperationException("Native method lookup requires completed same-output property owners.");
        var owners = properties.Inventory.Select(entry => entry.Owner).Concat(sources.CanonicalOwners).Distinct().ToArray();
        foreach (var owner in owners)
            if (owner is TypeBuilder builder && !builder.IsCreated())
                throw new InvalidOperationException("Native method lookup requires finalized source owners.");
        _functionOwner = functionOwner;
        UsesLegacy = sources.Blockers.Count != 0;
        EmitQuery(Handles.Select, owners, errors, select: true);
        EmitQuery(Handles.Enumerate, owners, errors, select: false);
        _owner.CreateType();
        _owners = Array.AsReadOnly(owners);
        IsComplete = true;
    }

    private void EmitQuery(MethodBuilder method, IEnumerable<Type> owners, EmittedErrorRuntime errors, bool select)
    {
        var il = method.GetILGenerator();
        if (UsesLegacy)
        {
            // An unproved generic owner keeps the entire original operation.
            // This is retention, never an inferred canonical owner or metadata absence proof.
            il.Emit(OpCodes.Ldarg_0);
            if (select) il.Emit(OpCodes.Ldarg_1);
            il.Emit(select ? OpCodes.Ldarg_2 : OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, select
                ? _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags))
                : _types.GetMethod(_types.Type, "GetMethods", typeof(BindingFlags)));
            il.Emit(OpCodes.Ret);
            return;
        }
        foreach (var owner in owners)
        {
            var nextOwner = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, owner);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, nextOwner);
            if (IsManualBridgeOwner(owner))
            {
                EmitFixedBridgeQuery(il, errors, select, owner, IsArrayBridgeOwner(owner) ? ArrayBridgeMessage : FunctionBridgeMessage);
                il.MarkLabel(nextOwner); continue;
            }
            foreach (var flags in FlagsFor(owner))
            {
                var nextFlags = il.DefineLabel();
                il.Emit(select ? OpCodes.Ldarg_2 : OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)flags);
                il.Emit(OpCodes.Bne_Un, nextFlags);
                il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                if (select) il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, (int)flags);
                il.Emit(OpCodes.Callvirt, select
                    ? _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags))
                    : _types.GetMethod(_types.Type, "GetMethods", typeof(BindingFlags)));
                il.Emit(OpCodes.Ret); il.MarkLabel(nextFlags);
            }
            GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, UnsupportedFlagsMessage);
            il.MarkLabel(nextOwner);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignOwnerMessage);
    }
}
