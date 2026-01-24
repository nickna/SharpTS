using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type guard analysis for control-flow based type narrowing.
/// </summary>
public partial class TypeChecker
{
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

        // Pattern 11: User-defined type predicate function call: isString(x)
        if (condition is Expr.Call predicateCall)
        {
            return AnalyzeTypePredicateCall(predicateCall);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Finds the parameter index for a given parameter name in a function type.
    /// Returns -1 if parameter names are not available or the name is not found.
    /// </summary>
    private static int FindParameterIndex(TypeInfo funcType, string paramName)
    {
        List<string>? paramNames = funcType switch
        {
            TypeInfo.Function f => f.ParamNames,
            TypeInfo.GenericFunction gf => gf.ParamNames,
            _ => null
        };

        if (paramNames == null) return 0; // Fallback to first param for backwards compatibility
        int index = paramNames.IndexOf(paramName);
        return index >= 0 ? index : 0; // Fallback to first param if not found
    }

    /// <summary>
    /// Analyzes a call to a user-defined type predicate function like isString(x).
    /// Returns narrowing info if the callee has a type predicate return type.
    /// </summary>
    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypePredicateCall(Expr.Call call)
    {
        // Get the callee's type
        TypeInfo? calleeType = null;

        // Handle direct function calls: isString(x)
        if (call.Callee is Expr.Variable funcVar)
        {
            calleeType = _environment.Get(funcVar.Name.Lexeme);
        }
        // Handle method calls: obj.isString(x)
        else if (call.Callee is Expr.Get getExpr)
        {
            var objType = CheckExpr(getExpr.Object);
            calleeType = GetMemberType(objType, getExpr.Name.Lexeme);
        }

        if (calleeType == null) return (null, null, null);

        // Handle GenericFunction by checking if it has a predicate return type
        TypeInfo? returnType = calleeType switch
        {
            TypeInfo.Function func => func.ReturnType,
            TypeInfo.GenericFunction gf => gf.ReturnType,
            _ => null
        };

        // Check for type predicate return type (not assertion - those are handled differently)
        if (returnType is TypeInfo.TypePredicate pred && !pred.IsAssertion)
        {
            // Look up the parameter index by name from the function type
            int paramIndex = FindParameterIndex(calleeType, pred.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
            {
                string varName = argVar.Name.Lexeme;
                TypeInfo? currentType = _environment.Get(varName);

                TypeInfo narrowedType = pred.PredicateType;
                TypeInfo? excludedType = currentType != null
                    ? ExcludeTypeFromUnion(currentType, narrowedType)
                    : null;

                return (varName, narrowedType, excludedType);
            }
        }

        return (null, null, null);
    }

    /// <summary>
    /// Excludes a type from a union, returning the remaining types.
    /// </summary>
    private static TypeInfo? ExcludeTypeFromUnion(TypeInfo source, TypeInfo toExclude)
    {
        if (source is TypeInfo.Union union)
        {
            var remaining = union.FlattenedTypes
                .Where(t => t.ToString() != toExclude.ToString())
                .ToList();

            if (remaining.Count == 0) return new TypeInfo.Never();
            if (remaining.Count == 1) return remaining[0];
            return new TypeInfo.Union(remaining);
        }

        // If source equals toExclude, nothing remains
        if (source.ToString() == toExclude.ToString())
            return new TypeInfo.Never();

        // Otherwise, the type doesn't change in else branch
        return source;
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
}
