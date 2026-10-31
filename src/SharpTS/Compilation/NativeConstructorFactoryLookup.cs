using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late lookup of factories recorded at the actual source marker producer.</summary>
internal sealed class NativeConstructorFactoryLookup
{
    internal const string ForeignFactoryMessage = "This constructor factory owner, name or flags are outside this native guest's compiler-owned factory records. Use --target dll or --target exe.";
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly Dictionary<TypeBuilder, Factory> _factories = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _blockers = [];
    private sealed record Factory(TypeBuilder Owner, MethodBuilder Method, ILGenerator Body);

    internal NativeConstructorFactoryLookup(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeConstructorFactoryLookup", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        FindFactory = _owner.DefineMethod("FindFactory", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.Type, types.String, typeof(BindingFlags)]);
    }

    internal MethodBuilder FindFactory { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy => _blockers.Count != 0;
    internal int FactoryCount => _factories.Count;
    internal IReadOnlyList<string> Blockers => _blockers.AsReadOnly();

    internal void Record(TypeBuilder owner, MethodBuilder factory)
    {
        EnsureMutable();
        if (owner.IsCreated() || !ReferenceEquals(owner.Module, _owner.Module) || !ReferenceEquals(factory.Module, _owner.Module) ||
            !ReferenceEquals(factory.DeclaringType, owner) || !ValidFactory(factory) || _factories.ContainsKey(owner) ||
            _factories.Values.Any(entry => ReferenceEquals(entry.Method, factory)))
            throw new InvalidOperationException("Native constructor factories require exact owned marker-producer declarations.");
        _factories.Add(owner, new(owner, factory, factory.GetILGenerator()));
    }

    internal void ValidateForEmitter(ModuleBuilder module, bool native)
    {
        if (!native || IsComplete || !ReferenceEquals(_owner.Module, module) || !FindFactory.IsStatic ||
            FindFactory.IsGenericMethod || FindFactory.ReturnType != _types.MethodInfo ||
            (FindFactory.CallingConvention & CallingConventions.VarArgs) != 0 ||
            !FindFactory.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([_types.Type, _types.String, typeof(BindingFlags)]))
            throw new InvalidOperationException("Native constructor-factory lookup requires this native output's exact declaration.");
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, EmittedRuntime runtime)
    {
        EnsureMutable();
        if (!sources.IsComplete || !ReferenceEquals(sources.Module, _owner.Module) ||
            !ReferenceEquals(runtime.DynamicConstruction.FactoryAttributeType.Module, _owner.Module) ||
            !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native constructor factories require completed metadata from this exact output.");
        _blockers.AddRange(sources.Blockers);
        foreach (var factory in _factories.Values)
        {
            if (!factory.Owner.IsCreated() || !sources.Implementers.Contains(factory.Owner) ||
                factory.Body.ILOffset == 0 || !ValidFactory(factory.Method))
                throw new InvalidOperationException("Native constructor factories require completed owned types, bodies and ABI.");
            // The same generic declaration can arrive open or with different
            // closed arguments. Existing canonical metadata proves only one of
            // those values; keep the whole old lookup until that input domain
            // is certified, without manufacturing any closed factory tokens.
            if (FunctionGenericParameterRegistry.ContainsUnboundType(factory.Owner))
                _blockers.Add($"Constructor factory has an unproved generic owner domain: {factory.Owner.FullName}");
        }

        var il = FindFactory.GetILGenerator();
        var legacy = il.DefineLabel();
        foreach (var factory in _factories.Values)
        {
            var next = il.DefineLabel();
            EmitType(il, factory.Owner); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
            il.Emit(OpCodes.Brfalse, next);
            if (UsesLegacy) il.Emit(OpCodes.Br, legacy);
            else
            {
                var name = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, factory.Method.Name);
                il.Emit(OpCodes.Call, _types.StringOpEquality); il.Emit(OpCodes.Brtrue, name);
                Refuse(il, runtime.Errors); il.MarkLabel(name);
                var flags = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)Flags); il.Emit(OpCodes.Beq, flags);
                Refuse(il, runtime.Errors); il.MarkLabel(flags);
                _types.EmitLoadMethodInfo(il, factory.Method); il.Emit(OpCodes.Ret);
            }
            il.MarkLabel(next);
        }
        if (_factories.Keys.Any(owner => owner.IsGenericTypeDefinition))
        {
            // Only a real runtime Type may expose generic-family metadata.
            // TypeDelegator or a hostile Type cannot forge a recorded owner.
            var foreign = il.DefineLabel();
            EmitType(il, _types.Object); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
            il.Emit(OpCodes.Bne_Un, foreign);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "IsGenericType"));
            il.Emit(OpCodes.Brfalse, foreign);
            var definition = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            il.Emit(OpCodes.Stloc, definition);
            foreach (var owner in _factories.Keys.Where(owner => owner.IsGenericTypeDefinition))
            {
                EmitType(il, owner); il.Emit(OpCodes.Ldloc, definition);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "Equals", _types.Object));
                il.Emit(OpCodes.Brtrue, legacy);
            }
            il.MarkLabel(foreign);
        }
        Refuse(il, runtime.Errors);
        if (UsesLegacy)
        {
            il.MarkLabel(legacy);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Ret);
        }
        _owner.CreateType(); IsComplete = true;
    }

    private bool ValidFactory(MethodInfo method) => method.IsStatic && method.IsPrivate && !method.IsGenericMethod &&
        method.ReturnType == _types.Object && (method.CallingConvention & CallingConventions.VarArgs) == 0 &&
        method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.ObjectArray, _types.Object]);

    private void EmitType(ILGenerator il, Type type)
    {
        il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
    }
    private static void Refuse(ILGenerator il, EmittedErrorRuntime errors) =>
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignFactoryMessage);
    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native constructor-factory lookup emission is complete.");
    }
}