using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Index access type checking - bracket notation for arrays, tuples, objects.
/// </summary>
/// <remarks>
/// Contains handlers for index operations:
/// CheckGetIndex, CheckSetIndex.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// noUncheckedIndexedAccess adds undefined to reads whose key is not known to
    /// designate an existing property/tuple element.
    /// </summary>
    private TypeInfo WithUncheckedUndefined(TypeInfo type)
    {
        if (!_noUncheckedIndexedAccess || !_strictNullChecks ||
            type is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Undefined ||
            type is TypeInfo.Union { ContainsUndefined: true })
        {
            return type;
        }
        return CreateUnion(type, TypeInfo.Undefined.Shared);
    }

    private TypeInfo WithOptionalPropertyUndefined(TypeInfo type, bool optional) =>
        optional && _strictNullChecks ? CreateUnion(type, TypeInfo.Undefined.Shared) : type;

    private TypeInfo CheckGetIndex(Expr.GetIndex getIndex)
    {
        TypeInfo objType = CheckExpr(getIndex.Object);
        TypeInfo indexType = CheckExpr(getIndex.Index);

        // An enum-typed index (`x[E.A]` — enum member accesses type as the enum) participates
        // as its underlying key kind: numeric enums hit number index signatures, string enums
        // hit string ones.
        if (indexType is TypeInfo.Enum indexEnum)
        {
            indexType = indexEnum.Kind == EnumKind.String
                ? TypeInfo.String.Shared
                : TypeInfo.Primitive.Number;
        }

        // Allow indexing on 'any' type (returns 'any')
        if (objType is TypeInfo.Any)
        {
            return TypeInfo.Any.Shared;
        }

        // TS2538: a value whose type can't be a property key — e.g. a function type such as
        // `Symbol.for` (the method, not a call) — cannot be used as an index. This is distinct from
        // the implicit-any TS7053 emitted below for a valid-but-unmodeled key.
        if (indexType is TypeInfo.Function or TypeInfo.OverloadedFunction)
        {
            throw new TypeCheckException($" Type '{indexType}' cannot be used as an index type.",
                line: TryGetExprLine(getIndex.Index), tsCode: "TS2538");
        }

        // Indexing a method's inferred-return-type placeholder (still being resolved during
        // body checking, e.g. calling a sibling static method from a ternary). Treat as any.
        if (objType is TypeInfo.Inferred)
        {
            return TypeInfo.Any.Shared;
        }

        // A deferred recursive alias (`part.subparts: DeepReadonly<Part[]>`) is resolved one
        // level so the array shape underneath it becomes indexable. ExpandRecursiveTypeAlias is
        // identity-cached, so re-entering the self-referential `DeepReadonly → DeepReadonlyObject
        // → DeepReadonly<Part[]> → …` chain reuses the cached node instead of recursing forever;
        // the resulting element type stays a deferred alias and only expands when a member of
        // `part.subparts[0]` is actually touched (#365).
        if (objType is TypeInfo.RecursiveTypeAlias rtaObj)
            objType = ExpandRecursiveTypeAlias(rtaObj);

        // An instantiated generic interface flattens so its numeric index signature (substituted)
        // is reachable. The element type may itself be a still-deferred recursive alias (the
        // `DeepReadonly<Part>` element of `DeepReadonlyArray<Part>`) — that is intentionally kept
        // deferred and surfaced as the index result, not force-expanded here.
        if (objType is TypeInfo.InstantiatedGeneric igObj && !ContainsOpenTypeVariable(igObj)
            && FlattenInstantiatedInterface(igObj) is { } flatObj
            && flatObj.NumberIndexType is not null)
            objType = flatObj;

        // A deferred conditional is indexed through its constraint — `x[0]` where
        // `x: T extends (infer U)[] ? U[] : never` reads through `unknown[]`
        // (conditionalTypes1 f22/f23). Evaluate first: a concrete check resolves to a branch.
        if (objType is TypeInfo.ConditionalType condObj)
        {
            var evaluated = EvaluateConditionalType(condObj);
            if (evaluated is TypeInfo.ConditionalType stillDeferred)
            {
                foreach (var constraint in GetConditionalConstraints(stillDeferred))
                {
                    if (CheckGetIndexOnType(constraint, indexType, getIndex) is { } viaConstraint)
                        return viaConstraint;
                }
                return TypeInfo.Any.Shared;
            }
            return CheckGetIndexOnType(evaluated, indexType, getIndex)
                ?? throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing '{evaluated}'.", tsCode: "TS7053");
        }

        // Optional bracket access: strip null/undefined from object type
        if (getIndex.Optional && objType is TypeInfo.Union optUnion)
        {
            var nonNullish = optUnion.FlattenedTypes
                .Where(t => t is not (TypeInfo.Null or TypeInfo.Undefined))
                .ToList();
            if (nonNullish.Count == 0)
            {
                return TypeInfo.Undefined.Shared;
            }
            objType = nonNullish.Count == 1 ? nonNullish[0] : new TypeInfo.Union(nonNullish);
        }

        // Handle Union types - distribute index access across all union members
        if (objType is TypeInfo.Union union)
        {
            List<TypeInfo> memberTypes = [];
            foreach (var member in union.FlattenedTypes)
            {
                try
                {
                    var memberType = CheckGetIndexOnType(member, indexType, getIndex);
                    if (memberType != null)
                    {
                        memberTypes.Add(memberType);
                    }
                    else
                    {
                        throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing member '{member}' of union type.", tsCode: "TS7053");
                    }
                }
                catch (TypeCheckException)
                {
                    throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing all members of union type '{union}'.", tsCode: "TS7053");
                }
            }
            // Return union of all member types
            var unique = memberTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
        }

        // Handle TypeParameter with constraint - delegate to constraint for indexing
        if (objType is TypeInfo.TypeParameter objTp)
        {
            // If index is a TypeParameter with keyof constraint matching this type param, allow it
            if (indexType is TypeInfo.TypeParameter indexTp && indexTp.Constraint is TypeInfo.KeyOf keyOf)
            {
                // keyof T where T is the same type parameter we're indexing - return the indexed access type
                if (keyOf.SourceType is TypeInfo.TypeParameter keyOfTp && keyOfTp.Name == objTp.Name)
                {
                    // Return IndexedAccess type that will be resolved when concrete types are provided
                    // For now, return Any since we can't know the exact property type until instantiation
                    return TypeInfo.Any.Shared;
                }
            }

            // Delegate to constraint type if available
            if (objTp.Constraint != null)
            {
                var constrainedResult = CheckGetIndexOnType(objTp.Constraint, indexType, getIndex);
                if (constrainedResult != null) return constrainedResult;
            }

            // Check if the index type has a keyof constraint on this type parameter
            // This handles cases like T[K] where K extends keyof T, and T is unconstrained
            if (indexType is TypeInfo.TypeParameter indexTp2 && indexTp2.Constraint is TypeInfo.KeyOf keyOf2)
            {
                if (keyOf2.SourceType is TypeInfo.TypeParameter keyOfTp2 && keyOfTp2.Name == objTp.Name)
                {
                    // K extends keyof T and we're indexing T with K - allow it
                    return TypeInfo.Any.Shared;
                }
            }

            // If index is a string/number type, return Any for generic flexibility
            if (IsString(indexType) || IsNumber(indexType))
            {
                return TypeInfo.Any.Shared;
            }

            // A generic key whose constraint is key-like (K extends string | number | symbol)
            // indexes a generic object the same way a plain string key does — T[K] can only be
            // judged at instantiation (inferTypes1 invoker: obj[key] with obj: T, key: K).
            if (indexType is TypeInfo.TypeParameter genericKeyForTp &&
                ApparentTypeOf(genericKeyForTp) is { } keyLikeApparent &&
                IsCompatible(KeyLikeUnion, keyLikeApparent))
            {
                return TypeInfo.Any.Shared;
            }

            // Unconstrained type parameter can't be indexed with arbitrary types
            throw new TypeCheckException($" Cannot index type parameter '{objTp.Name}' with type '{indexType}'.", tsCode: "TS7053");
        }

        // Handle TypeParameter index type with keyof constraint
        if (indexType is TypeInfo.TypeParameter indexTpOnly && indexTpOnly.Constraint is TypeInfo.KeyOf keyOfConstraint)
        {
            // Check if the keyof constraint's source type is compatible with objType
            var keyOfSourceType = keyOfConstraint.SourceType;
            if (keyOfSourceType is TypeInfo.TypeParameter)
            {
                // The keyof is on a type parameter - in generic context, allow it
                return TypeInfo.Any.Shared;
            }
            // If we can verify the keyof source matches objType, allow it
            if (IsCompatible(keyOfSourceType, objType))
            {
                return TypeInfo.Any.Shared;
            }
        }

        // Allow indexing with 'any' type key (returns 'any')
        if (indexType is TypeInfo.Any)
        {
            return TypeInfo.Any.Shared;
        }

        // A generic key (K extends string | number | symbol) reaching an object with an index
        // signature resolves to the signature's value type — `obj[key]` where
        // `obj: Record<K, V>` is V (inferTypes1 invoker).
        if (indexType is TypeInfo.TypeParameter genericKey &&
            ApparentTypeOf(genericKey) is { } keyApparent && IsCompatible(KeyLikeUnion, keyApparent))
        {
            if (objType is TypeInfo.Record { StringIndexType: { } recIdxType }) return WithUncheckedUndefined(recIdxType);
            if (objType is TypeInfo.Interface { StringIndexType: { } itfIdxType }) return WithUncheckedUndefined(itfIdxType);
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // String literal index - look up specific property
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return WithOptionalPropertyUndefined(fieldType, rec.IsFieldOptional(propName));
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return WithOptionalPropertyUndefined(memberType, itf.GetAllOptionalMembers().Contains(propName));
            }

            // Dynamic string index - use index signature if available
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return WithUncheckedUndefined(rec2.StringIndexType);
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return WithUncheckedUndefined(itf2.StringIndexType);
            if (GetClassIndexType(objType, TokenType.TYPE_STRING) is { } clsStr)
                return WithUncheckedUndefined(clsStr);

            // Allow bracket access on any object/interface (returns any for unknown keys)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return TypeInfo.Any.Shared;

            // `typeof x === 'object'` narrows x to TypeInfo.Object. Indexing such
            // a narrowed value by string is valid JS and must return Any — not
            // throwing here is important for code like `Object.keys(obj).forEach(k => obj[k])`.
            if (objType is TypeInfo.Object)
                return TypeInfo.Any.Shared;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple indexing with position-based types
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> exact element type
                if (getIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return tupleType.ElementTypes[i];
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return tupleType.RestElementType;
                    if (i < 0 || (tupleType.MaxLength != null && i >= tupleType.MaxLength))
                        throw new TypeCheckException($" Tuple index {i} is out of bounds.", tsCode: "TS2493");
                }
                // Dynamic index -> union of all possible types
                return WithUncheckedUndefined(ComputeTupleElementUnion(tupleType));
            }

            if (objType is TypeInfo.Array arrayType)
            {
                return WithUncheckedUndefined(arrayType.ElementType);
            }

            // String indexed by number returns a string (single character, or undefined at runtime).
            if (objType is TypeInfo.String or TypeInfo.StringLiteral)
            {
                return WithUncheckedUndefined(TypeInfo.String.Shared);
            }

            // TypedArray index access returns number
            if (objType is TypeInfo.TypedArray)
            {
                return WithUncheckedUndefined(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
            }

            // Buffer index access returns number (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
            {
                return WithUncheckedUndefined(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
            }

            // Enum reverse mapping: Direction[0] returns "Up" (only for numeric enums)
            if (objType is TypeInfo.Enum enumType)
            {
                // Const enums cannot use reverse mapping
                if (enumType.IsConst)
                {
                    throw new TypeCheckException($" A const enum member can only be accessed using its name, not by index. Cannot use reverse mapping on const enum '{enumType.Name}'.", tsCode: "TS2476");
                }
                if (enumType.Kind == EnumKind.String)
                {
                    throw new TypeCheckException($" Reverse mapping is not supported for string enum '{enumType.Name}'.", tsCode: "TS2476");
                }
                return TypeInfo.String.Shared;
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return WithUncheckedUndefined(itf3.NumberIndexType);
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return WithUncheckedUndefined(rec3.NumberIndexType);
            // A class with only a string index signature still accepts numeric keys (number keys
            // are a subset of string keys), matching TypeScript.
            if (GetClassIndexType(objType, TokenType.TYPE_NUMBER) is { } clsNum)
                return WithUncheckedUndefined(clsNum);
            if (GetClassIndexType(objType, TokenType.TYPE_STRING) is { } clsNumStr)
                return WithUncheckedUndefined(clsNumStr);
        }

        // Handle symbol index (Symbol and UniqueSymbol both qualify)
        if (indexType is TypeInfo.Symbol or TypeInfo.UniqueSymbol)
        {
            // A well-known symbol (`Symbol.iterator`, ...) that the object type models as a distinct
            // named member (canonical "@@name" — see CheckObject/TryGetWellKnownSymbolMemberName)
            // resolves to THAT member's own type, not the merged symbol index signature — which
            // would lose per-symbol precision when an object defines several distinct well-known
            // symbol members (#99 Cluster B).
            if (TryGetWellKnownSymbolMemberName(getIndex.Index) is { } wellKnownName &&
                GetMemberType(objType, wellKnownName) is { } wellKnownType)
                return wellKnownType;

            if (objType is TypeInfo.Interface itf4 && itf4.SymbolIndexType != null)
                return WithUncheckedUndefined(itf4.SymbolIndexType);
            if (objType is TypeInfo.Record rec4 && rec4.SymbolIndexType != null)
                return WithUncheckedUndefined(rec4.SymbolIndexType);
            if (GetClassIndexType(objType, TokenType.TYPE_SYMBOL) is { } clsSym)
                return WithUncheckedUndefined(clsSym);

            // Well-known symbols (Symbol.iterator/species/toStringTag/match/...) are
            // present on essentially every object type. SharpTS models them loosely,
            // so a symbol index on any object-like value resolves to `any` — this
            // already covered Record/Interface/Instance; extend it to the built-in
            // reference types (SharedArrayBuffer[Symbol.species], typed arrays, Map,
            // Promise, ...) which otherwise fell through to a spurious TS7053.
            if (IsSymbolIndexableObject(objType))
                return TypeInfo.Any.Shared;
        }

        throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing '{objType}'.", tsCode: "TS7053");
    }

    /// <summary>
    /// Object-like types on which a well-known-symbol index (<c>x[Symbol.iterator]</c>,
    /// <c>x[Symbol.species]</c>, ...) is permitted and resolves to <c>any</c>. Covers the
    /// structural object types plus the built-in reference types; excludes primitives,
    /// <c>null</c>/<c>undefined</c>, and unmodeled types (which keep their TS7053).
    /// </summary>
    private static bool IsSymbolIndexableObject(TypeInfo t) => t is
        TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance or TypeInfo.Object or
        TypeInfo.Array or TypeInfo.Tuple or TypeInfo.Function or TypeInfo.RegExp or
        TypeInfo.Map or TypeInfo.Set or TypeInfo.WeakMap or TypeInfo.WeakSet or
        TypeInfo.Promise or TypeInfo.Date or TypeInfo.Error or TypeInfo.Buffer or
        TypeInfo.SharedArrayBuffer or TypeInfo.ArrayBuffer or TypeInfo.DataView or
        TypeInfo.TypedArray;

    /// <summary>
    /// Returns the value type of a class's index signature for the given key type, or null if the
    /// type is not a class instance (or has no such index signature). Accepts both a bare
    /// <see cref="TypeInfo.Class"/> and a <see cref="TypeInfo.Instance"/> wrapping one.
    /// </summary>
    private static TypeInfo? GetClassIndexType(TypeInfo objType, TokenType keyType)
    {
        TypeInfo.Class? cls = objType switch
        {
            TypeInfo.Class c => c,
            TypeInfo.Instance { ClassType: TypeInfo.Class ic } => ic,
            _ => null
        };
        if (cls == null) return null;
        return keyType switch
        {
            TokenType.TYPE_STRING => cls.StringIndexType,
            TokenType.TYPE_NUMBER => cls.NumberIndexType,
            TokenType.TYPE_SYMBOL => cls.SymbolIndexType,
            _ => null
        };
    }

    private TypeInfo CheckSetIndex(Expr.SetIndex setIndex)
    {
        TypeInfo objType = CheckExpr(setIndex.Object);
        TypeInfo indexType = CheckExpr(setIndex.Index);
        TypeInfo valueType = CheckExpr(setIndex.Value);

        // Same enum-index widening as CheckGetIndex: `x[E.A] = v` uses the enum's key kind.
        if (indexType is TypeInfo.Enum indexEnum)
        {
            indexType = indexEnum.Kind == EnumKind.String
                ? TypeInfo.String.Shared
                : TypeInfo.Primitive.Number;
        }

        // Invalidate any narrowings affected by this index assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(setIndex.Object);
        if (basePath != null)
        {
            Narrowing.NarrowingPath? assignedPath = null;

            // For numeric literal index (tuple/array access), create ElementAccess path
            if (setIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
            {
                assignedPath = new Narrowing.NarrowingPath.ElementAccess(basePath, (int)d);
            }
            // For string literal index, treat as property access
            else if (setIndex.Index is Expr.Literal { Value: string propName })
            {
                assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, propName);
            }
            else
            {
                // For computed index, conservatively invalidate the entire object's narrowings
                assignedPath = basePath;
            }

            InvalidateNarrowingsFor(assignedPath);
        }

        // Allow setting on 'any' type
        if (objType is TypeInfo.Any)
        {
            return valueType;
        }

        // Resolve a deferred recursive alias one level (see the matching note in CheckGetIndex),
        // then flatten an instantiated generic interface so its (substituted) numeric index
        // signature — including its read-only flag — drives the checks below.
        if (objType is TypeInfo.RecursiveTypeAlias rtaSet)
            objType = ExpandRecursiveTypeAlias(rtaSet);

        if (objType is TypeInfo.InstantiatedGeneric igSet && !ContainsOpenTypeVariable(igSet)
            && FlattenInstantiatedInterface(igSet) is { } flatSet
            && flatSet.NumberIndexType is not null)
            objType = flatSet;

        // Writing through a read-only numeric index signature (an interface that extends
        // ReadonlyArray<…>) is rejected — #337 item 2 (TS2542).
        if ((IsNumber(indexType) || indexType is TypeInfo.NumberLiteral) &&
            objType is TypeInfo.Interface { ReadonlyNumberIndex: true })
        {
            throw new TypeCheckException(
                $" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");
        }

        // Handle Union types - verify assignment is valid for all union members
        if (objType is TypeInfo.Union union)
        {
            foreach (var member in union.FlattenedTypes)
            {
                // Verify the index is valid for each member
                var memberIndexResult = CheckSetIndexOnType(member, indexType, valueType, setIndex);
                if (memberIndexResult == null)
                {
                    throw new TypeCheckException($" Index type '{indexType}' is not valid for assigning to member '{member}' of union type.", tsCode: "TS7053");
                }
            }
            return valueType;
        }

        // Allow setting with 'any' type key
        if (indexType is TypeInfo.Any)
        {
            return valueType;
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // Check if value is compatible with string index signature
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (!IsCompatible(itf.StringIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to index signature type '{itf.StringIndexType}'.", tsCode: "TS2322");
                return valueType;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (!IsCompatible(rec.StringIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to index signature type '{rec.StringIndexType}'.", tsCode: "TS2322");
                return valueType;
            }
            if (GetClassIndexType(objType, TokenType.TYPE_STRING) is { } classStringIndex)
            {
                if (!IsCompatible(classStringIndex, valueType))
                    throw new TypeCheckException(
                        $" Cannot assign '{valueType}' to index signature type '{classStringIndex}'.",
                        tsCode: "TS2322");
                return valueType;
            }

            // Allow bracket assignment on any object/interface
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple index assignment
            if (objType is TypeInfo.Tuple tupleType)
            {
                // A readonly tuple (`[1, 2] as const`, `readonly [number, number]`) permits reading
                // only — reject the write before the element-type checks so a same-type write is
                // TS2542 ("only permits reading") rather than slipping through, and a wrong-type
                // write is TS2542 rather than TS2322 (#509). Keys off the tuple's own IsReadonly,
                // not its element types: `[{ n: 1 } as const]` has a writable array/tuple with a
                // readonly element, and that element-only readonly must not reject the slot write.
                if (tupleType.IsReadonly)
                    throw new TypeCheckException($" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");

                // Literal index -> check against specific element type
                if (setIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.ElementTypes[i], valueType))
                            throw new TypeCheckException($" Cannot assign '{valueType}' to tuple element of type '{tupleType.ElementTypes[i]}'.", tsCode: "TS2322");
                        return valueType;
                    }
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.RestElementType, valueType))
                            throw new TypeCheckException($" Cannot assign '{valueType}' to tuple rest element of type '{tupleType.RestElementType}'.", tsCode: "TS2322");
                        return valueType;
                    }
                    throw new TypeCheckException($" Tuple index {i} is out of bounds.", tsCode: "TS2493");
                }
                // Dynamic index -> value must be compatible with all possible element types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                if (!allTypes.All(t => IsCompatible(t, valueType)))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to tuple with mixed element types.", tsCode: "TS2322");
                return valueType;
            }

            if (objType is TypeInfo.Array arrayType)
            {
                // A readonly array (`readonly number[]`, `as const` array) permits reading only —
                // reject every numeric-index write with TS2542, ahead of (and regardless of) the
                // ECMA array-index range carve-out below, since a readonly index signature has no
                // writable slot at any key. ReadonlyArray<T> already routes through the interface
                // ReadonlyNumberIndex guard above; this covers the `readonly T[]` array form (#509).
                if (arrayType.IsReadonly)
                    throw new TypeCheckException($" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");

                // ECMA-262 array indices are integers in [0, 2^32 - 2]. Numeric
                // literals outside that range (e.g. 4294967295, -1) are regular
                // property assignments, not array-element writes — element-type
                // compatibility doesn't apply.
                if (IsArrayIndexInRange(setIndex.Index)
                    && !IsCompatible(arrayType.ElementType, valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to array of '{arrayType.ElementType}'.", tsCode: "TS2322");
                }
                return valueType;
            }
        }

        // String-keyed assignment on arrays. ECMA-262 array indices are
        // canonical numeric-string keys: \`a["0"] = v\` is equivalent to
        // \`a[0] = v\`. Type checker accepts both.
        if ((IsString(indexType) || indexType is TypeInfo.StringLiteral)
            && objType is TypeInfo.Array arrayWithStringIndex)
        {
            // A canonical numeric-string key (`a["0"] = v`) is an element write too, so a readonly
            // array rejects it with TS2542 just like the numeric-index form above (#509).
            if (arrayWithStringIndex.IsReadonly)
                throw new TypeCheckException($" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");
            if (!IsCompatible(arrayWithStringIndex.ElementType, valueType))
                throw new TypeCheckException($" Cannot assign '{valueType}' to array of '{arrayWithStringIndex.ElementType}'.", tsCode: "TS2322");
            return valueType;
        }

        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // TypedArray index assignment
            if (objType is TypeInfo.TypedArray typedArrayType)
            {
                // TypedArrays accept number values
                if (!IsNumber(valueType) && valueType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to {typedArrayType.ElementType}Array.", tsCode: "TS2322");
                }
                return valueType;
            }

            // Buffer index assignment (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
            {
                // Buffer accepts number values
                if (!IsNumber(valueType) && valueType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to Buffer.", tsCode: "TS2322");
                }
                return valueType;
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (!IsCompatible(itf2.NumberIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to number index signature type '{itf2.NumberIndexType}'.", tsCode: "TS2322");
                return valueType;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (!IsCompatible(rec2.NumberIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to number index signature type '{rec2.NumberIndexType}'.", tsCode: "TS2322");
                return valueType;
            }
            var classNumberIndex = GetClassIndexType(objType, TokenType.TYPE_NUMBER)
                                   ?? GetClassIndexType(objType, TokenType.TYPE_STRING);
            if (classNumberIndex != null)
            {
                if (!IsCompatible(classNumberIndex, valueType))
                    throw new TypeCheckException(
                        $" Cannot assign '{valueType}' to number index signature type '{classNumberIndex}'.",
                        tsCode: "TS2322");
                return valueType;
            }

            // Allow numeric index assignment on records/interfaces/instances
            // without an explicit number-index signature. JS allows arbitrary
            // numeric-key assignment on plain objects (`var o = {}; o[0] = 1`).
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // JS treats built-in objects (Date, RegExp, Map, Set, Promise, Function,
        // Error) as ordinary objects — `obj[0] = 1` is legal at runtime. Mirror
        // TypeChecker.Properties' permissiveness for property-set on these types.
        if (objType is TypeInfo.Date or TypeInfo.RegExp or TypeInfo.Map or TypeInfo.Set
            or TypeInfo.WeakMap or TypeInfo.WeakSet or TypeInfo.Promise or TypeInfo.Function
            or TypeInfo.Error)
        {
            return valueType;
        }

        // Handle symbol index (Symbol and UniqueSymbol both qualify)
        if (indexType is TypeInfo.Symbol or TypeInfo.UniqueSymbol)
        {
            // A well-known symbol (`Symbol.iterator`, ...) that the object type models as a distinct
            // named member (canonical "@@name") is validated against THAT member's own type, not the
            // merged symbol index signature — mirrors the read path in CheckGetIndex (#99 Cluster B).
            if (TryGetWellKnownSymbolMemberName(setIndex.Index) is { } wellKnownName &&
                GetMemberType(objType, wellKnownName) is { } wellKnownType)
            {
                if (!IsCompatible(wellKnownType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to symbol index signature type '{wellKnownType}'.", tsCode: "TS2322");
                return valueType;
            }

            if (objType is TypeInfo.Interface itf3 && itf3.SymbolIndexType != null)
            {
                if (!IsCompatible(itf3.SymbolIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to symbol index signature type '{itf3.SymbolIndexType}'.", tsCode: "TS2322");
                return valueType;
            }
            if (objType is TypeInfo.Record rec3 && rec3.SymbolIndexType != null)
            {
                if (!IsCompatible(rec3.SymbolIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to symbol index signature type '{rec3.SymbolIndexType}'.", tsCode: "TS2322");
                return valueType;
            }

            // Allow symbol bracket assignment on any object
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        throw new TypeCheckException($" Index type '{indexType}' is not valid for assigning to '{objType}'.", tsCode: "TS7053");
    }

    /// <summary>
    /// Checks index access on a given type (used for delegating from TypeParameter constraints).
    /// Returns null if the index type is not valid for the object type.
    /// </summary>
    private TypeInfo? CheckGetIndexOnType(TypeInfo objType, TypeInfo indexType, Expr.GetIndex getIndex)
    {
        // Recursive case for nested type parameters
        if (objType is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return CheckGetIndexOnType(tp.Constraint, indexType, getIndex);
        }

        if (indexType is TypeInfo.Any)
            return TypeInfo.Any.Shared;

        // Handle string index
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return WithOptionalPropertyUndefined(fieldType, rec.IsFieldOptional(propName));
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return WithOptionalPropertyUndefined(memberType, itf.GetAllOptionalMembers().Contains(propName));
            }
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return WithUncheckedUndefined(rec2.StringIndexType);
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return WithUncheckedUndefined(itf2.StringIndexType);
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return TypeInfo.Any.Shared;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple indexing — mirrors the main CheckGetIndex path so that a union of
            // tuples (e.g. `["a", number] | ["c", string]`) distributes `m[0]` over each
            // constituent and unions the element types. A literal index yields the exact
            // element; a dynamic index yields the union of all element types.
            if (objType is TypeInfo.Tuple tupleType)
            {
                if (getIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return tupleType.ElementTypes[i];
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return tupleType.RestElementType;
                    // Out of bounds for this constituent — signal failure so the union
                    // path reports the index as invalid across the whole union.
                    return null;
                }
                return WithUncheckedUndefined(ComputeTupleElementUnion(tupleType));
            }
            if (objType is TypeInfo.Array arrayType)
                return WithUncheckedUndefined(arrayType.ElementType);
            // TypedArray index access returns number
            if (objType is TypeInfo.TypedArray)
                return WithUncheckedUndefined(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
            // Buffer index access returns number (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
                return WithUncheckedUndefined(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
            // String indexed by number yields a single-character string.
            if (objType is TypeInfo.String or TypeInfo.StringLiteral)
                return WithUncheckedUndefined(TypeInfo.String.Shared);
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return WithUncheckedUndefined(itf3.NumberIndexType);
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return WithUncheckedUndefined(rec3.NumberIndexType);
        }

        return null;
    }

    /// <summary>
    /// Checks index assignment on a given type (used for delegating from Union types).
    /// Returns the value type if assignment is valid, null when the index is structurally invalid
    /// for the type (the union caller turns null into TS7053). A write to a <b>readonly</b> array or
    /// tuple member throws TS2542 directly — mirroring the direct-write guard in
    /// <see cref="CheckSetIndex"/> — so a readonly member of a surviving union makes the whole
    /// index write TS2542 (#594).
    /// </summary>
    private TypeInfo? CheckSetIndexOnType(TypeInfo objType, TypeInfo indexType, TypeInfo valueType, Expr.SetIndex setIndex)
    {
        // Recursive case for nested type parameters
        if (objType is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return CheckSetIndexOnType(tp.Constraint, indexType, valueType, setIndex);
        }

        if (indexType is TypeInfo.Any)
            return valueType;

        // Handle string index
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (IsCompatible(itf.StringIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (IsCompatible(rec.StringIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple member (the main CheckSetIndex path handles tuples; this helper previously did
            // not, so a union with a tuple member rejected every numeric write). A readonly tuple
            // rejects the write (TS2542); otherwise the value must match the addressed element. (#594)
            if (objType is TypeInfo.Tuple tupleType)
            {
                if (tupleType.IsReadonly)
                    throw new TypeCheckException($" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");
                if (setIndex.Index is Expr.Literal { Value: double tIdx })
                {
                    int i = (int)tIdx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return IsCompatible(tupleType.ElementTypes[i], valueType) ? valueType : null;
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return IsCompatible(tupleType.RestElementType, valueType) ? valueType : null;
                    return null; // out of bounds for this constituent
                }
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null) allTypes.Add(tupleType.RestElementType);
                return allTypes.All(t => IsCompatible(t, valueType)) ? valueType : null;
            }
            if (objType is TypeInfo.Array arrayType)
            {
                // A readonly array member permits reading only — reject the write with TS2542,
                // mirroring the direct-write guard in CheckSetIndex (#594).
                if (arrayType.IsReadonly)
                    throw new TypeCheckException($" Index signature in type '{objType}' only permits reading.", tsCode: "TS2542");
                // Same out-of-range carve-out as CheckSetIndex above.
                if (!IsArrayIndexInRange(setIndex.Index))
                    return valueType;
                if (IsCompatible(arrayType.ElementType, valueType))
                    return valueType;
                return null;
            }
            // TypedArray index assignment
            if (objType is TypeInfo.TypedArray)
            {
                if (IsNumber(valueType) || valueType is TypeInfo.Any)
                    return valueType;
                return null;
            }
            // Buffer index assignment
            if (objType is TypeInfo.Buffer)
            {
                if (IsNumber(valueType) || valueType is TypeInfo.Any)
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (IsCompatible(itf2.NumberIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (IsCompatible(rec2.NumberIndexType, valueType))
                    return valueType;
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if the index expression is either non-literal (so the
    /// strict element-type check should still apply) or is a numeric literal
    /// that falls in the ECMA-262 array-index range [0, 2^32 - 2]. Numeric
    /// literals outside that range are spec-equivalent to ordinary property
    /// assignments and do not write into an array element slot.
    /// </summary>
    private static bool IsArrayIndexInRange(Expr indexExpr)
    {
        if (TryGetNumericLiteral(indexExpr) is not double n) return true;
        return n >= 0 && n < (double)uint.MaxValue && n == Math.Floor(n);
    }

    private static double? TryGetNumericLiteral(Expr e)
    {
        if (e is Expr.Literal { Value: double d }) return d;
        if (e is Expr.Unary u && u.Operator.Type == TokenType.MINUS
            && u.Right is Expr.Literal { Value: double d2 }) return -d2;
        return null;
    }
}
