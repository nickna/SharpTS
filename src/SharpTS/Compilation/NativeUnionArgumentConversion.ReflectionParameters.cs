using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeUnionArgumentConversion
{
    internal ConstructorBuilder? ReflectionConstructor { get; set; }
    internal ConstructorBuilder? ReflectionCachedConstructor { get; set; }
    internal MethodBuilder? ReflectionGetOrCreate { get; set; }
    internal MethodBuilder? ReflectionSymbolFactory { get; set; }
    private (MethodBuilder Method, ILGenerator Body, EmittedNativeMethodLookupRuntime Lookup, int SelectOffset, int EnumerateOffset, int Length)? _reflectionSelector;
    private ConstructorBuilder? _originalConstructor;
    private ConstructorBuilder? _originalCachedConstructor;
    private MethodBuilder? _originalFactory;

    internal void BindOriginalConstructors(ConstructorBuilder constructor, ConstructorBuilder cached)
    {
        EnsureSourceMutable();
        if (_originalConstructor is not null || !ReferenceEquals(constructor.Module, _module) ||
            !ReferenceEquals(cached.Module, _module) || !ReferenceEquals(constructor.DeclaringType, cached.DeclaringType) ||
            constructor.IsStatic || cached.IsStatic || !constructor.IsPublic || !cached.IsPublic ||
            !constructor.GetParameters().Select(p => p.ParameterType).SequenceEqual([_types.Object, _types.MethodInfo]) ||
            !cached.GetParameters().Select(p => p.ParameterType).SequenceEqual([_types.Object, _types.MethodInfo, _types.String, _types.Int32]))
            throw new InvalidOperationException("Reflected transport requires the original exact public constructor ABI.");
        _originalConstructor = constructor; _originalCachedConstructor = cached;
    }

    internal void BindOriginalFactory(MethodBuilder factory)
    {
        EnsureSourceMutable();
        if (_originalFactory is not null || _originalConstructor is null || !ReferenceEquals(factory.Module, _module) ||
            !ReferenceEquals(factory.DeclaringType, _originalConstructor.DeclaringType) || !factory.IsPublic || !factory.IsStatic ||
            factory.ReturnType != _originalConstructor.DeclaringType ||
            !factory.GetParameters().Select(p => p.ParameterType).SequenceEqual([_types.MethodInfo, _types.String, _types.Int32]))
            throw new InvalidOperationException("Reflected transport requires the original exact cached factory ABI.");
        _originalFactory = factory;
    }

    internal bool MatchesOriginalConstructor(ConstructorInfo constructor, bool cached) =>
        ReferenceEquals(constructor, cached ? _originalCachedConstructor : _originalConstructor);
    internal bool MatchesOriginalFactory(MethodInfo factory) => ReferenceEquals(factory, _originalFactory);

    private readonly List<ReflectionMethodEmission> _reflectionMethods = [];
    private readonly List<(MethodBase Consumer, ReflectionMethodEmission Producer, ILGenerator Body, int Offset)> _reflectionConsumers = [];
    private readonly List<(ConstructorBuilder Writer, ILGenerator Body, int Getter, int Store, FieldBuilder Field)> _reflectionWriters = [];
    private (MethodBuilder Factory, ILGenerator Body, int Offset)? _reflectionFactory;
    private readonly List<string> _reflectionBlockers = [];
    internal bool ReflectionUsesLegacy { get; private set; } = true;
    internal IReadOnlyList<string> ReflectionBlockers => _reflectionBlockers.AsReadOnly();
    internal IReadOnlyList<Type> ReflectionTargets { get; private set; } = [];
    internal IReadOnlyList<MethodInfo> ReflectionMembers { get; private set; } = [];

    internal sealed record ReflectionMethodEmission(ILGenerator Body, int Offset, MethodInfo Producer,
        MethodInfo? Literal, LocalBuilder? Local, bool Symbol = false, FrameworkParameterToken? Framework = null, bool ArrayInstaller = false);

    internal void RecordReflectionSelector(MethodBuilder method, ILGenerator body, EmittedNativeMethodLookupRuntime lookup,
        int selectOffset, int enumerateOffset)
    {
        EnsureSourceMutable();
        if (_reflectionSelector is not null || !ReferenceEquals(method.Module, _module) || !ReferenceEquals(lookup.Module, _module) ||
            !ReferenceEquals(method.GetILGenerator(), body) || selectOffset < 0 || enumerateOffset <= selectOffset || body.ILOffset <= enumerateOffset)
            throw new InvalidOperationException("Reflected parameter selection requires its actual select/enumerate operations.");
        _reflectionSelector = (method, body, lookup, selectOffset, enumerateOffset, body.ILOffset);
    }

    internal ReflectionMethodEmission EmitReflectionLiteral(ILGenerator body, MethodInfo method, bool singleHandle = false)
    {
        EnsureSourceMutable();
        int offset = body.ILOffset;
        if (singleHandle) _types.EmitLoadMethodInfoViaHandle(body, method);
        else _types.EmitLoadMethodInfo(body, method);
        var emission = new ReflectionMethodEmission(body, offset, method, method, null);
        _reflectionMethods.Add(emission);
        return emission;
    }

    internal void EmitReflectionQuery(ILGenerator body, MethodBuilder selector, LocalBuilder local)
    {
        EnsureSourceMutable();
        int offset = body.ILOffset;
        body.Emit(OpCodes.Call, selector);
        body.Emit(OpCodes.Stloc, local);
        _reflectionMethods.Add(new(body, offset, selector, null, local));
    }

    internal void EmitSymbolParameterQuery(ILGenerator body, MethodBuilder selector, LocalBuilder local)
    {
        EnsureSourceMutable(); int offset = body.ILOffset;
        body.Emit(OpCodes.Call, selector); body.Emit(OpCodes.Stloc, local);
        _reflectionMethods.Add(new(body, offset, selector, null, local, Symbol: true));
    }

    internal ReflectionMethodEmission SymbolParameterQuery(ILGenerator body, LocalBuilder local) =>
        _reflectionMethods.First(row => row.Symbol && ReferenceEquals(row.Body, body) && row.Local == local);

    internal ReflectionMethodEmission RecordSymbolFactoryParameter(ILGenerator body, MethodBuilder factory, int offset)
    {
        EnsureSourceMutable();
        if (factory != ReflectionSymbolFactory || !ReferenceEquals(factory.GetILGenerator(), body) || offset < 0 || body.ILOffset <= offset)
            throw new InvalidOperationException("Symbol cached parameters require the actual selected original factory argument.");
        var emission = new ReflectionMethodEmission(body, offset, factory, null, null, Symbol: true);
        _reflectionMethods.Add(emission); return emission;
    }

    internal void EmitSymbolParameterFactory(ILGenerator body, ReflectionMethodEmission producer)
    {
        EnsureSourceMutable();
        if (!_reflectionMethods.Contains(producer) || !producer.Symbol || !ReferenceEquals(producer.Body, body) || ReflectionSymbolFactory is null)
            throw new InvalidOperationException("Symbol wrappers require their actual selected method producer.");
        int offset = body.ILOffset; body.Emit(OpCodes.Call, ReflectionSymbolFactory);
        _reflectionConsumers.Add((ReflectionSymbolFactory, producer, body, offset));
    }

    internal ReflectionMethodEmission ReflectionQuery(ILGenerator body, LocalBuilder local) =>
        _reflectionMethods.Single(row => ReferenceEquals(row.Body, body) && row.Local == local);

    internal void EmitReflectionConstructor(ILGenerator body, ReflectionMethodEmission producer, bool cached, NativePrototypeFieldsLookup? carriers = null)
    {
        EnsureSourceMutable();
        if (!_reflectionMethods.Contains(producer) || !ReferenceEquals(producer.Body, body) || body.ILOffset <= producer.Offset)
            throw new InvalidOperationException("Reflected cached parameters require their actual selected method producer.");
        body.Emit(OpCodes.Ldc_I4_1);
        int offset = body.ILOffset;
        var constructor = cached ? ReflectionCachedConstructor : ReflectionConstructor;
        if (constructor is null) throw new InvalidOperationException("Reflected parameter constructors have not been declared.");
        NativePrototypeFieldsLookup.EmitCarrierCreate(carriers, body, constructor);
        _reflectionConsumers.Add((constructor, producer, body, offset));
    }

    internal void EmitReflectionFactory(ILGenerator body, ReflectionMethodEmission producer)
    {
        EnsureSourceMutable();
        if (!_reflectionMethods.Contains(producer) || !ReferenceEquals(producer.Body, body) || body.ILOffset <= producer.Offset || ReflectionGetOrCreate is null)
            throw new InvalidOperationException("Reflected cached parameters require their actual factory and selected method.");
        body.Emit(OpCodes.Ldc_I4_1);
        int offset = body.ILOffset;
        body.Emit(OpCodes.Call, ReflectionGetOrCreate);
        _reflectionConsumers.Add((ReflectionGetOrCreate, producer, body, offset));
    }

    internal void RecordReflectionWriter(ConstructorBuilder constructor, ILGenerator body, int getter, int store, FieldBuilder field)
    {
        EnsureSourceMutable();
        if (!ReferenceEquals(constructor.Module, _module) || !ReferenceEquals(constructor.GetILGenerator(), body) ||
            constructor.DeclaringType != field.DeclaringType || field.IsStatic ||
            field.FieldType != _types.MakeArrayType(_types.ParameterInfo) || getter < 0 || store <= getter || body.ILOffset <= store ||
            _reflectionWriters.Any(row => row.Writer == constructor))
            throw new InvalidOperationException("Reflected parameter caches require their actual original GetParameters and field store.");
        _reflectionWriters.Add((constructor, body, getter, store, field));
    }

    internal void RecordReflectionFactory(MethodBuilder factory, ILGenerator body, int offset)
    {
        EnsureSourceMutable();
        if (_reflectionFactory is not null || !ReferenceEquals(factory.Module, _module) || !ReferenceEquals(factory.GetILGenerator(), body) ||
            factory != ReflectionGetOrCreate || offset < 0 || body.ILOffset <= offset)
            throw new InvalidOperationException("Reflected parameter factories require their actual cached constructor operation.");
        _reflectionFactory = (factory, body, offset);
    }

    private void CompleteReflectionConversion(IReadOnlyList<CompletedUnionConversion> conversions, EmittedRuntime? runtime,
        NativeClrAdmission.CompletedIngressProof? ingress, NativeMethodLookupCatalog? methods, NativeOwnerClosure? ownerClosure, EmittedErrorRuntime errors)
    {
        if (runtime is null || ingress is null || !ReferenceEquals(ingress.Module, _module) ||
            runtime.Deployment.Requirements != SharpTSRuntimeRequirements.None)
            _reflectionBlockers.Add("Actual reflected parameter ingress has not completed");
        if (_reflectionWriters.Count != 2 || !_reflectionWriters.Any(row => row.Writer == ReflectionConstructor) ||
            !_reflectionWriters.Any(row => row.Writer == ReflectionCachedConstructor))
            _reflectionBlockers.Add("Actual reflected parameter cache writers have not completed");
        foreach (var writer in _reflectionWriters)
            if (writer.Writer.DeclaringType is not TypeBuilder owner || !owner.IsCreated() || writer.Body.ILOffset <= writer.Store)
                throw new InvalidOperationException("Reflected parameter writer bodies must complete before conversion.");
        if (_reflectionFactory is not { } factory || factory.Factory != ReflectionGetOrCreate ||
            factory.Factory.DeclaringType is not TypeBuilder factoryOwner || !factoryOwner.IsCreated() || factory.Body.ILOffset <= factory.Offset)
            _reflectionBlockers.Add("Actual reflected cached factory has not completed");
        var members = new HashSet<MethodInfo>();
        bool needsQueries = _reflectionConsumers.Any(row => row.Producer.Literal is null && !row.Producer.Symbol && !row.Producer.ArrayInstaller);
        if (needsQueries)
        {
            if (methods?.IsComplete != true || methods.CompletedQueries.UsesLegacy || methods.CompletedQueries.Blockers.Count != 0 ||
                !ReferenceEquals(methods.CompletedQueries.Module, _module) || runtime?.ReflectedMethods.NativeMethodLookup != methods.Handles)
                _reflectionBlockers.Add("Actual reflected selected-member query closure has not completed");
            else
            {
                if (_reflectionSelector is not { } selector || selector.Method != runtime.ReflectedMethods.FindMethod ||
                    !ReferenceEquals(selector.Lookup, methods.Handles) || selector.Body.ILOffset != selector.Length ||
                    selector.Method.DeclaringType is not TypeBuilder selectorOwner || !selectorOwner.IsCreated())
                    _reflectionBlockers.Add("Actual reflected ambiguity-preserving selector has not completed");
                var snapshot = methods.CompletedQueries;
                if (snapshot.Rows.Count == 0 || !snapshot.Rows.Any(row => row.Producer == methods.Handles.Select) ||
                    !snapshot.Rows.Any(row => row.Producer == methods.Handles.Enumerate))
                    _reflectionBlockers.Add("Actual reflected selected-member query rows are missing");
                foreach (var query in snapshot.Rows)
                {
                    if (query.CallOffset < 0 || query.CompletedBodyLength <= query.CallOffset)
                        throw new InvalidOperationException("Reflected parameter query receipts require completed operation bodies.");
                    foreach (var member in query.Members) members.Add(member);
                }
            }
        }
        CompleteArrayInstallerParameters(members);
        bool needsSymbols = _reflectionConsumers.Any(row => row.Producer.Symbol);
        if (needsSymbols)
        {
            if (ownerClosure?.IsComplete != true || ownerClosure.CompletedSymbols.UsesLegacy ||
                !ReferenceEquals(ownerClosure.CompletedSymbols.Module, _module))
                _reflectionBlockers.Add("Actual symbol parameter registration closure has not completed");
            else
            {
                foreach (var registration in ownerClosure.CompletedSymbols.Rows)
                {
                    if (registration.Owner.IsGenericType || registration.Method.IsGenericMethod ||
                        !ReferenceEquals(registration.Owner.Module, _module) || !registration.Owner.IsCreated() ||
                        !ReferenceEquals(registration.Method.DeclaringType, registration.Owner) ||
                        registration.Register != runtime!.SymbolAccessors.RegisterMethod ||
                        registration.MethodTokenOffset <= registration.OwnerTokenOffset || registration.CallOffset <= registration.MethodTokenOffset ||
                        registration.CompletedBodyLength <= registration.CallOffset || registration.RegisteredBodyLength == 0 ||
                        registration.Method.GetILGenerator().ILOffset != registration.RegisteredBodyLength)
                        _reflectionBlockers.Add("Unproved actual symbol parameter registration: " + registration.Method.Name);
                    else members.Add(registration.Method);
                }
            }
            foreach (var producer in _reflectionMethods.Where(row => row.Symbol))
                if (producer.Producer != runtime?.SymbolAccessors.FindMethod && producer.Producer != runtime?.SymbolAccessors.FindOwnMethod &&
                    producer.Producer != ReflectionSymbolFactory || producer.Body.ILOffset <= producer.Offset ||
                    producer.Local is null && producer.Producer != ReflectionSymbolFactory ||
                    producer.Local is not null && producer.Local.LocalType != _types.Object)
                    _reflectionBlockers.Add("Unproved actual symbol parameter selector or factory input");
        }
        foreach (var site in _reflectionConsumers)
        {
            if (site.Consumer != ReflectionConstructor && site.Consumer != ReflectionCachedConstructor && site.Consumer != ReflectionGetOrCreate && site.Consumer != ReflectionSymbolFactory ||
                !ReferenceEquals(site.Body, site.Producer.Body) || site.Offset <= site.Producer.Offset || site.Body.ILOffset <= site.Offset)
                throw new InvalidOperationException("Reflected parameter consumers require their exact producer and completed construction.");
            var producer = site.Producer;
            if (producer.Framework is { } framework)
            {
                if (!ValidListAddParameters(framework, producer.Body))
                    _reflectionBlockers.Add("Unproved actual framework-negative parameter token");
                else members.Add(framework.Member);
            }
            else if (producer.Literal is { } literal)
            {
                if (literal is not MethodBuilder method || !ReferenceEquals(method.Module, _module) ||
                    method.DeclaringType is not TypeBuilder owner || !owner.IsCreated() ||
                    !(method.IsAbstract && owner.IsInterface && method.GetParameters().Length == 0) && method.GetILGenerator().ILOffset == 0)
                    _reflectionBlockers.Add("Unproved literal reflected parameter member: " + literal.Name);
                else members.Add(literal);
            }
            else if (!producer.Symbol && !producer.ArrayInstaller && (producer.Producer != runtime?.ReflectedMethods.FindMethod || producer.Local?.LocalType != _types.MethodInfo))
                _reflectionBlockers.Add("Unproved reflected parameter selector");
        }
        var targets = new HashSet<Type>();
        foreach (var member in members)
        {
            if (member.DeclaringType is null || FunctionGenericParameterRegistry.ContainsUnboundType(member.DeclaringType) ||
                member.IsGenericMethod && ReferenceEquals(member.Module, _module))
            {
                _reflectionBlockers.Add("Unproved reflected parameter member closure: " + member.Name);
                continue;
            }
            foreach (var parameter in member.GetParameters())
            {
                Type type = parameter.ParameterType;
                if (!TryCompletedParameter(type, conversions, out var target))
                    _reflectionBlockers.Add("Unproved reflected union parameter owner: " + member.Name + ": " + type.FullName);
                else if (target is not null) targets.Add(target);
            }
        }
        ReflectionUsesLegacy = _reflectionBlockers.Count != 0;
        ReflectionTargets = Array.AsReadOnly(targets.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        ReflectionMembers = Array.AsReadOnly(members.ToArray());
        if (ReflectionUsesLegacy) EmitOriginalConversion(ConvertCachedReflection);
        else if (ReflectionTargets.Count == 0) UnionArgumentConversionEmission.EmitNoOwnedUnions(ConvertCachedReflection.GetILGenerator(), _types, errors);
        else UnionArgumentConversionEmission.EmitOwned(ConvertCachedReflection.GetILGenerator(), _types, errors, ReflectionTargets);
    }
}
