using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeUnionArgumentConversion
{
    private readonly List<SourceParameterSite> _sourceSites = [];
    private readonly List<(ConstructorBuilder Writer, ILGenerator Body, int Getter, int Store, FieldBuilder Field)> _sourceWriters = [];
    private (MethodBuilder Factory, ConstructorBuilder Constructor, ILGenerator Body, int Offset)? _sourceFactory;
    private readonly List<string> _sourceBlockers = [];
    internal bool SourceUsesLegacy { get; private set; } = true;
    internal IReadOnlyList<string> SourceBlockers => _sourceBlockers.AsReadOnly();
    internal IReadOnlyList<Type> SourceTargets { get; private set; } = [];
    internal int SourceProducerCount => _sourceSites.Count;

    internal sealed record SourceParameterToken(MethodInfo Method, Type Owner, int MethodOffset, int OwnerOffset, int ResolveOffset);
    private sealed record SourceParameterSite(MethodBase Consumer, MethodInfo Original, SourceParameterToken Token, ILGenerator Body, int Offset);

    internal void RecordSourceConsumer(MethodBase consumer, MethodInfo original, SourceParameterToken token, ILGenerator body, int offset)
    {
        EnsureSourceMutable();
        if (!ReferenceEquals(consumer.Module, _module) || token.MethodOffset < 0 || token.OwnerOffset <= token.MethodOffset ||
            token.ResolveOffset <= token.OwnerOffset || offset <= token.ResolveOffset || body.ILOffset <= offset)
            throw new InvalidOperationException("Source parameter consumers require their actual token and construction operation.");
        _sourceSites.Add(new(consumer, original, token, body, offset));
    }

    internal void RecordSourceWriter(ConstructorBuilder constructor, ILGenerator body, int getter, int store, FieldBuilder field)
    {
        EnsureSourceMutable();
        if (!ReferenceEquals(constructor.Module, _module) || !ReferenceEquals(constructor.GetILGenerator(), body) ||
            constructor.DeclaringType != field.DeclaringType || field.IsStatic ||
            field.FieldType != _types.MakeArrayType(_types.ParameterInfo) || getter < 0 || store <= getter || body.ILOffset <= store ||
            _sourceWriters.Any(row => row.Writer == constructor))
            throw new InvalidOperationException("Source parameter caches require their actual GetParameters and array field store.");
        _sourceWriters.Add((constructor, body, getter, store, field));
    }

    internal void RecordSourceFactory(MethodBuilder factory, ConstructorBuilder constructor, ILGenerator body, int offset)
    {
        EnsureSourceMutable();
        if (_sourceFactory is not null || !ReferenceEquals(factory.Module, _module) || !ReferenceEquals(constructor.Module, _module) ||
            !ReferenceEquals(factory.GetILGenerator(), body) || offset < 0 || body.ILOffset <= offset)
            throw new InvalidOperationException("Source parameter factories require their actual cached constructor operation.");
        _sourceFactory = (factory, constructor, body, offset);
    }

    private void CompleteSourceConversion(IReadOnlyList<CompletedUnionConversion> conversions, EmittedRuntime? runtime,
        NativeClrAdmission.CompletedIngressProof? ingress, EmittedErrorRuntime errors)
    {
        var targets = new HashSet<Type>();
        var source = runtime?.NativeCallables?.SourceFunctions;
        if (source is null || !ReferenceEquals(source.ParameterConversion, this) || ingress is null || !ReferenceEquals(ingress.Module, _module))
            _sourceBlockers.Add("Actual source parameter ingress has not completed");
        if (runtime is not null && runtime.Deployment.Requirements != SharpTSRuntimeRequirements.None)
            _sourceBlockers.Add("A foreign managed runtime bridge remains enabled");
        if (source is not null)
        {
            if (!source.IsComplete || _sourceWriters.Count != 2 ||
                !_sourceWriters.Any(row => row.Writer == source.Constructor) || !_sourceWriters.Any(row => row.Writer == source.CachedConstructor))
                _sourceBlockers.Add("Actual source cached-parameter writers have not completed");
            foreach (var writer in _sourceWriters)
                if (writer.Writer.DeclaringType is not TypeBuilder owner || !owner.IsCreated() || writer.Body.ILOffset <= writer.Store)
                    throw new InvalidOperationException("Source cached-parameter writer bodies must complete before conversion.");
            if (_sourceFactory is not { } factory || factory.Factory != source.GetOrCreate || factory.Constructor != source.CachedConstructor ||
                factory.Factory.DeclaringType is not TypeBuilder factoryOwner || !factoryOwner.IsCreated() || factory.Body.ILOffset <= factory.Offset)
                _sourceBlockers.Add("Actual source cached factory has not completed");
        }
        if (_sourceSites.Count == 0) _sourceBlockers.Add("No actual source parameter producer has completed");
        foreach (var site in _sourceSites)
        {
            if (source is null || site.Consumer != source.Constructor && site.Consumer != source.CachedConstructor && site.Consumer != source.GetOrCreate)
                throw new InvalidOperationException("Source parameter records must reach the exact source constructor or factory.");
            var token = site.Token;
            if (!ReferenceEquals(token.Method.Module, _module) || !ReferenceEquals(token.Owner.Module, _module) ||
                site.Original.IsGenericMethod || token.Method.IsGenericMethod || token.Owner.IsGenericType ||
                !ReferenceEquals(site.Original, token.Method) || !ReferenceEquals(token.Method.DeclaringType, token.Owner) ||
                FunctionGenericParameterRegistry.ContainsUnboundType(token.Owner) ||
                token.Method.DeclaringType is not TypeBuilder owner || !owner.IsCreated() || site.Body.ILOffset <= site.Offset)
            {
                _sourceBlockers.Add("Unproved source parameter member or closure: " + token.Method.Name);
                continue;
            }
            foreach (var parameter in site.Original.GetParameters())
            {
                Type type = parameter.ParameterType;
                if (FunctionGenericParameterRegistry.ContainsUnboundType(type))
                {
                    _sourceBlockers.Add("Unproved source parameter type: " + type.Name);
                    continue;
                }
                if (!TryCompletedParameter(type, conversions, out var target))
                    _sourceBlockers.Add("Unproved source union parameter owner: " + type.FullName);
                else if (target is not null) targets.Add(target);
            }
        }
        SourceUsesLegacy = _sourceBlockers.Count != 0;
        SourceTargets = Array.AsReadOnly(targets.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        var selector = SelectParameterSource.GetILGenerator();
        // The cache always keeps the original method metadata, including its
        // parameter attributes. Eligibility requires the actual invocation
        // token to be this same nongeneric method and owner.
        selector.Emit(OpCodes.Ldarg_0); selector.Emit(OpCodes.Ret);
        if (SourceUsesLegacy) EmitOriginalConversion(ConvertCachedSource);
        else if (SourceTargets.Count == 0) UnionArgumentConversionEmission.EmitNoOwnedUnions(ConvertCachedSource.GetILGenerator(), _types, errors);
        else UnionArgumentConversionEmission.EmitOwned(ConvertCachedSource.GetILGenerator(), _types, errors, SourceTargets);
    }

    private void EnsureSourceMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Source union parameter conversion has completed.");
    }
}
