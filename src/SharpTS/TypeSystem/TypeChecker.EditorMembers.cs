using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private const int EditorMemberLimit = 256;
    private const int EditorMemberDepth = 32;
    private ConditionalWeakTable<SourceDocument, Dictionary<Expr, bool>>? _editorReceivers;

    private bool TryEditorReceiver(Expr expression, out SourceDocument? document, out bool optional)
    {
        document = CurrentSourceDocument;
        optional = false;
        if (!ShouldCaptureEditorFacts || document?.EditorSyntax is not { } syntax) return false;
        var receivers = (_editorReceivers ??= new()).GetValue(document, _ =>
        {
            var result = new Dictionary<Expr, bool>(ReferenceEqualityComparer.Instance);
            foreach (var member in syntax.Members)
            {
                // Recovery may replace a member name, never the written receiver. A special
                // super node owns both; its keyword view is the explicit receiver proof.
                bool written = syntax.GetRecords(member.Receiver).Any(record => record.IsAuthoritative &&
                    (record.Kind is EditorSyntaxKind.Expression or EditorSyntaxKind.Grouping ||
                     member.Receiver is Expr.Super && record.Token?.Type == TokenType.SUPER));
                if (!written) continue;
                result[member.Receiver] = member.IsOptional &&
                    (!result.TryGetValue(member.Receiver, out bool previous) || previous);
            }
            return result;
        });
        return receivers.TryGetValue(expression, out optional);
    }

    private void ResetEditorReceiver(Expr expression)
    {
        if (TryEditorReceiver(expression, out var document, out _))
            EditorFacts.SetReceiverMembers(document, expression, [], isComplete: false);
    }

    private void CaptureEditorReceiver(Expr expression, TypeInfo type)
    {
        // super.method has no separate receiver AST and its returned type is the method's
        // type. CaptureEditorSuperReceiver handles the real superclass at the lookup site.
        if (expression is Expr.Super || !TryEditorReceiver(expression, out var document, out bool optional)) return;
        var projection = CollectEditorMembers(type, optional);
        EditorFacts.SetReceiverMembers(document, expression, projection.Members.Values.ToArray(),
            projection.IsComplete, projection.IsTruncated);
    }

    private void CaptureEditorSuperReceiver(Expr.Super expression)
    {
        if (!TryEditorReceiver(expression, out var document, out _) || _currentClass?.Superclass is not { } superclass) return;
        var projection = _inStaticMethod ? new EditorMemberProjection { IsComplete = false } :
            CollectEditorClassMembers(superclass, isStatic: false, includePrivate: false);
        EditorFacts.SetReceiverMembers(document, expression,
            projection.Members.Values.Where(candidate => candidate.Kind == EditorMemberKind.Method).ToArray(),
            projection.IsComplete, projection.IsTruncated, _inStaticMethod ? null : superclass);
    }

    private sealed class EditorMemberProjection
    {
        private int _visited;
        public Dictionary<string, EditorReceiverCandidate> Members { get; } = new(StringComparer.Ordinal);
        public bool IsComplete { get; set; } = true;
        public bool IsTruncated { get; set; }
        public bool Visit()
        {
            if (++_visited <= EditorMemberLimit * 4) return true;
            IsComplete = false; IsTruncated = true;
            return false;
        }
        public void Add(EditorReceiverCandidate candidate)
        {
            if (Members.ContainsKey(candidate.Name)) return;
            if (Members.Count == EditorMemberLimit) { IsTruncated = true; IsComplete = false; return; }
            Members.Add(candidate.Name, candidate);
        }
    }

    private sealed class EditorProjectionBudget { public int Remaining = EditorMemberLimit; }

    private EditorMemberProjection CollectEditorMembers(TypeInfo type, bool optional = false, int depth = 0,
        EditorProjectionBudget? budget = null)
    {
        ThrowIfCancellationRequested();
        budget ??= new();
        if (depth >= EditorMemberDepth || --budget.Remaining < 0) return new() { IsComplete = false, IsTruncated = true };
        if (type is TypeInfo.TypeParameter parameter)
            return parameter.Constraint is { } constraint ? CollectEditorMembers(constraint, optional, depth + 1, budget) : new() { IsComplete = false };
        if (type is TypeInfo.Union union)
        {
            // Bound the raw input before optional access filters nullish constituents.
            if (union.Types.Count > EditorMemberDepth) return new() { IsComplete = false, IsTruncated = true };
            var parts = union.Types.Where(part => !optional || part is not (TypeInfo.Null or TypeInfo.Undefined)).ToArray();
            if (parts.Length is 0 or > EditorMemberDepth) return new() { IsComplete = false, IsTruncated = parts.Length > EditorMemberDepth };
            var sets = parts.Select(part => CollectEditorMembers(part, optional, depth + 1, budget)).ToArray();
            if (sets.Any(set => !set.IsComplete)) return new() { IsComplete = false, IsTruncated = sets.Any(set => set.IsTruncated) };
            var result = new EditorMemberProjection();
            foreach (var (name, first) in sets[0].Members)
            {
                if (sets.Skip(1).Any(set => !set.Members.ContainsKey(name))) continue;
                var candidates = sets.Select(set => set.Members[name]).ToArray();
                // Present each branch with its own generic substitutions before combining.
                // A mixed generic union is honest partial data until that presentation exists.
                if (candidates.Any(candidate => candidate.Substitutions is { Count: > 0 }))
                { result.IsComplete = false; continue; }
                var types = candidates.Select(candidate => candidate.Type).OfType<TypeInfo>()
                    .Distinct<TypeInfo>(ReferenceEqualityComparer.Instance).ToList();
                result.Add(first with
                {
                    Type = types.Count == 0 ? null : types.Count == 1 ? types[0] : new TypeInfo.Union(types),
                    IsReadonly = candidates.Any(candidate => candidate.IsReadonly),
                    IsOptional = candidates.Any(candidate => candidate.IsOptional),
                    Source = candidates.All(candidate => ReferenceEquals(candidate.Source, first.Source)) ? first.Source : null,
                });
            }
            return result;
        }
        if (type is TypeInfo.Intersection intersection)
        {
            var result = new EditorMemberProjection();
            int count = 0;
            foreach (var part in intersection.Types)
            {
                if (++count > EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; break; }
                var next = CollectEditorMembers(part, optional, depth + 1, budget);
                result.IsComplete &= next.IsComplete;
                result.IsTruncated |= next.IsTruncated;
                foreach (var candidate in next.Members.Values) result.Add(candidate);
            }
            return result;
        }
        if (type is TypeInfo.Instance instance) return CollectEditorClassMembers(instance.ResolvedClassType, false);
        if (type is TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass })
            return CollectEditorClassMembers(type, true);

        var projection = new EditorMemberProjection();
        switch (type)
        {
            case TypeInfo.Record record:
                foreach (var (name, memberType) in record.Fields)
                {
                    if (!projection.Visit()) break;
                    projection.Add(new(name, memberType, record.MethodMembers?.Contains(name) == true ? EditorMemberKind.Method : EditorMemberKind.Property,
                        IsReadonly: record.IsReadonly || record.GetterOnlyFields?.Contains(name) == true,
                        IsOptional: record.OptionalFields?.Contains(name) == true));
                }
                return projection;
            case TypeInfo.Interface contract:
                CollectEditorInterfaceMembers(contract, projection, new(ReferenceEqualityComparer.Instance), null, 0);
                return projection;
            case TypeInfo.GenericInterface generic:
                CollectEditorGenericInterfaceMembers(generic, projection, null);
                return projection;
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface interfaceDefinition } instantiated:
                if (interfaceDefinition.TypeParams.Count > EditorMemberDepth) return new() { IsComplete = false, IsTruncated = true };
                var substitutions = interfaceDefinition.TypeParams.Zip(instantiated.TypeArguments).ToDictionary(pair => pair.First.Name, pair => pair.Second, StringComparer.Ordinal);
                CollectEditorGenericInterfaceMembers(interfaceDefinition, projection, substitutions);
                return projection;
            case TypeInfo.Namespace ns:
                foreach (var (name, memberType) in ns.Values) { if (!projection.Visit()) break; projection.Add(new(name, memberType, EditorMemberKind.NamespaceMember)); }
                foreach (var (name, memberType) in ns.Types) { if (!projection.Visit()) break; projection.Add(new(name, memberType, EditorMemberKind.NamespaceMember)); }
                return projection;
            case TypeInfo.Enum enumeration:
                foreach (string name in enumeration.Members.Keys) { if (!projection.Visit()) break; projection.Add(new(name, enumeration)); }
                return projection;
            case TypeInfo.String or TypeInfo.StringLiteral:
                foreach (string name in BuiltInTypes.StringApparentMemberNames)
                    projection.Add(new(name, BuiltInTypes.GetStringMemberType(name)));
                return projection;
            case TypeInfo.Array array:
                foreach (string name in BuiltInTypes.ArrayApparentMemberNames)
                    projection.Add(new(name, BuiltInTypes.GetArrayMemberType(name, array.ElementType)));
                return projection;
            case TypeInfo.Tuple tuple:
                if (tuple.Elements.Count > EditorMemberLimit) return new() { IsComplete = false, IsTruncated = true };
                var elementTypes = tuple.Elements.Select(element => element.Type).ToList();
                if (tuple.RestElementType is { } rest) elementTypes.Add(rest);
                TypeInfo elementType = elementTypes.Count switch { 0 => TypeInfo.Never.Shared, 1 => elementTypes[0], _ => new TypeInfo.Union(elementTypes) };
                foreach (string name in BuiltInTypes.ArrayApparentMemberNames)
                    projection.Add(new(name, BuiltInTypes.GetArrayMemberType(name, elementType)));
                return projection;
        }
        if (BuiltInTypes.GetInstanceMemberNames(type) is { } names)
        {
            foreach (string name in names) projection.Add(new(name, BuiltInTypes.GetInstanceMemberType(type, name)));
            return projection;
        }
        projection.IsComplete = false;
        return projection;
    }

    private void CollectEditorGenericInterfaceMembers(TypeInfo.GenericInterface contract, EditorMemberProjection result,
        IReadOnlyDictionary<string, TypeInfo>? substitutions)
    {
        foreach (var (name, memberType) in contract.Members)
        {
            if (!result.Visit()) break;
            MemberAccessBrand? brand = null;
            var brandSeen = new HashSet<TypeInfo.Interface>(ReferenceEqualityComparer.Instance);
            int brandBudget = EditorMemberDepth;
            bool brandComplete = true;
            foreach (var parent in contract.Extends ?? [])
            {
                brand = EditorInterfaceBrand(parent, name, brandSeen, ref brandBudget, out brandComplete);
                if (!brandComplete || brand is not null) break;
            }
            if (!brandComplete) { result.IsComplete = false; result.IsTruncated = true; continue; }
            if (brand is { } accessBrand && !EditorMemberAccessible(accessBrand.Access, accessBrand.DeclaringClassId)) continue;
            result.Add(new(name, memberType, contract.MethodMembers?.Contains(name) == true ? EditorMemberKind.Method : EditorMemberKind.Property,
                Access: brand?.Access ?? AccessModifier.Public, IsReadonly: contract.ReadonlyMembers?.Contains(name) == true,
                IsOptional: contract.OptionalMembers.Contains(name), Substitutions: substitutions));
        }
        var seen = new HashSet<TypeInfo.Interface>(ReferenceEqualityComparer.Instance);
        if (contract.Extends?.Count > EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; }
        foreach (var parent in (contract.Extends ?? []).Take(EditorMemberDepth + 1)) CollectEditorInterfaceMembers(parent, result, seen, substitutions, 0);
    }

    private void CollectEditorInterfaceMembers(TypeInfo.Interface contract, EditorMemberProjection result,
        HashSet<TypeInfo.Interface> seen, IReadOnlyDictionary<string, TypeInfo>? substitutions, int depth)
    {
        ThrowIfCancellationRequested();
        if (!seen.Add(contract)) return;
        if (depth >= EditorMemberDepth || seen.Count > EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; return; }
        foreach (var (name, memberType) in contract.Members)
        {
            if (!result.Visit()) break;
            int brandBudget = EditorMemberDepth;
            var brand = EditorInterfaceBrand(contract, name, new(ReferenceEqualityComparer.Instance), ref brandBudget, out bool brandComplete);
            if (!brandComplete) { result.IsComplete = false; result.IsTruncated = true; continue; }
            if (brand is { } accessBrand && !EditorMemberAccessible(accessBrand.Access, accessBrand.DeclaringClassId)) continue;
            result.Add(new(name, memberType, contract.MethodMembers?.Contains(name) == true ? EditorMemberKind.Method : EditorMemberKind.Property,
                Access: brand?.Access ?? AccessModifier.Public, IsReadonly: contract.IsMemberReadonly(name),
                IsOptional: contract.OptionalMembers.Contains(name), Substitutions: substitutions));
        }
        if (contract.Extends?.Count > EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; }
        foreach (var parent in (contract.Extends ?? []).Take(EditorMemberDepth + 1)) CollectEditorInterfaceMembers(parent, result, seen, substitutions, depth + 1);
    }

    private MemberAccessBrand? EditorInterfaceBrand(TypeInfo.Interface contract, string name,
        HashSet<TypeInfo.Interface> seen, ref int budget, out bool complete)
    {
        ThrowIfCancellationRequested();
        complete = true;
        if (--budget < 0) { complete = false; return null; }
        if (!seen.Add(contract)) return null;
        if (contract.MemberBrands is { } brands && brands.TryGetValue(name, out var own)) return own;
        // The checker's class-derived brand takes precedence over a merged public
        // interface member. Walk only known bases, with a separate bounded budget.
        foreach (var parent in contract.Extends ?? [])
        {
            var inherited = EditorInterfaceBrand(parent, name, seen, ref budget, out complete);
            if (!complete || inherited is not null) return inherited;
        }
        return contract.Members.ContainsKey(name) ? new(AccessModifier.Public, 0) : null;
    }

    private bool EditorSameClass(int left, int right) => left != 0 && right != 0 &&
        (left == right || Members.IsSameSourceClass(left, right));

    private static IEnumerable<KeyValuePair<string, T>> EditorMemberEntries<T>(
        IReadOnlyDictionary<string, T> entries, EditorMemberProjection projection)
    {
        foreach (var entry in entries)
        {
            if (!projection.Visit()) yield break;
            yield return entry;
        }
    }

    private bool EditorMemberAccessible(AccessModifier access, int ownerId) => access == AccessModifier.Public ||
        _currentClass is { } current && (access == AccessModifier.Private
            ? EditorSameClass(current.Core.DeclarationId, ownerId)
            : EnumerateClassCores(current).Take(EditorMemberDepth).Any(core => EditorSameClass(core.DeclarationId, ownerId)));

    private EditorMemberProjection CollectEditorClassMembers(TypeInfo classType, bool isStatic, bool includePrivate = true)
    {
        var result = new EditorMemberProjection();
        var seen = new HashSet<ClassMetadataCore>(ReferenceEqualityComparer.Instance);
        var shadowed = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, TypeInfo> substitutions = FrozenDictionary<string, TypeInfo>.Empty;
        TypeInfo? current = classType;
        int depth = 0;
        while (current is not null)
        {
            ThrowIfCancellationRequested();
            if (++depth > EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; break; }
            if (current is TypeInfo.MutableClass mutable) current = mutable.Frozen;
            ClassMetadataCore? core = current switch
            {
                TypeInfo.Class cls => cls.Core,
                TypeInfo.GenericClass generic => generic.Core,
                TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } => generic.Core,
                _ => null,
            };
            if (core is null || !seen.Add(core)) { result.IsComplete = false; break; }
            if (current is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass genericDefinition } genericInstance)
            {
                var composed = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                foreach (var pair in genericDefinition.TypeParams.Zip(genericInstance.TypeArguments).Take(EditorMemberDepth + 1))
                {
                    if (composed.Count == EditorMemberDepth) { result.IsComplete = false; result.IsTruncated = true; return result; }
                    int budget = EditorMemberLimit;
                    if (!TryEditorTypeArgument(pair.Second, substitutions, out var argument, ref budget, 0))
                    { result.IsComplete = false; result.IsTruncated = true; return result; }
                    composed[pair.First.Name] = argument;
                }
                substitutions = composed;
            }
            else substitutions = FrozenDictionary<string, TypeInfo>.Empty;

            MemberFacet facet = isStatic ? MemberFacet.Static : MemberFacet.Instance;
            void Add(string name, TypeInfo memberType, EditorMemberKind kind, AccessModifier access, bool readOnly,
                MemberFacet memberFacet, bool isPrivate = false)
            {
                if (isPrivate && (!includePrivate || _currentClass is null ||
                    !EditorSameClass(_currentClass.Core.DeclarationId, core.DeclarationId) ||
                    !HasPrivateSourceOwner(isStatic ? classType : new TypeInfo.Instance(classType), core.DeclarationId, isStatic))) return;
                // A foreign ECMAScript private name is a different lexical brand. Its
                // spelling cannot hide an accessible brand declared by a base class.
                if (!shadowed.Add(name) || !EditorMemberAccessible(access, core.DeclarationId)) return;
                SourceMemberSymbol? source = Members.ResolveSelected(core.DeclarationId, memberFacet, name).Candidates.SingleOrDefault();
                EditorMemberKind sourceKind = source?.Kind switch
                {
                    SourceMemberKind.AutoAccessor => EditorMemberKind.AutoAccessor,
                    SourceMemberKind.ParameterProperty => EditorMemberKind.ParameterProperty,
                    _ => kind,
                };
                result.Add(new(name, memberType, sourceKind, access, memberFacet, readOnly, Source: source, Substitutions: substitutions));
            }
            if (isStatic)
            {
                foreach (var (name, memberType) in EditorMemberEntries(core.StaticMethods, result)) Add(name, memberType, EditorMemberKind.Method, core.StaticMethodAccessMap.GetValueOrDefault(name), false, facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.StaticProperties, result)) Add(name, memberType, EditorMemberKind.Field, core.StaticFieldAccessMap.GetValueOrDefault(name), core.StaticReadonlyFields?.Contains(name) == true, facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.StaticPrivateFieldTypes, result)) Add(name, memberType, EditorMemberKind.Field, AccessModifier.Private, core.StaticReadonlyFields?.Contains(name) == true, MemberFacet.PrivateStatic, true);
                foreach (var (name, memberType) in EditorMemberEntries(core.StaticPrivateMethodTypes, result)) Add(name, memberType, EditorMemberKind.Method, AccessModifier.Private, false, MemberFacet.PrivateStatic, true);
            }
            else
            {
                foreach (var (name, memberType) in EditorMemberEntries(core.Getters, result)) Add(name, memberType, EditorMemberKind.Accessor, core.FieldAccess.GetValueOrDefault(name), !core.Setters.ContainsKey(name), facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.Methods, result)) Add(name, memberType, EditorMemberKind.Method, core.MethodAccess.GetValueOrDefault(name), false, facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.FieldTypes, result)) Add(name, memberType, EditorMemberKind.Field, core.FieldAccess.GetValueOrDefault(name), core.ReadonlyFields.Contains(name), facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.Setters, result)) Add(name, memberType, EditorMemberKind.Accessor, core.FieldAccess.GetValueOrDefault(name), false, facet);
                foreach (var (name, memberType) in EditorMemberEntries(core.PrivateFieldTypes, result)) Add(name, memberType, EditorMemberKind.Field, AccessModifier.Private, core.ReadonlyFields.Contains(name), MemberFacet.PrivateInstance, true);
                foreach (var (name, memberType) in EditorMemberEntries(core.PrivateMethodTypes, result)) Add(name, memberType, EditorMemberKind.Method, AccessModifier.Private, false, MemberFacet.PrivateInstance, true);
            }
            current = core.Superclass;
        }
        return result;
    }

    // Only composes already-known generic base arguments. No compatibility, lookup, indexed
    // access simplification or conditional evaluation runs for editor presentation.
    private static bool TryEditorTypeArgument(TypeInfo input, IReadOnlyDictionary<string, TypeInfo> substitutions,
        out TypeInfo output, ref int budget, int depth)
    {
        output = input;
        if (substitutions.Count == 0) return true;
        if (--budget < 0 || depth >= EditorMemberDepth) return false;
        int children = input switch
        {
            TypeInfo.InstantiatedGeneric generic => generic.TypeArguments.Count,
            TypeInfo.Union union => union.Types.Count,
            TypeInfo.Intersection intersection => intersection.Types.Count,
            TypeInfo.Tuple tuple => tuple.Elements.Count,
            TypeInfo.Function function => function.ParamTypes.Count,
            TypeInfo.Record record => record.Fields.Count,
            TypeInfo.RecursiveTypeAlias alias => alias.TypeArguments?.Count ?? 0,
            _ => 0,
        };
        if (children > budget) return false;
        bool success = true;
        int remaining = budget;
        TypeInfo Rewrite(TypeInfo type)
        {
            if (!TryEditorTypeArgument(type, substitutions, out var rewritten, ref remaining, depth + 1)) success = false;
            return rewritten;
        }
        output = input switch
        {
            TypeInfo.TypeParameter parameter => substitutions.GetValueOrDefault(parameter.Name) ?? parameter,
            TypeInfo.Array array => array with { ElementType = Rewrite(array.ElementType) },
            TypeInfo.Promise promise => promise with { ValueType = Rewrite(promise.ValueType) },
            TypeInfo.Map map => map with { KeyType = Rewrite(map.KeyType), ValueType = Rewrite(map.ValueType) },
            TypeInfo.Set set => set with { ElementType = Rewrite(set.ElementType) },
            TypeInfo.Instance instance => instance with { ClassType = Rewrite(instance.ClassType) },
            TypeInfo.InstantiatedGeneric generic => generic with { TypeArguments = generic.TypeArguments.Select(Rewrite).ToList() },
            TypeInfo.Union union => union with { Types = union.Types.Select(Rewrite).ToList() },
            TypeInfo.Intersection intersection => intersection with { Types = intersection.Types.Select(Rewrite).ToList() },
            TypeInfo.Tuple tuple => tuple with { Elements = tuple.Elements.Select(element => element with { Type = Rewrite(element.Type) }).ToList(), RestElementType = tuple.RestElementType is { } rest ? Rewrite(rest) : null },
            TypeInfo.Function function => function with { ParamTypes = function.ParamTypes.Select(Rewrite).ToList(), ReturnType = Rewrite(function.ReturnType), ThisType = function.ThisType is { } self ? Rewrite(self) : null },
            TypeInfo.Record record when record.CallSignatures is null && record.ConstructorSignatures is null => record with { Fields = record.Fields.ToDictionary(pair => pair.Key, pair => Rewrite(pair.Value)).ToFrozenDictionary(), StringIndexType = record.StringIndexType is { } stringIndex ? Rewrite(stringIndex) : null, NumberIndexType = record.NumberIndexType is { } numberIndex ? Rewrite(numberIndex) : null, SymbolIndexType = record.SymbolIndexType is { } symbolIndex ? Rewrite(symbolIndex) : null },
            TypeInfo.RecursiveTypeAlias alias => alias with { TypeArguments = alias.TypeArguments?.Select(Rewrite).ToList() },
            TypeInfo.Primitive or TypeInfo.String or TypeInfo.NumberLiteral or TypeInfo.StringLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Never or TypeInfo.Void or TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.Interface or TypeInfo.GenericInterface => input,
            _ => Unsupported(),
        };
        budget = remaining;
        return success;
        TypeInfo Unsupported() { success = false; return input; }
    }
}
