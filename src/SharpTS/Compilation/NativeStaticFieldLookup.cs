using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Preserves static field selection on a finite set of completed owner identities.</summary>
internal sealed class NativeStaticFieldLookup
{
    internal const string UnsupportedOwnerMessage = "Static field lookup on an unproved CLR owner or with unproved flags is not supported in this native guest profile. Use --target dll or --target exe.";
    private static readonly BindingFlags[] SupportedFlags = [
        BindingFlags.Public | BindingFlags.Static,
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
    ];
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _owners = [];

    internal NativeStaticFieldLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeStaticFieldLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FindField = _owner.DefineMethod("FindField", MethodAttributes.Assembly | MethodAttributes.Static,
            types.FieldInfo, [types.Type, types.String, typeof(BindingFlags)]);
    }

    internal MethodBuilder FindField { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Owners => IsComplete ? _owners :
        throw new InvalidOperationException("Native static field metadata is incomplete.");

    internal void CompleteEmission(NativeRuntimeClassInitialization initializers, NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        if (IsComplete || !initializers.IsComplete || !sources.IsComplete ||
            !ReferenceEquals(initializers.Initialize.Module, _owner.Module) ||
            !ReferenceEquals(sources.Module, _owner.Module) || !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native static field lookup requires completed metadata from this exact output.");
        // Unproved generic owners retain the whole existing operation. This helper
        // does not create a new closed instantiation or change its static state.
        UsesLegacy = initializers.UsesLegacy;
        _owners = Array.AsReadOnly(initializers.Owners.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        var il = FindField.GetILGenerator();
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Ret);
        }
        else
        {
            foreach (var type in _owners)
            {
                if (FunctionGenericParameterRegistry.ContainsUnboundType(type) || type is TypeBuilder builder && !builder.IsCreated())
                    throw new InvalidOperationException("Native static field owners must be closed and finalized.");
                var nextOwner = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, nextOwner);
                foreach (var flags in SupportedFlags)
                {
                    var nextFlags = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)flags); il.Emit(OpCodes.Bne_Un, nextFlags);
                    // Execute the actual loaded binder query: field hiding,
                    // inheritance, ambiguity and null-name behavior stay intact.
                    il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                    il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)flags);
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags)));
                    il.Emit(OpCodes.Ret); il.MarkLabel(nextFlags);
                }
                EmitUnsupported(il, runtime.Errors);
                il.MarkLabel(nextOwner);
            }
            EmitUnsupported(il, runtime.Errors);
        }
        _owner.CreateType(); IsComplete = true;
    }

    private static void EmitUnsupported(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, UnsupportedOwnerMessage);
}
