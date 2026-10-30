using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late exact-owner presence queries at the in-operator's CLR fallback.</summary>
internal sealed class NativeHasInMemberLookup
{
    internal const string ForeignOwnerMessage = "The CLR member fallback for 'in' is not supported on an unproved owner in this native guest profile. Use --target dll or --target exe.";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _owners = [];

    internal NativeHasInMemberLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeHasInMemberLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        HasMember = _owner.DefineMethod("HasMember", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Boolean, [types.Type, types.String]);
    }

    internal MethodBuilder HasMember { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Owners => IsComplete ? _owners :
        throw new InvalidOperationException("Native in-operator member metadata is incomplete.");

    internal void CompleteEmission(NativeMethodLookupCatalog methods, NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        if (IsComplete || !methods.IsComplete || !sources.IsComplete ||
            !ReferenceEquals(methods.Handles.Module, _owner.Module) || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native in-operator members require completed metadata from this exact output.");
        UsesLegacy = methods.UsesLegacy;
        // Function objects, constructor Type values and source instances complete
        // their ordinary descriptor/prototype walk before this CLR fallback.
        _owners = Array.AsReadOnly(methods.Owners.Where(owner => !methods.IsFunctionBridgeOwner(owner)).ToArray());
        var il = HasMember.GetILGenerator();
        var present = il.DefineLabel();
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Brtrue, present);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Brtrue, present);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        }
        else
        {
            foreach (var owner in _owners)
            {
                if (FunctionGenericParameterRegistry.ContainsUnboundType(owner) || owner is TypeBuilder builder && !builder.IsCreated())
                    throw new InvalidOperationException("Native in-operator owners must be finalized and closed.");
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, owner);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
                il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags)));
                il.Emit(OpCodes.Brtrue, present);
                if (methods.IsArrayBridgeOwner(owner)) EmitArrayMethodPresence(il, owner, present);
                else
                {
                    il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
                    // Keep the direct binder operation, including ambiguity. HasIn
                    // has never used SafeGetMethod's deterministic overload fallback.
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
                    il.Emit(OpCodes.Brtrue, present);
                }
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.MarkLabel(next);
            }
            GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, ForeignOwnerMessage);
        }
        il.MarkLabel(present); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        _owner.CreateType(); IsComplete = true;
    }

    private void EmitArrayMethodPresence(ILGenerator il, Type owner, Label present)
    {
        // The declared object[] carrier has a finite framework method-name domain.
        // A named literal query preserves the loaded binder's overload exceptions,
        // while avoiding unrelated static Array.CreateInstance dynamic-code roots.
        var groups = _types.GetMethods(owner, Flags).GroupBy(method => method.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Any(group => group.Select(method => method.Name).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new InvalidOperationException("The native array method-name domain has case-ambiguous metadata.");
        foreach (string name in groups.Select(group => group.Key))
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String, _types.StringComparison));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            // The exact framework array inventory proves one canonical spelling
            // per ignore-case group. The comparison above handles name casing;
            // the real binder still selects overloads with the original instance
            // visibility. Leaving IgnoreCase set would force all public method
            // metadata roots even for this literal named query.
            il.Emit(OpCodes.Ldstr, name); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Brtrue, present);
            il.MarkLabel(next);
        }
    }
}