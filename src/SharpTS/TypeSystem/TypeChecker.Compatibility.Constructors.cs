using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private int _ordinaryConstructorRelationDepth;
    private readonly HashSet<OrdinaryConstructorPair> _ordinaryConstructorPairs = new(new OrdinaryConstructorPairComparer());
    private readonly HashSet<OrdinaryCompatibilityPair> _ordinaryCompatibilityPairs = new(new OrdinaryCompatibilityPairComparer());
    private readonly HashSet<OrdinaryInstancePair> _ordinaryInstancePairs = new(new OrdinaryInstancePairComparer());
    private void RegisterOrdinaryConstructorOptionalMembers(TypeInfo.MutableClass owner,
        IReadOnlyList<Stmt.Field> fields, IReadOnlyList<Stmt.Function> methods)
    {
        var optional = owner.OrdinaryOptionalMembers;
        optional.Clear();
        foreach (var field in fields.Where(field => field.IsOptional && !field.IsPrivate))
            optional.Add((field.IsStatic, GetFieldMemberName(field)));
        foreach (var method in methods.Where(method => method.IsOptional && !method.IsPrivate))
        {
            string? name = method.ComputedKey == null ? method.Name.Lexeme
                : TryGetWellKnownSymbolMemberName(method.ComputedKey);
            if (name != null) optional.Add((method.IsStatic, name));
        }
        owner.ResetFrozenCache();
    }

    private readonly record struct OrdinaryCompatibilityPair(TypeInfo Target, TypeInfo Source, bool Method, bool Callback);

    private sealed class OrdinaryCompatibilityPairComparer : IEqualityComparer<OrdinaryCompatibilityPair>
    {
        public bool Equals(OrdinaryCompatibilityPair x, OrdinaryCompatibilityPair y) =>
            ReferenceEquals(x.Target, y.Target) && ReferenceEquals(x.Source, y.Source)
            && x.Method == y.Method && x.Callback == y.Callback;

        public int GetHashCode(OrdinaryCompatibilityPair pair) => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Target),
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Source), pair.Method, pair.Callback);
    }

    private readonly record struct OrdinaryConstructorPair(
        ClassMetadataCore Target, ClassMetadataCore Source, bool Method, bool Callback);

    private sealed class OrdinaryConstructorPairComparer : IEqualityComparer<OrdinaryConstructorPair>
    {
        public bool Equals(OrdinaryConstructorPair x, OrdinaryConstructorPair y) =>
            ReferenceEquals(x.Target, y.Target) && ReferenceEquals(x.Source, y.Source)
            && x.Method == y.Method && x.Callback == y.Callback;

        public int GetHashCode(OrdinaryConstructorPair pair) => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Target),
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Source), pair.Method, pair.Callback);
    }

    private sealed record OrdinaryConstructorLevel(ClassMetadataCore Core, Dictionary<string, TypeInfo> Substitutions);
    private sealed record OrdinaryConstructorMember(TypeInfo Type, bool IsMethod, AccessModifier Access,
        ClassMetadataCore DeclaringCore, bool IsOptional = false);

    private readonly record struct OrdinaryInstancePair(
        OrdinaryConstructorLevel Target, OrdinaryConstructorLevel Source, bool Method, bool Callback);

    private sealed class OrdinaryInstancePairComparer : IEqualityComparer<OrdinaryInstancePair>
    {
        // Substitution creates fresh Instance wrappers during recursive member checks.
        // Retain the instantiated arguments in the key instead of identifying a pair by cores alone.
        private static bool SameLevel(OrdinaryConstructorLevel x, OrdinaryConstructorLevel y) =>
            ReferenceEquals(x.Core, y.Core) && x.Substitutions.Count == y.Substitutions.Count
            && x.Substitutions.All(entry => y.Substitutions.TryGetValue(entry.Key, out var value)
                && ReferenceEquals(entry.Value, value));

        public bool Equals(OrdinaryInstancePair x, OrdinaryInstancePair y) =>
            x.Method == y.Method && x.Callback == y.Callback
            && SameLevel(x.Target, y.Target) && SameLevel(x.Source, y.Source);

        public int GetHashCode(OrdinaryInstancePair pair) => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Target.Core),
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Source.Core), pair.Method, pair.Callback);
    }

    private static bool IsOrdinaryConstructorPair(TypeInfo target, TypeInfo source) =>
        target is TypeInfo.Class or TypeInfo.MutableClass && source is TypeInfo.Class or TypeInfo.MutableClass;

    private static TypeInfo.Class? CurrentConstructorView(TypeInfo type) => type switch
    {
        TypeInfo.Class cls => cls,
        // Never freeze an unfinished declaration merely to ask about assignability.
        TypeInfo.MutableClass mutable => mutable.Frozen,
        _ => null
    };

    private bool OrdinaryConstructorsRelated(TypeInfo targetType, TypeInfo sourceType)
    {
        var target = CurrentConstructorView(targetType);
        var source = CurrentConstructorView(sourceType);
        if (target is null || source is null) return false;

        var pair = new OrdinaryConstructorPair(target.Core, source.Core,
            _methodBivarianceDepth > 0, _inCallbackComparison);
        if (!_ordinaryConstructorPairs.Add(pair)) return true;
        _ordinaryConstructorRelationDepth++;
        try
        {
            if (!TryGetOrdinaryConstructorHierarchy(target, out var targets)
                || !TryGetOrdinaryConstructorHierarchy(source, out var sources)) return false;
            if (!target.IsAbstract && source.IsAbstract) return false;

            var targetConstructor = OrdinaryConstructSignatures(targets);
            var sourceConstructor = OrdinaryConstructSignatures(sources);
            // TypeScript constructor visibility is directional: a private target imposes
            // no accessibility requirement, whereas a public target needs a public source.
            if (targetConstructor.Access != AccessModifier.Private
                && sourceConstructor.Access == AccessModifier.Private) return false;
            if (targetConstructor.Access == AccessModifier.Public
                && sourceConstructor.Access != AccessModifier.Public) return false;

            _methodBivarianceDepth++;
            try
            {
                if (!targetConstructor.Signatures.All(t => sourceConstructor.Signatures.Any(s => SignatureRelatedTo(s, t))))
                    return false;
            }
            finally { _methodBivarianceDepth--; }

            if (!OrdinaryConstructorMembersRelated(OrdinaryConstructorMembers(targets, isStatic: true),
                    OrdinaryConstructorMembers(sources, isStatic: true), sources)) return false;
            return OrdinaryInstanceFacetsRelated(target, source, targets, sources);
        }
        finally
        {
            _ordinaryConstructorRelationDepth--;
            _ordinaryConstructorPairs.Remove(pair);
        }
    }

    private bool OrdinaryInstancesRelated(TypeInfo.Instance target, TypeInfo.Instance source)
    {
        var targetType = target.ResolvedClassType;
        var sourceType = source.ResolvedClassType;
        if (!TryGetOrdinaryConstructorHierarchy(targetType, out var targets)
            || !TryGetOrdinaryConstructorHierarchy(sourceType, out var sources)) return false;
        var pair = new OrdinaryInstancePair(targets[0], sources[0],
            _methodBivarianceDepth > 0, _inCallbackComparison);
        if (!_ordinaryInstancePairs.Add(pair)) return true;
        try { return OrdinaryInstanceFacetsRelated(targetType, sourceType, targets, sources); }
        finally { _ordinaryInstancePairs.Remove(pair); }
    }

    private bool OrdinaryInstanceFacetsRelated(TypeInfo targetType, TypeInfo sourceType,
        List<OrdinaryConstructorLevel> targets, List<OrdinaryConstructorLevel> sources)
    {
        if (!ConstructorPrivateBrandsRelated(targetType, sourceType, isStatic: false)) return false;
        var targetMembers = OrdinaryConstructorMembers(targets, isStatic: false);
        var sourceMembers = OrdinaryConstructorMembers(sources, isStatic: false);
        if (!OrdinaryConstructorMembersRelated(targetMembers, sourceMembers, sources)) return false;
        // Compare the instance surface directly; the general instance relation also
        // has nominal/name shortcuts that do not establish this structural contract.
        var targetShape = OrdinaryConstructorInstanceShape(targets, targetMembers);
        var sourceShape = OrdinaryConstructorInstanceShape(sources, sourceMembers);
        // Class instances do not acquire the implicit index signatures available
        // to anonymous object types, even though we use records for this projection.
        if (targetShape.StringIndexType is not null && sourceShape.StringIndexType is null) return false;
        if (targetShape.NumberIndexType is { } targetNumberIndex)
        {
            var sourceNumberIndex = sourceShape.NumberIndexType ?? sourceShape.StringIndexType;
            if (sourceNumberIndex is null || !IsCompatible(targetNumberIndex, sourceNumberIndex)) return false;
        }
        return !FailsWeakTypeCheck(targetShape, sourceShape)
            && IndexSignaturesSatisfied(targetShape, sourceShape)
            && (targetShape.SymbolIndexType is null || sourceShape.SymbolIndexType is not null
                && IsCompatible(targetShape.SymbolIndexType, sourceShape.SymbolIndexType));
    }

    private bool TryGetOrdinaryConstructorHierarchy(TypeInfo type, out List<OrdinaryConstructorLevel> levels)
    {
        levels = [];
        Dictionary<string, TypeInfo> substitutions = [];
        HashSet<ClassMetadataCore> seen = new(ReferenceEqualityComparer.Instance);
        TypeInfo? current = type;
        for (int guard = 0; current is not null && guard < 256; guard++)
        {
            ClassMetadataCore? core;
            if (current is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instance)
            {
                var arguments = instance.TypeArguments.Select(a => SubstituteConstructorMember(a, substitutions)).ToList();
                substitutions = GenericClassSubs(generic, arguments);
                core = generic.Core;
            }
            else core = current switch
            {
                TypeInfo.Class cls => cls.Core,
                TypeInfo.GenericClass genericClass => genericClass.Core,
                TypeInfo.MutableClass mutable => mutable.Frozen?.Core,
                _ => null
            };
            if (core is null || !seen.Add(core)) return false;
            levels.Add(new(core, substitutions));
            current = core.Superclass;
        }
        return current is null;
    }

    private (List<NormalizedSignature> Signatures, AccessModifier Access) OrdinaryConstructSignatures(
        List<OrdinaryConstructorLevel> levels)
    {
        foreach (var level in levels)
        {
            if (!level.Core.Methods.TryGetValue("constructor", out var member)) continue;
            member = SubstituteConstructorMember(member, level.Substitutions);
            var functions = member switch
            {
                TypeInfo.Function function => new List<TypeInfo.Function> { function },
                TypeInfo.OverloadedFunction overloads => overloads.Signatures,
                _ => []
            };
            return (functions.Select(f => new NormalizedSignature(null, f with { ReturnType = TypeInfo.Void.Shared })).ToList(),
                level.Core.MethodAccess.GetValueOrDefault("constructor", AccessModifier.Public));
        }
        return ([new NormalizedSignature(null, new TypeInfo.Function([], TypeInfo.Void.Shared))], AccessModifier.Public);
    }

    private Dictionary<string, OrdinaryConstructorMember> OrdinaryConstructorMembers(
        List<OrdinaryConstructorLevel> levels, bool isStatic)
    {
        Dictionary<string, OrdinaryConstructorMember> members = [];
        foreach (var level in levels)
        {
            var core = level.Core;
            var fields = isStatic ? core.StaticProperties : core.FieldTypes;
            var methods = isStatic ? core.StaticMethods : core.Methods;
            var fieldAccess = isStatic ? core.StaticFieldAccessMap : core.FieldAccess;
            var methodAccess = isStatic ? core.StaticMethodAccessMap : core.MethodAccess;
            bool Optional(string name) => core.OrdinaryOptionalMembers?.Contains((isStatic, name)) == true;
            if (!isStatic && core.ComputedConstructorMethodType is { } constructorMethod)
                members.TryAdd("constructor", new(SubstituteConstructorMember(constructorMethod, level.Substitutions),
                    true, core.ComputedConstructorMethodAccess, core));
            foreach (var (name, type) in fields)
                members.TryAdd(name, new(SubstituteConstructorMember(type, level.Substitutions), false,
                    fieldAccess.GetValueOrDefault(name, AccessModifier.Public), core, Optional(name)));
            foreach (var (name, type) in methods)
                if (name != "constructor")
                    members.TryAdd(name, new(SubstituteConstructorMember(type, level.Substitutions), true,
                        methodAccess.GetValueOrDefault(name, AccessModifier.Public), core, Optional(name)));
            if (!isStatic)
            {
                foreach (var (name, type) in core.Getters)
                    members.TryAdd(name, new(SubstituteConstructorMember(type, level.Substitutions), false,
                        fieldAccess.GetValueOrDefault(name, AccessModifier.Public), core));
                foreach (var (name, type) in core.Setters)
                    members.TryAdd(name, new(SubstituteConstructorMember(type, level.Substitutions), false,
                        fieldAccess.GetValueOrDefault(name, AccessModifier.Public), core));
            }
        }
        // ES static #private elements are brands of the constructor object itself, not
        // members of its public constructor type. Instance #brands remain enforced above.
        return members;
    }

    private bool OrdinaryConstructorMembersRelated(Dictionary<string, OrdinaryConstructorMember> targets,
        Dictionary<string, OrdinaryConstructorMember> sources, List<OrdinaryConstructorLevel> sourceHierarchy)
    {
        foreach (var (name, target) in targets)
        {
            if (!sources.TryGetValue(name, out var source))
            {
                if (target.IsOptional) continue;
                return false;
            }
            if (!target.IsOptional && source.IsOptional) return false;
            if (target.Access != AccessModifier.Public || source.Access != AccessModifier.Public)
            {
                bool SameOrigin(ClassMetadataCore core) => ReferenceEquals(core, target.DeclaringCore)
                    || target.DeclaringCore.DeclarationId != 0 && core.DeclarationId == target.DeclaringCore.DeclarationId;
                bool sameOrigin = SameOrigin(source.DeclaringCore);
                bool protectedOrigin = target.Access == AccessModifier.Protected
                    && sourceHierarchy.Any(level => SameOrigin(level.Core));
                if (target.Access == AccessModifier.Protected)
                {
                    if (source.Access == AccessModifier.Private || !protectedOrigin) return false;
                }
                else if (target.Access != source.Access || !sameOrigin) return false;
            }
            TypeInfo targetType = target.IsOptional && !_exactOptionalPropertyTypes
                ? CreateUnion(target.Type, TypeInfo.Undefined.Shared) : target.Type;
            if (!IsMemberCompatible(targetType, source.Type, target.IsMethod)) return false;
        }
        return true;
    }

    private TypeInfo.Record OrdinaryConstructorInstanceShape(List<OrdinaryConstructorLevel> levels,
        Dictionary<string, OrdinaryConstructorMember> members)
    {
        TypeInfo? Index(Func<ClassMetadataCore, TypeInfo?> select)
        {
            foreach (var level in levels)
                if (select(level.Core) is { } index) return SubstituteConstructorMember(index, level.Substitutions);
            return null;
        }
        return new TypeInfo.Record(members.ToFrozenDictionary(p => p.Key, p => p.Value.Type),
            Index(c => c.StringIndexType), Index(c => c.NumberIndexType), Index(c => c.SymbolIndexType),
            OptionalFields: members.Where(member => member.Value.IsOptional).Select(member => member.Key).ToFrozenSet());
    }

    private TypeInfo SubstituteConstructorMember(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
    {
        if (substitutions.Count == 0) return type;
        // Preserve function/member metadata while substituting inherited generic facets.
        // In particular, an empty substitution must preserve recursive self references.
        var result = type switch
        {
            TypeInfo.Function function => function with
            {
                ParamTypes = function.ParamTypes.Select(p => SubstituteConstructorMember(p, substitutions)).ToList(),
                ReturnType = SubstituteConstructorMember(function.ReturnType, substitutions),
                ThisType = function.ThisType is null ? null : SubstituteConstructorMember(function.ThisType, substitutions),
                InstantiatedTypeParamPositions = MarkInstantiatedParamPositions(function, substitutions)
            },
            TypeInfo.OverloadedFunction overloads => overloads with
            {
                Signatures = overloads.Signatures.Select(f => (TypeInfo.Function)SubstituteConstructorMember(f, substitutions)).ToList(),
                Implementation = (TypeInfo.Function)SubstituteConstructorMember(overloads.Implementation, substitutions)
            },
            TypeInfo.GenericFunction generic => SubstituteConstructorGenericMember(generic, substitutions),
            TypeInfo.Record record => SubstituteRecordMembers(record, t => SubstituteConstructorMember(t, substitutions)),
            TypeInfo.Array array => array with { ElementType = SubstituteConstructorMember(array.ElementType, substitutions) },
            TypeInfo.Promise promise => promise with { ValueType = SubstituteConstructorMember(promise.ValueType, substitutions) },
            TypeInfo.Union union => new TypeInfo.Union(union.Types.Select(t => SubstituteConstructorMember(t, substitutions)).ToList()),
            TypeInfo.Intersection intersection => new TypeInfo.Intersection(intersection.Types.Select(t => SubstituteConstructorMember(t, substitutions)).ToList()),
            TypeInfo.Tuple tuple when !tuple.HasSpread => tuple with
            {
                Elements = tuple.Elements.Select(e => e with { Type = SubstituteConstructorMember(e.Type, substitutions) }).ToList(),
                RestElementType = tuple.RestElementType is null ? null : SubstituteConstructorMember(tuple.RestElementType, substitutions)
            },
            _ => SubstitutePreservingSignatures(type, substitutions)
        };
        if (EditorFacts.IsEnabled) CopyEditorSignatureMetadata(type, result);
        return result;
    }

    private TypeInfo.GenericFunction SubstituteConstructorGenericMember(TypeInfo.GenericFunction function,
        Dictionary<string, TypeInfo> substitutions)
    {
        // A method's own parameters shadow the containing class's parameters.
        var local = new Dictionary<string, TypeInfo>(substitutions);
        foreach (var parameter in function.TypeParams) local.Remove(parameter.Name);
        TypeInfo Sub(TypeInfo type) => SubstituteConstructorMember(type, local);
        return function with
        {
            TypeParams = function.TypeParams.Select(p => p with
            {
                Constraint = p.Constraint is null ? null : Sub(p.Constraint),
                Default = p.Default is null ? null : Sub(p.Default)
            }).ToList(),
            ParamTypes = function.ParamTypes.Select(Sub).ToList(),
            ReturnType = Sub(function.ReturnType),
            ThisType = function.ThisType is null ? null : Sub(function.ThisType)
        };
    }
}
