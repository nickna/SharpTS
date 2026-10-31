using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late native argument conversion backed by the completed union declaration owner.</summary>
internal sealed partial class NativeUnionArgumentConversion
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
        ConvertCachedSource = _owner.DefineMethod("ConvertCachedSource", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.MakeArrayType(types.ParameterInfo), types.ObjectArray]);
        ConvertCachedReflection = _owner.DefineMethod("ConvertCachedReflection", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.MakeArrayType(types.ParameterInfo), types.ObjectArray]);
        SelectParameterSource = _owner.DefineMethod("SelectParameterSource", MethodAttributes.Assembly | MethodAttributes.Static,
            types.MethodInfo, [types.MethodInfo, types.MethodInfo]);
    }

    internal MethodBuilder Convert { get; }
    internal MethodBuilder ConvertCachedSource { get; }
    internal MethodBuilder SelectParameterSource { get; }
    internal MethodBuilder ConvertCachedReflection { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Declarations => IsComplete ? _declarations :
        throw new InvalidOperationException("Native union declaration snapshot is not complete.");

    internal void CompleteEmission(UnionTypeGenerator generator, TypeMapper mapper, Type unionTypeInterface, EmittedErrorRuntime errors, EmittedRuntime? runtime = null, NativeClrAdmission.CompletedIngressProof? ingress = null, NativeMethodLookupCatalog? methods = null, NativeOwnerClosure? ownerClosure = null)
    {
        if (IsComplete) throw new InvalidOperationException("Native union argument conversion is complete.");
        ArgumentNullException.ThrowIfNull(generator);
        var completed = generator.RequireCompletedTypes(mapper, _module, unionTypeInterface);
        UsesLegacy = completed.Count != 0;
        var il = Convert.GetILGenerator();
        if (UsesLegacy) UnionArgumentConversionEmission.EmitLegacy(il, _types);
        else UnionArgumentConversionEmission.EmitNoOwnedUnions(il, _types, errors);
        CompleteSourceConversion(generator.RequireCompletedConversions(mapper, _module, unionTypeInterface), runtime, ingress, errors);
        CompleteReflectionConversion(generator.RequireCompletedConversions(mapper, _module, unionTypeInterface), runtime, ingress, methods, ownerClosure, errors);
        _owner.CreateType();
        _declarations = completed;
        IsComplete = true;
    }
    private void EmitOriginalConversion(MethodBuilder method)
    {
        var body = method.GetILGenerator();
        body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldarg_1);
        body.Emit(OpCodes.Call, Convert); body.Emit(OpCodes.Ret);
    }

    internal bool TryCompletedParameter(Type type, IReadOnlyList<CompletedUnionConversion> conversions, out Type? target)
    {
        target = null;
        try
        {
            // This is the original first guard, read from the actual original
            // cached metadata. It also proves the open generic reference slots
            // returned by the completed framework queries cannot reach the binder.
            if (type.GetType() != _types.Object.GetType() &&
                !(type.GetType() == _owner.GetType() && type is TypeBuilder definition &&
                  ReferenceEquals(definition.Module, _module) && definition.IsCreated())) return false;
            if (!type.IsValueType) return true;
            string name = type.Name;
            // Compare only the ordinary ASCII letter prefix, folding case merely
            // to make this negative certificate more conservative. A distinct
            // base letter before Union's punctuation cannot be ignored by a
            // culture-sensitive prefix comparison. Non-ASCII/ignorable prefixes
            // and names sharing all five base letters remain unproved.
            if (HasCultureStableNonUnionPrefix(name)) return true;
            var conversion = conversions.SingleOrDefault(row => ReferenceEquals(type, row.Owner));
            if (conversion is null) return false;
            target = conversion.Owner;
            return true;
        }
        catch (NotSupportedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static bool HasCultureStableNonUnionPrefix(string name)
    {
        const string prefix = "union";
        for (int i = 0; i < Math.Min(name.Length, prefix.Length); i++)
        {
            char letter = name[i];
            if (letter is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z')) return false;
            if ((letter | 32) != prefix[i]) return true;
        }
        return false;
    }

}
