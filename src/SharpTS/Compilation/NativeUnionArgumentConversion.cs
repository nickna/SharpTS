using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late native argument conversion backed by the completed union declaration owner.</summary>
internal sealed class NativeUnionArgumentConversion
{
    private readonly ModuleBuilder _module;
    private readonly TypeBuilder _owner;
    private readonly TypeProvider _types;
    private IReadOnlyList<Type> _declarations = [];

    internal NativeUnionArgumentConversion(ModuleBuilder module, TypeProvider types)
    {
        _module = module;
        _types = types;
        _owner = module.DefineType("$NativeUnionArgumentConversion", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Convert = _owner.DefineMethod("Convert", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.MakeArrayType(types.ParameterInfo), types.ObjectArray]);
    }

    internal MethodBuilder Convert { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Declarations => IsComplete ? _declarations :
        throw new InvalidOperationException("Native union declaration snapshot is not complete.");

    internal void CompleteEmission(UnionTypeGenerator generator, TypeMapper mapper, Type unionTypeInterface, EmittedErrorRuntime errors)
    {
        if (IsComplete) throw new InvalidOperationException("Native union argument conversion is complete.");
        ArgumentNullException.ThrowIfNull(generator);
        var completed = generator.RequireCompletedTypes(mapper, _module, unionTypeInterface);
        UsesLegacy = completed.Count != 0;
        var il = Convert.GetILGenerator();
        if (UsesLegacy) UnionArgumentConversionEmission.EmitLegacy(il, _types);
        else UnionArgumentConversionEmission.EmitNoOwnedUnions(il, _types, errors);
        _owner.CreateType();
        _declarations = completed;
        IsComplete = true;
    }
}
