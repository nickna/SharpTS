using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late native normalization backed by completed owned method-generic declarations.</summary>
internal sealed class NativeGenericMethodNormalization
{
    [Flags]
    internal enum SourceMarker { PadUndefined = 1, FunctionLength = 2, FunctionName = 4 }
    internal const string ConstructedBoundaryMessage = "This generic constructor entry is outside this native guest's compiler-owned canonical entry/core pairs. Use --target dll or --target exe.";
    private readonly TypeBuilder _owner;
    private readonly TypeProvider _types;
    private readonly Dictionary<MethodBuilder, GenericTypeParameterBuilder[]> _definitions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBuilder, SourceMarker> _markers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBuilder, MethodBuilder> _constructs = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _blockers = [];
    private IReadOnlyDictionary<MethodBuilder, MethodInfo> _canonicalTargets = new Dictionary<MethodBuilder, MethodInfo>();

    internal NativeGenericMethodNormalization(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeGenericMethodNormalization",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Normalize = _owner.DefineMethod("Normalize", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.MethodInfo]);
        ResolveConstructed = _owner.DefineMethod("ResolveConstructed", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.MethodInfo, types.MethodInfo]);
    }

    internal MethodBuilder Normalize { get; }
    internal MethodBuilder ResolveConstructed { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0;
    internal int GenericDefinitionCount => _definitions.Count;
    internal int NormalizedDefinitionCount { get; private set; }
    internal int ConstructedPairCount { get; private set; }
    internal IReadOnlyCollection<MethodBuilder> Definitions => _definitions.Keys;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();

    internal bool TryGetCanonicalTarget(MethodBuilder definition, out MethodInfo target)
    {
        if (!IsComplete) throw new InvalidOperationException("Native generic-method targets are incomplete.");
        return _canonicalTargets.TryGetValue(definition, out target!);
    }

    internal void RecordDefinition(MethodBuilder method, GenericTypeParameterBuilder[] parameters)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(parameters);
        var declared = method.GetGenericArguments() ?? Type.EmptyTypes;
        if (method.Module.ModuleVersionId != _owner.Module.ModuleVersionId || !method.IsGenericMethodDefinition ||
            parameters.Length == 0 || parameters.Length != declared.Length ||
            parameters.Where((parameter, index) => parameter is null || parameter.GenericParameterPosition != index ||
                !ReferenceEquals(parameter, declared[index])).Any())
            throw new InvalidOperationException("Native generic-method records must match this output's exact definition and parameter order.");
        if (!_definitions.TryAdd(method, parameters.ToArray()))
            throw new InvalidOperationException("Native generic-method definition is already recorded.");
    }

    // Called immediately after each actual source-marker SetCustomAttribute. Persisted
    // MethodBuilder cannot enumerate custom attributes; saved-PE tests verify these hooks.
    internal void RecordSourceMarker(MethodBuilder method, SourceMarker marker)
    {
        EnsureMutable();
        if (marker is not (SourceMarker.PadUndefined or SourceMarker.FunctionLength or SourceMarker.FunctionName))
            throw new ArgumentOutOfRangeException(nameof(marker));
        if (_definitions.ContainsKey(method)) _markers[method] = _markers.GetValueOrDefault(method) | marker;
    }

    internal void RecordConstructedBody(MethodBuilder entry, MethodBuilder core)
    {
        EnsureMutable();
        if (!_definitions.ContainsKey(entry) || !_definitions.ContainsKey(core) || entry.DeclaringType != core.DeclaringType ||
            entry.IsStatic != core.IsStatic || !core.IsAssembly || _constructs.ContainsValue(core) || !_constructs.TryAdd(entry, core))
            throw new InvalidOperationException("Native generic constructor records require exact owned entry/core definitions.");
    }

    internal void CompleteEmission(FunctionGenericParameterRegistry registry,
        EmittedFunctionAttributesRuntime attributes, EmittedErrorRuntime errors)
    {
        EnsureMutable();
        if (new[] { attributes.FunctionNameType, attributes.FunctionLengthType, attributes.PadUndefinedType, errors.Type }
            .Any(type => type.Module.ModuleVersionId != _owner.Module.ModuleVersionId))
            throw new InvalidOperationException("Native generic-method completion requires this output's marker and error metadata.");
        var completed = registry.RequireCompletedGenericDefinitions();
        if (completed.Count != _definitions.Count || completed.Any(pair =>
            !_definitions.TryGetValue(pair.Key, out var recorded) || recorded.Length != pair.Value.Count ||
            recorded.Where((parameter, index) => !ReferenceEquals(parameter, pair.Value[index])).Any()))
            throw new InvalidOperationException("Native generic-method certificate does not match completed function metadata.");
        var erased = registry.RequireCompletedErasedValueInvocationTargets();
        var targets = new Dictionary<MethodBuilder, MethodInfo>(ReferenceEqualityComparer.Instance);
        var cores = new HashSet<MethodBuilder>(_constructs.Values, ReferenceEqualityComparer.Instance);
        foreach (var definition in _definitions.Keys)
        {
            if (_markers.GetValueOrDefault(definition) != (SourceMarker.PadUndefined | SourceMarker.FunctionLength | SourceMarker.FunctionName)
                && !cores.Contains(definition))
            {
                Block(definition, "Source markers or constructor-core producer are not proved");
                continue;
            }
            if (!TryProveTarget(definition, erased.GetValueOrDefault(definition), out var target))
            {
                Block(definition, "Canonical target does not match every legacy erased-invocation condition");
                continue;
            }
            targets.Add(definition, target);
        }
        _canonicalTargets = new System.Collections.ObjectModel.ReadOnlyDictionary<MethodBuilder, MethodInfo>(targets);
        EmitNormalize(targets, cores, attributes, errors);
        EmitConstructed(targets, errors);
        _owner.CreateType();
        IsComplete = true;
    }

    private bool TryProveTarget(MethodBuilder definition, MethodInfo? target, out MethodInfo proved)
    {
        proved = null!;
        if (target is null || FunctionGenericParameterRegistry.ContainsUnboundType(definition.DeclaringType!) ||
            FunctionGenericParameterRegistry.ContainsUnboundType(definition.ReturnType) ||
            definition.GetParameters().Any(parameter => FunctionGenericParameterRegistry.ContainsUnboundType(parameter.ParameterType)) ||
            !target.IsGenericMethod || target.IsGenericMethodDefinition ||
            !ReferenceEquals(target.GetGenericMethodDefinition(), definition)) return false;
        var selected = new List<Type>();
        foreach (var parameter in _definitions[definition])
        {
            // Exactly EmitLegacy's selection: no special attributes; zero
            // constraints => object; one closed constraint => that constraint.
            if (parameter.GenericParameterAttributes != GenericParameterAttributes.None) return false;
            var constraints = parameter.GetGenericParameterConstraints();
            if (constraints.Length > 1 || constraints.Any(FunctionGenericParameterRegistry.ContainsUnboundType)) return false;
            selected.Add(constraints.Length == 0 ? _types.Object : constraints[0]);
        }
        if (!target.GetGenericArguments().SequenceEqual(selected) ||
            target.GetGenericArguments().Any(FunctionGenericParameterRegistry.ContainsUnboundType)) return false;
        proved = target;
        return true;
    }

    private void EmitNormalize(Dictionary<MethodBuilder, MethodInfo> targets, HashSet<MethodBuilder> cores,
        EmittedFunctionAttributesRuntime attributes, EmittedErrorRuntime errors)
    {
        var il = Normalize.GetILGenerator();
        var legacy = il.DefineLabel();
        foreach (var definition in _definitions.Keys.Where(definition => !cores.Contains(definition)))
        {
            var next = il.DefineLabel();
            EmitExactMatch(il, definition, 0, next);
            if (targets.TryGetValue(definition, out var target))
            {
                _types.EmitLoadMethodInfo(il, target);
                il.Emit(OpCodes.Ret);
                NormalizedDefinitionCount++;
            }
            else il.Emit(OpCodes.Br, legacy);
            il.MarkLabel(next);
        }
        // A foreign source marker cannot become an ownership certificate merely
        // because another definition in this output needs legacy handling.
        ErasedSourceInvocationEmission.EmitNoOwnedGenerics(il, _types, attributes, errors);
        if (_definitions.Keys.Any(definition => !cores.Contains(definition) && !targets.ContainsKey(definition)))
        {
            il.MarkLabel(legacy);
            ErasedSourceInvocationEmission.EmitLegacy(il, _types, attributes);
        }
    }

    private void EmitConstructed(Dictionary<MethodBuilder, MethodInfo> targets, EmittedErrorRuntime errors)
    {
        var il = ResolveConstructed.GetILGenerator();
        foreach (var (entry, core) in _constructs)
        {
            var next = il.DefineLabel();
            EmitExactMatch(il, core, 0, next);
            if (targets.TryGetValue(entry, out var entryTarget) && targets.TryGetValue(core, out var coreTarget) &&
                entryTarget.GetGenericArguments().SequenceEqual(coreTarget.GetGenericArguments()) &&
                core.ReturnType == entry.ReturnType && core.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(entry.GetParameters().Select(parameter => parameter.ParameterType).Append(_types.Object)))
            {
                var accepted = il.DefineLabel();
                EmitExactMatch(il, entry, 1, accepted, branchWhenEqual: true);
                EmitExactMatch(il, entryTarget, 1, next);
                il.MarkLabel(accepted);
                _types.EmitLoadMethodInfo(il, coreTarget);
                il.Emit(OpCodes.Ret);
                ConstructedPairCount++;
            }
            else
            {
                Block(core, "Exact constructor entry/core ABI and canonical arguments are not proved");
                // Only an exact unproved owned core can retain dynamic closure.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.MethodInfo, "GetGenericArguments"));
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "MakeGenericMethod", _types.MakeArrayType(_types.Type)));
                il.Emit(OpCodes.Ret);
            }
            il.MarkLabel(next);
        }
        // A completed but unclassified owned definition is an explicit blocker,
        // not evidence of a foreign core. Keep its entire previous operation.
        foreach (var definition in _definitions.Keys.Where(definition =>
            !_constructs.ContainsValue(definition) && !targets.ContainsKey(definition)))
        {
            var next = il.DefineLabel();
            EmitExactMatch(il, definition, 0, next);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.MethodInfo, "GetGenericArguments"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "MakeGenericMethod", _types.MakeArrayType(_types.Type)));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ConstructedBoundaryMessage);
    }

    private void EmitExactMatch(ILGenerator il, MethodInfo literal, int argument, Label branch, bool branchWhenEqual = false)
    {
        _types.EmitLoadMethodInfo(il, literal);
        il.Emit(OpCodes.Ldarg, argument);
        // The real literal owns Equals; caller-controlled MethodInfo overrides
        // cannot select a compiler-owned definition or MethodSpec.
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Equals", _types.Object));
        il.Emit(branchWhenEqual ? OpCodes.Brtrue : OpCodes.Brfalse, branch);
    }

    private void Block(MethodBuilder definition, string reason) => _blockers.Add($"{reason}: {definition.Name}");
    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native generic-method normalization emission is complete.");
    }
}
