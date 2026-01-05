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
    private bool IsCompatible(TypeInfo expected, TypeInfo actual)
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

        // Type parameter as actual: can be assigned to any or same type parameter
        if (actual is TypeInfo.TypeParameter)
        {
            return expected is TypeInfo.Any;
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

        // Null compatibility
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) return true;
            if (expected is TypeInfo.Null) return true;
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
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_STRING } && actual is TypeInfo.StringLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            return true;

        // Union-to-union: each type in actual must be compatible with at least one type in expected
        if (expected is TypeInfo.Union expectedUnion && actual is TypeInfo.Union actualUnion)
        {
            return actualUnion.FlattenedTypes.All(actualType =>
                expectedUnion.FlattenedTypes.Any(expectedType => IsCompatible(expectedType, actualType)));
        }

        // Union as expected: actual must match at least one member
        if (expected is TypeInfo.Union expUnion)
        {
            return expUnion.FlattenedTypes.Any(t => IsCompatible(t, actual));
        }

        // Union as actual: all members must be compatible with expected
        if (actual is TypeInfo.Union actUnion)
        {
            return actUnion.FlattenedTypes.All(t => IsCompatible(expected, t));
        }

        // Intersection as expected: actual must satisfy ALL member types
        if (expected is TypeInfo.Intersection expIntersection)
        {
            return expIntersection.FlattenedTypes.All(t => IsCompatible(t, actual));
        }

        // Intersection as actual: satisfies expected if any member does
        // (because intersection value has all the properties of all its constituents)
        if (actual is TypeInfo.Intersection actIntersection)
        {
            return actIntersection.FlattenedTypes.Any(t => IsCompatible(expected, t));
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
            if (expectedEnum.Kind == EnumKind.String &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                return true;

            // Heterogeneous enum accepts both
            if (expectedEnum.Kind == EnumKind.Heterogeneous &&
                actual is TypeInfo.Primitive p &&
                (p.Type == TokenType.TYPE_NUMBER || p.Type == TokenType.TYPE_STRING))
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

            if (actualEnumType.Kind == EnumKind.String &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                return true;

            if (actualEnumType.Kind == EnumKind.Heterogeneous &&
                expected is TypeInfo.Primitive ep &&
                (ep.Type == TokenType.TYPE_NUMBER || ep.Type == TokenType.TYPE_STRING))
                return true;
        }

        if (expected is TypeInfo.Primitive p1 && actual is TypeInfo.Primitive p2)
        {
            return p1.Type == p2.Type;
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

        if (expected is TypeInfo.Instance i1 && actual is TypeInfo.Instance i2)
        {
            // Handle InstantiatedGeneric comparison
            if (i1.ClassType is TypeInfo.InstantiatedGeneric expectedIG &&
                i2.ClassType is TypeInfo.InstantiatedGeneric actualIG)
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
                return false;
            }

            // Handle regular Class comparison
            if (i1.ClassType is TypeInfo.Class expectedClass && i2.ClassType is TypeInfo.Class actualClass)
            {
                TypeInfo.Class? current = actualClass;
                while (current != null)
                {
                    if (current.Name == expectedClass.Name) return true;
                    current = current.Superclass;
                }
            }

            // Mixed case: InstantiatedGeneric vs regular Class - not compatible
            return false;
        }

        if (expected is TypeInfo.Interface itf)
        {
            return CheckStructuralCompatibility(itf.Members, actual, itf.OptionalMemberSet);
        }

        // Handle InstantiatedGeneric interface (e.g., Container<number>)
        if (expected is TypeInfo.InstantiatedGeneric expectedInterfaceIG &&
            expectedInterfaceIG.GenericDefinition is TypeInfo.GenericInterface gi)
        {
            // Build substitution map
            var subs = new Dictionary<string, TypeInfo>();
            for (int i = 0; i < gi.TypeParams.Count; i++)
                subs[gi.TypeParams[i].Name] = expectedInterfaceIG.TypeArguments[i];

            // Substitute type parameters in interface members
            var substitutedMembers = new Dictionary<string, TypeInfo>();
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
            // All explicit fields in expected must exist in actual with compatible types
            foreach (var (name, expectedFieldType) in expRecord.Fields)
            {
                if (!actRecord.Fields.TryGetValue(name, out var actualFieldType))
                    return false;
                if (!IsCompatible(expectedFieldType, actualFieldType))
                    return false;
            }
            // If expected has only index signatures (no explicit fields), empty object is compatible
            // Index signatures allow any number of keys (including zero)
            return true;
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

        // Function type compatibility
        if (expected is TypeInfo.Function f1 && actual is TypeInfo.Function f2)
        {
            // For callbacks, actual can have fewer params than expected (unused params)
            if (f2.ParamTypes.Count > f1.ParamTypes.Count) return false;
            for (int i = 0; i < f2.ParamTypes.Count; i++)
            {
                if (!IsCompatible(f1.ParamTypes[i], f2.ParamTypes[i])) return false;
            }
            // Return type: actual must be compatible with expected
            return IsCompatible(f1.ReturnType, f2.ReturnType);
        }

        return false;
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
        // Pattern: typeof x === "string" or typeof x == "string"
        if (condition is Expr.Binary bin &&
            bin.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v } &&
            bin.Right is Expr.Literal { Value: string typeStr })
        {
            var currentType = _environment.Get(v.Name.Lexeme);

            // Handle unknown type narrowing - typeof checks narrow unknown to specific types
            if (currentType is TypeInfo.Unknown)
            {
                TypeInfo? narrowedType = typeStr switch
                {
                    "string" => new TypeInfo.Primitive(TokenType.TYPE_STRING),
                    "number" => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                    "boolean" => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
                    "bigint" => new TypeInfo.BigInt(),
                    _ => null
                };
                // Excluded type remains unknown (we don't know what else it could be)
                return (v.Name.Lexeme, narrowedType, new TypeInfo.Unknown());
            }

            if (currentType is TypeInfo.Union union)
            {
                var narrowed = union.FlattenedTypes.Where(t => TypeMatchesTypeof(t, typeStr)).ToList();
                var excluded = union.FlattenedTypes.Where(t => !TypeMatchesTypeof(t, typeStr)).ToList();

                TypeInfo? narrowedType = narrowed.Count == 0 ? null :
                    narrowed.Count == 1 ? narrowed[0] : new TypeInfo.Union(narrowed);
                TypeInfo? excludedType = excluded.Count == 0 ? null :
                    excluded.Count == 1 ? excluded[0] : new TypeInfo.Union(excluded);

                return (v.Name.Lexeme, narrowedType, excludedType);
            }
        }
        return (null, null, null);
    }

    private bool TypeMatchesTypeof(TypeInfo type, string typeofResult) => typeofResult switch
    {
        "string" => type is TypeInfo.Primitive { Type: TokenType.TYPE_STRING },
        "number" => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER },
        "boolean" => type is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN },
        "bigint" => type is TypeInfo.BigInt,
        "object" => type is TypeInfo.Null or TypeInfo.Record or TypeInfo.Array or TypeInfo.Instance,
        "function" => type is TypeInfo.Function,
        _ => false
    };

    private bool CheckStructuralCompatibility(Dictionary<string, TypeInfo> requiredMembers, TypeInfo actual, HashSet<string>? optionalMembers = null)
    {
        optionalMembers ??= [];
        foreach (var member in requiredMembers)
        {
            TypeInfo? actualMemberType = GetMemberType(actual, member.Key);

            // If member is optional and not present, that's OK
            if (actualMemberType == null && optionalMembers.Contains(member.Key))
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

    private TypeInfo? GetMemberType(TypeInfo type, string name)
    {
        if (type is TypeInfo.Record record)
        {
            return record.Fields.TryGetValue(name, out var t) ? t : null;
        }
        if (type is TypeInfo.Instance instance)
        {
            // Handle InstantiatedGeneric
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                if (gc.Methods.TryGetValue(name, out var methodType)) return methodType;
                var current = gc.Superclass;
                while (current != null)
                {
                    if (current.Methods.TryGetValue(name, out var superMethod)) return superMethod;
                    current = current.Superclass;
                }
            }
            else if (instance.ClassType is TypeInfo.Class classType)
            {
                TypeInfo.Class? current = classType;
                while (current != null)
                {
                    if (current.Methods.TryGetValue(name, out var methodType)) return methodType;
                    current = current.Superclass;
                }
            }
        }
        return null;
    }

    private bool IsNumber(TypeInfo t) => t is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER || t is TypeInfo.NumberLiteral || t is TypeInfo.Any;
    private bool IsString(TypeInfo t) => t is TypeInfo.Primitive p && p.Type == TokenType.TYPE_STRING || t is TypeInfo.StringLiteral || t is TypeInfo.Any;
    private bool IsBigInt(TypeInfo t) => t is TypeInfo.BigInt || t is TypeInfo.Any;

    private bool IsSubclassOf(TypeInfo.Class? subclass, TypeInfo.Class target)
    {
        if (subclass == null) return false;
        TypeInfo.Class? current = subclass;
        while (current != null)
        {
            if (current.Name == target.Name) return true;
            current = current.Superclass;
        }
        return false;
    }
}
