using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late constructor-body selection from the actual marker producers.</summary>
internal sealed class NativeConstructedBodyLookup
{
    internal const string ForeignEntryMessage = "This constructor entry is outside this native guest's compiler-owned entry/core pairs. Use --target dll or --target exe.";
    private const BindingFlags LegacyFlags = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly TypeBuilder _owner;
    private readonly TypeProvider _types;
    private readonly Dictionary<MethodBuilder, Pair> _pairs = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _blockers = [];

    private sealed record Pair(MethodBuilder Entry, MethodBuilder Core, bool Intrinsic,
        ILGenerator EntryBody, ILGenerator CoreBody);

    internal NativeConstructedBodyLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeConstructedBodyLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FindCore = _owner.DefineMethod("FindCore", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.MethodInfo, types.String]);
    }

    internal MethodBuilder FindCore { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0;
    internal int PairCount => _pairs.Count;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        if (!native || IsComplete || !ReferenceEquals(_owner.Module, module) ||
            !FindCore.IsStatic || FindCore.IsGenericMethod || FindCore.ReturnType != _types.MethodInfo ||
            (FindCore.CallingConvention & CallingConventions.VarArgs) != 0 ||
            !FindCore.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.MethodInfo, _types.String]))
            throw new InvalidOperationException("Native constructor-body lookup requires this native output's exact declaration.");
    }

    internal void RecordSourcePair(MethodBuilder entry, MethodBuilder core) => Record(entry, core, false);
    internal void RecordIntrinsicPair(MethodBuilder entry, MethodBuilder core) => Record(entry, core, true);

    private void Record(MethodBuilder entry, MethodBuilder core, bool intrinsic)
    {
        EnsureMutable();
        if (!ReferenceEquals(entry.Module, _owner.Module) || !ReferenceEquals(core.Module, _owner.Module) ||
            !ReferenceEquals(entry.DeclaringType, core.DeclaringType) || entry.DeclaringType is not TypeBuilder owner ||
            owner.IsCreated() || entry.IsStatic != core.IsStatic || entry == core ||
            ((entry.CallingConvention | core.CallingConvention) & CallingConventions.VarArgs) != 0 ||
            (entry.GetGenericArguments() ?? []).Length != (core.GetGenericArguments() ?? []).Length ||
            (intrinsic ? entry.IsGenericMethod || !core.IsPublic : !core.IsAssembly) ||
            _pairs.ContainsKey(entry) || _pairs.Values.Any(pair => ReferenceEquals(pair.Core, core)))
            throw new InvalidOperationException("Native constructor-body records require exact owned marker-producer entry/core pairs.");
        _pairs.Add(entry, new(entry, core, intrinsic, entry.GetILGenerator(), core.GetILGenerator()));
    }

    internal void CompleteEmission(FunctionGenericParameterRegistry registry, NativeGenericMethodNormalization generics,
        IReadOnlyDictionary<MethodBuilder, MethodBuilder> sourceBodies, EmittedFunctionAttributesRuntime attributes,
        EmittedErrorRuntime errors)
    {
        EnsureMutable();
        if (!registry.IsComplete || !generics.IsComplete ||
            !ReferenceEquals(generics.Normalize.Module, _owner.Module) ||
            !ReferenceEquals(attributes.NewTargetCoreType.Module, _owner.Module) ||
            !ReferenceEquals(errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native constructor-body lookup requires completed metadata from this exact output.");
        var sourcePairs = _pairs.Values.Where(pair => !pair.Intrinsic).ToArray();
        if (sourcePairs.Length != sourceBodies.Count || sourcePairs.Any(pair =>
            !sourceBodies.TryGetValue(pair.Entry, out var body) || !ReferenceEquals(body, pair.Core)))
            throw new InvalidOperationException("Native constructor-body records do not match the completed source body inventory.");

        foreach (var pair in _pairs.Values)
        {
            if (pair.EntryBody.ILOffset == 0 || pair.CoreBody.ILOffset == 0)
                throw new InvalidOperationException("Native constructor-body lookup requires both producer bodies to be emitted.");
            if (!pair.Intrinsic) _ = registry.Require(pair.Core);
            if (!ProvePair(pair, generics))
                _blockers.Add($"Exact constructor entry/core ABI or canonical metadata is unproved: {pair.Entry.Name}");
        }
        var il = FindCore.GetILGenerator();
        foreach (var pair in _pairs.Values)
        {
            var next = il.DefineLabel();
            var selected = il.DefineLabel();
            EmitMatch(il, pair.Entry, selected);
            if (generics.TryGetCanonicalTarget(pair.Entry, out var target)) EmitMatch(il, target, selected);
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(selected);
            if (UsesLegacy)
            {
                // Ownership came from a real literal MethodInfo above. Keep the
                // entire old name/flags operation for an unproved inventory.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(typeof(MemberInfo), "DeclaringType"));
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)LegacyFlags);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
            }
            else _types.EmitLoadMethodInfo(il, pair.Core);
            il.Emit(OpCodes.Ret); il.MarkLabel(next);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignEntryMessage);
        _owner.CreateType(); IsComplete = true;
    }

    private bool ProvePair(Pair pair, NativeGenericMethodNormalization generics)
    {
        var entry = pair.Entry; var core = pair.Core;
        if (FunctionGenericParameterRegistry.ContainsUnboundType(entry.DeclaringType!) ||
            FunctionGenericParameterRegistry.ContainsUnboundType(entry.ReturnType) ||
            entry.GetParameters().Any(parameter => FunctionGenericParameterRegistry.ContainsUnboundType(parameter.ParameterType)) ||
            core.ReturnType != entry.ReturnType || !core.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(entry.GetParameters().Select(parameter => parameter.ParameterType).Append(_types.Object)))
            return false;
        if (!entry.IsGenericMethodDefinition) return !core.IsGenericMethod;
        return generics.TryGetCanonicalTarget(entry, out var entryTarget) &&
            generics.TryGetCanonicalTarget(core, out var coreTarget) &&
            entryTarget.GetGenericArguments().SequenceEqual(coreTarget.GetGenericArguments());
    }

    private void EmitMatch(ILGenerator il, MethodInfo literal, Label selected)
    {
        _types.EmitLoadMethodInfo(il, literal);
        il.Emit(OpCodes.Ldarg_0);
        // The trusted real method owns Equals, never the caller's MethodInfo.
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Equals", _types.Object));
        il.Emit(OpCodes.Brtrue, selected);
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native constructor-body lookup emission is complete.");
    }
}