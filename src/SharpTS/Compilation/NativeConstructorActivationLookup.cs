using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Constructor admission derived only from actual constructor-value emission.</summary>
internal sealed class NativeConstructorActivationLookup
{
    internal const string ForeignConstructorMessage = "This constructor Type is outside this native guest's recorded constructor values. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly List<NativeConstructorValueProducer> _producers = [];
    private readonly HashSet<Type> _intrinsics = new(ReferenceEqualityComparer.Instance);
    private NativeConstructorValueProducerSnapshot? _completed;
    private readonly List<Type> _literalActivationOwners = [];
    internal IReadOnlyList<Type> LiteralActivationOwners => IsComplete ? _literalActivationOwners.AsReadOnly() :
        throw new InvalidOperationException("Literal activation producers have not completed.");

    internal NativeConstructorActivationLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeConstructorActivation", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        AdmitType = _owner.DefineMethod("AdmitType", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.Type]);
        Activate = _owner.DefineMethod("Activate", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Object, [types.Type, types.ObjectArray]);
    }

    internal MethodBuilder AdmitType { get; }
    internal MethodBuilder Activate { get; }
    internal bool IsComplete => _completed is not null;
    internal bool UsesLegacy { get; private set; }
    internal NativeConstructorValueProducerSnapshot CompletedSnapshot => _completed ??
        throw new InvalidOperationException("Native constructor-value producers are incomplete.");

    // The record cannot be detached from the token operation it certifies. Managed
    // emission uses exactly the same instructions, without retaining a catalog.
    internal static void EmitValue(NativeConstructorActivationLookup? catalog, TypeProvider types,
        ILGenerator il, Type owner, NativeConstructorValueProducerKind kind,
        MethodBase? method = null, Type? lookupOwner = null, bool normalizeSourceGeneric = false)
    {
        catalog?.EnsureMutable();
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
        if (normalizeSourceGeneric && owner.IsGenericType)
            il.Emit(OpCodes.Callvirt, types.GetMethodNoParams(types.Type, "GetGenericTypeDefinition"));
        catalog?._producers.Add(new(owner, kind, il, il.ILOffset, method, lookupOwner));
    }

    // Called only while emitting an existing exact-Type intrinsic arm, after its
    // return. This is operation evidence, independent of names and annotations.
    internal void RecordIntrinsicReturn(Type owner, ILGenerator body)
    {
        EnsureMutable();
        if (body.ILOffset == 0) throw new InvalidOperationException("An intrinsic constructor arm must have a body.");
        _intrinsics.Add(owner);
    }

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        if (!native || IsComplete || !ReferenceEquals(module, _owner.Module) ||
            !Valid(AdmitType, _types.Void, [_types.Type]) || !Valid(Activate, _types.Object, [_types.Type, _types.ObjectArray]))
            throw new InvalidOperationException("Native constructor activation requires this native output's declaration.");
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native constructor activation requires completed owners from this exact output.");
        var owners = new List<NativeConstructorValueOwnerSnapshot>();
        foreach (var group in _producers.GroupBy(p => p.ExactTokenOwner))
        {
            var type = group.Key;
            var producers = group.ToArray();
            if (type is TypeBuilder owned && (!ReferenceEquals(owned.Module, _owner.Module) || !owned.IsCreated()))
                throw new InvalidOperationException("Constructor value builders must be finalized in this exact output.");
            if (producers.Any(p => p.ValueOffset <= 0 || p.Body.ILOffset < p.ValueOffset ||
                p.Method is not null && !ReferenceEquals(p.Method.Module, _owner.Module)))
                throw new InvalidOperationException("Constructor values require actual completed output bodies.");
            bool source = producers.Any(p => p.Kind == NativeConstructorValueProducerKind.SourceClass);
            Type? definition = null;
            var blockers = new List<string>();
            NativeConstructorValueClosureKind closure;
            if (source && type.IsGenericType)
            {
                definition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                closure = NativeConstructorValueClosureKind.UnprovedSourceGenericFamily;
                if (!ReferenceEquals(definition.Module, _owner.Module) ||
                    !sources.Implementers.Contains(definition))
                    throw new InvalidOperationException("A source generic constructor value must belong to the completed source output.");
                blockers.Add($"Source generic constructor values retain the existing Activator operation: {definition.FullName}");
            }
            else if (type.ContainsGenericParameters)
            {
                closure = NativeConstructorValueClosureKind.UnprovedValue;
                blockers.Add($"Constructor value has an unproved open owner: {type.FullName}");
            }
            else closure = type.IsGenericType ? NativeConstructorValueClosureKind.ExactClosedBclToken :
                NativeConstructorValueClosureKind.NongenericLiteral;
            bool intrinsic = _intrinsics.Contains(type);
            bool proved = TryProveConstructors(type, out var constructors);
            if (!proved && !intrinsic && closure != NativeConstructorValueClosureKind.UnprovedSourceGenericFamily)
                blockers.Add($"Constructor public ABI or body is unproved: {type.FullName}");
            owners.Add(new(type, producers, closure, definition, intrinsic, proved, constructors, blockers));
        }
        UsesLegacy = owners.Any(o => o.ClosureKind == NativeConstructorValueClosureKind.UnprovedSourceGenericFamily);
        var snapshot = new NativeConstructorValueProducerSnapshot(_owner.Module, owners, owners.SelectMany(o => o.Blockers));
        EmitAdmission(snapshot, runtime.Errors);
        EmitActivation(snapshot, runtime.Errors);
        _owner.CreateType();
        _completed = snapshot;
    }

    private bool TryProveConstructors(Type type, out ConstructorInfo[] constructors)
    {
        constructors = [];
        if (type.IsGenericTypeDefinition || FunctionGenericParameterRegistry.ContainsUnboundType(type) ||
            type is TypeBuilder builder && !builder.IsCreated()) return false;
        try
        {
            constructors = _types.GetConstructors(type);
            foreach (var constructor in constructors)
            {
                if (!constructor.IsPublic || constructor.IsStatic || constructor.DeclaringType != type ||
                    (constructor.CallingConvention & CallingConventions.VarArgs) != 0 ||
                    constructor.GetParameters().Any(p => p.ParameterType.ContainsGenericParameters ||
                        p.ParameterType.IsByRef || p.ParameterType.IsPointer || p.ParameterType.IsByRefLike)) return false;
                // Persisted builders do not expose MethodBody, even after CreateType.
                // Retain their actual emitted body instead of inventing body proof.
                if (constructor is ConstructorBuilder cb && cb.GetILGenerator().ILOffset == 0) return false;
            }
            return true;
        }
        catch (NotSupportedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private void EmitAdmission(NativeConstructorValueProducerSnapshot snapshot, EmittedErrorRuntime errors)
    {
        var il = AdmitType.GetILGenerator();
        var admitted = il.DefineLabel();
        EmitKnownOwners(il, snapshot, admitted);
        Refuse(il, errors);
        il.MarkLabel(admitted); il.Emit(OpCodes.Ret);
    }

    private void EmitKnownOwners(ILGenerator il, NativeConstructorValueProducerSnapshot snapshot, Label admitted)
    {
        foreach (var type in snapshot.Owners.Where(IsAdmissible)
            .Select(owner => owner.SourceGenericDefinition ?? owner.ExactTokenOwner).Distinct())
        {
            // A source token can carry its enclosing generic context. Its final
            // constructor value is the normalized definition; never transplant
            // an open TypeSpec into this nongeneric helper's body.
            EmitType(il, type); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brtrue, admitted);
        }
        var families = snapshot.Owners.Where(o => o.SourceGenericDefinition is not null)
            .Select(o => o.SourceGenericDefinition!).Distinct().ToArray();
        if (families.Length == 0) return;
        var foreign = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, foreign);
        // object.GetType is nonvirtual. The left-hand Type object is a trusted
        // literal; no caller-controlled Type metadata is read before this fence.
        NativeTypeIdentityEmitter.EmitRuntimeTypeFence(il, _types, () => il.Emit(OpCodes.Ldarg_0), foreign);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
        il.Emit(OpCodes.Brfalse, foreign);
        var definition = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Stloc, definition);
        foreach (var family in families)
        {
            EmitType(il, family); il.Emit(OpCodes.Ldloc, definition);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brtrue, admitted);
        }
        il.MarkLabel(foreign);
    }

    private void EmitActivation(NativeConstructorValueProducerSnapshot snapshot, EmittedErrorRuntime errors)
    {
        var il = Activate.GetILGenerator();
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AdmitType);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            EmitActivator(il); il.Emit(OpCodes.Ret);
            return;
        }
        foreach (var owner in snapshot.Owners.Where(o => IsAdmissible(o) && !o.IntrinsicHandled))
        {
            var next = il.DefineLabel();
            EmitType(il, owner.ExactTokenOwner); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brfalse, next);
            // Keep the loaded Activator binder and full arguments. A direct newobj
            // would change overload selection, exceptions and initialization order.
            EmitType(il, owner.ExactTokenOwner); il.Emit(OpCodes.Ldarg_1);
            EmitActivator(il); il.Emit(OpCodes.Ret);
            _literalActivationOwners.Add(owner.ExactTokenOwner);
            il.MarkLabel(next);
        }
        Refuse(il, errors);
    }

    private static bool IsAdmissible(NativeConstructorValueOwnerSnapshot owner) =>
        owner.ClosureKind == NativeConstructorValueClosureKind.UnprovedSourceGenericFamily ||
        owner.ClosureKind != NativeConstructorValueClosureKind.UnprovedValue &&
        (owner.IntrinsicHandled || owner.PublicConstructorsProved);
    private static bool Valid(MethodInfo method, Type result, Type[] parameters) => method.IsStatic &&
        !method.IsGenericMethod && (method.CallingConvention & CallingConventions.VarArgs) == 0 &&
        method.ReturnType == result && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters);
    private void EmitActivator(ILGenerator il) => il.Emit(OpCodes.Call,
        _types.GetMethod(typeof(Activator), "CreateInstance", _types.Type, _types.ObjectArray));
    private void EmitType(ILGenerator il, Type type)
    {
        il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
    }
    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignConstructorMessage);
    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native constructor-value emission is complete.");
    }
}
