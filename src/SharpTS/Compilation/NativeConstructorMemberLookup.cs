using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Exact source-constructor member enumeration at the existing key lookup point.</summary>
internal sealed class NativeConstructorMemberLookup
{
    internal const string UnsupportedOwnerMessage = "Constructor member enumeration on an unproved CLR owner is not supported in this native guest profile. Use --target dll or --target exe.";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _owners = [];

    internal NativeConstructorMemberLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeConstructorMemberLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        GetMembers = _owner.DefineMethod("GetMembers", MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(MemberInfo[]), [types.Type, typeof(BindingFlags)]);
    }

    internal MethodBuilder GetMembers { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Owners => IsComplete ? _owners :
        throw new InvalidOperationException("Native constructor member metadata is incomplete.");

    internal void CompleteEmission(NativeRuntimeClassInitialization initializers, NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        if (IsComplete || !initializers.IsComplete || !sources.IsComplete ||
            !ReferenceEquals(initializers.Initialize.Module, _owner.Module) || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(sources.Contract, runtime.ObjectFields.Interface) || !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native constructor member lookup requires completed source metadata from this exact output.");
        UsesLegacy = initializers.UsesLegacy;
        _owners = Array.AsReadOnly((UsesLegacy ? sources.Implementers.Cast<Type>() : initializers.Owners.Where(type => sources.Implementers.Contains(
            type.IsGenericType ? type.GetGenericTypeDefinition() : type))).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        var il = GetMembers.GetILGenerator();
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMembers", typeof(BindingFlags)));
            il.Emit(OpCodes.Ret);
        }
        else
        {
            var correctFlags = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags); il.Emit(OpCodes.Beq, correctFlags);
            Refuse(il, runtime.Errors); il.MarkLabel(correctFlags);
            foreach (var type in _owners)
            {
                if (type is TypeBuilder builder && !builder.IsCreated() || FunctionGenericParameterRegistry.ContainsUnboundType(type) ||
                    type.Module.ModuleVersionId != _owner.Module.ModuleVersionId)
                    throw new InvalidOperationException("Native constructor members require a finalized closed source declaration.");
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
                // Keep the loaded runtime's complete member ordering and attribute
                // identity. Only this exact source owner's declared static members
                // are retained; no framework member enumeration is introduced.
                il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Ldc_I4, (int)Flags);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMembers", typeof(BindingFlags)));
                il.Emit(OpCodes.Ret); il.MarkLabel(next);
            }
            Refuse(il, runtime.Errors);
        }
        _owner.CreateType(); IsComplete = true;
    }

    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, UnsupportedOwnerMessage);
}
