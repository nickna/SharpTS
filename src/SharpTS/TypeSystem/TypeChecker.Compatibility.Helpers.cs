using SharpTS.Runtime.BuiltIns;
using SharpTS.Parsing;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Helper methods for type compatibility checking - type predicates and class accessors.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Generic helper for type checking with union support.
    /// Checks if a type matches a predicate, with automatic handling for Any, Union, and TypeParameter types.
    /// </summary>
    /// <param name="t">The type to check.</param>
    /// <param name="baseTypeCheck">Predicate for checking base (non-Any, non-Union) types.</param>
    /// <returns>True if the type matches or is Any, or if all union members match, or if TypeParameter constraint matches.</returns>
    private bool IsTypeOfKind(TypeInfo t, Func<TypeInfo, bool> baseTypeCheck) =>
        baseTypeCheck(t) ||
        t is TypeInfo.Any ||
        (t is TypeInfo.Union u && u.FlattenedTypes.All(inner => IsTypeOfKind(inner, baseTypeCheck))) ||
        (t is TypeInfo.TypeParameter tp && tp.Constraint != null && IsTypeOfKind(tp.Constraint, baseTypeCheck));

    private bool IsNumber(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER ||
            type is TypeInfo.NumberLiteral);

    private bool IsString(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.String ||
            type is TypeInfo.StringLiteral);

    private bool IsBigInt(TypeInfo t) =>
        IsTypeOfKind(t, type => type is TypeInfo.BigInt or TypeInfo.BigIntLiteral);

    /// <summary>
    /// True if <paramref name="t"/> is symbol, or a union with AT LEAST ONE symbol constituent.
    /// Deliberately NOT built on the Any-permissive <see cref="IsTypeOfKind"/> (which would make
    /// `any` satisfy this too) — operator operand checks use this to decide whether tsc's
    /// symbol-specific rejection applies, e.g. `(sym || "") + ""` still gets "The '+' operator
    /// cannot be applied to type 'symbol'" even though the left side isn't purely symbol, while
    /// `sym + any` must NOT be treated as "both sides are symbol".
    /// </summary>
    private static bool ContainsSymbolType(TypeInfo t) => t switch
    {
        TypeInfo.Symbol or TypeInfo.UniqueSymbol => true,
        TypeInfo.Union u => u.FlattenedTypes.Any(ContainsSymbolType),
        _ => false,
    };

    /// <summary>
    /// Checks if a type is a primitive (not valid as WeakMap key or WeakSet value).
    /// </summary>
    private bool IsPrimitiveType(TypeInfo t) => t is TypeInfo.String or TypeInfo.Primitive or TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.BigIntLiteral or TypeInfo.Symbol or TypeInfo.UniqueSymbol;

    /// <summary>
    /// Checks if a type is an object type (has properties that could be mutated).
    /// Used for determining if passing to a function should invalidate property narrowings.
    /// </summary>
    private static bool IsObjectType(TypeInfo t) => t is TypeInfo.Record
        or TypeInfo.Interface
        or TypeInfo.Instance
        or TypeInfo.Class
        or TypeInfo.GenericClass
        or TypeInfo.InstantiatedGeneric
        or TypeInfo.Array
        or TypeInfo.Map
        or TypeInfo.Set;

    private static TypeInfo? GetSuperclass(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.Superclass, gc => gc.Superclass);

    /// <summary>
    /// True when type parameter <paramref name="tp"/> is directly or indirectly constrained to a type
    /// parameter named <paramref name="targetName"/> (i.e. <c>tp extends … extends targetName</c>).
    /// Per TypeScript, a source parameter is assignable to a target parameter only when its constraint
    /// chain reaches it.
    /// </summary>
    private static bool TypeParameterConstrainedTo(TypeInfo.TypeParameter tp, string targetName)
    {
        var current = tp.Constraint;
        for (int guard = 0; current is TypeInfo.TypeParameter c && guard < 64; guard++)
        {
            if (c.Name == targetName) return true;
            current = c.Constraint;
        }
        return false;
    }

    /// <summary>
    /// The apparent (constraint) type of a type parameter: walks the constraint chain to the first
    /// non-parameter constraint (e.g. <c>T extends U extends Date</c> → Date). Null if unconstrained.
    /// </summary>
    private static TypeInfo? ApparentTypeOf(TypeInfo.TypeParameter tp)
    {
        TypeInfo? current = tp.Constraint;
        for (int guard = 0; current is TypeInfo.TypeParameter c && guard < 64; guard++)
            current = c.Constraint;
        return current;
    }

    /// <summary>
    /// The concrete base type an assigned value contributes when narrowing a reference whose
    /// declared type is a bare type parameter — tsc's narrow-by-assignment over the constraint
    /// domain. Only a deferred distributive conditional (e.g. <c>NonNullable&lt;T&gt;</c>,
    /// <c>Extract&lt;T, …&gt;</c>) whose check parameter is constrained yields a base type: the
    /// type it evaluates to when that parameter is instantiated with its constraint (so
    /// <c>NonNullable&lt;T&gt;</c> with <c>T extends string | undefined</c> → <c>string</c>).
    ///
    /// A bare type parameter assigned value (e.g. <c>u = t</c> with <c>t: T</c>) is deliberately
    /// NOT reduced to its constraint: collapsing it to the constraint would make a later
    /// in-chain assignment (<c>v = u</c> with <c>V</c> the constraint) spuriously fail. Returns
    /// null in every case where no concrete base can be derived — the caller then leaves the
    /// reference at its declared type rather than installing a vacuous (or harmful) narrowing.
    /// </summary>
    private TypeInfo? AssignmentNarrowedBaseType(TypeInfo value)
    {
        if (value is TypeInfo.ConditionalType cond &&
            cond.IsDistributive && cond.CheckType is TypeInfo.TypeParameter checkTp &&
            ApparentTypeOf(checkTp) is { } checkConstraint)
        {
            var instantiated = EvaluateConditionalType(
                cond, new Dictionary<string, TypeInfo> { [checkTp.Name] = checkConstraint });
            if (instantiated is not (TypeInfo.ConditionalType or TypeInfo.Never or TypeInfo.Any or TypeInfo.Unknown))
                return instantiated;
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="cls"/> (or any class in its hierarchy) carries a nominal brand,
    /// i.e. declares a private or protected member. TypeScript compares classes structurally for
    /// assignment except when the target type is so branded, in which case it requires the source
    /// to originate from the same class.
    /// </summary>
    private static bool HasNominalClassBrand(TypeInfo.Class cls)
    {
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            var core = c.Core;
            if (core.PrivateFieldTypes.Count > 0 || core.PrivateMethodTypes.Count > 0)
                return true;
            foreach (var access in core.FieldAccess.Values)
                if (access != AccessModifier.Public) return true;
            foreach (var access in core.MethodAccess.Values)
                if (access != AccessModifier.Public) return true;
            current = GetSuperclass(current);
        }
        return false;
    }

    private static bool IsPublicMember(FrozenDictionary<string, AccessModifier> access, string name)
        => !access.TryGetValue(name, out var mod) || mod == AccessModifier.Public;

    // ─── Member accessibility (TypeScript private/protected) ──────────────────────────────────
    // TypeScript relates object types structurally, but a member declared `private` or `protected`
    // is matched nominally: it is only compatible with the *identical* declaration (private) or a
    // declaration in a derived class (protected). This is checked per shared member, in both
    // directions, so it is independent of which whole-type "brand" short-circuits also fire.

    /// <summary>Object-like types whose shared members participate in the accessibility relation.</summary>
    private static bool IsObjectLikeForAccessibility(TypeInfo t) => t is
        TypeInfo.Instance or TypeInfo.Class or TypeInfo.GenericClass or
        TypeInfo.InstantiatedGeneric or TypeInfo.Interface or TypeInfo.Record;

    /// <summary>Resolves an object-like type to its class-like form (or null) for hierarchy walking.</summary>
    private static TypeInfo? ResolveToClassLike(TypeInfo? type) => type switch
    {
        TypeInfo.Instance inst => inst.ResolvedClassType,
        TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric => type,
        _ => null,
    };

    /// <summary>Walks the class metadata cores of a class-like type from most-derived to base.</summary>
    private static IEnumerable<ClassMetadataCore> EnumerateClassCores(TypeInfo? type)
    {
        TypeInfo? current = ResolveToClassLike(type);
        for (int guard = 0; current is not null && guard < 256; guard++)
        {
            ClassMetadataCore? core = current switch
            {
                TypeInfo.Class c => c.Core,
                TypeInfo.GenericClass gc => gc.Core,
                TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass igc } => igc.Core,
                _ => null,
            };
            if (core is null) yield break;
            yield return core;
            current = core.Superclass;
        }
    }

    /// <summary>True when a class-like type declares a TypeScript <c>private</c>/<c>protected</c>
    /// member anywhere in its hierarchy (ES <c>#private</c> fields are tracked separately).</summary>
    private static bool HasTsAccessModifierMember(TypeInfo type)
    {
        foreach (var core in EnumerateClassCores(type))
        {
            foreach (var access in core.FieldAccess.Values)
                if (access != AccessModifier.Public) return true;
            foreach (var access in core.MethodAccess.Values)
                if (access != AccessModifier.Public) return true;
        }
        return false;
    }

    /// <summary>True when the type carries any non-public member relevant to the accessibility relation.</summary>
    private static bool HasAnyNonPublicMember(TypeInfo type) => type switch
    {
        TypeInfo.Interface itf => InterfaceHasNonPublicMember(itf),
        TypeInfo.Record => false,
        _ => HasTsAccessModifierMember(type),
    };

    /// <summary>An interface carries non-public members when it (or any base it extends — e.g. a class
    /// it inherited private members from via <c>interface I extends SomeClass</c>) brands a member.</summary>
    private static bool InterfaceHasNonPublicMember(TypeInfo.Interface itf)
    {
        if (itf.MemberBrands is { Count: > 0 }) return true;
        if (itf.Extends is { } bases)
            foreach (var b in bases)
                if (InterfaceHasNonPublicMember(b)) return true;
        return false;
    }

    /// <summary>The names of non-public (private/protected) members of an object-like type.</summary>
    private static IEnumerable<string> NonPublicMemberNames(TypeInfo type)
    {
        if (type is TypeInfo.Interface itf)
        {
            foreach (var name in InterfaceNonPublicMemberNames(itf, [])) yield return name;
            yield break;
        }
        if (type is TypeInfo.Record) yield break;
        var seen = new HashSet<string>();
        foreach (var core in EnumerateClassCores(type))
        {
            foreach (var (name, mod) in core.FieldAccess)
                if (mod != AccessModifier.Public && seen.Add(name)) yield return name;
            foreach (var (name, mod) in core.MethodAccess)
                if (mod != AccessModifier.Public && seen.Add(name)) yield return name;
        }
    }

    /// <summary>Non-public member names of an interface, including those inherited from extended bases.
    /// Own members shadow base members of the same name.</summary>
    private static IEnumerable<string> InterfaceNonPublicMemberNames(TypeInfo.Interface itf, HashSet<string> shadowed)
    {
        if (itf.MemberBrands is { } brands)
            foreach (var name in brands.Keys)
                if (!shadowed.Contains(name) && shadowed.Add(name)) yield return name;
        if (itf.Extends is { } bases)
        {
            var own = itf.Members.Keys.ToHashSet();
            foreach (var b in bases)
                foreach (var name in InterfaceNonPublicMemberNames(b, shadowed))
                    if (!own.Contains(name)) yield return name;
        }
    }

    /// <summary>
    /// Looks up a member's effective accessibility and declaring-class identity. Returns false when
    /// the member is absent (a missing member is a structural concern, not an accessibility one).
    /// Public members report <see cref="AccessModifier.Public"/> and a declaring id of 0.
    /// </summary>
    private static bool TryGetMemberAccessBrand(TypeInfo type, string name, out AccessModifier access, out int declaringId)
    {
        access = AccessModifier.Public;
        declaringId = 0;
        switch (type)
        {
            case TypeInfo.Record r:
                return r.Fields.ContainsKey(name);
            case TypeInfo.Interface itf:
                return TryGetInterfaceMemberBrand(itf, name, out access, out declaringId);
            default:
                foreach (var core in EnumerateClassCores(type))
                {
                    if (core.FieldTypes.ContainsKey(name))
                    {
                        if (core.FieldAccess.TryGetValue(name, out var fa)) access = fa;
                        declaringId = core.DeclarationId;
                        return true;
                    }
                    if (name != "constructor" && core.Methods.ContainsKey(name))
                    {
                        if (core.MethodAccess.TryGetValue(name, out var ma)) access = ma;
                        declaringId = core.DeclarationId;
                        return true;
                    }
                    if (core.Getters.ContainsKey(name))
                    {
                        declaringId = core.DeclarationId;
                        return true;
                    }
                }
                return false;
        }
    }

    /// <summary>Resolves a member's accessibility brand on an interface, consulting its own brands,
    /// then own (public) members, then — for members inherited from an extended class — its bases.</summary>
    private static bool TryGetInterfaceMemberBrand(TypeInfo.Interface itf, string name, out AccessModifier access, out int declaringId)
    {
        access = AccessModifier.Public;
        declaringId = 0;
        if (itf.MemberBrands is { } brands && brands.TryGetValue(name, out var brand))
        {
            access = brand.Access;
            declaringId = brand.DeclaringClassId;
            return true;
        }
        if (itf.Members.ContainsKey(name)) return true; // own interface member shadows bases (public)
        if (itf.Extends is { } bases)
            foreach (var b in bases)
                if (TryGetInterfaceMemberBrand(b, name, out access, out declaringId)) return true;
        access = AccessModifier.Public;
        declaringId = 0;
        return false;
    }

    /// <summary>True when <paramref name="source"/>'s class hierarchy includes the class identified by
    /// <paramref name="declaringId"/> — the derivation rule that lets a derived class satisfy a
    /// protected member of its base.</summary>
    private static bool SourceDerivesFromDeclaration(TypeInfo source, int declaringId)
    {
        foreach (var core in EnumerateClassCores(source))
            if (core.DeclarationId == declaringId) return true;
        return false;
    }

    /// <summary>
    /// The TypeScript member-accessibility relation: assignability fails when a member shared by both
    /// object types has conflicting accessibility origins. A public member cannot satisfy a
    /// private/protected one (or vice versa); two private members must come from the same declaration;
    /// a protected member additionally accepts a source declared in a derived class.
    /// </summary>
    private bool MembersAccessibilityCompatible(TypeInfo target, TypeInfo source)
    {
        if (!HasAnyNonPublicMember(target) && !HasAnyNonPublicMember(source))
            return true; // fast path: nothing non-public, so no nominal constraints

        var checkd = new HashSet<string>();
        foreach (var name in NonPublicMemberNames(target).Concat(NonPublicMemberNames(source)))
        {
            if (!checkd.Add(name)) continue;
            if (!TryGetMemberAccessBrand(target, name, out var tAccess, out var tDecl)) continue;
            if (!TryGetMemberAccessBrand(source, name, out var sAccess, out var sDecl)) continue;
            if (tAccess == AccessModifier.Public && sAccess == AccessModifier.Public) continue;
            if (tAccess != sAccess) return false;       // public vs non-public, or private vs protected
            if (tDecl == sDecl) continue;               // same declaration is always compatible
            if (tAccess == AccessModifier.Protected && SourceDerivesFromDeclaration(source, tDecl)) continue;
            return false;                               // private (or unrelated protected) needs identity
        }
        return true;
    }

    /// <summary>True when a class declares an ES <c>#private</c> field/method anywhere in its hierarchy.
    /// Such classes stay strictly nominal — a <c>#</c>-member has no structural surface to relate.</summary>
    private static bool HasEsPrivateBrand(TypeInfo.Class cls)
    {
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            if (c.Core.PrivateFieldTypes.Count > 0 || c.Core.PrivateMethodTypes.Count > 0) return true;
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Collects all instance members (public and TypeScript private/protected) of a class and its
    /// superclasses. Used for a TypeScript-branded target whose private members must be structurally
    /// provided by the source; accessibility itself is enforced separately by
    /// <see cref="MembersAccessibilityCompatible"/>.
    /// </summary>
    private Dictionary<string, TypeInfo> CollectAllInstanceMembers(TypeInfo.Class cls)
    {
        Dictionary<string, TypeInfo> members = [];
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            var core = c.Core;
            foreach (var (name, type) in core.FieldTypes) members.TryAdd(name, type);
            foreach (var (name, type) in core.Methods)
                if (name != "constructor") members.TryAdd(name, type);
            foreach (var (name, type) in core.Getters) members.TryAdd(name, type);
            current = GetSuperclass(current);
        }
        // A generic-instantiation superclass (`class Sub extends Base<number>`) is not a
        // TypeInfo.Class, so the walk above stops at it. Fold in the generic base's members —
        // including private/protected — with its type arguments substituted, mirroring the public
        // collector (CollectPublicInstanceMembers, #506) but in all-members mode (#639). Derived
        // members already collected shadow these.
        if (current is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass baseGc } baseIg)
            foreach (var (name, type) in CollectGenericClassMembers(baseGc, baseIg.TypeArguments, includeNonPublic: true))
                members.TryAdd(name, type);
        return members;
    }

    /// <summary>The required (non-optional) member names of an object-like type.</summary>
    private IEnumerable<string> RequiredMemberNames(TypeInfo t)
    {
        switch (t)
        {
            case TypeInfo.Record r:
                foreach (var k in r.Fields.Keys)
                    if (!r.IsFieldOptional(k)) yield return k;
                break;
            case TypeInfo.Interface i:
                var optional = i.GetAllOptionalMembers().ToHashSet();
                foreach (var m in i.GetAllMembers())
                    if (!optional.Contains(m.Key)) yield return m.Key;
                break;
            case TypeInfo.Class c:
                foreach (var k in CollectPublicInstanceMembers(c).Keys) yield return k;
                break;
            case TypeInfo.Instance { ResolvedClassType: TypeInfo.Class ic }:
                foreach (var k in CollectPublicInstanceMembers(ic).Keys) yield return k;
                break;
        }
    }

    /// <summary>
    /// Chooses the diagnostic code for a failed assignment: <c>TS2741</c> ("Property 'X' is missing in
    /// type … but required in type …") when the object-like source lacks a property the target
    /// requires, otherwise the generic <c>TS2322</c>. tsc reports missing-property failures with TS2741
    /// distinctly from type mismatches (TS2322), and the conformance runner matches on the code.
    /// </summary>
    private string AssignmentDiagnosticCode(TypeInfo target, TypeInfo source)
    {
        // Union sides report the generic assignability code (tsc nests the per-constituent
        // detail under the "Type 'D | E' is not assignable…" headline).
        if (target is TypeInfo.Union || source is TypeInfo.Union)
            return "TS2322";

        // Weak-type failures report tsc's dedicated code.
        if (FailsWeakTypeCheck(target, source))
            return "TS2559";

        // tsc promotes the missing-property elaboration to a top-level TS2741 only the FIRST time
        // a given type pair fails — its relation cache makes repeated failures of the same pair
        // report the plain headline (TS2322). unionTypesAssignability pins this: `d = e` is
        // TS2741 at its first occurrence and TS2322 when repeated later in the file.
        // The pair is tracked by INSTANCE identity, mirroring tsc's per-type-id relation cache:
        // structurally identical but separately declared/written types (two same-shape anonymous
        // annotations, same-name interfaces in different modules) are distinct pairs and each get
        // their first TS2741 (assignmentCompatWithObjectMembersOptionality2 pins this).
        if (MissingRequiredMember(target, source))
        {
            _ts2741Reported ??= new(IdentityPairComparer.Instance);
            if (_ts2741Reported.Add((target, source)))
                return "TS2741";
        }
        return "TS2322";
    }

    /// <summary>Type pairs whose missing-property failure has already been reported as TS2741.</summary>
    private HashSet<(TypeInfo Expected, TypeInfo Actual)>? _ts2741Reported;

    /// <summary>Reference-identity comparer for type pairs (tsc's relation caches key on type ids).</summary>
    private sealed class IdentityPairComparer : IEqualityComparer<(TypeInfo Expected, TypeInfo Actual)>
    {
        public static readonly IdentityPairComparer Instance = new();

        public bool Equals((TypeInfo Expected, TypeInfo Actual) x, (TypeInfo Expected, TypeInfo Actual) y) =>
            ReferenceEquals(x.Expected, y.Expected) && ReferenceEquals(x.Actual, y.Actual);

        public int GetHashCode((TypeInfo Expected, TypeInfo Actual) obj) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Expected),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Actual));
    }

    /// <summary>True when the object-like source lacks a member the target requires.</summary>
    private bool MissingRequiredMember(TypeInfo target, TypeInfo source)
    {
        if (source is not (TypeInfo.Record or TypeInfo.Interface or TypeInfo.Class or TypeInfo.Instance))
            return false;
        foreach (var name in RequiredMemberNames(target))
            if (GetMemberType(source, name) is null)
                return true;
        return false;
    }

    /// <summary>Type-parameter substitution map for a generic-class instantiation.</summary>
    private static Dictionary<string, TypeInfo> GenericClassSubs(TypeInfo.GenericClass gc, List<TypeInfo> args)
    {
        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gc.TypeParams.Count && i < args.Count; i++)
            subs[gc.TypeParams[i].Name] = args[i];
        return subs;
    }

    /// <summary>
    /// The string index signature value type of any object-like type, with generic-class type
    /// arguments substituted (e.g. <c>A&lt;Base&gt;</c> where <c>A&lt;T&gt;</c> has <c>[x]: T</c> yields Base). Null if none.
    /// </summary>
    private TypeInfo? StringIndexOf(TypeInfo t) => t switch
    {
        TypeInfo.Record r => r.StringIndexType,
        TypeInfo.Interface i => i.StringIndexType,
        TypeInfo.Class c => c.StringIndexType,
        TypeInfo.Instance inst => StringIndexOf(inst.ResolvedClassType),
        // SubstitutePreservingSignatures so a construct/call-signature-valued index (`[k: string]:
        // new () => T`, a Record carrying a ConstructorSignature) survives substitution and can be
        // related against a derived override — plain Substitute would collapse it to `{}` and the
        // class-extends index check (TS2415) would vacuously pass under generics (#896). Identical to
        // Substitute for the common non-Record index value (a bare T, a primitive, …).
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig =>
            gc.Core.StringIndexType is { } sit ? SubstitutePreservingSignatures(sit, GenericClassSubs(gc, ig.TypeArguments)) : null,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface gi } ig =>
            gi.StringIndexType is { } sit ? SubstitutePreservingSignatures(sit, GenericInterfaceSubs(gi, ig.TypeArguments)) : null,
        _ => null
    };

    /// <summary>The number index signature value type of any object-like type (generic args substituted), or null.</summary>
    private TypeInfo? NumberIndexOf(TypeInfo t) => t switch
    {
        TypeInfo.Record r => r.NumberIndexType,
        TypeInfo.Interface i => i.NumberIndexType,
        TypeInfo.Class c => c.NumberIndexType,
        TypeInfo.Instance inst => NumberIndexOf(inst.ResolvedClassType),
        // See StringIndexOf: SubstitutePreservingSignatures keeps a construct/call-signature-valued
        // index alive through substitution so TS2415 fires under generics (#896).
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig =>
            gc.Core.NumberIndexType is { } nit ? SubstitutePreservingSignatures(nit, GenericClassSubs(gc, ig.TypeArguments)) : null,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface gi } ig =>
            gi.NumberIndexType is { } nit ? SubstitutePreservingSignatures(nit, GenericInterfaceSubs(gi, ig.TypeArguments)) : null,
        _ => null
    };

    /// <summary>The named (non-index) member value types of any object-like type (generic args substituted).</summary>
    private IEnumerable<TypeInfo> NamedMemberTypesOf(TypeInfo t) => t switch
    {
        TypeInfo.Record r => r.Fields.Values,
        TypeInfo.Interface i => i.GetAllMembers().Select(m => m.Value),
        TypeInfo.Class c => CollectPublicInstanceMembers(c).Values,
        TypeInfo.Instance inst => NamedMemberTypesOf(inst.ResolvedClassType),
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig =>
            CollectGenericClassMembers(gc, ig.TypeArguments).Values,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface gi } ig =>
            gi.Members.Values.Select(m => Substitute(m, GenericInterfaceSubs(gi, ig.TypeArguments))),
        _ => []
    };

    /// <summary>Type-parameter substitution map for a generic-interface instantiation.</summary>
    private static Dictionary<string, TypeInfo> GenericInterfaceSubs(TypeInfo.GenericInterface gi, List<TypeInfo> args)
    {
        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gi.TypeParams.Count && i < args.Count; i++)
            subs[gi.TypeParams[i].Name] = args[i];
        return subs;
    }

    /// <summary>
    /// True when <paramref name="actual"/> satisfies the index signatures of <paramref name="expected"/>
    /// (TypeScript "index signatures must be compatible"). For a string index <c>[s: string]: V</c> on
    /// the target, the source's string/number index types and every named member must be assignable to
    /// <c>V</c>; likewise for a number index. Returns true when the target declares no index signature.
    /// </summary>
    private bool IndexSignaturesSatisfied(TypeInfo expected, TypeInfo actual)
    {
        var expStr = StringIndexOf(expected);
        var expNum = NumberIndexOf(expected);
        if (expStr is null && expNum is null) return true;

        if (expStr is not null)
        {
            if (StringIndexOf(actual) is { } actStr && !IsCompatible(expStr, actStr)) return false;
            if (NumberIndexOf(actual) is { } actNum && !IsCompatible(expStr, actNum)) return false;
            foreach (var memberType in NamedMemberTypesOf(actual))
                if (!IsCompatible(expStr, memberType)) return false;
        }
        if (expNum is not null)
        {
            if (NumberIndexOf(actual) is { } actNum && !IsCompatible(expNum, actNum)) return false;
            // Numeric-named members must satisfy the number index signature. Unlike the
            // string-index case above, an OPTIONAL member's implicit `undefined` is NOT exempt
            // (tsc's optional-property exemption applies to string index signatures only), so
            // under strictNullChecks `{ 1?: string }` fails `[key: number]: string`.
            foreach (var (name, memberType, isOptional) in NamedMembersWithOptionality(actual))
            {
                if (!double.TryParse(name, out _)) continue;
                var effective = isOptional && _strictNullChecks
                    ? CreateUnion(memberType, TypeInfo.Undefined.Shared)
                    : memberType;
                if (!IsCompatible(expNum, effective)) return false;
            }
        }
        return true;
    }

    /// <summary>True when the named member of an object-like type is declared optional.</summary>
    private bool IsMemberOptionalOn(TypeInfo t, string name) => t switch
    {
        TypeInfo.Record r => r.IsFieldOptional(name),
        TypeInfo.Interface i => i.GetAllOptionalMembers().Contains(name),
        TypeInfo.Instance inst => IsMemberOptionalOn(inst.ResolvedClassType, name),
        _ => false,
    };

    /// <summary>
    /// tsc's WEAK TYPE check (TS2559): a target with at least one property, ALL of them optional,
    /// is not satisfied by a source that has properties but NONE in common — even though the
    /// all-optional target would otherwise be vacuously satisfied. Targets with index or
    /// call/construct signatures are exempt, as are empty sources.
    /// </summary>
    private bool FailsWeakTypeCheck(TypeInfo expected, TypeInfo actual)
    {
        List<string> targetNames;
        HashSet<string> targetOptional;
        switch (expected)
        {
            case TypeInfo.Interface i when !i.IsCallable && !i.IsConstructable &&
                                           i.StringIndexType is null && i.NumberIndexType is null:
                targetNames = i.GetAllMembers().Select(kv => kv.Key).ToList();
                targetOptional = i.GetAllOptionalMembers().ToHashSet();
                break;
            case TypeInfo.Record r when !r.IsCallable && !r.IsConstructable && !r.HasIndexSignature:
                targetNames = r.Fields.Keys.ToList();
                targetOptional = r.OptionalFields?.ToHashSet() ?? [];
                break;
            default:
                return false;
        }
        if (targetNames.Count == 0 || !targetNames.All(targetOptional.Contains))
            return false;

        var sourceNames = NamedMembersWithOptionality(actual).Select(m => m.Name).ToList();
        return sourceNames.Count > 0 && !sourceNames.Any(targetNames.Contains);
    }

    /// <summary>Named members of an object-like type with their declared optionality.</summary>
    private IEnumerable<(string Name, TypeInfo Type, bool IsOptional)> NamedMembersWithOptionality(TypeInfo t)
    {
        switch (t)
        {
            case TypeInfo.Record r:
                foreach (var (name, type) in r.Fields)
                    yield return (name, type, r.IsFieldOptional(name));
                break;
            case TypeInfo.Interface i:
                var optional = i.GetAllOptionalMembers().ToHashSet();
                foreach (var (name, type) in i.GetAllMembers())
                    yield return (name, type, optional.Contains(name));
                break;
            case TypeInfo.Instance inst:
                foreach (var entry in NamedMembersWithOptionality(inst.ResolvedClassType))
                    yield return entry;
                break;
            case TypeInfo.Class c:
                foreach (var (name, type) in CollectPublicInstanceMembers(c))
                    yield return (name, type, false);
                break;
            default:
                // Built-in object types (Date, RegExp, Map, …, Error, Buffer, …) model their members
                // through the shared apparent-members projection, not a member dictionary. Surface them
                // so the weak-type check (TS2559) sees a built-in source's properties (#529); a built-in
                // sharing no member with an all-optional target is rejected, matching tsc. Built-in
                // instance members are never optional.
                if (BuiltInTypes.GetInstanceMemberNames(t) is { } builtInNames)
                    foreach (var builtInName in builtInNames)
                        yield return (builtInName,
                            BuiltInTypes.GetInstanceMemberType(t, builtInName) ?? TypeInfo.Any.Shared, false);
                break;
        }
    }

    /// <summary>
    /// Returns the parameter type of <paramref name="f"/> at <paramref name="index"/>, expanding a
    /// trailing rest parameter to its element type so it covers any position at or beyond the rest
    /// slot (e.g. <c>(...a: number[])</c> yields <c>number</c> for every position). Returns null when
    /// the position is past a non-rest parameter list.
    /// </summary>
    private static TypeInfo? EffectiveParamType(TypeInfo.Function f, int index)
    {
        int count = f.ParamTypes.Count;
        if (f.HasRestParam && count > 0)
        {
            int restIndex = count - 1;
            if (index < restIndex) return f.ParamTypes[index];
            return f.ParamTypes[restIndex] is TypeInfo.Array arr ? arr.ElementType : f.ParamTypes[restIndex];
        }
        return index < count ? f.ParamTypes[index] : null;
    }

    /// <summary>
    /// Collects the public instance members (fields, methods, getters) of a class and its
    /// superclasses into a structural member map. Derived members shadow inherited ones.
    /// Used to check structural assignability against an unbranded target class.
    /// </summary>
    private Dictionary<string, TypeInfo> CollectPublicInstanceMembers(TypeInfo.Class cls)
    {
        Dictionary<string, TypeInfo> members = [];
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            var core = c.Core;
            foreach (var (name, type) in core.FieldTypes)
                if (IsPublicMember(core.FieldAccess, name) && !members.ContainsKey(name))
                    members[name] = type;
            foreach (var (name, type) in core.Methods)
                // The constructor is keyed as a method named "constructor" but is not part of the
                // instance type's structural surface in TypeScript — exclude it.
                if (name != "constructor" && IsPublicMember(core.MethodAccess, name) && !members.ContainsKey(name))
                    members[name] = type;
            foreach (var (name, type) in core.Getters)
                if (!members.ContainsKey(name))
                    members[name] = type;
            current = GetSuperclass(current);
        }
        // A generic-instantiation superclass (`class Sub extends Base<number>`) is not a
        // TypeInfo.Class, so the walk above stops at it. Fold in the generic base's members with its
        // type arguments substituted; CollectGenericClassMembers recurses through the rest of the
        // chain. Already-collected derived members shadow these. (#506)
        if (current is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass baseGc } baseIg)
            foreach (var (name, type) in CollectGenericClassMembers(baseGc, baseIg.TypeArguments))
                members.TryAdd(name, type);
        return members;
    }

    /// <summary>True when a class metadata core declares a private/protected member (nominal brand).</summary>
    private static bool CoreHasNominalBrand(ClassMetadataCore core)
    {
        if (core.PrivateFieldTypes.Count > 0 || core.PrivateMethodTypes.Count > 0) return true;
        foreach (var access in core.FieldAccess.Values)
            if (access != AccessModifier.Public) return true;
        foreach (var access in core.MethodAccess.Values)
            if (access != AccessModifier.Public) return true;
        return false;
    }

    /// <summary>Nominal-brand check for a generic class (own core plus a non-generic superclass chain).</summary>
    private static bool GenericClassHasNominalBrand(TypeInfo.GenericClass gc)
    {
        if (CoreHasNominalBrand(gc.Core)) return true;
        return gc.Superclass is TypeInfo.Class sc && HasNominalClassBrand(sc);
    }

    /// <summary>
    /// Collects the instance members of a generic class with its type arguments substituted
    /// (e.g. a field <c>item: T</c> on <c>A&lt;Base&gt;</c> becomes <c>item: Base</c>). Inherited members from a
    /// superclass are included with substitutions composed down the chain. By default only public
    /// members are collected; <paramref name="includeNonPublic"/> additionally includes
    /// TypeScript private/protected members (the all-members mode used for a branded target, #639).
    /// </summary>
    private Dictionary<string, TypeInfo> CollectGenericClassMembers(TypeInfo.GenericClass gc, List<TypeInfo> args, bool includeNonPublic = false)
    {
        var subs = GenericClassSubs(gc, args);
        Dictionary<string, TypeInfo> members = [];
        var core = gc.Core;
        foreach (var (name, type) in core.FieldTypes)
            if ((includeNonPublic || IsPublicMember(core.FieldAccess, name)) && !members.ContainsKey(name))
                members[name] = Substitute(type, subs);
        foreach (var (name, type) in core.Methods)
            if (name != "constructor" && (includeNonPublic || IsPublicMember(core.MethodAccess, name)) && !members.ContainsKey(name))
                members[name] = Substitute(type, subs);
        foreach (var (name, type) in core.Getters)
            if (!members.ContainsKey(name))
                members[name] = Substitute(type, subs);
        if (gc.Superclass is TypeInfo.Class sc)
            foreach (var (name, type) in (includeNonPublic ? CollectAllInstanceMembers(sc) : CollectPublicInstanceMembers(sc)))
                members.TryAdd(name, type);
        // A generic superclass (`class Sub<U> extends Base<U>`): compose substitutions down the chain
        // by substituting this class's type arguments into the base's, then collect the base's
        // members under the resulting instantiation (`Sub<number>` → `Base<number>`). (#506)
        else if (gc.Superclass is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass baseGc } baseIg)
        {
            var baseArgs = baseIg.TypeArguments.Select(a => Substitute(a, subs)).ToList();
            foreach (var (name, type) in CollectGenericClassMembers(baseGc, baseArgs, includeNonPublic))
                members.TryAdd(name, type);
        }
        return members;
    }

    /// <summary>
    /// Attempts structural assignment of <paramref name="source"/> to an unbranded class-like target —
    /// a <see cref="TypeInfo.Class"/> or a generic-class instantiation (<see cref="TypeInfo.InstantiatedGeneric"/>
    /// of a <see cref="TypeInfo.GenericClass"/>) — comparing public members and index signatures with the
    /// generic type arguments substituted. Unbranded member-less targets have the
    /// structural shape <c>{}</c>, matching TypeScript class semantics.
    /// </summary>
    private bool StructurallyAssignableToClassTarget(TypeInfo targetResolved, TypeInfo source,
        bool emptyTargetAcceptsObjectSource = false)
    {
        Dictionary<string, TypeInfo> members;
        bool hasIndex;
        TypeInfo indexCarrier;
        switch (targetResolved)
        {
            case TypeInfo.Class c:
                // ES #private fields keep the class strictly nominal (no structural surface to relate).
                if (HasEsPrivateBrand(c)) return false;
                // A TypeScript-branded (private/protected) target is matched structurally over ALL its
                // members — accessibility is enforced by MembersAccessibilityCompatible before we get
                // here, so reaching this point means a same-origin source (same class, a subclass, or
                // an interface that extends the class). An unbranded target uses public members only.
                members = HasTsAccessModifierMember(c)
                    ? CollectAllInstanceMembers(c)
                    : CollectPublicInstanceMembers(c);
                hasIndex = c.Core.HasIndexSignature;
                indexCarrier = c;
                break;
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig:
                if (GenericClassHasNominalBrand(gc)) return false;
                members = CollectGenericClassMembers(gc, ig.TypeArguments);
                hasIndex = gc.Core.HasIndexSignature;
                indexCarrier = targetResolved;
                break;
            default:
                return false;
        }
        // TypeScript classes without private/protected branding are structural,
        // including the empty shape. An empty class is therefore compatible with
        // any non-nullish object-like source, not only interfaces/records.
        if (members.Count == 0 && !hasIndex) return true;
        return CheckStructuralCompatibility(members, source) && IndexSignaturesSatisfied(indexCarrier, source);
    }

    private static FrozenDictionary<string, TypeInfo>? GetMethods(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.Methods, gc => gc.Methods);

    private static string? GetClassName(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.Name, gc => gc.Name);

    /// <summary>
    /// True when an instance's class hierarchy includes a built-in Error type (Error, TypeError, …),
    /// i.e. it was declared <c>class X extends Error</c>. Lets such a user subclass satisfy an
    /// Error-typed target as a nominal subtype, now that <c>Error</c> resolves to TypeInfo.Error rather
    /// than <c>any</c> (#528). The <c>extends Error</c> placeholder superclass is a MutableClass whose
    /// Name is the error type's leaf name, so read that directly (ClassInfoAccessor skips MutableClass).
    /// </summary>
    private static bool ExtendsBuiltInError(TypeInfo.Instance instance)
    {
        TypeInfo? current = instance.ResolvedClassType;
        for (int guard = 0; current != null && guard < 64; guard++)
        {
            string? name = current is TypeInfo.MutableClass mc ? mc.Name : GetClassName(current);
            if (name != null && BuiltInNames.IsErrorTypeName(name)) return true;
            current = GetSuperclass(current);
        }
        return false;
    }

    private static FrozenDictionary<string, TypeInfo>? GetStaticMethods(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.StaticMethods, gc => gc.StaticMethods);

    private static FrozenDictionary<string, TypeInfo>? GetStaticProperties(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.StaticProperties, gc => gc.StaticProperties);

    /// <summary>
    /// Converts a class-like type to a TypeInfo.Class for walking hierarchy.
    /// Returns null if the type is not class-like.
    /// </summary>
    private static TypeInfo.Class? AsClass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c,
        _ => null
    };

    private static FrozenDictionary<string, TypeInfo>? GetFieldTypes(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.FieldTypes, gc => gc.FieldTypes);

    private static FrozenDictionary<string, TypeInfo>? GetGetters(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.Getters, gc => gc.Getters);

    private static FrozenDictionary<string, TypeInfo>? GetSetters(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.Setters, gc => gc.Setters);

    private static FrozenDictionary<string, AccessModifier>? GetMethodAccess(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.MethodAccess, gc => gc.MethodAccess);

    private static FrozenDictionary<string, AccessModifier>? GetStaticMethodAccess(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.StaticMethodAccess, gc => gc.StaticMethodAccess);

    private static FrozenDictionary<string, AccessModifier>? GetFieldAccess(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.FieldAccess, gc => gc.FieldAccess);

    private static FrozenDictionary<string, AccessModifier>? GetStaticFieldAccess(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.StaticFieldAccess, gc => gc.StaticFieldAccess);

    private static FrozenSet<string>? GetReadonlyFields(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.ReadonlyFields, gc => gc.ReadonlyFields);

    private static FrozenSet<string>? GetAbstractMethods(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.AbstractMethodSet, gc => gc.AbstractMethodSet);

    private static FrozenSet<string>? GetAbstractGetters(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.AbstractGetterSet, gc => gc.AbstractGetterSet);

    private static FrozenSet<string>? GetAbstractSetters(TypeInfo? classType) =>
        ClassInfoAccessor.Get(classType, c => c.AbstractSetterSet, gc => gc.AbstractSetterSet);
}
