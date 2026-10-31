using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late evaluated-constructor operations backed by completed real value producers.</summary>
internal sealed class NativeEvaluatedConstructorMetadata
{
    internal const string ForeignTypeMessage = "This evaluated constructor Type is outside this native guest's proved constructor-value producers. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly List<string> _blockers = [];

    internal NativeEvaluatedConstructorMetadata(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeEvaluatedConstructorMetadata", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        var close = _owner.DefineMethod("CloseEvaluated", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Type, [types.Type, types.MakeArrayType(types.Type), types.Type]);
        var first = _owner.DefineMethod("FirstPublicConstructor", MethodAttributes.Assembly | MethodAttributes.Static,
            types.ConstructorInfo, [types.Type]);
        Handles = new(module, close, first);
    }

    internal EmittedNativeConstructorMetadataRuntime Handles { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();
    internal IReadOnlyList<Type> ClosedOwners { get; private set; } = [];
    internal IReadOnlyList<Type> LegacyFamilies { get; private set; } = [];

    // Production callers obtain this immutable object only from the activation
    // catalog's CompletedSnapshot getter after both producer stages complete.
    internal void CompleteEmission(NativeConstructorValueProducerSnapshot snapshot, EmittedRuntime runtime)
    {
        if (IsComplete || !ReferenceEquals(snapshot.Module, _owner.Module) ||
            !ReferenceEquals(runtime.DynamicConstruction.FactoryAttributeType.Module, _owner.Module) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Evaluated constructor metadata requires this output's completed producer snapshot.");
        var closed = new HashSet<Type>();
        var families = new HashSet<Type>();
        _blockers.AddRange(snapshot.Blockers);
        foreach (var entry in snapshot.Owners)
        {
            if (entry.Producers.Count == 0 || entry.Producers.Any(producer =>
                producer.ExactTokenOwner != entry.ExactTokenOwner || producer.ValueOffset < 0 ||
                producer.Body.ILOffset <= producer.ValueOffset ||
                producer.Method is { } method && !ReferenceEquals(method.Module, snapshot.Module)))
                throw new InvalidOperationException("Constructor metadata requires actual completed value-emission identities.");
            if (entry.ClosureKind is NativeConstructorValueClosureKind.NongenericLiteral or NativeConstructorValueClosureKind.ExactClosedBclToken &&
                ProveClosedOwner(entry.ExactTokenOwner))
            {
                closed.Add(entry.ExactTokenOwner);
                continue;
            }
            if (entry.ClosureKind == NativeConstructorValueClosureKind.UnprovedSourceGenericFamily &&
                entry.SourceGenericDefinition is { IsGenericTypeDefinition: true } definition &&
                ReferenceEquals(definition.Module, _owner.Module) &&
                entry.Producers.All(producer => producer.Kind == NativeConstructorValueProducerKind.SourceClass))
            {
                families.Add(definition);
                _blockers.Add($"Unproved owned constructor generic family: {definition.FullName}");
            }
            else _blockers.Add($"Constructor value is not proved closed: {entry.ExactTokenOwner.FullName}");
        }
        ClosedOwners = Array.AsReadOnly(closed.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        LegacyFamilies = Array.AsReadOnly(families.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        UsesLegacy = LegacyFamilies.Count != 0;
        EmitClose(runtime);
        EmitFirst(runtime);
        _owner.CreateType(); IsComplete = true;
    }

    private bool ProveClosedOwner(Type owner)
    {
        var seen = new HashSet<Type>();
        for (Type? context = owner; context is not null;)
        {
            if (!seen.Add(context) || FunctionGenericParameterRegistry.ContainsUnboundType(context) ||
                context is TypeBuilder builder && !builder.IsCreated()) return false;
            try { context = context.BaseType; }
            catch (NotSupportedException) { return false; }
        }
        return true;
    }

    private void EmitClose(EmittedRuntime runtime)
    {
        var il = Handles.CloseEvaluated.GetILGenerator();
        foreach (var owner in ClosedOwners)
        {
            var next = il.DefineLabel();
            EmitExact(il, owner, next);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret); il.MarkLabel(next);
        }
        var legacy = il.DefineLabel();
        EmitFamilyAdmission(il, legacy);
        Refuse(il, runtime.Errors);
        if (UsesLegacy)
        {
            il.MarkLabel(legacy);
            EvaluatedConstructorClosureEmission.EmitLegacy(il, _types, runtime.DynamicConstruction.FactoryAttributeType);
        }
    }

    private void EmitFirst(EmittedRuntime runtime)
    {
        var il = Handles.FirstPublicConstructor.GetILGenerator();
        foreach (var owner in ClosedOwners)
        {
            var next = il.DefineLabel();
            EmitExact(il, owner, next);
            il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            EmitFirstQuery(il); il.MarkLabel(next);
        }
        var legacy = il.DefineLabel();
        EmitFamilyAdmission(il, legacy);
        Refuse(il, runtime.Errors);
        if (UsesLegacy)
        {
            il.MarkLabel(legacy); il.Emit(OpCodes.Ldarg_0); EmitFirstQuery(il);
        }
    }

    private void EmitFirstQuery(ILGenerator il)
    {
        // Keep the complete loaded operation: order and the zero-constructor
        // IndexOutOfRangeException belong to GetConstructors()[0], not a guessed ABI.
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetConstructors"));
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Ret);
    }

    private void EmitExact(ILGenerator il, Type literal, Label next)
    {
        il.Emit(OpCodes.Ldtoken, literal); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
        il.Emit(OpCodes.Brfalse, next);
    }

    private void EmitFamilyAdmission(ILGenerator il, Label legacy)
    {
        if (!UsesLegacy) return;
        var refused = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, refused);
        // Object.GetType is final; a TypeDelegator cannot expose its own
        // generic metadata while pretending to be the real runtime Type.
        il.Emit(OpCodes.Ldtoken, _types.Object); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Bne_Un, refused);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
        il.Emit(OpCodes.Brfalse, refused);
        var definition = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Stloc, definition);
        foreach (var owner in LegacyFamilies)
        {
            il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldloc, definition); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brtrue, legacy);
        }
        il.MarkLabel(refused);
    }

    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignTypeMessage);
}