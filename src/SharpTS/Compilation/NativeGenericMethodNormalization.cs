using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late native normalization backed by completed owned method-generic declarations.</summary>
internal sealed class NativeGenericMethodNormalization
{
    private readonly TypeBuilder _owner;
    private readonly TypeProvider _types;
    private readonly Dictionary<MethodBuilder, GenericTypeParameterBuilder[]> _definitions = new(ReferenceEqualityComparer.Instance);

    internal NativeGenericMethodNormalization(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeGenericMethodNormalization",
            System.Reflection.TypeAttributes.NotPublic | System.Reflection.TypeAttributes.Abstract |
            System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.BeforeFieldInit);
        Normalize = _owner.DefineMethod("Normalize", System.Reflection.MethodAttributes.Assembly | System.Reflection.MethodAttributes.Static,
            types.MethodInfo, [types.MethodInfo]);
    }

    internal MethodBuilder Normalize { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal int GenericDefinitionCount => _definitions.Count;
    internal IReadOnlyCollection<MethodBuilder> Definitions => _definitions.Keys;

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

    internal void CompleteEmission(FunctionGenericParameterRegistry registry,
        EmittedFunctionAttributesRuntime attributes, EmittedErrorRuntime errors)
    {
        EnsureMutable();
        var completed = registry.RequireCompletedGenericDefinitions();
        if (completed.Count != _definitions.Count || completed.Any(pair =>
            !_definitions.TryGetValue(pair.Key, out var recorded) || recorded.Length != pair.Value.Count ||
            recorded.Where((parameter, index) => !ReferenceEquals(parameter, pair.Value[index])).Any()))
            throw new InvalidOperationException("Native generic-method certificate does not match completed function metadata.");
        UsesLegacy = completed.Count != 0;
        var il = Normalize.GetILGenerator();
        if (UsesLegacy) ErasedSourceInvocationEmission.EmitLegacy(il, _types, attributes);
        else ErasedSourceInvocationEmission.EmitNoOwnedGenerics(il, _types, attributes, errors);
        _owner.CreateType();
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native generic-method normalization emission is complete.");
    }
}
