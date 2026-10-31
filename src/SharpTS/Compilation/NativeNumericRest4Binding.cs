using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late exact-method binding for compiler-owned native numeric-rest entries.</summary>
internal sealed class NativeNumericRest4Binding
{
    private static readonly Type DelegateType = typeof(Func<double, double, double, double, double>);
    private readonly TypeBuilder _owner;
    private readonly TypeProvider _types;
    private readonly Dictionary<MethodBuilder, MethodBuilder> _entries = [];
    private readonly HashSet<MethodBuilder> _registeredEntries = [];

    internal NativeNumericRest4Binding(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeNumericRest4Binding",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Bind = _owner.DefineMethod("Bind", MethodAttributes.Assembly | MethodAttributes.Static,
            DelegateType, [types.MethodInfo]);
    }

    internal MethodBuilder Bind { get; }
    internal bool IsComplete { get; private set; }
    internal int ProducerCount => _entries.Count;

    internal void RegisterProducer(MethodBuilder original, MethodBuilder entry, ConstructorInfo marker)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(marker);
        bool SameModule(MemberInfo member) => member.Module.ModuleVersionId == _owner.Module.ModuleVersionId;
        if (!SameModule(original) || !SameModule(entry) || !SameModule(marker) ||
            original.DeclaringType != entry.DeclaringType || original.DeclaringType is null ||
            original.DeclaringType.IsGenericType || original.IsGenericMethod || entry.IsGenericMethod ||
            !original.IsStatic || !entry.IsStatic || !entry.IsAssembly ||
            original.ReturnType != _types.Double || entry.ReturnType != _types.Double ||
            !original.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.ListOfObject]) ||
            !entry.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [_types.Object, _types.Double, _types.Double, _types.Double, _types.Double]) ||
            !marker.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.String]))
            throw new InvalidOperationException("Native numeric-rest binding requires exact same-module static source and entry signatures.");
        if (_entries.ContainsKey(original) || _registeredEntries.Contains(entry))
            throw new InvalidOperationException("Native numeric-rest producer has already been registered.");

        // The only owned marker producer attaches and records together, after
        // all validation. Failed validation never publishes a partial entry.
        original.SetCustomAttribute(marker, CustomAttributeEncoder.Encode(marker, entry.Name));
        _entries.Add(original, entry);
        _registeredEntries.Add(entry);
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        var il = Bind.GetILGenerator();
        var equality = _types.GetMethod(_types.MethodInfo, "op_Equality", _types.MethodInfo, _types.MethodInfo);
        var constructor = typeof(Func<double, double, double, double, double>)
            .GetConstructor([typeof(object), typeof(IntPtr)])!;
        foreach (var (original, entry) in _entries)
        {
            var next = il.DefineLabel();
            _types.EmitLoadMethodInfo(il, original);
            // RuntimeMethodInfo must own equality; an unknown MethodInfo can
            // override Equals and must not choose a compiler-owned entry.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            // Preserve the established closed-static ABI. The entry ignores its
            // object parameter; the original MethodInfo is a non-null bound target.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, entry);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        _owner.CreateType();
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native numeric-rest binding emission is complete.");
    }
}
