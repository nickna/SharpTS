using SharpTS.TypeSystem.Exceptions;
using SharpTS.Runtime.BuiltIns;
using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type compatibility checking - structural and nominal typing.
/// </summary>
/// <remarks>
/// Contains compatibility logic:
/// IsCompatible, IsTupleCompatible, IsTupleToArrayCompatible, IsArrayToTupleCompatible,
/// AnalyzeTypeGuard, TypeMatchesTypeof, CheckStructuralCompatibility, GetMemberType,
/// IsNumber, IsString, IsBigInt, IsSubclassOf.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Checks type compatibility with memoization.
    /// Uses structural equality of TypeInfo records for cache key matching.
    /// </summary>
    private bool IsCompatible(TypeInfo expected, TypeInfo actual)
    {
        _compatibilityCache ??= new();
        var key = (expected, actual);

        if (_compatibilityCache.TryGetValue(key, out var cached))
            return cached;

        var result = IsCompatibleCore(expected, actual);
        _compatibilityCache[key] = result;
        return result;
    }

    /// <summary>
    /// Core type compatibility logic without caching.
    /// </summary>
    private bool IsCompatibleCore(TypeInfo expected, TypeInfo actual)
    {
        if (expected is TypeInfo.Any || actual is TypeInfo.Any) return true;

        // Type parameter compatibility: same name = compatible
        if (expected is TypeInfo.TypeParameter expectedTp && actual is TypeInfo.TypeParameter actualTp)
        {
            return expectedTp.Name == actualTp.Name;
        }

        // Type parameter as expected: actual satisfies if it matches the constraint
        if (expected is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
                return IsCompatible(tp.Constraint, actual);
            return true; // Unconstrained type parameter accepts anything
        }

        // Type parameter as actual: can be assigned to any, same type parameter, or a union containing the type parameter
        if (actual is TypeInfo.TypeParameter actualTpOnly)
        {
            if (expected is TypeInfo.Any) return true;
            // T is assignable to T | U (union containing T)
            if (expected is TypeInfo.Union expUnionForTp)
            {
                return expUnionForTp.FlattenedTypes.Any(t =>
                    t is TypeInfo.TypeParameter unionTp && unionTp.Name == actualTpOnly.Name);
            }
            return false;
        }

        // never as actual: assignable to anything (bottom type)
        if (actual is TypeInfo.Never) return true;

        // never as expected: nothing assignable to never except never
        if (expected is TypeInfo.Never) return actual is TypeInfo.Never;

        // unknown as expected: anything can be assigned TO unknown (top type)
        if (expected is TypeInfo.Unknown) return true;

        // unknown as actual: can only be assigned to unknown or any
        if (actual is TypeInfo.Unknown)
            return expected is TypeInfo.Unknown || expected is TypeInfo.Any;

        // object type: accepts non-primitive, non-null values
        if (expected is TypeInfo.Object)
        {
            if (actual is TypeInfo.Never) return true;  // never is bottom type
            if (actual is TypeInfo.Any) return true;    // any is assignable to anything
            if (actual is TypeInfo.Object) return true; // object to object
            if (IsPrimitiveType(actual)) return false;  // reject primitives
            if (actual is TypeInfo.Null or TypeInfo.Undefined) return false;
            // Accept: Record, Array, Instance, Class, Function, Map, Set, etc.
            return true;
        }

        // object as actual: can only assign to object, any, unknown
        if (actual is TypeInfo.Object)
        {
            return expected is TypeInfo.Object or TypeInfo.Any or TypeInfo.Unknown;
        }

        // Null compatibility
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) return true;
            if (expected is TypeInfo.Null) return true;
            return false;
        }

        // Undefined compatibility
        if (actual is TypeInfo.Undefined)
        {
            if (expected is TypeInfo.Union u && u.ContainsUndefined) return true;
            if (expected is TypeInfo.Undefined) return true;
            return false;
        }

        // Literal type compatibility - literal to literal (must have same value)
        if (expected is TypeInfo.StringLiteral sl1 && actual is TypeInfo.StringLiteral sl2)
            return sl1.Value == sl2.Value;
        if (expected is TypeInfo.NumberLiteral nl1 && actual is TypeInfo.NumberLiteral nl2)
            return nl1.Value == nl2.Value;
        if (expected is TypeInfo.BooleanLiteral bl1 && actual is TypeInfo.BooleanLiteral bl2)
            return bl1.Value == bl2.Value;

        // Literal to primitive widening
        if (expected is TypeInfo.String && actual is TypeInfo.StringLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            return true;

        // Template literal type compatibility

        // Template literal widens to string
        if (expected is TypeInfo.String && actual is TypeInfo.TemplateLiteralType)
            return true;

        // String literal matches template literal pattern
        if (expected is TypeInfo.TemplateLiteralType expectedTL && actual is TypeInfo.StringLiteral actualSL)
            return MatchesTemplateLiteralPattern(expectedTL, actualSL.Value);

        // Template literal to template literal: structural compatibility
        if (expected is TypeInfo.TemplateLiteralType expTL && actual is TypeInfo.TemplateLiteralType actTL)
            return TemplatePatternStructurallyCompatible(expTL, actTL);

        // Intrinsic string type: evaluate and check
        if (actual is TypeInfo.IntrinsicStringType ist)
        {
            var evaluated = EvaluateIntrinsicStringType(ist.Inner, ist.Operation);
            return IsCompatible(expected, evaluated);
        }

        // Union-to-union: each type in actual must be compatible with at least one type in expected
        if (expected is TypeInfo.Union expectedUnion && actual is TypeInfo.Union actualUnion)
        {
            var expectedTypes = expectedUnion.FlattenedTypes;
            var actualTypes = actualUnion.FlattenedTypes;
            return actualTypes.All(actualType =>
                expectedTypes.Any(expectedType => IsCompatible(expectedType, actualType)));
        }

        // Union as expected: actual must match at least one member
        if (expected is TypeInfo.Union expUnion)
        {
            var expTypes = expUnion.FlattenedTypes;
            return expTypes.Any(t => IsCompatible(t, actual));
        }

        // Union as actual: all members must be compatible with expected
        if (actual is TypeInfo.Union actUnion)
        {
            var actTypes = actUnion.FlattenedTypes;
            return actTypes.All(t => IsCompatible(expected, t));
        }

        // Intersection as expected: actual must satisfy ALL member types
        if (expected is TypeInfo.Intersection expIntersection)
        {
            var expTypes = expIntersection.FlattenedTypes;
            return expTypes.All(t => IsCompatible(t, actual));
        }

        // Intersection as actual: satisfies expected if any member does
        // (because intersection value has all the properties of all its constituents)
        if (actual is TypeInfo.Intersection actIntersection)
        {
            var actTypes = actIntersection.FlattenedTypes;
            return actTypes.Any(t => IsCompatible(expected, t));
        }

        // KeyOf type compatibility - must evaluate to compare
        // Special handling for keyof T where T is a type parameter - don't try to expand
        if (expected is TypeInfo.KeyOf expectedKeyOf)
        {
            // If source is a type parameter, don't expand to avoid infinite recursion
            if (expectedKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                // keyof T is compatible with string, number, symbol, or any
                return actual is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.TypeParameter;
            }
            TypeInfo expandedExpected = EvaluateKeyOf(expectedKeyOf.SourceType);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.KeyOf actualKeyOf)
        {
            // If source is a type parameter, don't expand to avoid infinite recursion
            if (actualKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                // keyof T is compatible with string, number, symbol, or any
                return expected is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.KeyOf;
            }
            TypeInfo expandedActual = EvaluateKeyOf(actualKeyOf.SourceType);
            return IsCompatible(expected, expandedActual);
        }

        // Mapped type compatibility - expand lazily then compare
        if (expected is TypeInfo.MappedType expectedMapped)
        {
            TypeInfo expandedExpected = ExpandMappedType(expectedMapped);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.MappedType actualMapped)
        {
            TypeInfo expandedActual = ExpandMappedType(actualMapped);
            return IsCompatible(expected, expandedActual);
        }

        // Indexed access type compatibility - resolve then compare
        if (expected is TypeInfo.IndexedAccess expectedIA)
        {
            TypeInfo expandedExpected = ResolveIndexedAccess(expectedIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.IndexedAccess actualIA)
        {
            TypeInfo expandedActual = ResolveIndexedAccess(actualIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expected, expandedActual);
        }

        // Conditional type compatibility - evaluate then compare
        if (expected is TypeInfo.ConditionalType expectedCond)
        {
            TypeInfo expandedExpected = EvaluateConditionalType(expectedCond);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.ConditionalType actualCond)
        {
            TypeInfo expandedActual = EvaluateConditionalType(actualCond);
            return IsCompatible(expected, expandedActual);
        }

        // InferredTypeParameter should not appear in compatibility checks
        // (they should be resolved during conditional type evaluation)
        if (expected is TypeInfo.InferredTypeParameter || actual is TypeInfo.InferredTypeParameter)
        {
            return false; // Unresolved infer parameters are not compatible with anything
        }

        // Enum compatibility: primitive values are assignable to their enum type
        // (e.g., Direction.Up which is typed as 'number' can be assigned to Direction)
        if (expected is TypeInfo.Enum expectedEnum)
        {
            // Same enum type is compatible
            if (actual is TypeInfo.Enum actualEnum && expectedEnum.Name == actualEnum.Name)
                return true;

            // Numeric enum accepts number
            if (expectedEnum.Kind == EnumKind.Numeric &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            // String enum accepts string
            if (expectedEnum.Kind == EnumKind.String && actual is TypeInfo.String)
                return true;

            // Heterogeneous enum accepts both
            if (expectedEnum.Kind == EnumKind.Heterogeneous &&
                (actual is TypeInfo.String || actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;

            return false;
        }

        // Enum as actual: can be assigned to compatible primitive type
        // (e.g., a Direction variable can be used where a number is expected)
        if (actual is TypeInfo.Enum actualEnumType)
        {
            if (actualEnumType.Kind == EnumKind.Numeric &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            if (actualEnumType.Kind == EnumKind.String && expected is TypeInfo.String)
                return true;

            if (actualEnumType.Kind == EnumKind.Heterogeneous &&
                (expected is TypeInfo.String || expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;
        }

        if (expected is TypeInfo.Primitive p1 && actual is TypeInfo.Primitive p2)
        {
            return p1.Type == p2.Type;
        }

        // String type compatibility
        if (expected is TypeInfo.String && actual is TypeInfo.String)
        {
            return true;
        }

        // Symbol type compatibility
        if (expected is TypeInfo.Symbol && actual is TypeInfo.Symbol)
        {
            return true;
        }

        // BigInt type compatibility
        if (expected is TypeInfo.BigInt && actual is TypeInfo.BigInt)
        {
            return true;
        }

        // Promise type compatibility - Promise<A> is compatible with Promise<B> if A is compatible with B
        if (expected is TypeInfo.Promise expPromise && actual is TypeInfo.Promise actPromise)
        {
            return IsCompatible(expPromise.ValueType, actPromise.ValueType);
        }

        // Map type compatibility - Map<K1, V1> is compatible with Map<K2, V2> if K1=K2 and V1=V2
        if (expected is TypeInfo.Map expMap && actual is TypeInfo.Map actMap)
        {
            return IsCompatible(expMap.KeyType, actMap.KeyType) &&
                   IsCompatible(expMap.ValueType, actMap.ValueType);
        }

        // Set type compatibility - Set<T1> is compatible with Set<T2> if T1=T2
        if (expected is TypeInfo.Set expSet && actual is TypeInfo.Set actSet)
        {
            return IsCompatible(expSet.ElementType, actSet.ElementType);
        }

        // WeakMap type compatibility - WeakMap<K1, V1> is compatible with WeakMap<K2, V2> if K1=K2 and V1=V2
        if (expected is TypeInfo.WeakMap expWeakMap && actual is TypeInfo.WeakMap actWeakMap)
        {
            return IsCompatible(expWeakMap.KeyType, actWeakMap.KeyType) &&
                   IsCompatible(expWeakMap.ValueType, actWeakMap.ValueType);
        }

        // WeakSet type compatibility - WeakSet<T1> is compatible with WeakSet<T2> if T1=T2
        if (expected is TypeInfo.WeakSet expWeakSet && actual is TypeInfo.WeakSet actWeakSet)
        {
            return IsCompatible(expWeakSet.ElementType, actWeakSet.ElementType);
        }

        if (expected is TypeInfo.Instance i1 && actual is TypeInfo.Instance i2)
        {
            // Handle InstantiatedGeneric expected type - check if actual's class hierarchy includes it
            if (i1.ClassType is TypeInfo.InstantiatedGeneric expectedIG)
            {
                // Check if actual is also the same InstantiatedGeneric
                if (i2.ClassType is TypeInfo.InstantiatedGeneric actualIG)
                {
                    // Same generic definition and compatible type arguments
                    if (expectedIG.GenericDefinition is TypeInfo.GenericClass gc1 &&
                        actualIG.GenericDefinition is TypeInfo.GenericClass gc2 &&
                        gc1.Name == gc2.Name)
                    {
                        if (expectedIG.TypeArguments.Count != actualIG.TypeArguments.Count)
                            return false;
                        for (int i = 0; i < expectedIG.TypeArguments.Count; i++)
                        {
                            if (!IsCompatible(expectedIG.TypeArguments[i], actualIG.TypeArguments[i]))
                                return false;
                        }
                        return true;
                    }
                    // Check if actualIG's hierarchy includes expectedIG
                    return IsInSuperclassChain(actualIG, expectedIG);
                }

                // Check if actual is a regular Class that extends the expected InstantiatedGeneric
                // e.g., NumberBox extends Box<number>, checking if NumberBox assignable to Box<number>
                if (i2.ClassType is TypeInfo.Class actualClassForIG)
                {
                    return IsInSuperclassChain(actualClassForIG, expectedIG);
                }
            }

            // Handle regular Class comparison (including MutableClass resolution)
            // Use ResolvedClassType to handle MutableClass instances that may occur during signature collection
            var resolvedExpected = i1.ResolvedClassType;
            var resolvedActual = i2.ResolvedClassType;

            if (resolvedExpected is TypeInfo.Class expectedClass && resolvedActual is TypeInfo.Class actualClass)
            {
                // Check direct class hierarchy (by name)
                TypeInfo? current = actualClass;
                while (current != null)
                {
                    if (current is TypeInfo.Class cls && cls.Name == expectedClass.Name) return true;
                    current = GetSuperclass(current);
                }
            }
            // Handle MutableClass (unfrozen) comparison by name - occurs during signature collection
            else if (resolvedExpected is TypeInfo.MutableClass mc1 && resolvedActual is TypeInfo.MutableClass mc2)
            {
                return mc1.Name == mc2.Name;
            }

            // Mixed case: InstantiatedGeneric vs regular Class - not compatible unless in hierarchy
            return false;
        }

        if (expected is TypeInfo.Interface itf)
        {
            // If actual is also an interface, compare member-to-member structurally
            if (actual is TypeInfo.Interface actualItf)
            {
                // Check that actual has all required members with compatible types (including inherited)
                var allExpectedMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
                var allExpectedOptional = itf.GetAllOptionalMembers().ToHashSet();
                var allActualMembers = actualItf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);

                foreach (var member in allExpectedMembers)
                {
                    if (!allActualMembers.TryGetValue(member.Key, out var actualMemberType))
                    {
                        // Member missing - check if optional
                        if (!allExpectedOptional.Contains(member.Key))
                            return false;
                    }
                    else if (!IsCompatible(member.Value, actualMemberType))
                    {
                        return false;
                    }
                }
                return true;
            }
            // Use GetAllMembers to include inherited members when checking structural compatibility
            var allMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
            var allOptional = itf.GetAllOptionalMembers().ToHashSet();
            return CheckStructuralCompatibility(allMembers, actual, allOptional);
        }

        // Handle InstantiatedGeneric interface (e.g., Container<number>)
        if (expected is TypeInfo.InstantiatedGeneric expectedInterfaceIG &&
            expectedInterfaceIG.GenericDefinition is TypeInfo.GenericInterface gi)
        {
            // Build substitution map
            Dictionary<string, TypeInfo> subs = [];
            for (int i = 0; i < gi.TypeParams.Count; i++)
                subs[gi.TypeParams[i].Name] = expectedInterfaceIG.TypeArguments[i];

            // Substitute type parameters in interface members
            Dictionary<string, TypeInfo> substitutedMembers = [];
            foreach (var kvp in gi.Members)
                substitutedMembers[kvp.Key] = Substitute(kvp.Value, subs);

            return CheckStructuralCompatibility(substitutedMembers, actual, gi.OptionalMembers);
        }

        if (expected is TypeInfo.Array a1 && actual is TypeInfo.Array a2)
        {
            return IsCompatible(a1.ElementType, a2.ElementType);
        }

        // Record-to-Record compatibility (inline object types)
        if (expected is TypeInfo.Record expRecord && actual is TypeInfo.Record actRecord)
        {
            // All required fields in expected must exist in actual with compatible types
            // Optional fields can be omitted
            foreach (var (name, expectedFieldType) in expRecord.Fields)
            {
                if (!actRecord.Fields.TryGetValue(name, out var actualFieldType))
                {
                    // Field missing - only OK if the field is optional
                    if (!expRecord.IsFieldOptional(name))
                        return false;
                }
                else if (!IsCompatible(expectedFieldType, actualFieldType))
                {
                    return false;
                }
            }
            // If expected has only index signatures (no explicit fields), empty object is compatible
            // Index signatures allow any number of keys (including zero)
            return true;
        }

        // Record constraint compatibility with types that have members (String, Array, etc.)
        // This handles cases like `T extends { length: number }` with strings or arrays
        if (expected is TypeInfo.Record expRec)
        {
            // Use CheckStructuralCompatibility to check if actual type has all required fields
            return CheckStructuralCompatibility(expRec.Fields, actual, expRec.OptionalFields);
        }

        // Tuple-to-tuple compatibility
        if (expected is TypeInfo.Tuple expTuple && actual is TypeInfo.Tuple actTuple)
        {
            return IsTupleCompatible(expTuple, actTuple);
        }

        // Tuple assignable to array (e.g., [string, number] -> (string | number)[])
        if (expected is TypeInfo.Array expArr && actual is TypeInfo.Tuple actTuple2)
        {
            return IsTupleToArrayCompatible(expArr, actTuple2);
        }

        // Array assignable to tuple (limited - only for rest tuples or all-optional)
        if (expected is TypeInfo.Tuple expTuple2 && actual is TypeInfo.Array actArr)
        {
            return IsArrayToTupleCompatible(expTuple2, actArr);
        }

        if (expected is TypeInfo.Void && actual is TypeInfo.Void) return true;

        // OverloadedFunction expected: actual function must satisfy all overload signatures
        // This handles cases like interface method overloads being satisfied by a union-parameter function
        if (expected is TypeInfo.OverloadedFunction overloadedFunc && actual is TypeInfo.Function actualFunc)
        {
            // The actual function must satisfy ALL overload signatures
            foreach (var signature in overloadedFunc.Signatures)
            {
                if (!IsFunctionCompatibleWithSignature(actualFunc, signature))
                    return false;
            }
            return true;
        }

        // Function type compatibility
        if (expected is TypeInfo.Function f1 && actual is TypeInfo.Function f2)
        {
            // For callbacks, actual can have fewer params than expected (unused params)
            if (f2.ParamTypes.Count > f1.ParamTypes.Count) return false;
            for (int i = 0; i < f2.ParamTypes.Count; i++)
            {
                if (!IsCompatible(f1.ParamTypes[i], f2.ParamTypes[i])) return false;
            }
            // Return type: if expected return type is void, any return type is acceptable
            // This is standard TypeScript behavior - void context ignores the return value
            if (f1.ReturnType is TypeInfo.Void) return true;
            // Otherwise, actual must be compatible with expected
            return IsCompatible(f1.ReturnType, f2.ReturnType);
        }

        return false;
    }

    /// <summary>
    /// Checks if an actual function can satisfy a specific signature from an overloaded function.
    /// Used for structural typing when an interface has overloaded methods.
    /// The actual function with union parameters can satisfy multiple specific signatures.
    /// </summary>
    private bool IsFunctionCompatibleWithSignature(TypeInfo.Function actualFunc, TypeInfo.Function signature)
    {
        // The actual function must have at least as many parameters as the signature requires
        // But it can have more parameters (they'd be optional or union parameters)
        if (actualFunc.ParamTypes.Count < signature.MinArity)
            return false;

        // For each parameter position in the signature, the signature's param type
        // must be assignable to the actual param type (contravariance)
        // This ensures the actual function can accept calls matching the signature
        for (int i = 0; i < signature.ParamTypes.Count && i < actualFunc.ParamTypes.Count; i++)
        {
            // Signature param must be assignable to actual param (contravariance for function params)
            // If signature expects (number), actual can accept (number | string)
            if (!IsCompatible(actualFunc.ParamTypes[i], signature.ParamTypes[i]))
                return false;
        }

        // Return type: actual must be assignable to signature (covariance)
        if (signature.ReturnType is not TypeInfo.Void)
        {
            if (!IsCompatible(signature.ReturnType, actualFunc.ReturnType))
                return false;
        }

        return true;
    }

    private bool IsTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        // Actual must have at least the required elements
        if (actual.ElementTypes.Count < expected.RequiredCount) return false;

        // If expected has fixed length (no rest), actual cannot be longer
        if (expected.MaxLength != null && actual.ElementTypes.Count > expected.MaxLength) return false;

        // Check element type compatibility for overlapping positions
        int minLen = Math.Min(expected.ElementTypes.Count, actual.ElementTypes.Count);
        for (int i = 0; i < minLen; i++)
        {
            if (!IsCompatible(expected.ElementTypes[i], actual.ElementTypes[i]))
                return false;
        }

        // If expected has rest, check remaining actual elements against rest type
        if (expected.RestElementType != null)
        {
            for (int i = expected.ElementTypes.Count; i < actual.ElementTypes.Count; i++)
            {
                if (!IsCompatible(expected.RestElementType, actual.ElementTypes[i]))
                    return false;
            }
            // If actual also has rest, check rest compatibility
            if (actual.RestElementType != null &&
                !IsCompatible(expected.RestElementType, actual.RestElementType))
                return false;
        }

        return true;
    }

    private bool IsTupleToArrayCompatible(TypeInfo.Array expected, TypeInfo.Tuple actual)
    {
        // All tuple element types must be compatible with array element type
        foreach (var elemType in actual.ElementTypes)
        {
            if (!IsCompatible(expected.ElementType, elemType))
                return false;
        }
        if (actual.RestElementType != null &&
            !IsCompatible(expected.ElementType, actual.RestElementType))
            return false;
        return true;
    }

    private bool IsArrayToTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Array actual)
    {
        // Array can match tuple only if tuple has rest or is all-optional
        if (expected.RequiredCount > 0 && expected.RestElementType == null)
            return false;

        // All expected element types must be compatible with actual's element type
        foreach (var elemType in expected.ElementTypes)
        {
            if (!IsCompatible(elemType, actual.ElementType))
                return false;
        }
        if (expected.RestElementType != null &&
            !IsCompatible(expected.RestElementType, actual.ElementType))
            return false;
        return true;
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeGuard(Expr condition)
    {
        // Pattern 1: typeof x === "string" or typeof x == "string"
        if (condition is Expr.Binary bin &&
            bin.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v } &&
            bin.Right is Expr.Literal { Value: string typeStr })
        {
            return AnalyzeTypeofGuard(v.Name.Lexeme, typeStr, negated: false);
        }

        // Pattern 1b: "string" === typeof x (reversed operands)
        if (condition is Expr.Binary bin1b &&
            bin1b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin1b.Right is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v1b } &&
            bin1b.Left is Expr.Literal { Value: string typeStr1b })
        {
            return AnalyzeTypeofGuard(v1b.Name.Lexeme, typeStr1b, negated: false);
        }

        // Pattern 2: typeof x !== "string" or typeof x != "string"
        if (condition is Expr.Binary bin2 &&
            bin2.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin2.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v2 } &&
            bin2.Right is Expr.Literal { Value: string typeStr2 })
        {
            return AnalyzeTypeofGuard(v2.Name.Lexeme, typeStr2, negated: true);
        }

        // Pattern 3: x instanceof ClassName
        if (condition is Expr.Binary bin3 &&
            bin3.Operator.Type == TokenType.INSTANCEOF &&
            bin3.Left is Expr.Variable v3 &&
            bin3.Right is Expr.Variable classVar)
        {
            return AnalyzeInstanceofGuard(v3.Name.Lexeme, classVar.Name);
        }

        // Pattern 4: x === null or x == null
        if (condition is Expr.Binary bin4 &&
            bin4.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin4.Left is Expr.Variable v4 &&
            bin4.Right is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v4.Name.Lexeme, checkingForNull: true, negated: false);
        }

        // Pattern 4b: null === x (reversed)
        if (condition is Expr.Binary bin4b &&
            bin4b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin4b.Right is Expr.Variable v4b &&
            bin4b.Left is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v4b.Name.Lexeme, checkingForNull: true, negated: false);
        }

        // Pattern 5: x !== null or x != null
        if (condition is Expr.Binary bin5 &&
            bin5.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin5.Left is Expr.Variable v5 &&
            bin5.Right is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v5.Name.Lexeme, checkingForNull: true, negated: true);
        }

        // Pattern 6: x === undefined (undefined is a literal, not a variable)
        if (condition is Expr.Binary bin6 &&
            bin6.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin6.Left is Expr.Variable v6 &&
            bin6.Right is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v6.Name.Lexeme, checkingForNull: false, negated: false);
        }

        // Pattern 6b: undefined === x (reversed)
        if (condition is Expr.Binary bin6b &&
            bin6b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin6b.Right is Expr.Variable v6b &&
            bin6b.Left is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v6b.Name.Lexeme, checkingForNull: false, negated: false);
        }

        // Pattern 7: x !== undefined
        if (condition is Expr.Binary bin7 &&
            bin7.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin7.Left is Expr.Variable v7 &&
            bin7.Right is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v7.Name.Lexeme, checkingForNull: false, negated: true);
        }

        // Pattern 8: "prop" in x (in operator narrowing)
        if (condition is Expr.Binary bin8 &&
            bin8.Operator.Type == TokenType.IN &&
            bin8.Left is Expr.Literal { Value: string propName } &&
            bin8.Right is Expr.Variable v8)
        {
            return AnalyzeInOperatorGuard(v8.Name.Lexeme, propName);
        }

        // Pattern 9: x.kind === "literal" (discriminated union narrowing)
        if (condition is Expr.Binary bin9 &&
            bin9.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin9.Left is Expr.Get { Object: Expr.Variable v9, Name: var propToken } &&
            bin9.Right is Expr.Literal { Value: string literalValue })
        {
            return AnalyzeDiscriminatedUnionGuard(v9.Name.Lexeme, propToken.Lexeme, literalValue);
        }

        // Pattern 9b: "literal" === x.kind (reversed)
        if (condition is Expr.Binary bin9b &&
            bin9b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin9b.Right is Expr.Get { Object: Expr.Variable v9b, Name: var propToken9b } &&
            bin9b.Left is Expr.Literal { Value: string literalValue9b })
        {
            return AnalyzeDiscriminatedUnionGuard(v9b.Name.Lexeme, propToken9b.Lexeme, literalValue9b);
        }

        // Pattern 10: Array.isArray(x)
        if (condition is Expr.Call call &&
            call.Callee is Expr.Get { Object: Expr.Variable { Name.Lexeme: "Array" }, Name.Lexeme: "isArray" } &&
            call.Arguments.Count == 1 &&
            call.Arguments[0] is Expr.Variable v10)
        {
            return AnalyzeArrayIsArrayGuard(v10.Name.Lexeme);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Analyzes typeof type guards like `typeof x === "string"`.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeofGuard(
        string varName, string typeStr, bool negated)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        // Handle unknown type narrowing - typeof checks narrow unknown to specific types
        if (currentType is TypeInfo.Unknown)
        {
            TypeInfo? narrowedType = TypeofStringToType(typeStr);
            // Excluded type remains unknown (we don't know what else it could be)
            if (negated)
                return (varName, new TypeInfo.Unknown(), narrowedType);
            return (varName, narrowedType, new TypeInfo.Unknown());
        }

        // Handle any type narrowing
        if (currentType is TypeInfo.Any)
        {
            TypeInfo? narrowedType = TypeofStringToType(typeStr);
            if (negated)
                return (varName, new TypeInfo.Any(), narrowedType);
            return (varName, narrowedType, new TypeInfo.Any());
        }

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = flattenedTypes.Where(t => TypeMatchesTypeof(t, typeStr)).ToList();
            var nonMatching = flattenedTypes.Where(t => !TypeMatchesTypeof(t, typeStr)).ToList();

            TypeInfo? narrowedType = matching.Count == 0 ? null :
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            if (negated)
                return (varName, excludedType, narrowedType);
            return (varName, narrowedType, excludedType);
        }

        // Non-union type: check if current type matches typeof
        if (TypeMatchesTypeof(currentType, typeStr))
        {
            // Current type matches - narrowed stays same, excluded is never
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }
        else
        {
            // Current type doesn't match - narrowed is never, excluded stays same
            if (negated)
                return (varName, currentType, new TypeInfo.Never());
            return (varName, new TypeInfo.Never(), currentType);
        }
    }

    /// <summary>
    /// Converts a typeof result string to the corresponding TypeInfo.
    /// </summary>
    private static TypeInfo? TypeofStringToType(string typeStr) => typeStr switch
    {
        "string" => new TypeInfo.String(),
        "number" => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
        "boolean" => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
        "bigint" => new TypeInfo.BigInt(),
        "function" => new TypeInfo.Function([], new TypeInfo.Any(), 0, false),
        "object" => new TypeInfo.Object(),
        "symbol" => new TypeInfo.Symbol(),
        "undefined" => new TypeInfo.Undefined(),
        _ => null
    };

    /// <summary>
    /// Analyzes instanceof type guards like `x instanceof Dog`.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeInstanceofGuard(
        string varName, Token classToken)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        // Look up the class
        var classType = _environment.Get(classToken.Lexeme);
        if (classType == null) return (null, null, null);

        // Determine the instance type we're narrowing to
        TypeInfo instanceType;
        if (classType is TypeInfo.Class cls)
        {
            instanceType = new TypeInfo.Instance(cls);
        }
        else if (classType is TypeInfo.MutableClass mc)
        {
            instanceType = new TypeInfo.Instance(mc.Frozen ?? (TypeInfo)mc);
        }
        else if (classType is TypeInfo.Instance inst)
        {
            instanceType = inst;
        }
        else
        {
            // Not a class - can't narrow
            return (null, null, null);
        }

        // If current type is a union containing the class or its subclasses, narrow
        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = new List<TypeInfo>();
            var nonMatching = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                if (IsInstanceOf(t, classType))
                    matching.Add(t);
                else
                    nonMatching.Add(t);
            }

            TypeInfo? narrowedType = matching.Count == 0 ? instanceType : // Narrow to the class if no match
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            return (varName, narrowedType, excludedType);
        }

        // For non-union types (e.g., base class), narrow to the specific class
        if (currentType is TypeInfo.Instance currentInst)
        {
            // If checking instanceof a subclass, narrow to that subclass
            return (varName, instanceType, currentType);
        }

        // For any/unknown types, narrow to the instance type
        if (currentType is TypeInfo.Any or TypeInfo.Unknown)
        {
            return (varName, instanceType, currentType);
        }

        return (varName, instanceType, currentType);
    }

    /// <summary>
    /// Checks if a type is an instance of a class or its subclass.
    /// </summary>
    private bool IsInstanceOf(TypeInfo type, TypeInfo classType)
    {
        if (type is not TypeInfo.Instance inst) return false;

        TypeInfo.Class? targetClass = classType switch
        {
            TypeInfo.Class c => c,
            TypeInfo.MutableClass mc => mc.Frozen,
            TypeInfo.Instance i when i.ClassType is TypeInfo.Class ic => ic,
            _ => null
        };

        if (targetClass == null) return false;

        TypeInfo? current = inst.ClassType switch
        {
            TypeInfo.Class c => c,
            TypeInfo.MutableClass mc => mc.Frozen,
            _ => null
        };

        while (current != null)
        {
            if (GetClassName(current) == targetClass.Name) return true;
            current = GetSuperclass(current);
        }

        return false;
    }

    /// <summary>
    /// Analyzes null/undefined equality checks like `x === null` or `x !== undefined`.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeNullCheck(
        string varName, bool checkingForNull, bool negated)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        TypeInfo nullishType = checkingForNull ? new TypeInfo.Null() : new TypeInfo.Undefined();

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var nonNullish = flattenedTypes.Where(t =>
                checkingForNull ? t is not TypeInfo.Null : t is not TypeInfo.Undefined).ToList();
            var nullish = flattenedTypes.Where(t =>
                checkingForNull ? t is TypeInfo.Null : t is TypeInfo.Undefined).ToList();

            TypeInfo? nonNullishType = nonNullish.Count == 0 ? new TypeInfo.Never() :
                nonNullish.Count == 1 ? nonNullish[0] : new TypeInfo.Union(nonNullish);
            TypeInfo? nullishResultType = nullish.Count == 0 ? nullishType :
                nullish.Count == 1 ? nullish[0] : new TypeInfo.Union(nullish);

            // if (x === null) -> then: null, else: non-null
            // if (x !== null) -> then: non-null, else: null
            if (negated)
                return (varName, nonNullishType, nullishResultType);
            return (varName, nullishResultType, nonNullishType);
        }

        // Non-union type: if it's nullable, we can still narrow
        if (currentType is TypeInfo.Null && checkingForNull)
        {
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }
        if (currentType is TypeInfo.Undefined && !checkingForNull)
        {
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }

        // Type is not nullable - narrowing has no effect
        return (null, null, null);
    }

    /// <summary>
    /// Analyzes 'in' operator type guards like `"prop" in x`.
    /// Narrows union types to only those members that have the specified property.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeInOperatorGuard(
        string varName, string propertyName)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var hasProperty = new List<TypeInfo>();
            var lacksProperty = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                if (TypeHasProperty(t, propertyName))
                    hasProperty.Add(t);
                else
                    lacksProperty.Add(t);
            }

            TypeInfo? narrowedType = hasProperty.Count == 0 ? null :
                hasProperty.Count == 1 ? hasProperty[0] : new TypeInfo.Union(hasProperty);
            TypeInfo? excludedType = lacksProperty.Count == 0 ? null :
                lacksProperty.Count == 1 ? lacksProperty[0] : new TypeInfo.Union(lacksProperty);

            return (varName, narrowedType, excludedType);
        }

        // Non-union type: check if it has the property
        if (TypeHasProperty(currentType, propertyName))
        {
            return (varName, currentType, new TypeInfo.Never());
        }

        return (null, null, null);
    }

    /// <summary>
    /// Checks if a type has a specific property.
    /// </summary>
    private bool TypeHasProperty(TypeInfo type, string propertyName)
    {
        return type switch
        {
            TypeInfo.Interface iface => iface.Members.ContainsKey(propertyName),
            TypeInfo.Record record => record.Fields.ContainsKey(propertyName),
            TypeInfo.Class cls => cls.FieldTypes.ContainsKey(propertyName) ||
                                  cls.Methods.ContainsKey(propertyName) ||
                                  cls.Getters.ContainsKey(propertyName),
            TypeInfo.Instance inst => TypeHasProperty(inst.ResolvedClassType, propertyName),
            _ => false
        };
    }

    /// <summary>
    /// Analyzes discriminated union type guards like `x.kind === "circle"`.
    /// Narrows union types to only those members where the discriminant property matches.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeDiscriminatedUnionGuard(
        string varName, string discriminantProp, string literalValue)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = new List<TypeInfo>();
            var nonMatching = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                var propType = GetDiscriminantPropertyType(t, discriminantProp);
                if (propType != null && DiscriminantMatches(propType, literalValue))
                    matching.Add(t);
                else
                    nonMatching.Add(t);
            }

            TypeInfo? narrowedType = matching.Count == 0 ? null :
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            return (varName, narrowedType, excludedType);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Gets the type of a discriminant property from a type.
    /// </summary>
    private static TypeInfo? GetDiscriminantPropertyType(TypeInfo type, string propertyName)
    {
        return type switch
        {
            TypeInfo.Interface iface => iface.Members.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Record record => record.Fields.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Class cls => cls.FieldTypes.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Instance inst => GetDiscriminantPropertyType(inst.ResolvedClassType, propertyName),
            _ => null
        };
    }

    /// <summary>
    /// Checks if a discriminant property type matches a literal value.
    /// </summary>
    private static bool DiscriminantMatches(TypeInfo propType, string literalValue)
    {
        return propType switch
        {
            TypeInfo.StringLiteral sl => sl.Value == literalValue,
            TypeInfo.String => true, // String type could match any literal (less precise)
            _ => false
        };
    }

    /// <summary>
    /// Analyzes Array.isArray type guards.
    /// Narrows union types to array types vs non-array types.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeArrayIsArrayGuard(
        string varName)
    {
        var currentType = _environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var arrays = flattenedTypes.Where(t => t is TypeInfo.Array or TypeInfo.Tuple).ToList();
            var nonArrays = flattenedTypes.Where(t => t is not TypeInfo.Array and not TypeInfo.Tuple).ToList();

            TypeInfo? narrowedType = arrays.Count == 0 ? null :
                arrays.Count == 1 ? arrays[0] : new TypeInfo.Union(arrays);
            TypeInfo? excludedType = nonArrays.Count == 0 ? null :
                nonArrays.Count == 1 ? nonArrays[0] : new TypeInfo.Union(nonArrays);

            return (varName, narrowedType, excludedType);
        }

        // Non-union type
        if (currentType is TypeInfo.Array or TypeInfo.Tuple)
        {
            return (varName, currentType, new TypeInfo.Never());
        }

        // For any/unknown, narrow to array
        if (currentType is TypeInfo.Any or TypeInfo.Unknown)
        {
            return (varName, new TypeInfo.Array(new TypeInfo.Any()), currentType);
        }

        return (null, null, null);
    }

    private bool TypeMatchesTypeof(TypeInfo type, string typeofResult) => typeofResult switch
    {
        "string" => type is TypeInfo.String or TypeInfo.StringLiteral,
        "number" => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral,
        "boolean" => type is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral,
        "bigint" => type is TypeInfo.BigInt,
        "object" => type is TypeInfo.Null or TypeInfo.Record or TypeInfo.Array or TypeInfo.Instance or TypeInfo.Object,
        "function" => type is TypeInfo.Function,
        _ => false
    };

    private bool CheckStructuralCompatibility(IReadOnlyDictionary<string, TypeInfo> requiredMembers, TypeInfo actual, IReadOnlySet<string>? optionalMembers = null)
    {
        foreach (var member in requiredMembers)
        {
            TypeInfo? actualMemberType = GetMemberType(actual, member.Key);

            // If member is optional and not present, that's OK
            if (actualMemberType == null && (optionalMembers?.Contains(member.Key) ?? false))
            {
                continue;
            }

            if (actualMemberType == null || !IsCompatible(member.Value, actualMemberType))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks for excess properties in fresh object literals assigned to typed variables.
    /// TypeScript performs this check to catch typos and enforce exact object shapes.
    /// </summary>
    /// <param name="actual">The actual object record type from the literal</param>
    /// <param name="expected">The expected type from the variable declaration</param>
    /// <param name="sourceExpr">The source expression for error context</param>
    private void CheckExcessProperties(TypeInfo.Record actual, TypeInfo expected, Expr sourceExpr)
    {
        // Get all valid property names and check for index signatures
        HashSet<string> expectedKeys = [];
        bool hasStringIndex = false;
        bool hasNumberIndex = false;

        if (expected is TypeInfo.Record expRecord)
        {
            foreach (var key in expRecord.Fields.Keys)
            {
                expectedKeys.Add(key);
            }
            // Check for index signatures
            hasStringIndex = expRecord.StringIndexType != null;
            hasNumberIndex = expRecord.NumberIndexType != null;
        }
        else if (expected is TypeInfo.Interface iface)
        {
            // Include inherited members from extended interfaces
            foreach (var member in iface.GetAllMembers())
            {
                expectedKeys.Add(member.Key);
            }
            // Check for index signatures in interfaces
            hasStringIndex = iface.StringIndexType != null;
            hasNumberIndex = iface.NumberIndexType != null;
        }
        else if (expected is TypeInfo.Class cls)
        {
            foreach (var field in cls.FieldTypes)
            {
                expectedKeys.Add(field.Key);
            }
            foreach (var method in cls.Methods)
            {
                expectedKeys.Add(method.Key);
            }
            foreach (var getter in cls.Getters)
            {
                expectedKeys.Add(getter.Key);
            }
            foreach (var setter in cls.Setters)
            {
                expectedKeys.Add(setter.Key);
            }
        }
        else
        {
            // For other types (primitives, unions, etc.), skip excess property check
            return;
        }

        // If type has index signatures, all properties are valid
        if (hasStringIndex || hasNumberIndex)
        {
            return;
        }

        // Find properties in actual that are not in expected
        List<string> excessKeys = [];
        foreach (var actualKey in actual.Fields.Keys)
        {
            if (!expectedKeys.Contains(actualKey))
            {
                excessKeys.Add(actualKey);
            }
        }

        // Throw error if excess properties found
        if (excessKeys.Count > 0)
        {
            string excessList = string.Join(", ", excessKeys.Select(k => $"'{k}'"));
            throw new TypeCheckException(
                $"Object literal may only specify known properties. " +
                $"Excess {(excessKeys.Count == 1 ? "property" : "properties")}: {excessList}"
            );
        }
    }

    private TypeInfo? GetMemberType(TypeInfo type, string name)
    {
        if (type is TypeInfo.Record record)
        {
            return record.Fields.TryGetValue(name, out var t) ? t : null;
        }

        // Handle String type - has length property and string methods
        if (type is TypeInfo.String or TypeInfo.StringLiteral)
        {
            return BuiltInTypes.GetStringMemberType(name);
        }

        // Handle Array type - has length property and array methods
        if (type is TypeInfo.Array arr)
        {
            return BuiltInTypes.GetArrayMemberType(name, arr.ElementType);
        }

        // Handle Tuple type - treat as array for member access
        if (type is TypeInfo.Tuple tuple)
        {
            var allTypes = tuple.ElementTypes.ToList();
            if (tuple.RestElementType != null)
                allTypes.Add(tuple.RestElementType);
            var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            TypeInfo unionElem = unique.Count == 0
                ? new TypeInfo.Any()
                : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
            return BuiltInTypes.GetArrayMemberType(name, unionElem);
        }

        // Handle TypeParameter with constraint - delegate to constraint
        if (type is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return GetMemberType(tp.Constraint, name);
        }

        // Handle Interface type
        if (type is TypeInfo.Interface itf)
        {
            foreach (var member in itf.GetAllMembers())
            {
                if (member.Key == name) return member.Value;
            }
            return null;
        }

        if (type is TypeInfo.Instance instance)
        {
            // Handle InstantiatedGeneric
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                // Check fields first, then methods
                if (gc.FieldTypes.TryGetValue(name, out var fieldType)) return fieldType;
                if (gc.Methods.TryGetValue(name, out var methodType)) return methodType;
                TypeInfo? current = gc.Superclass;
                while (current != null)
                {
                    var fields = GetFieldTypes(current);
                    if (fields != null && fields.TryGetValue(name, out var superField)) return superField;
                    var methods = GetMethods(current);
                    if (methods != null && methods.TryGetValue(name, out var superMethod)) return superMethod;
                    current = GetSuperclass(current);
                }
            }
            else if (instance.ClassType is TypeInfo.Class classType)
            {
                TypeInfo? current = classType;
                while (current != null)
                {
                    // Check fields first, then methods
                    var fields = GetFieldTypes(current);
                    if (fields != null && fields.TryGetValue(name, out var fieldType)) return fieldType;
                    var methods = GetMethods(current);
                    if (methods != null && methods.TryGetValue(name, out var methodType)) return methodType;
                    current = GetSuperclass(current);
                }
            }
            // Handle MutableClass (during signature collection)
            else if (instance.ClassType is TypeInfo.MutableClass mutableClass)
            {
                // Check fields first, then methods
                if (mutableClass.FieldTypes.TryGetValue(name, out var fieldType)) return fieldType;
                if (mutableClass.Methods.TryGetValue(name, out var methodType)) return methodType;
                // Check frozen version if available (may have superclass methods)
                if (mutableClass.Frozen is TypeInfo.Class frozen)
                {
                    TypeInfo? current = frozen.Superclass;
                    while (current != null)
                    {
                        var fields = GetFieldTypes(current);
                        if (fields != null && fields.TryGetValue(name, out var superField)) return superField;
                        var methods = GetMethods(current);
                        if (methods != null && methods.TryGetValue(name, out var superMethod)) return superMethod;
                        current = GetSuperclass(current);
                    }
                }
            }
        }
        return null;
    }

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
        IsTypeOfKind(t, type => type is TypeInfo.BigInt);

    /// <summary>
    /// Checks if a type is a primitive (not valid as WeakMap key or WeakSet value).
    /// </summary>
    private bool IsPrimitiveType(TypeInfo t) => t is TypeInfo.String or TypeInfo.Primitive or TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.Symbol;

    /// <summary>
    /// Checks if a name is a built-in Error type name.
    /// Delegates to ErrorBuiltIns for centralized type name knowledge.
    /// </summary>
    private static bool IsErrorTypeName(string name) => ErrorBuiltIns.IsErrorTypeName(name);

    private bool IsSubclassOf(TypeInfo.Class? subclass, TypeInfo.Class target)
    {
        if (subclass == null) return false;
        TypeInfo? current = subclass;
        while (current != null)
        {
            if (current is TypeInfo.Class cls && cls.Name == target.Name) return true;
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Gets the superclass of a class type, handling both Class and InstantiatedGeneric.
    /// </summary>
    private static TypeInfo? GetSuperclass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Superclass,
        TypeInfo.GenericClass gc => gc.Superclass,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Superclass,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the methods dictionary from a class-like type (Class, GenericClass, or InstantiatedGeneric).
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Methods,
        TypeInfo.GenericClass gc => gc.Methods,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Methods,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the name of a class-like type (Class, GenericClass, or InstantiatedGeneric).
    /// </summary>
    private static string? GetClassName(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Name,
        TypeInfo.GenericClass gc => gc.Name,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Name,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the static methods dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetStaticMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.StaticMethods,
        TypeInfo.GenericClass gc => gc.StaticMethods,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.StaticMethods,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the static properties dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetStaticProperties(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.StaticProperties,
        TypeInfo.GenericClass gc => gc.StaticProperties,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.StaticProperties,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Converts a class-like type to a TypeInfo.Class for walking hierarchy.
    /// Returns null if the type is not class-like.
    /// </summary>
    private static TypeInfo.Class? AsClass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c,
        _ => null
    };

    /// <summary>
    /// Gets the field types dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetFieldTypes(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.FieldTypes,
        TypeInfo.GenericClass gc => gc.FieldTypes,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.FieldTypes,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the getters dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetGetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Getters,
        TypeInfo.GenericClass gc => gc.Getters,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Getters,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the setters dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, TypeInfo>? GetSetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Setters,
        TypeInfo.GenericClass gc => gc.Setters,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Setters,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the method access dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, AccessModifier>? GetMethodAccess(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.MethodAccess,
        TypeInfo.GenericClass gc => gc.MethodAccess,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.MethodAccess,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the field access dictionary from a class-like type.
    /// </summary>
    private static FrozenDictionary<string, AccessModifier>? GetFieldAccess(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.FieldAccess,
        TypeInfo.GenericClass gc => gc.FieldAccess,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.FieldAccess,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the readonly fields set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetReadonlyFields(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.ReadonlyFields,
        TypeInfo.GenericClass gc => gc.ReadonlyFields,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.ReadonlyFields,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract methods set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractMethodSet,
        TypeInfo.GenericClass gc => gc.AbstractMethodSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractMethodSet,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract getters set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractGetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractGetterSet,
        TypeInfo.GenericClass gc => gc.AbstractGetterSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractGetterSet,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Gets the abstract setters set from a class-like type.
    /// </summary>
    private static FrozenSet<string>? GetAbstractSetters(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.AbstractSetterSet,
        TypeInfo.GenericClass gc => gc.AbstractSetterSet,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.AbstractSetterSet,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Checks if a target InstantiatedGeneric is in the superclass chain of a class.
    /// Used for checking if NumberBox (extends Box&lt;number&gt;) is assignable to Box&lt;number&gt;.
    /// </summary>
    private bool IsInSuperclassChain(TypeInfo classType, TypeInfo.InstantiatedGeneric target)
    {
        TypeInfo? current = classType switch
        {
            TypeInfo.Class c => c.Superclass,
            TypeInfo.InstantiatedGeneric ig => GetSuperclass(ig),
            _ => null
        };

        while (current != null)
        {
            if (current is TypeInfo.InstantiatedGeneric ig)
            {
                // Check if this InstantiatedGeneric matches the target
                if (InstantiatedGenericsMatch(ig, target))
                    return true;

                // Continue up the chain
                current = GetSuperclass(ig);
            }
            else if (current is TypeInfo.Class c)
            {
                // Regular class in chain, continue up
                current = c.Superclass;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two InstantiatedGeneric types match (same generic definition and compatible type arguments).
    /// </summary>
    private bool InstantiatedGenericsMatch(TypeInfo.InstantiatedGeneric a, TypeInfo.InstantiatedGeneric b)
    {
        // Must be the same generic definition
        if (a.GenericDefinition is TypeInfo.GenericClass gcA &&
            b.GenericDefinition is TypeInfo.GenericClass gcB &&
            gcA.Name == gcB.Name)
        {
            if (a.TypeArguments.Count != b.TypeArguments.Count)
                return false;

            for (int i = 0; i < a.TypeArguments.Count; i++)
            {
                if (!IsCompatible(a.TypeArguments[i], b.TypeArguments[i]))
                    return false;
            }
            return true;
        }

        return false;
    }

    // ============== TEMPLATE LITERAL PATTERN MATCHING ==============

    /// <summary>
    /// Checks if a string literal matches a template literal pattern.
    /// </summary>
    private bool MatchesTemplateLiteralPattern(TypeInfo.TemplateLiteralType pattern, string value)
    {
        int pos = 0;

        for (int i = 0; i < pattern.InterpolatedTypes.Count; i++)
        {
            string prefix = pattern.Strings[i];

            // Check prefix
            if (pos + prefix.Length > value.Length || !value[pos..].StartsWith(prefix))
                return false;
            pos += prefix.Length;

            // Find where the next static string starts (or end of string)
            string nextStatic = pattern.Strings[i + 1];
            int nextPos;

            if (string.IsNullOrEmpty(nextStatic))
            {
                // No more static parts after this interpolation
                if (i == pattern.InterpolatedTypes.Count - 1)
                {
                    // Last interpolation - capture rest of string
                    nextPos = value.Length;
                }
                else
                {
                    // Find next non-empty static part
                    int foundIdx = -1;
                    for (int j = i + 2; j < pattern.Strings.Count; j++)
                    {
                        if (!string.IsNullOrEmpty(pattern.Strings[j]))
                        {
                            foundIdx = value.IndexOf(pattern.Strings[j], pos);
                            break;
                        }
                    }
                    nextPos = foundIdx >= pos ? foundIdx : value.Length;
                }
            }
            else
            {
                nextPos = value.IndexOf(nextStatic, pos);
            }

            if (nextPos < pos) return false;

            // Extract the matched portion and check against interpolated type
            string matched = value[pos..nextPos];
            TypeInfo interpolatedType = pattern.InterpolatedTypes[i];

            if (!MatchesInterpolatedType(matched, interpolatedType))
                return false;

            pos = nextPos;
        }

        // Check final suffix
        return value[pos..] == pattern.Strings[^1];
    }

    /// <summary>
    /// Checks if a string matches an interpolated type slot in a template literal.
    /// </summary>
    private bool MatchesInterpolatedType(string value, TypeInfo type) => type switch
    {
        TypeInfo.String => true,  // 'string' matches any string
        TypeInfo.StringLiteral sl => sl.Value == value,
        TypeInfo.NumberLiteral nl => double.TryParse(value, out var d) && d == nl.Value,
        TypeInfo.BooleanLiteral bl => (bl.Value ? "true" : "false") == value,
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => double.TryParse(value, out _),
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => value is "true" or "false",
        TypeInfo.Union u => u.FlattenedTypes.Any(t => MatchesInterpolatedType(value, t)),
        _ => false
    };

    /// <summary>
    /// Checks if two template literal types are structurally compatible.
    /// </summary>
    private bool TemplatePatternStructurallyCompatible(TypeInfo.TemplateLiteralType expected, TypeInfo.TemplateLiteralType actual)
    {
        // Must have same structure
        if (expected.Strings.Count != actual.Strings.Count ||
            expected.InterpolatedTypes.Count != actual.InterpolatedTypes.Count)
            return false;

        // All static strings must match
        for (int i = 0; i < expected.Strings.Count; i++)
        {
            if (expected.Strings[i] != actual.Strings[i])
                return false;
        }

        // All interpolated types must be compatible
        for (int i = 0; i < expected.InterpolatedTypes.Count; i++)
        {
            if (!IsCompatible(expected.InterpolatedTypes[i], actual.InterpolatedTypes[i]))
                return false;
        }

        return true;
    }
}
