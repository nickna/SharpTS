using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Receiver-adapter queries backed by actual constructor and class-template value emission.</summary>
internal sealed class NativeReceiverInitializerLookup
{
    internal const string ForeignOwnerMessage = "This receiver-initializer Type is outside this native guest's actual constructor and class-template producers. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly Dictionary<TypeBuilder, Adapter> _adapters = new(ReferenceEqualityComparer.Instance);
    private readonly List<TemplateEmission> _templates = [];
    private readonly List<string> _blockers = [];
    private sealed record Adapter(TypeBuilder Owner, MethodBuilder Method, ILGenerator Body);

    internal sealed class TemplateEmission(Type owner, ILGenerator body, int tokenOffset, MethodBase method, Type? context)
    {
        internal Type Owner { get; } = owner;
        internal ILGenerator Body { get; } = body;
        internal int TokenOffset { get; } = tokenOffset;
        internal MethodBase Method { get; } = method;
        internal Type? Context { get; } = context;
        internal MethodBuilder? Create { get; set; }
        internal int CreateOffset { get; set; } = -1;
    }

    internal NativeReceiverInitializerLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeReceiverInitializerLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Find = _owner.DefineMethod("Find", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type]);
    }

    internal MethodBuilder Find { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => LegacyFamilies.Count != 0;
    internal int AdapterCount => _adapters.Count;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();
    internal IReadOnlyList<Type> ClosedOwners { get; private set; } = [];
    internal IReadOnlyList<Type> LegacyFamilies { get; private set; } = [];
    internal IReadOnlyList<Type> TemplateOwners { get; private set; } = [];

    internal void Record(TypeBuilder owner, MethodBuilder method)
    {
        EnsureMutable();
        if (owner.IsCreated() || !ReferenceEquals(owner.Module, _owner.Module) ||
            !ReferenceEquals(method.DeclaringType, owner) || !ReferenceEquals(method.Module, _owner.Module) ||
            !ValidAdapter(method) || !_adapters.TryAdd(owner, new(owner, method, method.GetILGenerator())))
            throw new InvalidOperationException("Receiver adapters require the actual same-output initializer declaration.");
    }

    // The two APIs emit the original token and Create instructions themselves.
    // A bare token record cannot become completed class-template evidence.
    internal static TemplateEmission? EmitTemplateValue(NativeReceiverInitializerLookup? lookup,
        TypeProvider types, ILGenerator il, Type template, MethodBase? method, Type? context)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Ldtoken, template); il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
        if (lookup is null) return null;
        if (method is null || !ReferenceEquals(method.Module, lookup._owner.Module) ||
            !ReferenceEquals(Body(method), il) || !ReferenceEquals(template.Module, lookup._owner.Module) ||
            context is not null && !ReferenceEquals(context.Module, lookup._owner.Module))
            throw new InvalidOperationException("Class templates require their actual output token-emitting body.");
        var emission = new TemplateEmission(template, il, offset, method, context);
        lookup._templates.Add(emission);
        return emission;
    }

    internal static void EmitTemplateCreate(NativeReceiverInitializerLookup? lookup, ILGenerator il,
        TemplateEmission? emission, MethodBuilder create, NativeClosedSourceFamilies? families = null)
    {
        lookup?.EnsureMutable();
        int offset = il.ILOffset;
        il.Emit(OpCodes.Call, create);
        families?.RecordRuntimeClassCreate(create, il, offset, emission?.Method);
        if (lookup is null) return;
        if (emission is null || !lookup._templates.Contains(emission) || emission.Create is not null ||
            !ReferenceEquals(emission.Body, il) || !ReferenceEquals(create.Module, lookup._owner.Module) ||
            offset <= emission.TokenOffset)
            throw new InvalidOperationException("Class-template evidence must reach its actual Create call exactly once.");
        emission.Create = create; emission.CreateOffset = offset;
    }

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        if (!native || IsComplete || !ReferenceEquals(module, _owner.Module) || !Find.IsStatic ||
            Find.IsGenericMethod || Find.ReturnType != _types.MethodInfo ||
            (Find.CallingConvention & CallingConventions.VarArgs) != 0 ||
            !Find.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.Type]))
            throw new InvalidOperationException("Native receiver lookup requires this exact output's declaration.");
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, NativeConstructorValueProducerSnapshot constructors,
        EmittedRuntime runtime, IReadOnlyDictionary<TypeBuilder, MethodBuilder> expectedAdapters,
        NativeClosedSourceFamilySnapshot? certificate = null)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(constructors.Module, _owner.Module) ||
            !ReferenceEquals(sources.Contract, runtime.ObjectFields.Interface) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module) ||
            !ReferenceEquals(runtime.NativeReceiverInitializers, this))
            throw new InvalidOperationException("Receiver lookup requires completed same-output producer metadata.");
        if (expectedAdapters.Count != _adapters.Count || expectedAdapters.Any(entry =>
            !_adapters.TryGetValue(entry.Key, out var adapter) || !ReferenceEquals(adapter.Method, entry.Value)))
            throw new InvalidOperationException("Receiver-adapter records must equal the actual initializer inventory.");
        if (certificate is not null && !ReferenceEquals(certificate.Module, _owner.Module))
            throw new InvalidOperationException("Closed receiver contexts must belong to this output.");
        foreach (var adapter in _adapters.Values)
        {
            if (!adapter.Owner.IsCreated() || !sources.Implementers.Contains(adapter.Owner) ||
                adapter.Body.ILOffset == 0 || !ReferenceEquals(Body(adapter.Method), adapter.Body) ||
                !ValidAdapter(adapter.Method))
                throw new InvalidOperationException("Receiver adapters require completed exact owners, bodies and ABI.");
        }

        var closed = new HashSet<Type>();
        var families = new HashSet<Type>();
        _blockers.AddRange(constructors.Blockers);
        foreach (var value in constructors.Owners)
        {
            if (value.Producers.Count == 0 || value.Producers.Any(producer =>
                producer.ExactTokenOwner != value.ExactTokenOwner || producer.ValueOffset <= 0 ||
                producer.Body.ILOffset <= producer.ValueOffset ||
                producer.Method is { } method && !ReferenceEquals(method.Module, _owner.Module)))
                throw new InvalidOperationException("Receiver lookup requires actual completed constructor-value bodies.");
            if (value.ClosureKind is NativeConstructorValueClosureKind.NongenericLiteral or NativeConstructorValueClosureKind.ExactClosedBclToken
                && (ProveClosed(value.ExactTokenOwner) || certificate?.ProvesClosedOwner(value.ExactTokenOwner) == true)) closed.Add(value.ExactTokenOwner);
            else if (value.ClosureKind == NativeConstructorValueClosureKind.UnprovedSourceGenericFamily &&
                value.SourceGenericDefinition is { } definition) AddFamily(definition);
            else _blockers.Add($"Unproved constructor-value owner for receiver lookup: {value.ExactTokenOwner.FullName}");
        }
        foreach (var template in _templates)
        {
            if (template.Create is null || !ReferenceEquals(template.Create, runtime.ClassDefinitions.Create) ||
                template.CreateOffset <= template.TokenOffset || template.Body.ILOffset <= template.CreateOffset ||
                !ReferenceEquals(Body(template.Method), template.Body))
                throw new InvalidOperationException("Class templates require their completed exact Create producer body.");
            Type definition = template.Owner.IsGenericType ? template.Owner.GetGenericTypeDefinition() : template.Owner;
            if (definition is not TypeBuilder builder || !sources.Implementers.Contains(builder) || !builder.IsCreated())
                throw new InvalidOperationException("Class-template tokens must belong to actual completed source definitions.");
            if (definition.IsGenericTypeDefinition) AddFamily(definition);
            else if (ProveClosed(template.Owner) || certificate?.ProvesClosedOwner(template.Owner) == true) closed.Add(template.Owner);
            else _blockers.Add($"Unproved class-template owner for receiver lookup: {template.Owner.FullName}");
        }
        ClosedOwners = Array.AsReadOnly(closed.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        LegacyFamilies = Array.AsReadOnly(families.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray());
        TemplateOwners = Array.AsReadOnly(_templates.Select(template => template.Owner).Distinct().ToArray());
        EmitQuery(runtime.Errors);
        _owner.CreateType(); IsComplete = true;

        void AddFamily(Type definition)
        {
            if (!definition.IsGenericTypeDefinition || !ReferenceEquals(definition.Module, _owner.Module) ||
                definition is not TypeBuilder builder || !sources.Implementers.Contains(builder))
                throw new InvalidOperationException("Legacy receiver lookup requires an actual owned source generic family.");
            if (certificate?.ContextsFor(definition) is { } contexts)
            {
                // The adapter declaration/body was independently validated above.
                // Keep the complete loaded binder query for each actual context,
                // including inherited or absent adapters.
                foreach (var context in contexts) closed.Add(context);
                return;
            }
            families.Add(definition);
            _blockers.Add($"Receiver lookup retains the whole query for an unproved source generic family: {definition.FullName}");
        }
    }

    private bool ValidAdapter(MethodInfo method) => method.Name == "$InitializeReceiver" &&
        method.IsPublic && method.IsStatic && !method.IsGenericMethod && method.ReturnType == _types.Object &&
        (method.CallingConvention & CallingConventions.VarArgs) == 0 &&
        method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual([_types.Object, _types.ObjectArray, _types.Object, _types.Object]);

    private static ILGenerator? Body(MethodBase method) => method switch
    {
        MethodBuilder builder => builder.GetILGenerator(),
        ConstructorBuilder builder => builder.GetILGenerator(),
        _ => null,
    };

    private static bool ProveClosed(Type owner)
    {
        var seen = new HashSet<Type>();
        for (Type? current = owner; current is not null;)
        {
            if (!seen.Add(current) || FunctionGenericParameterRegistry.ContainsUnboundType(current) ||
                current is TypeBuilder builder && !builder.IsCreated()) return false;
            try { current = current.BaseType; }
            catch (NotSupportedException) { return false; }
        }
        return true;
    }

    private void EmitQuery(EmittedErrorRuntime errors)
    {
        var il = Find.GetILGenerator();
        foreach (var owner in ClosedOwners)
        {
            var next = il.DefineLabel();
            TypeLiteral(il, owner); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brfalse, next);
            TypeLiteral(il, owner); Query(il); il.MarkLabel(next);
        }
        var legacy = il.DefineLabel();
        if (UsesLegacy)
        {
            var foreign = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, foreign);
            TypeLiteral(il, _types.Object); il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "GetType"));
            il.Emit(OpCodes.Bne_Un, foreign);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
            il.Emit(OpCodes.Brfalse, foreign);
            var definition = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            il.Emit(OpCodes.Stloc, definition);
            foreach (var family in LegacyFamilies)
            {
                TypeLiteral(il, family); il.Emit(OpCodes.Ldloc, definition);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
                il.Emit(OpCodes.Brtrue, legacy);
            }
            il.MarkLabel(foreign);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignOwnerMessage);
        if (UsesLegacy)
        {
            il.MarkLabel(legacy); il.Emit(OpCodes.Ldarg_0); Query(il);
        }
    }

    private void Query(ILGenerator il)
    {
        // Execute the full loaded lookup, including inherited methods, ambiguity
        // and a named miss. The producer record does not guess a binder result.
        il.Emit(OpCodes.Ldstr, "$InitializeReceiver");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void TypeLiteral(ILGenerator il, Type type)
    {
        il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native receiver-initializer lookup is complete.");
    }
}
