using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

// Type category helper methods are defined in TypeChecker.Properties.Helpers.cs

/// <summary>
/// Property and member access type checking.
/// </summary>
/// <remarks>
/// Contains handlers for property access:
/// CheckThis, CheckSuper, CheckGet, CheckSet.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckSuper(Expr.Super expr)
    {
        CaptureEditorSuperReceiver(expr);
        using var memberLookup = expr.Method is { } name
            ? BeginSourceMemberLookup(expr, name, MemberOperation.Read) : null;
        TypeInfo result = CheckSuperCore(expr);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo CheckSuperCore(Expr.Super expr)
    {
        if (_currentClass == null)
        {
            throw new TypeCheckException("Cannot use 'super' outside of a class.", tsCode: "TS2335");
        }
        if (_currentClass.Superclass == null)
        {
            throw new TypeCheckException($" Class '{_currentClass.Name}' does not have a superclass.", tsCode: "TS2335");
        }

        var superName = GetClassName(_currentClass.Superclass) ?? "unknown";

        // If the superclass is a placeholder MutableClass (from Any-typed globals like Error),
        // treat all super access as Any — we can't statically know the exact signatures.
        if (_currentClass.Superclass is TypeInfo.MutableClass)
        {
            return TypeInfo.Any.Shared;
        }

        // super() constructor call - Method is null
        if (expr.Method == null)
        {
            var (constructor, constructorSubs) = FindInheritedConstructor(_currentClass.Superclass, []);
            if (constructor is not null)
            {
                return SubstituteConstructorMember(constructor, constructorSubs);
            }
            // Default constructor with no parameters
            return new TypeInfo.Function([], TypeInfo.Void.Shared);
        }

        TypeInfo? current = _currentClass.Superclass;
        Dictionary<string, TypeInfo> substitutions = [];
        while (current != null)
        {
            substitutions = ComposeInheritedClassSubs(current, substitutions);
            bool isStatic = _inStaticMethod || _inStaticBlock;
            TypeInfo? methodType = isStatic
                ? GetStaticMethods(current)?.GetValueOrDefault(expr.Method.Lexeme)
                    ?? GetStaticProperties(current)?.GetValueOrDefault(expr.Method.Lexeme)
                : GetMethods(current)?.GetValueOrDefault(expr.Method.Lexeme);
            bool isAccessor = false;
            if (!isStatic && methodType == null)
            {
                // A setter-only descriptor still declares the read type in
                // TypeScript, and must stop lookup before an ancestor getter.
                methodType = GetGetters(current)?.GetValueOrDefault(expr.Method.Lexeme)
                    ?? GetSetters(current)?.GetValueOrDefault(expr.Method.Lexeme);
                isAccessor = methodType != null;
                if (methodType == null && GetFieldTypes(current)?.ContainsKey(expr.Method.Lexeme) == true)
                    throw new TypeCheckException(
                        $" Class field '{expr.Method.Lexeme}' defined by the parent class is not accessible in the child class via super.",
                        expr.Method.Line, tsCode: "TS2855");
            }
            if (methodType != null)
            {
                if (isStatic)
                    EnforceStaticMemberAccess(current, expr.Method);
                else if (isAccessor)
                    EnforceSuperAccessorReadAccess(current, expr.Method);
                else if (GetMethodAccess(current)?.GetValueOrDefault(expr.Method.Lexeme) == AccessModifier.Private)
                    throw new TypeCheckException($" Property '{expr.Method.Lexeme}' is private and only accessible within class '{GetClassName(current)}'.", tsCode: "TS2341");
                ObserveSourceMemberSelection(expr.Method, current, isStatic ? MemberFacet.Static : MemberFacet.Instance);
                return substitutions.Count == 0 ? methodType : Substitute(methodType, substitutions);
            }
            current = GetSuperclass(current);
        }

        throw new TypeCheckException($" Property '{expr.Method.Lexeme}' does not exist on superclass '{superName}'.", tsCode: "TS2339");
    }

    private void EnforceSuperAccessorReadAccess(TypeInfo declaringClass, Token memberName)
    {
        AccessModifier access = GetFieldAccess(declaringClass)?.GetValueOrDefault(memberName.Lexeme)
            ?? AccessModifier.Public;
        if (access == AccessModifier.Public) return;
        int declaringId = SelectedSourceClassId(declaringClass);
        bool sameDeclaration = _currentClass is { } current &&
            (current.Core.DeclarationId == declaringId ||
                Members.IsSameSourceClass(current.Core.DeclarationId, declaringId));
        if (access == AccessModifier.Private && !sameDeclaration)
            throw new TypeCheckException(
                $" Property '{memberName.Lexeme}' is private and only accessible within class '{GetClassName(declaringClass)}'.",
                memberName.Line, tsCode: "TS2341");
        if (access == AccessModifier.Protected && !sameDeclaration &&
            (_currentClass == null || !SourceDerivesFromDeclaration(_currentClass, declaringId)))
            throw new TypeCheckException(
                $" Property '{memberName.Lexeme}' is protected and only accessible within class '{GetClassName(declaringClass)}' and its subclasses.",
                memberName.Line, tsCode: "TS2445");
    }

    private TypeInfo CheckThis(Expr.This expr)
    {
        // If there's an explicit 'this' type from a this parameter, use it
        if (_currentFunctionThisType != null)
        {
            return _currentFunctionThisType;
        }

        if (_currentClass == null)
        {
            if (_noImplicitThis && _currentFunctionReturnType is not null)
            {
                RecordTypeError(new TypeCheckException(
                    "'this' implicitly has type 'any' because it does not have a type annotation.",
                    line: expr.Keyword.Line,
                    tsCode: "TS2683"));
                return TypeInfo.Any.Shared;
            }
            if (_hasDefaultLibraries && _currentFunctionReturnType is null &&
                _currentModule?.IsScript == true)
                return GetGlobalThisType();
            // Allow `this` in regular functions (JS constructor-function
            // pattern) and at module top level. Type it as Any so members
            // resolve permissively — matches how CJS code uses
            // `function Foo() { this.x = 1 }`.
            return TypeInfo.Any.Shared;
        }
        // In static blocks, 'this' refers to the class constructor (the class type itself)
        if (_inStaticBlock)
        {
            return _currentClass;
        }
        if (_inStaticMethod)
        {
            // Static methods: `this` is the class itself.
            return _currentClass;
        }
        return new TypeInfo.Instance(_currentClass);
    }

    private TypeInfo CheckGet(Expr.Get get)
    {
        using var memberLookup = BeginSourceMemberLookup(get, get.Name, MemberOperation.Read);
        TypeInfo result = CheckGetCore(get);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo CheckGetCore(Expr.Get get)
    {
        if (get.Object is Expr.Variable variable &&
            _excessivelyRecursiveVariables.Contains(variable.Name.Lexeme))
            throw new TypeCheckException(
                "Type instantiation is excessively deep and possibly infinite.",
                get.Name.Line,
                tsCode: "TS2589");

        bool directGlobalObject = get.Object is Expr.Variable { Name.Lexeme: "globalThis" } ||
            get.Object is Expr.This &&
                (_currentArrowCapturesScriptGlobalThis ||
                 _currentClass is null && _currentFunctionThisType is null &&
                 _currentFunctionReturnType is null);
        if (_hasDefaultLibraries && directGlobalObject &&
            !GetGlobalThisType().Members.ContainsKey(get.Name.Lexeme))
        {
            if (_globalObjectLexicalNames.Contains(get.Name.Lexeme))
                throw new TypeCheckException(
                    $"Property '{get.Name.Lexeme}' does not exist on type 'typeof globalThis'.",
                    get.Name.Line,
                    tsCode: "TS2339");
            if (!_noImplicitAny)
                return TypeInfo.Any.Shared;
            throw new TypeCheckException(
                $"Element implicitly has an 'any' type because type 'typeof globalThis' has no index signature.",
                get.Name.Line,
                tsCode: "TS7017");
        }

        // The standard-library declaration grammar is intentionally best-effort;
        // consult the type facet directly for Object's target-sensitive additions
        // instead of allowing a hoisted `any` value to erase their absence.
        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "Object" } &&
            get.Name.Lexeme is "values" or "entries")
        {
            if (_environment.GetTypeBinding("ObjectConstructor") is TypeInfo.Interface objectCtor &&
                objectCtor.Members.TryGetValue(get.Name.Lexeme, out TypeInfo? member))
                return member;
            throw new TypeCheckException(
                $"Property '{get.Name.Lexeme}' does not exist on type 'ObjectConstructor'. " +
                "Try changing the 'lib' compiler option to 'es2017' or later.",
                get.Name.Line,
                tsCode: "TS2550");
        }

        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "SharedArrayBuffer" } &&
            get.Name.Lexeme == "length")
            return TypeInfo.Primitive.Number;

        // Runtime support for Intl is broader than the declaration parser's
        // current overloaded namespace fidelity. Constructor calls below apply
        // the diagnostics that matter; static members remain callable values.
        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "Intl" })
            return TypeInfo.Any.Shared;

        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "Atomics" })
        {
            TypeInfo atomicsType = CheckExpr(get.Object);
            if (get.Name.Lexeme == "waitAsync" &&
                GetMemberType(atomicsType, get.Name.Lexeme) is null)
            {
                var error = new TypeCheckException(
                    "Property 'waitAsync' does not exist on type 'Atomics'. Try changing the 'lib' compiler option to 'es2024' or later.",
                    get.Name.Line,
                    tsCode: "TS2550");
                if (!_recoveryMode && _moduleResolver is null)
                    throw error;
                RecordTypeError(error);
            }
            return TypeInfo.Any.Shared;
        }

        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "self" or "window" } &&
            GetGlobalThisType().Members.TryGetValue(get.Name.Lexeme, out TypeInfo? windowGlobalMember))
            return windowGlobalMember;

        TypeInfo objType = CheckExpr(get.Object);
        ExpectSourceMemberReceiver(objType);

        if (_hasDefaultLibraries &&
            get.Object is Expr.Variable { Name.Lexeme: "top" } &&
            objType is TypeInfo.Union topUnion && topUnion.ContainsNull &&
            _globalObjectVarNames.Contains(get.Name.Lexeme))
        {
            RecordTypeError(NullableMemberAccessError(topUnion, get.Name, get.Object));
            throw new TypeCheckException(
                $"Property '{get.Name.Lexeme}' does not exist on type 'Window'.",
                get.Name.Line,
                tsCode: "TS2339");
        }

        if (_recoveryMode &&
            get.Object is Expr.Get
            {
                Object: Expr.Variable { Name.Lexeme: var resultName },
                Name.Lexeme: "groups"
            } groupsAccess &&
            objType is TypeInfo.Union groupsUnion && groupsUnion.ContainsUndefined)
        {
            RecordTypeError(new TypeCheckException(
                $"'{resultName}.groups' is possibly 'undefined'.",
                groupsAccess.Name.Line,
                tsCode: "TS18048"));
            TypeInfo? nonUndefined = groupsUnion.FlattenedTypes
                .FirstOrDefault(type => type is not TypeInfo.Undefined);
            if (nonUndefined is TypeInfo.Interface { StringIndexType: { } stringIndex })
                return stringIndex;
        }

        // Special case: Symbol.iterator, Symbol.asyncIterator, etc. return unique symbol types
        if (get.Object is Expr.Variable v && v.Name.Lexeme == "Symbol")
        {
            var wellKnownType = WellKnownSymbolTypes.TryGet(get.Name.Lexeme);
            if (wellKnownType != null)
                return wellKnownType;

            // Symbol.for/keyFor/prototype referenced as plain values (not called) — mirrors the
            // typed signatures TryCheckBuiltinCall already gives the CALL form of Symbol.for/keyFor.
            // Without this, bare `Symbol.for` fell through to CheckExpr(Symbol)=Any then `.for` on
            // Any=Any, silently accepting it wherever a real, non-`any` type is required (e.g. a
            // computed property name, TS2464) even though tsc types it as a real function/object.
            switch (get.Name.Lexeme)
            {
                case "for":
                    return new TypeInfo.Function([TypeInfo.String.Shared], TypeInfo.Symbol.Shared, 1, false, null, ["key"]);
                case "keyFor":
                    return new TypeInfo.Function([TypeInfo.Symbol.Shared], new TypeInfo.Union([TypeInfo.String.Shared, TypeInfo.Undefined.Shared]), 1, false, null, ["sym"]);
                case "prototype":
                    return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
            }

            // A declaration-merged SymbolConstructor may add symbol-valued static members. Read
            // the latest type binding instead of the earlier lib variable's frozen interface.
            if (_environment.GetTypeBinding("SymbolConstructor") is TypeInfo.Interface symbolConstructor
                && symbolConstructor.Members.TryGetValue(get.Name.Lexeme, out var augmentedMember))
            {
                return augmentedMember;
            }
        }

        // Check for property narrowing (e.g., after "if (obj.prop !== null)" or nested "obj.a.b")
        // Use NarrowingPathExtractor to support nested property access
        var path = Narrowing.NarrowingPathExtractor.TryExtract(get);
        if (path != null)
        {
            var narrowedType = GetNarrowing(path);
            if (narrowedType != null)
            {
                // An alias write may establish this path without reading it.
                // Narrowing the value does not grant accessor visibility.
                if (objType is TypeInfo.Instance narrowedReceiver &&
                    TrySelectInstanceAccessor(narrowedReceiver, get.Name.Lexeme, out var accessor))
                {
                    EnforceAccessorAccess(accessor.DeclaringType, narrowedReceiver.ResolvedClassType,
                        get.Name, isStatic: false, isWrite: false);
                    ObserveSourceMemberSelection(get.Name, accessor.DeclaringType, MemberFacet.Instance);
                }
                return narrowedType;
            }
        }

        // Expand recursive type aliases lazily before property access
        if (objType is TypeInfo.RecursiveTypeAlias rta)
        {
            objType = ExpandRecursiveTypeAlias(rta);
        }
        objType = ResolveMappedTypeForAccess(objType);

        // A property read synthesized by destructuring desugaring that is covered by a default
        // (its own, or a default on an enclosing pattern) tolerates a missing property: type it as
        // `undefined` instead of reporting TS2339, since the wrapping ternary / enclosing default
        // supplies the value. A non-defaulted read stays strict (`const { a } = {}` still errors). #796
        if (get.Defaulted)
        {
            try { return ResolveMemberType(get, objType); }
            catch (TypeCheckException ex) when (ex.Diagnostic.TsCode == "TS2339")
            {
                return TypeInfo.Undefined.Shared;
            }
        }

        try
        {
            return ResolveMemberType(get, objType);
        }
        catch (TypeCheckException ex) when (
            ex.Diagnostic.TsCode == "TS2339" &&
            get.Object is Expr.Variable { Name.Lexeme: "Object" } &&
            get.Name.Lexeme is "values" or "entries")
        {
            throw new TypeCheckException(
                $"Property '{get.Name.Lexeme}' does not exist on type 'ObjectConstructor'. " +
                "Try changing the 'lib' compiler option to 'es2017' or later.",
                get.Name.Line,
                tsCode: "TS2550");
        }
    }

    /// <summary>
    /// Resolves the type of a property read on an already-evaluated receiver type, dispatching by
    /// type category. Throws TS2339 when the member is absent. Shared by <see cref="CheckGet"/>'s
    /// strict and (#796) defaulted-destructuring-tolerant paths.
    /// </summary>
    private TypeInfo ResolveMemberType(Expr.Get get, TypeInfo objType)
    {
        if (objType is TypeInfo.Unknown)
        {
            throw new TypeCheckException(
                "Object is of type 'unknown'.",
                get.Name.Line,
                tsCode: "TS18046");
        }

        // A bare `null`/`undefined` receiver has no properties — `tsc` rejects access on it (TS2339
        // for `undefined`, TS2531 for `null`), just as it does for a union containing them (see
        // CheckGetOnUnion). Optional chaining (`x?.p`) short-circuits to `undefined` instead. Without
        // this guard these types classify to TypeCategory.Null/Undefined, miss the dispatch switch
        // below, and silently yield `any`. #742
        switch (objType)
        {
            case TypeInfo.Undefined:
                if (get.Optional) return TypeInfo.Undefined.Shared;
                if (get.Object is Expr.This)
                    throw new TypeCheckException(
                        "Object is possibly 'undefined'.",
                        get.Name.Line,
                        tsCode: "TS2532");
                throw new TypeCheckException(
                    $"Property '{get.Name.Lexeme}' does not exist on type 'undefined'.",
                    get.Name.Line,
                    tsCode: "TS2339");
            case TypeInfo.Null:
                if (get.Optional) return TypeInfo.Undefined.Shared;
                throw new TypeCheckException("Object is possibly 'null'.", get.Name.Line, tsCode: "TS2531");
        }

        // Preserve concrete type arguments while reading a member from an instantiated generic
        // interface. Without flattening here, category dispatch recognizes an interface shape but
        // cannot enter the TypeInfo.Interface arm, so reads such as `.isRequired` collapse to any.
        if (objType is TypeInfo.InstantiatedGeneric instantiated &&
            FlattenInstantiatedInterface(instantiated) is { } flattenedInterface)
            objType = flattenedInterface;

        var category = TypeCategoryResolver.Classify(objType);
        string memberName = get.Name.Lexeme;

        // Fast path for built-in types with explicit member validation
        if (TypeCategoryResolver.HasBuiltInMemberValidation(category))
        {
            var memberType = ResolveBuiltInMemberType(category, objType, memberName);
            if (memberType != null) return memberType;
            IReadOnlyList<string>? candidates = category switch
            {
                TypeCategory.String => BuiltInTypes.StringApparentMemberNames,
                TypeCategory.Array or TypeCategory.Tuple => BuiltInTypes.ArrayApparentMemberNames,
                _ => null,
            };
            string? suggestion = candidates is null
                ? null
                : ClosestMemberSuggestion(memberName, candidates);
            if (suggestion is not null)
            {
                throw new TypeCheckException(
                    $"Property '{memberName}' does not exist on type '{GetTypeDisplayName(category, objType)}'. Did you mean '{suggestion}'?",
                    get.Name.Line,
                    tsCode: "TS2551");
            }
            throw new TypeCheckException($" Property '{memberName}' does not exist on type '{GetTypeDisplayName(category, objType)}'.", tsCode: "TS2339");
        }

        // Category-based dispatch for user-defined and special types
        return category switch
        {
            TypeCategory.TypeParameter when objType is TypeInfo.TypeParameter tp =>
                CheckGetOnTypeParameter(tp, get.Name),
            TypeCategory.Class when objType is TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric =>
                CheckGetOnClass(objType, get.Name),
            TypeCategory.Instance when objType is TypeInfo.Instance instance =>
                CheckGetOnInstance(instance, get.Name),
            TypeCategory.Interface when objType is TypeInfo.Interface itf =>
                CheckGetOnInterface(itf, get.Name),
            TypeCategory.Record when objType is TypeInfo.Record record =>
                CheckGetOnRecord(record, get.Name),
            TypeCategory.Enum when objType is TypeInfo.Enum enumType =>
                CheckGetOnEnum(enumType, get.Name),
            TypeCategory.Namespace when objType is TypeInfo.Namespace nsType =>
                CheckGetOnNamespace(nsType, get.Name),
            TypeCategory.Union when objType is TypeInfo.Union union =>
                CheckGetOnUnion(union, get.Name, get.Optional, get.Object),
            TypeCategory.Intersection when objType is TypeInfo.Intersection intersection =>
                CheckGetOnIntersection(intersection, get.Name),
            _ => TypeInfo.Any.Shared
        };
    }

    private static string? ClosestMemberSuggestion(string name, IReadOnlyList<string> candidates)
    {
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in candidates)
        {
            int distance = EditDistance(name, candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        int threshold = name.Length <= 4 ? 1 : 2;
        return bestDistance <= threshold ? best : null;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    /// <summary>
    /// Type checks property access on a union type.
    /// For optional chaining (isOptional=true), null/undefined members are skipped.
    /// Without optional chaining, null/undefined in the union causes an error.
    /// If a property doesn't exist on some non-null/undefined members, the result
    /// includes undefined for those members (mimicking TypeScript's permissive behavior).
    /// </summary>
    private TypeInfo CheckGetOnUnion(TypeInfo.Union union, Token memberName, bool isOptional = false, Expr? receiver = null)
    {
        List<TypeInfo> memberTypes = [];
        bool hasNullOrUndefined = false;
        bool hasMissingProperty = false;

        foreach (var member in union.FlattenedTypes)
        {
            // null and undefined don't have properties
            if (member is TypeInfo.Null or TypeInfo.Undefined)
            {
                if (isOptional)
                {
                    // Optional chaining: skip null/undefined, result will include undefined
                    hasNullOrUndefined = true;
                    continue;
                }
                var nullableError = NullableMemberAccessError(union, memberName, receiver);
                if (!_recoveryMode && _moduleResolver is null)
                    throw nullableError;
                RecordTypeError(nullableError);
                hasNullOrUndefined = true;
                continue;
            }

            try
            {
                var memberType = CheckGetOnType(member, memberName);
                memberTypes.Add(memberType);
            }
            catch (TypeCheckException)
            {
                // Property doesn't exist on this member - result will include undefined
                // This is permissive behavior matching TypeScript's handling of union property access
                hasMissingProperty = true;
            }
        }

        // A union exposes only properties present on every non-nullish constituent.
        if (hasMissingProperty)
        {
            throw new TypeCheckException(
                $"Property '{memberName.Lexeme}' does not exist on type '{union}'.",
                memberName.Line,
                tsCode: "TS2339");
        }

        // For optional chaining with null/undefined in the union, add undefined to result
        if (isOptional && hasNullOrUndefined)
        {
            memberTypes.Add(TypeInfo.Undefined.Shared);
        }

        // A union containing only nullish members has no value-side member.
        if (memberTypes.Count == 0)
        {
            return TypeInfo.Any.Shared;
        }

        // Return union of all member types
        var unique = memberTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
        return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
    }

    /// <summary>
    /// Builds the "possibly null/undefined" error for non-optional member access on
    /// a nullable union, picking the same diagnostic tsc would. The code depends on
    /// which of null/undefined the union carries and on whether the receiver is a bare
    /// identifier: identifiers use TS18047/18048/18049 ("'x' is possibly ..."), other
    /// expressions use TS2531/2532/2533 ("Object is possibly ..."). Collapsing all six
    /// into TS2533 (the old behaviour) mismatched most strict-null conformance baselines.
    /// </summary>
    private static TypeCheckException NullableMemberAccessError(TypeInfo.Union union, Token memberName, Expr? receiver)
    {
        bool hasNull = union.ContainsNull;
        bool hasUndefined = union.ContainsUndefined;
        string? ident = receiver switch
        {
            Expr.Variable v => v.Name.Lexeme,
            Expr.Get { Object: Expr.Variable root, Name: var property } =>
                $"{root.Name.Lexeme}.{property.Lexeme}",
            _ => null,
        };

        // The subject clause matches tsc's wording; the code is picked to match too.
        // (SharpTS keeps the member name in the message — more actionable than tsc's
        // bare form — since conformance matching is on the code, not the text.)
        (string code, string subject) = (hasNull, hasUndefined, ident) switch
        {
            (true, false, null) => ("TS2531", "Object is possibly 'null'."),
            (false, true, null) => ("TS2532", "Object is possibly 'undefined'."),
            (true, true, null)  => ("TS2533", "Object is possibly 'null' or 'undefined'."),
            (true, false, { } n) => ("TS18047", $"'{n}' is possibly 'null'."),
            (false, true, { } n) => ("TS18048", $"'{n}' is possibly 'undefined'."),
            (true, true, { } n)  => ("TS18049", $"'{n}' is possibly 'null' or 'undefined'."),
            // Reached only if the forcing member was null/undefined but the flags say
            // otherwise (shouldn't happen); keep the broadest code as a safe default.
            _ => ("TS2533", "Object is possibly 'null' or 'undefined'."),
        };
        return new TypeCheckException(
            $"Property '{memberName.Lexeme}' cannot be accessed. {subject}", memberName.Line, tsCode: code);
    }

    private static TypeCheckException NullableIndexAccessError(TypeInfo.Union union, Expr receiver)
    {
        int line = TryGetExprLine(receiver) ?? 0;
        return NullableMemberAccessError(
            union,
            new Token(TokenType.IDENTIFIER, "[index]", null, line),
            receiver);
    }

    /// <summary>
    /// Type checks property access on an intersection type.
    /// The property is looked up on each member type until found.
    /// </summary>
    private TypeInfo CheckGetOnIntersection(TypeInfo.Intersection intersection, Token memberName)
    {
        foreach (var member in intersection.FlattenedTypes)
        {
            try
            {
                return CheckGetOnType(member, memberName);
            }
            catch (TypeCheckException)
            {
                // Continue to next type in intersection
            }
        }
        throw new TypeCheckException($"Property '{memberName.Lexeme}' does not exist on type '{intersection}'.", tsCode: "TS2339");
    }

    /// <summary>
    /// Gets a display name for error messages based on the type category.
    /// </summary>
    private static string GetTypeDisplayName(TypeCategory category, TypeInfo objType) => category switch
    {
        TypeCategory.String => "string",
        TypeCategory.Number => "number",
        TypeCategory.Boolean => "boolean",
        TypeCategory.Array => "array",
        TypeCategory.Tuple => "tuple",
        TypeCategory.Map => "Map",
        TypeCategory.Set => "Set",
        TypeCategory.WeakMap => "WeakMap",
        TypeCategory.WeakSet => "WeakSet",
        TypeCategory.Date => "Date",
        TypeCategory.RegExp => "RegExp",
        TypeCategory.Error when objType is TypeInfo.Error err => err.Name,
        TypeCategory.Timeout => "Timeout",
        TypeCategory.Buffer => "Buffer",
        TypeCategory.Function => "function",
        TypeCategory.WeakRef => "WeakRef",
        TypeCategory.FinalizationRegistry => "FinalizationRegistry",
        TypeCategory.AbortController => "AbortController",
        TypeCategory.AbortSignal => "AbortSignal",
        TypeCategory.Iterator => "Iterator",
        TypeCategory.Iterable => "Iterable",
        TypeCategory.Generator => "Generator",
        TypeCategory.AsyncGenerator => "AsyncGenerator",
        TypeCategory.Promise => "Promise",
        TypeCategory.EventEmitter => "EventEmitter",
        _ => objType.ToString() ?? "unknown"
    };

    /// <summary>
    /// Computes the post-write narrowed type for a typed slot by filtering the declared
    /// union's members to those the RHS could actually be. Matches TypeScript's
    /// control-flow narrowing across assignments: writing `"s"` to a `string | null`
    /// slot narrows subsequent reads to `string`. Returns null if no narrowing is
    /// produced (declared isn't a union, every member survives, or no member matches).
    /// </summary>
    private TypeInfo? NarrowToDeclaredSlot(TypeInfo declaredType, TypeInfo valueType)
    {
        if (declaredType is not TypeInfo.Union union) return null;

        var declaredMembers = union.FlattenedTypes;
        IReadOnlyList<TypeInfo> rhsMembers = valueType is TypeInfo.Union rhsUnion
            ? rhsUnion.FlattenedTypes
            : [valueType];

        var surviving = new List<TypeInfo>();
        var seen = new HashSet<TypeInfo>(TypeInfoEqualityComparer.Instance);
        foreach (var declared in declaredMembers)
        {
            foreach (var rhs in rhsMembers)
            {
                // Weak object types can structurally accept an array solely because every field is
                // optional. That does not make the object arm a possible runtime result of an
                // array initializer; prefer the array/tuple arms for flow narrowing.
                bool incompatibleRuntimeShape = rhs is TypeInfo.Array or TypeInfo.Tuple &&
                    declared is not TypeInfo.Array and not TypeInfo.Tuple;
                if (!incompatibleRuntimeShape && IsCompatible(declared, rhs))
                {
                    if (seen.Add(declared))
                        surviving.Add(declared);
                    break;
                }
            }
        }

        if (surviving.Count == 0 || surviving.Count == declaredMembers.Count) return null;
        return surviving.Count == 1 ? surviving[0] : new TypeInfo.Union(surviving);
    }

    /// <summary>
    /// After a successful property/setter write, installs a narrowing on the written
    /// path so subsequent reads see the tighter type. Also mirrors onto exact variable
    /// aliases (<c>const alias = obj</c>) since those are guaranteed to refer to the
    /// same object. Does not mirror onto escape-analyzer paths, which are only
    /// may-alias (installing there would invent false narrowings).
    /// </summary>
    private void InstallPostAssignmentNarrowing(
        Narrowing.NarrowingPath.PropertyAccess assignedPath,
        TypeInfo declaredSlotType,
        TypeInfo valueType)
    {
        var narrowed = NarrowToDeclaredSlot(declaredSlotType, valueType);
        if (narrowed == null) return;

        AddNarrowing(assignedPath, narrowed);

        if (assignedPath.Base is Narrowing.NarrowingPath.Variable varPath &&
            _variableAliases.TryGetValue(varPath.Name, out var originalVar))
        {
            var aliasedPath = new Narrowing.NarrowingPath.PropertyAccess(
                new Narrowing.NarrowingPath.Variable(originalVar),
                assignedPath.Property);
            AddNarrowing(aliasedPath, narrowed);
        }
    }

    /// <summary>
    /// Expands a concrete mapped type to its equivalent Interface/Record so a member access on it
    /// resolves real members. A consumer can receive a mapped type that was never expanded by its
    /// producer — e.g. <c>DeepReadonlyArray&lt;Part&gt;</c>'s numeric-index element is the
    /// <c>DeepReadonlyObject&lt;Part&gt;</c> mapped node, since <see cref="EvaluateConditionalType"/>
    /// substitutes a branch without running the post-expansion <see cref="ResolveGenericType"/>
    /// applies. A still-deferred key domain (a generic key-filter) or one mentioning open type
    /// variables is left untouched so it stays deferred (#365).
    /// </summary>
    private TypeInfo ResolveMappedTypeForAccess(TypeInfo objType)
    {
        if (objType is TypeInfo.MappedType mapped && !ContainsOpenTypeVariable(mapped)
            && !IsDeferredKeyDomain(ResolveMappedKeyDomain(mapped)))
            return ExpandMappedType(mapped);
        return objType;
    }

    private TypeInfo CheckSet(Expr.Set set)
    {
        using var memberLookup = BeginSourceMemberLookup(set, set.Name, MemberOperation.Write);
        TypeInfo result = CheckSetCore(set);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo CheckSetCore(Expr.Set set)
    {
        bool directGlobalObject = set.Object is Expr.Variable { Name.Lexeme: "globalThis" } ||
            set.Object is Expr.This && _currentFunctionReturnType is null;
        if (_hasDefaultLibraries && directGlobalObject &&
            !GetGlobalThisType().Members.ContainsKey(set.Name.Lexeme))
        {
            if (_globalObjectLexicalNames.Contains(set.Name.Lexeme))
                throw new TypeCheckException(
                    $"Property '{set.Name.Lexeme}' does not exist on type 'typeof globalThis'.",
                    set.Name.Line,
                    tsCode: "TS2339");
            // Checked JavaScript permits creating globals through `this`/`globalThis`.
            if (_filePath?.EndsWith(".js", StringComparison.OrdinalIgnoreCase) == true || !_noImplicitAny)
                return CheckExpr(set.Value);
            throw new TypeCheckException(
                "Element implicitly has an 'any' type because type 'typeof globalThis' has no index signature.",
                set.Name.Line,
                tsCode: "TS7017");
        }

        TypeInfo objType = CheckExpr(set.Object);

        // A generic interface is the same mutable object shape for writes as for reads.
        if (objType is TypeInfo.InstantiatedGeneric instantiated &&
            FlattenInstantiatedInterface(instantiated) is { } flattenedInterface)
            objType = flattenedInterface;

        // A direct variable may have been flow-narrowed to one constituent by its initializer.
        // For a property present on every declared constituent, TypeScript checks a write against
        // the union of those declared member types (for example, writing `"a" | "b"` to a
        // discriminant currently narrowed to `"a"`). Keep member-specific writes on the narrowed
        // object type when the member is not common to the full declared union.
        if (set.Object is Expr.Variable variable &&
            GetDeclaredType(variable.Name.Lexeme) is TypeInfo.Union declaredUnion &&
            declaredUnion.FlattenedTypes.All(constituent =>
                constituent is TypeInfo.Any ||
                GetMemberTypeWithOptionality(constituent, set.Name.Lexeme) is not null))
        {
            objType = declaredUnion;
        }
        ExpectSourceMemberReceiver(objType);

        if (objType is TypeInfo.Undefined && set.Object is Expr.This)
        {
            throw new TypeCheckException(
                "Object is possibly 'undefined'.",
                set.Name.Line,
                tsCode: "TS2532");
        }

        // Expand recursive type aliases lazily before property assignment — mirrors CheckGet, so a
        // write through a deferred alias (`part.subparts[0].id = …` where the element is the still
        // deferred `DeepReadonly<Part>`) sees the readonly `DeepReadonlyObject<Part>` shape and
        // rejects with TS2540 instead of "Only instances and objects have properties" (#365).
        if (objType is TypeInfo.RecursiveTypeAlias rtaObj)
            objType = ExpandRecursiveTypeAlias(rtaObj);
        objType = ResolveMappedTypeForAccess(objType);

        // Extract the narrowing path for the written location, if narrowable.
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(set.Object);
        Narrowing.NarrowingPath.PropertyAccess? assignedPath = null;
        if (basePath != null)
        {
            assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, set.Name.Lexeme);
            InvalidateNarrowingsFor(assignedPath);

            // Also invalidate narrowings on the original variable if this is an alias
            // e.g., if "alias.prop = x" and alias was assigned from obj, also invalidate "obj.prop"
            if (basePath is Narrowing.NarrowingPath.Variable varPath &&
                _variableAliases.TryGetValue(varPath.Name, out var originalVar))
            {
                var originalPath = new Narrowing.NarrowingPath.PropertyAccess(
                    new Narrowing.NarrowingPath.Variable(originalVar),
                    set.Name.Lexeme);
                InvalidateNarrowingsFor(originalPath);
            }

            // Inter-procedural escape analysis: if the base is a global/outer-scope variable,
            // it might alias any escaped local variable. Invalidate narrowings on all escaped
            // variables' properties with the same name.
            // e.g., "globalAlias.prop = null" should invalidate "obj.prop" if obj escaped.
            if (basePath is Narrowing.NarrowingPath.Variable baseVar)
            {
                foreach (var escapedVar in _escapeAnalyzer.GetPotentiallyAffectedEscapedVariables(baseVar.Name))
                {
                    var escapedPath = new Narrowing.NarrowingPath.PropertyAccess(
                        new Narrowing.NarrowingPath.Variable(escapedVar),
                        set.Name.Lexeme);
                    InvalidateNarrowingsFor(escapedPath);
                }
            }
        }

        // Handle TypeParameter - delegate to constraint type for property assignment
        if (objType is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
            {
                // An index signature on a constraint permits reading an arbitrary named member,
                // but not writing it through the type parameter: a concrete instantiation may
                // declare that member with a narrower type. Only an explicitly declared member is
                // a sound write target (`T extends { x: number }`, not merely
                // `T extends { [key: string]: number }`).
                var propType = GetMemberType(tp.Constraint, set.Name.Lexeme);
                if (propType is null)
                    throw new TypeCheckException(
                        $" Property '{set.Name.Lexeme}' does not exist on type '{tp.Name}'.",
                        set.Name.Line,
                        tsCode: "TS2339");
                TypeInfo valueType = CheckExpr(set.Value);
                if (!IsCompatible(propType, valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{propType}'.", tsCode: "TS2322");
                }
                if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, propType, valueType);
                return valueType;
            }
            throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on type '{tp.Name}'. Consider adding a constraint to the type parameter.", tsCode: "TS2339");
        }

        // Generic class values expose the same checked static property metadata.
        if (objType is TypeInfo.Class or TypeInfo.GenericClass)
        {
            TypeInfo? current = objType;
            while (current != null)
            {
                var staticMethods = GetStaticMethods(current);
                if (staticMethods != null && staticMethods.TryGetValue(set.Name.Lexeme, out var staticMethodType))
                {
                    EnforceStaticMemberAccess(current, set.Name);
                    ObserveSourceMemberSelection(set.Name, current, MemberFacet.Static);
                    TypeInfo methodValueType = CheckExpr(set.Value);
                    if (!IsCompatible(staticMethodType, methodValueType))
                        throw new TypeCheckException($" Cannot assign '{methodValueType}' to static method '{set.Name.Lexeme}' of type '{staticMethodType}'.", tsCode: "TS2322");
                    return methodValueType;
                }
                var staticProps = GetStaticProperties(current);
                if (staticProps != null && staticProps.TryGetValue(set.Name.Lexeme, out var staticPropType))
                {
                    var setterType = GetStaticSetters(current)?.GetValueOrDefault(set.Name.Lexeme);
                    if (setterType is not null)
                        EnforceAccessorAccess(current, objType, set.Name, isStatic: true, isWrite: true);
                    else if (TryGetAccessorSetterAccess(current, isStatic: true, set.Name.Lexeme, out _))
                        EnforceAccessorAccess(current, objType, set.Name, isStatic: true, isWrite: false);
                    else
                        EnforceStaticMemberAccess(current, set.Name);
                    if (ClassInfoAccessor.Get(current, c => c.Core.StaticReadonlyFields, gc => gc.Core.StaticReadonlyFields)?.Contains(set.Name.Lexeme) == true)
                        throw new TypeCheckException($" Cannot assign to '{set.Name.Lexeme}' because it is a read-only property.", tsCode: "TS2540");
                    ObserveSourceMemberSelection(set.Name, current, MemberFacet.Static);
                    TypeInfo writeType = setterType ?? staticPropType;
                    TypeInfo valueType = CheckExpr(set.Value);
                    if (!IsCompatible(writeType, valueType))
                    {
                        throw new TypeCheckException($" Cannot assign '{valueType}' to static property '{set.Name.Lexeme}' of type '{writeType}'.", tsCode: "TS2322");
                    }
                    if (assignedPath != null && GetStaticSetters(current)?.ContainsKey(set.Name.Lexeme) != true)
                        InstallPostAssignmentNarrowing(assignedPath, staticPropType, valueType);
                    return valueType;
                }
                current = GetSuperclass(current);
            }
            return CheckExpr(set.Value);
        }

        if (objType is TypeInfo.Instance instance)
        {
             string memberName = set.Name.Lexeme;
             if (TrySelectInstanceAccessor(instance, memberName, out var accessor))
             {
                 EnforceAccessorAccess(accessor.DeclaringType, instance.ResolvedClassType,
                     set.Name, isStatic: false, isWrite: accessor.SetterType is not null);
                 if (accessor.SetterType is not { } setterType)
                     throw new TypeCheckException(
                         $"Cannot assign to '{memberName}' because it is a read-only property (has getter but no setter).",
                         set.Name.Line, tsCode: "TS2540");
                 ObserveSourceMemberSelection(set.Name, accessor.DeclaringType, MemberFacet.Instance);
                 TypeInfo valueType = CheckExpr(set.Value);
                 if (!IsCompatible(setterType, valueType))
                     throw new TypeCheckException(
                         $"Cannot assign '{valueType}' to property '{memberName}' expecting '{setterType}'.",
                         set.Name.Line, tsCode: "TS2322");
                 if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, setterType, valueType);
                 return valueType;
             }

             bool promiseHeritage = TryGetDefaultLibraryPromiseType(instance, out _);
             var promiseGeneric = promiseHeritage && instance.ResolvedClassType is TypeInfo.InstantiatedGeneric
                 { GenericDefinition: TypeInfo.GenericClass } projected ? projected : null;
             Dictionary<string, TypeInfo> promiseSubstitutions = promiseGeneric is not null
                 ? GenericClassSubs((TypeInfo.GenericClass)promiseGeneric.GenericDefinition, promiseGeneric.TypeArguments)
                 : [];

             // Handle InstantiatedGeneric
             if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                 ig.GenericDefinition is TypeInfo.GenericClass gc && !promiseHeritage)
             {
                 // Build substitution map
                 Dictionary<string, TypeInfo> subs = [];
                 for (int i = 0; i < gc.TypeParams.Count; i++)
                     subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                 // Check for field
                 if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                 {
                     ObserveSourceMemberSelection(set.Name, gc, MemberFacet.Instance);
                     var substitutedType = Substitute(fieldType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new TypeCheckException($" Cannot assign '{valueType}' to field '{memberName}' of type '{substitutedType}'.", tsCode: "TS2322");
                     }
                     if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, substitutedType, valueType);
                     return valueType;
                 }

                 return CheckExpr(set.Value);
             }

             // Handle regular Class
             var startClass = instance.ResolvedClassType as TypeInfo.Class
                 ?? (promiseGeneric is not null ? new TypeInfo.Class(((TypeInfo.GenericClass)promiseGeneric.GenericDefinition).Core) : null);
             if (startClass is null)
                 return CheckExpr(set.Value);

             TypeInfo? current = startClass;
             var substitutions = promiseSubstitutions;

             // Check access and readonly
             while (current != null)
             {
                 // The nearest member was ordinary, otherwise the accessor path
                 // above would have handled the write. Farther accessors cannot
                 // impose their read-side visibility on that nearer declaration.
                 if (GetGetters(current)?.ContainsKey(memberName) == true ||
                     GetSetters(current)?.ContainsKey(memberName) == true)
                 {
                     current = GetSuperclass(current);
                     continue;
                 }

                 // Check access modifier
                 AccessModifier access = AccessModifier.Public;
                 var fieldAccess = GetFieldAccess(current);
                 if (fieldAccess != null && fieldAccess.TryGetValue(memberName, out var fa))
                     access = fa;

                 var currentName = GetClassName(current);
                 if (access == AccessModifier.Private && _currentClass?.Name != currentName)
                 {
                     throw new TypeCheckException($" Property '{memberName}' is private and only accessible within class '{currentName}'.", tsCode: "TS2341");
                 }
                 var currentClass2 = AsClass(current);
                 if (access == AccessModifier.Protected && currentClass2 != null && !IsSubclassOf(_currentClass, currentClass2))
                 {
                     throw new TypeCheckException($" Property '{memberName}' is protected and only accessible within class '{currentName}' and its subclasses.", tsCode: "TS2445");
                 }

                 // Check readonly - only allow assignment in constructor
                 var readonlyFields = GetReadonlyFields(current);
                 if (readonlyFields != null && readonlyFields.Contains(memberName))
                 {
                     // Allow in constructor
                     bool inConstructor = _currentClass?.Name == currentName &&
                         _environment.IsDefined("this");
                     // Simplified check - just allow if we're in the same class
                     if (_currentClass?.Name != currentName)
                     {
                         throw new TypeCheckException($" Cannot assign to '{memberName}' because it is a read-only property.", tsCode: "TS2540");
                     }
                 }

                 // A nearer mutable declaration shadows the projected native
                 // member's readonly/access flags. Keep the ordinary path intact.
                 if (promiseHeritage && (GetFieldTypes(current)?.ContainsKey(memberName) == true ||
                     GetMethods(current)?.ContainsKey(memberName) == true ||
                     GetGetters(current)?.ContainsKey(memberName) == true ||
                     GetSetters(current)?.ContainsKey(memberName) == true))
                     break;

                 current = GetSuperclass(current);
             }

             // Check the assigned value against the field's declared type (walking the chain). Plain
             // (non-setter) instance fields were previously assigned without a compatibility check.
             TypeInfo fieldValueType = CheckExpr(set.Value);
             current = startClass;
             substitutions = promiseSubstitutions;
             while (current != null)
             {
                 if (promiseHeritage)
                     substitutions = ComposePromiseHeritageSubstitutions(current, substitutions);
                 var fieldTypes = GetFieldTypes(current);
                 if (fieldTypes != null && fieldTypes.TryGetValue(memberName, out var fieldDeclType))
                 {
                     if (promiseHeritage)
                         fieldDeclType = SubstituteConstructorMember(fieldDeclType, substitutions);
                     ObserveSourceMemberSelection(set.Name, current, MemberFacet.Instance);
                     if (fieldDeclType is not (TypeInfo.Inferred or TypeInfo.Any)
                         && !IsCompatible(fieldDeclType, fieldValueType))
                     {
                         throw new TypeCheckException($" Cannot assign type '{fieldValueType}' to field '{memberName}' of type '{fieldDeclType}'.", tsCode: "TS2322");
                     }
                     if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, fieldDeclType, fieldValueType);
                     break;
                 }
                 current = GetSuperclass(current);
             }
             return fieldValueType;
        }
        else if (objType is TypeInfo.Record record)
        {
             if (record.Fields.TryGetValue(set.Name.Lexeme, out var fieldType))
             {
                 // A readonly record (`as const`, `Readonly<T>`, a const type parameter) rejects all
                 // member writes with TS2540, in preference to the literal-type mismatch TS2322 the
                 // value-compatibility check below would otherwise report (#493).
                 if (record.IsReadonly)
                 {
                     throw new TypeCheckException(
                         $" Cannot assign to '{set.Name.Lexeme}' because it is a read-only property.",
                         set.Name.Line,
                         tsCode: "TS2540");
                 }
                 TypeInfo valueType = CheckExpr(set.Value);
                 // Getter-only properties: allow at type-check time, runtime handles sloppy/strict
                 if (record.IsGetterOnly(set.Name.Lexeme))
                 {
                     return valueType;
                 }
                 TypeInfo writeType = record.IsFieldOptional(set.Name.Lexeme) && !_exactOptionalPropertyTypes
                     ? CreateUnion(fieldType, TypeInfo.Undefined.Shared)
                     : fieldType;
                 if (!IsCompatible(writeType, valueType))
                 {
                     throw new TypeCheckException($" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{fieldType}'.", tsCode: "TS2322");
                 }
                 if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, fieldType, valueType);
                 return valueType;
             }
             if (record.StringIndexType is { } recordStringIndex)
             {
                 TypeInfo valueType = CheckExpr(set.Value);
                 if (!IsCompatible(recordStringIndex, valueType))
                     throw new TypeCheckException(
                         $" Cannot assign '{valueType}' to string index type '{recordStringIndex}'.",
                         set.Name.Line,
                         tsCode: "TS2322");
                 return valueType;
             }
             // Without an index signature, adding a new property is not valid for a closed record.
             throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on type '{record}'.", tsCode: "TS2339");
        }
        else if (objType is TypeInfo.Interface itf)
        {
            foreach (var member in itf.GetAllMembers())
            {
                if (member.Key == set.Name.Lexeme)
                {
                    // readonly members (incl. those produced by a `readonly [P in K]` mapped type,
                    // e.g. DeepReadonlyObject<T>) reject assignment — #337 item 2.
                    if (itf.IsMemberReadonly(set.Name.Lexeme))
                    {
                        throw new TypeCheckException(
                            $" Cannot assign to '{set.Name.Lexeme}' because it is a read-only property.",
                            set.Name.Line,
                            tsCode: "TS2540");
                    }
                    TypeInfo valueType = CheckExpr(set.Value);
                    TypeInfo writeType = itf.GetAllOptionalMembers().Contains(set.Name.Lexeme)
                        && !_exactOptionalPropertyTypes
                            ? CreateUnion(member.Value, TypeInfo.Undefined.Shared)
                            : member.Value;
                    if (!IsCompatible(writeType, valueType))
                    {
                        throw new TypeCheckException($" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{member.Value}'.", tsCode: "TS2322");
                    }
                    if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, member.Value, valueType);
                    return valueType;
                }
            }
            if (itf.StringIndexType is { } interfaceStringIndex)
            {
                TypeInfo valueType = CheckExpr(set.Value);
                if (!IsCompatible(interfaceStringIndex, valueType))
                    throw new TypeCheckException(
                        $" Cannot assign '{valueType}' to string index type '{interfaceStringIndex}'.",
                        set.Name.Line,
                        tsCode: "TS2322");
                return valueType;
            }
            throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on interface '{itf.Name}'.", line: set.Name.Line, tsCode: "TS2339");
        }
        // Handle Error property assignment (name, message, stack are mutable strings; cause is any)
        if (objType is TypeInfo.Error)
        {
            string propName = set.Name.Lexeme;
            if (ErrorBuiltIns.CanSetProperty(propName))
            {
                TypeInfo valueType = CheckExpr(set.Value);
                // cause accepts any type; name, message, stack must be string
                if (propName != "cause" && !IsCompatible(TypeInfo.String.Shared, valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to property '{propName}' of type 'string'.", tsCode: "TS2322");
                }
                return valueType;
            }
            // Other names — Error is an ordinary object in JS; allow ad-hoc
            // property assignment (\`new Error(); e.foo = 1\` is legal at runtime).
            return CheckExpr(set.Value);
        }
        // Allow property assignment on built-in types with settable properties
        if (objType is TypeInfo.AbortSignal && set.Name.Lexeme == "onabort")
        {
            return CheckExpr(set.Value);
        }
        // JS treats built-in objects (Date, RegExp, Map, Set, Promise, etc.) as
        // ordinary objects — \`new Date(); d.foo = 1\` is legal. Mirror that
        // permissiveness so test262 patterns like \`var obj = new Date();
        // obj.length = 1; obj[0] = 1\` (used to test Array.prototype.X.call
        // on array-likes) compile.
        if (objType is TypeInfo.Date or TypeInfo.RegExp or TypeInfo.Map or TypeInfo.Set
            or TypeInfo.WeakMap or TypeInfo.WeakSet or TypeInfo.Promise)
        {
            return CheckExpr(set.Value);
        }
        // Allow property assignment on Any type (e.g., 'this' in object method shorthand)
        if (objType is TypeInfo.Any)
        {
            return CheckExpr(set.Value);
        }
        // Namespace objects expose their value members at runtime. SharpTS built-in
        // facades use this for explicitly mutable exports such as
        // cluster.schedulingPolicy.
        if (objType is TypeInfo.Namespace namespaceType)
        {
            if (!namespaceType.Values.TryGetValue(set.Name.Lexeme, out var memberType))
                throw new TypeCheckException(
                    $" Property '{set.Name.Lexeme}' does not exist on namespace '{namespaceType.Name}'.",
                    set.Name.Line, tsCode: "TS2339");

            TypeInfo valueType = CheckExpr(set.Value);
            if (!IsCompatible(memberType, valueType))
                throw new TypeCheckException(
                    $" Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{memberType}'.",
                    set.Name.Line, tsCode: "TS2322");
            return valueType;
        }
        // Functions are objects in JavaScript and support property assignment.
        // (Very common in CommonJS: `fn.prototype = {}`, `fn.DNS = "..."`.)
        if (objType is TypeInfo.Function)
        {
            return CheckExpr(set.Value);
        }
        // Arrays accept `a.length = N` (truncate / extend) and arbitrary named
        // properties (arrays are objects in JS). The runtime interpreter and
        // compiler dispatch on SharpTSArray and route `length` to SetLength.
        if (objType is TypeInfo.Array)
        {
            if (set.Name.Lexeme == "length")
            {
                TypeInfo valueType = CheckExpr(set.Value);
                var numberType = TypeInfo.Primitive.Number;
                if (!IsCompatible(numberType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to array 'length' (expected number).", tsCode: "TS2322");
                return valueType;
            }
            return CheckExpr(set.Value);
        }
        // Property write on a union: every constituent must expose the member, and the value
        // must be assignable to the union of the constituents' member types. (tsc permits
        // discriminant-property writes that select a constituent — `axis.type = getAxisType()`
        // where axis: ILinearAxis | ICategoricalAxis.)
        if (objType is TypeInfo.Union unionObj)
        {
            List<TypeInfo> memberTypes = [];
            foreach (var constituent in unionObj.FlattenedTypes)
            {
                if (constituent is TypeInfo.Any) { memberTypes.Add(constituent); continue; }
                if (GetMemberTypeWithOptionality(constituent, set.Name.Lexeme) is not { } member)
                    throw new TypeCheckException($" Property '{set.Name.Lexeme}' does not exist on type '{unionObj}'.", tsCode: "TS2339");
                memberTypes.Add(member.Type);
            }
            var memberUnion = memberTypes.Aggregate(CreateUnion);
            TypeInfo unionValueType = CheckExpr(set.Value);
            if (!IsCompatible(memberUnion, unionValueType))
                throw new TypeCheckException($" Cannot assign '{unionValueType}' to property '{set.Name.Lexeme}' of type '{memberUnion}'.", tsCode: "TS2322");
            return unionValueType;
        }
        throw new TypeCheckException("Only instances and objects have properties.", tsCode: "TS2339");
    }

    /// <summary>
    /// Resolves member access on a given type without needing an actual expression.
    /// Used for TypeParameter constraint delegation and other scenarios where we need
    /// to check member access on a type directly.
    /// </summary>
    private TypeInfo CheckGetOnType(TypeInfo objType, Token memberName)
    {
        // Expand recursive type aliases lazily before property access
        if (objType is TypeInfo.RecursiveTypeAlias rta)
        {
            objType = ExpandRecursiveTypeAlias(rta);
        }
        objType = ResolveMappedTypeForAccess(objType);

        if (objType is TypeInfo.InstantiatedGeneric instantiated &&
            FlattenInstantiatedInterface(instantiated) is { } flattenedInterface)
            objType = flattenedInterface;

        // Handle TypeParameter recursively - delegate to constraint
        if (objType is TypeInfo.TypeParameter tp)
        {
            return CheckGetOnTypeParameter(tp, memberName);
        }

        // Handle Interface - check own and inherited members
        if (objType is TypeInfo.Interface itf)
        {
            return CheckGetOnInterface(itf, memberName);
        }

        // Handle Record type - check fields and index signatures
        if (objType is TypeInfo.Record record)
        {
            return CheckGetOnRecord(record, memberName);
        }

        // Handle built-in types via category-based dispatch
        var builtInCategory = TypeCategoryResolver.Classify(objType);
        if (TypeCategoryResolver.HasBuiltInMemberValidation(builtInCategory))
        {
            var builtInMemberType = ResolveBuiltInMemberType(builtInCategory, objType, memberName.Lexeme);
            if (builtInMemberType != null) return builtInMemberType;
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{GetTypeDisplayName(builtInCategory, objType)}'.", tsCode: "TS2339");
        }

        // Handle primitive number type - no methods
        if (objType is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER)
        {
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type 'number'.", tsCode: "TS2339");
        }

        // Handle Class type - check static members
        if (objType is TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass })
        {
            return CheckGetOnClass(objType, memberName);
        }

        // Handle Instance type - check instance members
        if (objType is TypeInfo.Instance instance)
        {
            return CheckGetOnInstance(instance, memberName);
        }

        // Handle Union type - check if all members have the property
        if (objType is TypeInfo.Union union)
        {
            List<TypeInfo> memberTypes = [];
            foreach (var member in union.FlattenedTypes)
            {
                try
                {
                    var memberType = CheckGetOnType(member, memberName);
                    memberTypes.Add(memberType);
                }
                catch (TypeCheckException)
                {
                    // If any member doesn't have the property, it's an error
                    throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on all members of union type '{union}'.", tsCode: "TS2339");
                }
            }
            // Return union of all member types
            var unique = memberTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
        }

        // Handle Intersection type - merge members from all types
        if (objType is TypeInfo.Intersection intersection)
        {
            // Try each type in the intersection - first match wins
            foreach (var member in intersection.FlattenedTypes)
            {
                try
                {
                    return CheckGetOnType(member, memberName);
                }
                catch (TypeCheckException)
                {
                    // Continue to next type in intersection
                }
            }
            throw new TypeCheckException($" Property '{memberName.Lexeme}' does not exist on type '{intersection}'.", tsCode: "TS2339");
        }

        // Handle Any type
        if (objType is TypeInfo.Any)
        {
            return TypeInfo.Any.Shared;
        }

        return TypeInfo.Any.Shared;
    }

    /// <summary>
    /// Type checks ES2022 private field access: obj.#field
    /// Private fields are only accessible within the declaring class body.
    /// </summary>
    private TypeInfo CheckGetPrivate(Expr.GetPrivate get)
    {
        using var memberLookup = BeginSourceMemberLookup(get, get.Name, MemberOperation.Read);
        TypeInfo result = CheckGetPrivateCore(get);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo CheckGetPrivateCore(Expr.GetPrivate get)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{get.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        // Check the object expression
        TypeInfo objType = CheckExpr(get.Object);

        // For 'this' access, verify we have the private field
        string fieldName = get.Name.Lexeme;
        var owner = FindLexicalPrivateOwner(fieldName);
        if (owner is null)
            throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");

        // Select the nearest lexical declaration before checking the member kind.
        if (owner.PrivateFieldTypes.TryGetValue(fieldName, out var fieldType))
        {
            ValidatePrivateReceiver(objType, owner, isStatic: false, get.Name);
            ObservePendingGetterFieldRead(owner, isStatic: false, fieldName);
            ObservePrivateSourceMemberSelection(get.Name, objType, owner, MemberFacet.PrivateInstance);
            return fieldType;
        }
        if (owner.PrivateMethodTypes.TryGetValue(fieldName, out var methodType))
        {
            ValidatePrivateReceiver(objType, owner, isStatic: false, get.Name);
            ObservePrivateSourceMemberSelection(get.Name, objType, owner, MemberFacet.PrivateInstance);
            return methodType;
        }
        if (owner.StaticPrivateMethodTypes.TryGetValue(fieldName, out var staticMethodType))
        {
            ObservePrivateSourceMemberSelection(get.Name, objType, owner, MemberFacet.PrivateStatic);
            return objType is TypeInfo.Any ? TypeInfo.Any.Shared : staticMethodType;
        }

        // Check static private fields if accessing on the class itself
        if (IsStaticPrivateConstructor(objType, owner) && owner.StaticPrivateFieldTypes.TryGetValue(fieldName, out var staticFieldType))
        {
            ObservePendingGetterFieldRead(owner, isStatic: true, fieldName);
            ObservePrivateSourceMemberSelection(get.Name, objType, owner, MemberFacet.PrivateStatic);
            return objType is TypeInfo.Any ? TypeInfo.Any.Shared : staticFieldType;
        }

        throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
    }

    /// <summary>
    /// Type checks ES2022 private field assignment: obj.#field = value
    /// </summary>
    private TypeInfo CheckSetPrivate(Expr.SetPrivate set)
    {
        using var memberLookup = BeginSourceMemberLookup(set, set.Name, MemberOperation.Write);
        TypeInfo result = CheckSetPrivateCore(set);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo CheckSetPrivateCore(Expr.SetPrivate set)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{set.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        TypeInfo objType = CheckExpr(set.Object);
        TypeInfo valueType = CheckExpr(set.Value);
        string fieldName = set.Name.Lexeme;
        var owner = FindLexicalPrivateOwner(fieldName);
        if (owner is null)
            throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");

        // An inner declaration shadows all enclosing declarations of the same name.
        TypeInfo? fieldType = null;
        if (owner.PrivateFieldTypes.TryGetValue(fieldName, out var pf))
        {
            ValidatePrivateReceiver(objType, owner, isStatic: false, set.Name);
            ObservePrivateSourceMemberSelection(set.Name, objType, owner, MemberFacet.PrivateInstance);
            fieldType = pf;
        }
        else if (IsStaticPrivateConstructor(objType, owner) && owner.StaticPrivateFieldTypes.TryGetValue(fieldName, out var spf))
        {
            ObservePrivateSourceMemberSelection(set.Name, objType, owner, MemberFacet.PrivateStatic);
            fieldType = objType is TypeInfo.Any ? TypeInfo.Any.Shared : spf;
        }

        if (fieldType == null)
        {
            throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
        }

        if (!IsCompatible(fieldType, valueType))
        {
            throw new TypeCheckException($" Cannot assign type '{valueType}' to private field '{fieldName}' of type '{fieldType}'.", line: set.Name.Line, tsCode: "TS2322");
        }

        return valueType;
    }

    /// <summary>
    /// Type checks ES2022 private method call: obj.#method(args)
    /// </summary>
    private TypeInfo CheckCallPrivate(Expr.CallPrivate call)
    {
        using var invocationAttempt = BeginEditorInvocation(call, EditorInvocationKind.PrivateCall, call.Arguments);
        TypeInfo result = CheckCallPrivateCore(call);
        invocationAttempt?.Succeed(result);
        return result;
    }

    private TypeInfo CheckCallPrivateCore(Expr.CallPrivate call)
    {
        ForgetSourceMemberCall(call, call.Name, call);
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot call private method '{call.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        TypeInfo objType = CheckExpr(call.Object);
        string methodName = call.Name.Lexeme;
        var owner = FindLexicalPrivateOwner(methodName);
        if (owner is null)
            throw new TypeCheckException($" Private method '{methodName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");

        // Private names follow lexical nesting, never superclass lookup.
        TypeInfo? methodType = null;
        MemberFacet selectedFacet = MemberFacet.PrivateInstance;
        if (owner.PrivateFieldTypes.TryGetValue(methodName, out var pf))
        {
            ObservePendingGetterFieldRead(owner, isStatic: false, methodName);
            methodType = pf;
        }
        else if (owner.PrivateMethodTypes.TryGetValue(methodName, out var pm))
        {
            methodType = pm;
        }
        else if (IsStaticPrivateConstructor(objType, owner) &&
            (owner.StaticPrivateFieldTypes.TryGetValue(methodName, out var spm) ||
                owner.StaticPrivateMethodTypes.TryGetValue(methodName, out spm)))
        {
            methodType = spm;
            selectedFacet = MemberFacet.PrivateStatic;
            if (owner.StaticPrivateFieldTypes.ContainsKey(methodName))
                ObservePendingGetterFieldRead(owner, isStatic: true, methodName);
        }

        if (methodType == null)
        {
            throw new TypeCheckException($" Private method '{methodName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
        }

        if (selectedFacet == MemberFacet.PrivateInstance)
            ValidatePrivateReceiver(objType, owner, isStatic: false, call.Name);
        RecordPrivateSourceMemberRead(call, call.Name, objType, owner, selectedFacet);
        if (methodType is TypeInfo.Any || selectedFacet == MemberFacet.PrivateStatic && objType is TypeInfo.Any)
        {
            foreach (var argument in call.Arguments) CheckExpr(argument);
            return TypeInfo.Any.Shared;
        }

        TypeInfo originalMethodType = methodType;
        if (methodType is TypeInfo.GenericFunction genericMethod)
        {
            var argumentTypes = CheckGenericInferenceArguments(call.Arguments);
            try
            {
                var arguments = InferTypeArguments(genericMethod,
                    PrepareFreshCallArgumentsForInference(genericMethod.ParamTypes, call.Arguments, argumentTypes));
                methodType = InstantiateGenericFunction(genericMethod, arguments);
            }
            catch (TypeCheckException exception) when (exception.Diagnostic.TsCode == "TS2344")
            {
                throw new TypeCheckException(exception.Message, call.Name.Line, tsCode: "TS2345");
            }
        }

        // Check argument types against the callable member's signature.
        if (methodType is not TypeInfo.Function funcType)
        {
            throw new TypeCheckException($" Private member '{methodName}' is not callable.", line: call.Name.Line, tsCode: "TS2349");
        }
        if (EditorFacts.IsEnabled && HasPrivateSourceOwner(objType, owner.Core.DeclarationId,
                selectedFacet == MemberFacet.PrivateStatic))
            RecordEditorCallCandidates(originalMethodType);

        CheckPrivateCallArguments(call, funcType);

        RecordProvenSourceMemberCall(call, call.Name, call);
        if (!ReferenceEquals(originalMethodType, funcType))
            RecordEditorInvocationInstantiation(originalMethodType, funcType);
        RecordEditorInvocationSelection(originalMethodType, funcType);
        return funcType.ReturnType;
    }

    // Any receivers still require a lexical declaration; runtime branding checks their value.
    // Generic constructor aliases retain their declaration identity. Accepting
    // every generic class here would expose another declaration's same-spelled
    // private member through the current class's metadata.
    private bool IsStaticPrivateConstructor(TypeInfo type, TypeInfo.Class owner) =>
        type is TypeInfo.Any or TypeInfo.Class ||
        type is TypeInfo.MutableClass mutable && mutable.DeclarationId != 0 &&
        mutable.DeclarationId == owner.Core.DeclarationId ||
        type is TypeInfo.GenericClass generic &&
        SourceDerivesFromDeclaration(generic, owner.Core.DeclarationId);
}
