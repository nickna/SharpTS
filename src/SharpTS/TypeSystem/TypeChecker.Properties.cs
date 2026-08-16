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
    /// <summary>
    /// When the current class extends a generic-class instantiation (e.g. <c>extends Box&lt;number&gt;</c>),
    /// substitutes the superclass's type arguments into a member type resolved from it — so a
    /// <c>super(...)</c> call or <c>super.method(...)</c> sees the instantiated parameter/return types
    /// (e.g. <c>T</c> → <c>number</c>) rather than the generic parameters.
    /// </summary>
    private TypeInfo SubstituteSuperclassTypeArgs(TypeInfo memberType)
    {
        if (_currentClass?.Superclass is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig)
            return Substitute(memberType, GenericClassSubs(gc, ig.TypeArguments));
        return memberType;
    }

    private TypeInfo CheckSuper(Expr.Super expr)
    {
        if (_currentClass == null)
        {
            throw new TypeCheckException("Cannot use 'super' outside of a class.", tsCode: "TS2335");
        }
        if (_currentClass.Superclass == null)
        {
            throw new TypeCheckException($" Class '{_currentClass.Name}' does not have a superclass.", tsCode: "TS2335");
        }

        // Get methods from superclass, handling both Class and InstantiatedGeneric
        var superMethods = GetMethods(_currentClass.Superclass);
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
            if (superMethods != null && superMethods.TryGetValue("constructor", out var ctorType))
            {
                return SubstituteSuperclassTypeArgs(ctorType);
            }
            // Default constructor with no parameters
            return new TypeInfo.Function([], TypeInfo.Void.Shared);
        }

        if (superMethods != null && superMethods.TryGetValue(expr.Method.Lexeme, out var methodType))
        {
            return SubstituteSuperclassTypeArgs(methodType);
        }

        throw new TypeCheckException($" Property '{expr.Method.Lexeme}' does not exist on superclass '{superName}'.", tsCode: "TS2339");
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
                throw new TypeCheckException(
                    "'this' implicitly has type 'any' because it does not have a type annotation.",
                    line: expr.Keyword.Line,
                    tsCode: "TS2683");
            }
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
        }

        // Check for property narrowing (e.g., after "if (obj.prop !== null)" or nested "obj.a.b")
        // Use NarrowingPathExtractor to support nested property access
        var path = Narrowing.NarrowingPathExtractor.TryExtract(get);
        if (path != null)
        {
            var narrowedType = GetNarrowing(path);
            if (narrowedType != null)
                return narrowedType;
        }

        TypeInfo objType = CheckExpr(get.Object);

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

        return ResolveMemberType(get, objType);
    }

    /// <summary>
    /// Resolves the type of a property read on an already-evaluated receiver type, dispatching by
    /// type category. Throws TS2339 when the member is absent. Shared by <see cref="CheckGet"/>'s
    /// strict and (#796) defaulted-destructuring-tolerant paths.
    /// </summary>
    private TypeInfo ResolveMemberType(Expr.Get get, TypeInfo objType)
    {
        // A bare `null`/`undefined` receiver has no properties — `tsc` rejects access on it (TS2339
        // for `undefined`, TS2531 for `null`), just as it does for a union containing them (see
        // CheckGetOnUnion). Optional chaining (`x?.p`) short-circuits to `undefined` instead. Without
        // this guard these types classify to TypeCategory.Null/Undefined, miss the dispatch switch
        // below, and silently yield `any`. #742
        switch (objType)
        {
            case TypeInfo.Undefined:
                if (get.Optional) return TypeInfo.Undefined.Shared;
                throw new TypeCheckException($"Property '{get.Name.Lexeme}' does not exist on type 'undefined'.", get.Name.Line, tsCode: "TS2339");
            case TypeInfo.Null:
                if (get.Optional) return TypeInfo.Undefined.Shared;
                throw new TypeCheckException("Object is possibly 'null'.", get.Name.Line, tsCode: "TS2531");
        }

        var category = TypeCategoryResolver.Classify(objType);
        string memberName = get.Name.Lexeme;

        // Fast path for built-in types with explicit member validation
        if (TypeCategoryResolver.HasBuiltInMemberValidation(category))
        {
            var memberType = ResolveBuiltInMemberType(category, objType, memberName);
            if (memberType != null) return memberType;
            throw new TypeCheckException($" Property '{memberName}' does not exist on type '{GetTypeDisplayName(category, objType)}'.", tsCode: "TS2339");
        }

        // Category-based dispatch for user-defined and special types
        return category switch
        {
            TypeCategory.TypeParameter when objType is TypeInfo.TypeParameter tp =>
                CheckGetOnTypeParameter(tp, get.Name),
            TypeCategory.Class when objType is TypeInfo.Class classType =>
                CheckGetOnClass(classType, get.Name),
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
                throw NullableMemberAccessError(union, memberName, receiver);
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

        // If property is missing on some members, add undefined to result
        if (hasMissingProperty)
        {
            memberTypes.Add(TypeInfo.Undefined.Shared);
        }

        // For optional chaining with null/undefined in the union, add undefined to result
        if (isOptional && hasNullOrUndefined)
        {
            memberTypes.Add(TypeInfo.Undefined.Shared);
        }

        // If no members have the property at all, fall back to Any
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
        string? ident = receiver is Expr.Variable v ? v.Name.Lexeme : null;

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
                if (IsCompatible(declared, rhs))
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
        TypeInfo objType = CheckExpr(set.Object);

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
                // Check that the property exists on the constraint
                var propType = CheckGetOnType(tp.Constraint, set.Name);
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

        // Handle static property assignment
        if (objType is TypeInfo.Class classType)
        {
            TypeInfo? current = classType;
            while (current != null)
            {
                var staticProps = GetStaticProperties(current);
                if (staticProps != null && staticProps.TryGetValue(set.Name.Lexeme, out var staticPropType))
                {
                    EnforceStaticMemberAccess(current, set.Name);
                    TypeInfo valueType = CheckExpr(set.Value);
                    if (!IsCompatible(staticPropType, valueType))
                    {
                        throw new TypeCheckException($" Cannot assign '{valueType}' to static property '{set.Name.Lexeme}' of type '{staticPropType}'.", tsCode: "TS2322");
                    }
                    if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, staticPropType, valueType);
                    return valueType;
                }
                current = GetSuperclass(current);
            }
            return CheckExpr(set.Value);
        }

        if (objType is TypeInfo.Instance instance)
        {
             string memberName = set.Name.Lexeme;

             // Handle InstantiatedGeneric
             if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                 ig.GenericDefinition is TypeInfo.GenericClass gc)
             {
                 // Build substitution map
                 Dictionary<string, TypeInfo> subs = [];
                 for (int i = 0; i < gc.TypeParams.Count; i++)
                     subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                 // Check for setter
                 if (gc.Setters?.TryGetValue(memberName, out var setterType) == true)
                 {
                     var substitutedType = Substitute(setterType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new TypeCheckException($" Cannot assign '{valueType}' to property '{memberName}' expecting '{substitutedType}'.", tsCode: "TS2322");
                     }
                     if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, substitutedType, valueType);
                     return valueType;
                 }

                 // Check for field
                 if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                 {
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
             if (instance.ClassType is not TypeInfo.Class startClass)
                 return CheckExpr(set.Value);

             TypeInfo? current = startClass;

             // Check for setter first
             while (current != null)
             {
                 var setters = GetSetters(current);
                 var getters = GetGetters(current);
                 if (setters != null && setters.TryGetValue(memberName, out var setterType))
                 {
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(setterType, valueType))
                     {
                         throw new TypeCheckException($" Cannot assign '{valueType}' to property '{memberName}' expecting '{setterType}'.", tsCode: "TS2322");
                     }
                     if (assignedPath != null) InstallPostAssignmentNarrowing(assignedPath, setterType, valueType);
                     return valueType;
                 }

                 // Check if there's a getter but no setter (read-only property)
                 if (getters != null && getters.ContainsKey(memberName) && (setters == null || !setters.ContainsKey(memberName)))
                 {
                     throw new TypeCheckException($" Cannot assign to '{memberName}' because it is a read-only property (has getter but no setter).", tsCode: "TS2540");
                 }

                 current = GetSuperclass(current);
             }

             // Reset to check access and readonly
             current = startClass;

             // Check access and readonly
             while (current != null)
             {
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

                 current = GetSuperclass(current);
             }

             // Check the assigned value against the field's declared type (walking the chain). Plain
             // (non-setter) instance fields were previously assigned without a compatibility check.
             TypeInfo fieldValueType = CheckExpr(set.Value);
             current = startClass;
             while (current != null)
             {
                 var fieldTypes = GetFieldTypes(current);
                 if (fieldTypes != null && fieldTypes.TryGetValue(memberName, out var fieldDeclType))
                 {
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
                     throw new TypeCheckException($" Cannot assign to '{set.Name.Lexeme}' because it is a read-only property.", tsCode: "TS2540");
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
             // For now, disallow adding new properties to records via assignment to mimic strictness
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
                        throw new TypeCheckException($" Cannot assign to '{set.Name.Lexeme}' because it is a read-only property.", tsCode: "TS2540");
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
        if (objType is TypeInfo.Class classType)
        {
            return CheckGetOnClass(classType, memberName);
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
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{get.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        // Check the object expression
        TypeInfo objType = CheckExpr(get.Object);

        // For 'this' access, verify we have the private field
        string fieldName = get.Name.Lexeme;

        // Look up in current class's private fields (NOT inherited - private fields are not inherited)
        if (_currentClass.PrivateFieldTypes.TryGetValue(fieldName, out var fieldType))
        {
            return fieldType;
        }

        // Check static private fields if accessing on the class itself
        if (objType is TypeInfo.Class && _currentClass.StaticPrivateFieldTypes.TryGetValue(fieldName, out var staticFieldType))
        {
            return staticFieldType;
        }

        throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
    }

    /// <summary>
    /// Type checks ES2022 private field assignment: obj.#field = value
    /// </summary>
    private TypeInfo CheckSetPrivate(Expr.SetPrivate set)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot access private field '{set.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        TypeInfo objType = CheckExpr(set.Object);
        TypeInfo valueType = CheckExpr(set.Value);
        string fieldName = set.Name.Lexeme;

        // Look up in current class's private fields (NOT inherited)
        TypeInfo? fieldType = null;
        if (_currentClass.PrivateFieldTypes.TryGetValue(fieldName, out var pf))
        {
            fieldType = pf;
        }
        else if (objType is TypeInfo.Class && _currentClass.StaticPrivateFieldTypes.TryGetValue(fieldName, out var spf))
        {
            fieldType = spf;
        }

        if (fieldType == null)
        {
            throw new TypeCheckException($" Private field '{fieldName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
        }

        if (!IsCompatible(fieldType, valueType))
        {
            throw new TypeCheckException($" Cannot assign type '{valueType}' to private field '{fieldName}' of type '{fieldType}'.", tsCode: "TS2322");
        }

        return valueType;
    }

    /// <summary>
    /// Type checks ES2022 private method call: obj.#method(args)
    /// </summary>
    private TypeInfo CheckCallPrivate(Expr.CallPrivate call)
    {
        // Verify we're inside a class body
        if (_currentClass == null)
        {
            throw new TypeCheckException($" Cannot call private method '{call.Name.Lexeme}' outside of a class body.", tsCode: "TS18013");
        }

        TypeInfo objType = CheckExpr(call.Object);
        string methodName = call.Name.Lexeme;

        // Look up in current class's private methods (NOT inherited)
        TypeInfo? methodType = null;
        if (_currentClass.PrivateMethodTypes.TryGetValue(methodName, out var pm))
        {
            methodType = pm;
        }
        else if (objType is TypeInfo.Class && _currentClass.StaticPrivateMethodTypes.TryGetValue(methodName, out var spm))
        {
            methodType = spm;
        }

        if (methodType == null)
        {
            throw new TypeCheckException($" Private method '{methodName}' does not exist on class '{_currentClass.Name}'.", tsCode: "TS2339");
        }

        // Check argument types against method signature
        if (methodType is not TypeInfo.Function funcType)
        {
            throw new TypeCheckException($" Private member '{methodName}' is not a method.", tsCode: "TS2349");
        }

        // Check argument count
        if (call.Arguments.Count < funcType.RequiredParams)
        {
            throw new TypeCheckException($" Private method '{methodName}' requires at least {funcType.RequiredParams} arguments, got {call.Arguments.Count}.", tsCode: "TS2554");
        }

        if (!funcType.HasRestParam && call.Arguments.Count > funcType.ParamTypes.Count)
        {
            throw new TypeCheckException($" Private method '{methodName}' accepts at most {funcType.ParamTypes.Count} arguments, got {call.Arguments.Count}.", tsCode: "TS2554");
        }

        // Check each argument type
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            TypeInfo argType = CheckExpr(call.Arguments[i]);
            TypeInfo paramType = i < funcType.ParamTypes.Count
                ? funcType.ParamTypes[i]
                : funcType.ParamTypes[^1]; // Rest parameter type

            // Optional/default params accept an explicit `undefined` (#668). A rest parameter's
            // elements are not optional in that sense, so only widen for non-rest positions.
            bool optional = i >= funcType.MinArity &&
                            !(funcType.HasRestParam && i >= funcType.ParamTypes.Count - 1);
            if (!IsArgumentCompatible(paramType, argType, optional))
            {
                throw new TypeCheckException($" Argument {i + 1} to private method '{methodName}' has type '{argType}' but expected '{paramType}'.", tsCode: "TS2345");
            }
        }

        return funcType.ReturnType;
    }
}
