using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late exact-owner metadata queries used only by the existing deletion paths.</summary>
internal sealed class NativeDeletionLookup
{
    internal const string UnsupportedMessage = "Deletion metadata lookup on an unproved CLR owner or with unproved name/flags is not supported in this native guest profile. Use --target dll or --target exe.";
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags DynamicFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _staticOwners = [];
    private IReadOnlyList<Type> _dynamicOwners = [];

    internal NativeDeletionLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeDeletionLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FindStaticProperty = _owner.DefineMethod("FindStaticProperty", MethodAttributes.Assembly | MethodAttributes.Static,
            types.PropertyInfo, [types.Type, types.String, typeof(BindingFlags)]);
        FindDynamicFields = _owner.DefineMethod("FindDynamicFields", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type, types.String, typeof(BindingFlags)]);
    }

    internal MethodBuilder FindStaticProperty { get; }
    internal MethodBuilder FindDynamicFields { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> StaticOwners => IsComplete ? _staticOwners :
        throw new InvalidOperationException("Native deletion metadata is incomplete.");
    internal IReadOnlyList<Type> DynamicOwners => IsComplete ? _dynamicOwners :
        throw new InvalidOperationException("Native deletion metadata is incomplete.");

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        if (!native || IsComplete || !ReferenceEquals(_owner.Module, module) ||
            !Valid(FindStaticProperty, _types.PropertyInfo) || !Valid(FindDynamicFields, _types.MethodInfo))
            throw new InvalidOperationException("Native deletion declarations must have this native output's exact module and signatures.");

        bool Valid(MethodInfo method, Type result) => ReferenceEquals(method.Module, module) && method.IsStatic &&
            !method.IsGenericMethod && (method.CallingConvention & CallingConventions.VarArgs) == 0 && method.ReturnType == result &&
            method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.Type, _types.String, typeof(BindingFlags)]);
    }

    internal void CompleteEmission(NativeRuntimeClassInitialization initializers, NativePropertyLookupCatalog properties,
        NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        if (IsComplete || !initializers.IsComplete || !properties.IsComplete || !sources.IsComplete ||
            !ReferenceEquals(initializers.Initialize.Module, _owner.Module) || !ReferenceEquals(properties.FindGetter.Module, _owner.Module) ||
            !ReferenceEquals(sources.Module, _owner.Module) || !ReferenceEquals(sources.Contract, runtime.ObjectFields.Interface) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native deletion lookup requires completed metadata from this exact output.");
        UsesLegacy = initializers.UsesLegacy;
        _staticOwners = Array.AsReadOnly(initializers.Owners.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        _dynamicOwners = Array.AsReadOnly(properties.Inventory.Select(entry => entry.Owner).Append(runtime.ObjectStorage.Type)
            .Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        EmitQuery(FindStaticProperty, _staticOwners, StaticFlags, "GetProperty", null, runtime.Errors);
        EmitQuery(FindDynamicFields, _dynamicOwners, DynamicFlags, "GetMethod", "$EnsureFields", runtime.Errors);
        _owner.CreateType(); IsComplete = true;
    }

    private void EmitQuery(MethodBuilder query, IReadOnlyList<Type> owners, BindingFlags flags,
        string operation, string? fixedName, EmittedErrorRuntime errors)
    {
        var il = query.GetILGenerator();
        var lookup = _types.GetMethod(_types.Type, operation, _types.String, typeof(BindingFlags));
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, lookup); il.Emit(OpCodes.Ret);
            return;
        }
        foreach (var type in owners)
        {
            if (FunctionGenericParameterRegistry.ContainsUnboundType(type) || type is TypeBuilder builder && !builder.IsCreated())
                throw new InvalidOperationException("Native deletion owners must be closed and finalized.");
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
            var accepted = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)flags); il.Emit(OpCodes.Beq, accepted);
            Refuse(il, errors); il.MarkLabel(accepted);
            if (fixedName is not null)
            {
                var correctName = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, fixedName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, correctName); Refuse(il, errors); il.MarkLabel(correctName);
            }
            // Preserve the actual loaded binder and its exceptions. The name is
            // literal for the private dynamic-store seam, never a broad method root.
            il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            if (fixedName is null) il.Emit(OpCodes.Ldarg_1); else il.Emit(OpCodes.Ldstr, fixedName);
            il.Emit(OpCodes.Ldc_I4, (int)flags); il.Emit(OpCodes.Callvirt, lookup); il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        Refuse(il, errors);
    }

    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, UnsupportedMessage);
}
