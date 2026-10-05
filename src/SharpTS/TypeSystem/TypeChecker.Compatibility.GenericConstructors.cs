using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private bool GenericConstructorsRelated(TypeInfo.GenericClass target, TypeInfo.GenericClass source)
    {
        if (!MembersAccessibilityCompatible(target, source)
            || !ConstructorPrivateBrandsRelated(target, source, isStatic: false)
            || !ConstructorPrivateBrandsRelated(target, source, isStatic: true)
            || !ConstructorStaticAccessibilityRelated(target, source))
            return false;

        // Put both observable sides in the signature result so contextual inference
        // uses one substitution for parameters, instance members and static members.
        var targets = ProjectGenericConstructors(target);
        var sources = ProjectGenericConstructors(source);
        return targets.All(t => sources.Any(s => SignatureRelatedTo(s, t)));
    }

    private List<NormalizedSignature> ProjectGenericConstructors(TypeInfo.GenericClass type)
    {
        var arguments = type.TypeParams.Cast<TypeInfo>().ToList();
        var members = CollectGenericClassMembers(type, arguments, includeNonPublic: true);
        foreach (var (name, member) in CollectConstructorStaticMembers(type))
            members.Add("'static:" + name, member);
        if (type.StringIndexType is { } stringIndex) members.Add("'stringIndex", stringIndex);
        if (type.NumberIndexType is { } numberIndex) members.Add("'numberIndex", numberIndex);
        if (type.SymbolIndexType is { } symbolIndex) members.Add("'symbolIndex", symbolIndex);
        var result = new TypeInfo.Record(members.ToFrozenDictionary());

        var constructor = ResolveClassMemberTypeSubstituted(
            new TypeInfo.InstantiatedGeneric(type, arguments), "constructor");
        var functions = constructor switch
        {
            TypeInfo.Function f => new List<TypeInfo.Function> { f },
            TypeInfo.OverloadedFunction overloads => overloads.Signatures,
            _ => [new TypeInfo.Function([], TypeInfo.Void.Shared)]
        };
        return functions.Select(f => new NormalizedSignature(type.TypeParams,
            new TypeInfo.Function(f.ParamTypes, result, f.RequiredParams, f.HasRestParam,
                f.ThisType, f.ParamNames))).ToList();
    }

    private Dictionary<string, TypeInfo> CollectConstructorStaticMembers(TypeInfo type)
    {
        Dictionary<string, TypeInfo> members = [];
        Dictionary<string, TypeInfo> substitutions = [];
        TypeInfo? current = type;
        for (int guard = 0; current is not null && guard < 256; guard++)
        {
            ClassMetadataCore? core;
            if (current is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instance)
            {
                var arguments = instance.TypeArguments.Select(a => Substitute(a, substitutions)).ToList();
                substitutions = GenericClassSubs(generic, arguments);
                core = generic.Core;
            }
            else
                core = current switch
                {
                    TypeInfo.Class c => c.Core,
                    TypeInfo.GenericClass c => c.Core,
                    TypeInfo.MutableClass c => c.Freeze().Core,
                    _ => null
                };
            if (core is null) break;
            foreach (var (name, member) in core.StaticProperties.Concat(core.StaticMethods))
                members.TryAdd(name, SubstitutePreservingSignatures(member, substitutions));
            current = core.Superclass;
        }
        return members;
    }

    private static bool ConstructorPrivateBrandsRelated(TypeInfo target, TypeInfo source, bool isStatic)
    {
        var sourceCores = EnumerateClassCores(source).ToList();
        foreach (var core in EnumerateClassCores(target))
        {
            bool branded = isStatic
                ? core.StaticPrivateFieldTypes.Count > 0 || core.StaticPrivateMethodTypes.Count > 0
                : core.PrivateFieldTypes.Count > 0 || core.PrivateMethodTypes.Count > 0;
            if (branded && !sourceCores.Any(s => ReferenceEquals(s, core)
                || core.DeclarationId != 0 && s.DeclarationId == core.DeclarationId))
                return false;
        }
        return true;
    }

    private static bool ConstructorStaticAccessibilityRelated(TypeInfo target, TypeInfo source)
    {
        var targets = CollectStaticBrands(target);
        var sources = CollectStaticBrands(source);
        foreach (var (name, targetBrand) in targets)
        {
            if (!sources.TryGetValue(name, out var sourceBrand)) continue;
            if (targetBrand.Access == AccessModifier.Public && sourceBrand.Access == AccessModifier.Public) continue;
            if (targetBrand.Access != sourceBrand.Access) return false;
            if (targetBrand.DeclaringClassId != 0 && targetBrand.DeclaringClassId == sourceBrand.DeclaringClassId) continue;
            if (targetBrand.Access == AccessModifier.Protected
                && SourceDerivesFromDeclaration(source, targetBrand.DeclaringClassId)) continue;
            return false;
        }
        return true;
    }

    private static Dictionary<string, MemberAccessBrand> CollectStaticBrands(TypeInfo type)
    {
        Dictionary<string, MemberAccessBrand> brands = [];
        foreach (var core in EnumerateClassCores(type))
        {
            foreach (var name in core.StaticProperties.Keys)
                brands.TryAdd(name, new MemberAccessBrand(
                    core.StaticFieldAccessMap.GetValueOrDefault(name, AccessModifier.Public), core.DeclarationId));
            foreach (var name in core.StaticMethods.Keys)
                brands.TryAdd(name, new MemberAccessBrand(
                    core.StaticMethodAccessMap.GetValueOrDefault(name, AccessModifier.Public), core.DeclarationId));
        }
        return brands;
    }
}
