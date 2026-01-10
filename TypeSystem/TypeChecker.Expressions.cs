using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Expression type checking - CheckExpr dispatch and basic expression handlers.
/// </summary>
/// <remarks>
/// Contains the main expression dispatch (CheckExpr) and handlers for:
/// literals, arrays, objects, templates, spread, arrow functions, assign, type assertions,
/// and basic helper methods (LookupVariable, GetLiteralType).
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckExpr(Expr expr)
    {
        TypeInfo result = expr switch
        {
            Expr.Literal literal => GetLiteralType(literal.Value),
            Expr.Variable variable => LookupVariable(variable.Name),
            Expr.Assign assign => CheckAssign(assign),
            Expr.Binary binary => CheckBinary(binary),
            Expr.Logical logical => CheckLogical(logical),
            Expr.NullishCoalescing nc => CheckNullishCoalescing(nc),
            Expr.Ternary ternary => CheckTernary(ternary),
            Expr.Call call => CheckCall(call),
            Expr.Grouping grouping => CheckExpr(grouping.Expression),
            Expr.Unary unary => CheckUnary(unary),
            Expr.Get get => CheckGet(get),
            Expr.Set set => CheckSet(set),
            Expr.This thisExpr => CheckThis(thisExpr),
            Expr.New newExpr => CheckNew(newExpr),
            Expr.ArrayLiteral array => CheckArray(array),
            Expr.ObjectLiteral obj => CheckObject(obj),
            Expr.GetIndex getIndex => CheckGetIndex(getIndex),
            Expr.SetIndex setIndex => CheckSetIndex(setIndex),
            Expr.Super super => CheckSuper(super),
            Expr.CompoundAssign compound => CheckCompoundAssign(compound),
            Expr.CompoundSet compoundSet => CheckCompoundSet(compoundSet),
            Expr.CompoundSetIndex compoundSetIndex => CheckCompoundSetIndex(compoundSetIndex),
            Expr.PrefixIncrement prefix => CheckPrefixIncrement(prefix),
            Expr.PostfixIncrement postfix => CheckPostfixIncrement(postfix),
            Expr.ArrowFunction arrow => CheckArrowFunction(arrow),
            Expr.TemplateLiteral template => CheckTemplateLiteral(template),
            Expr.Spread spread => CheckSpread(spread),
            Expr.TypeAssertion ta => CheckTypeAssertion(ta),
            Expr.Await awaitExpr => CheckAwait(awaitExpr),
            Expr.DynamicImport di => CheckDynamicImport(di),
            Expr.Yield yieldExpr => CheckYield(yieldExpr),
            Expr.RegexLiteral => new TypeInfo.RegExp(),
            _ => new TypeInfo.Any()
        };

        // Store the resolved type in the TypeMap for use by ILCompiler/Interpreter
        _typeMap.Set(expr, result);

        return result;
    }

    private TypeInfo CheckAwait(Expr.Await awaitExpr)
    {
        if (!_inAsyncFunction)
        {
            throw new Exception("Type Error: 'await' is only valid inside an async function.");
        }

        TypeInfo exprType = CheckExpr(awaitExpr.Expression);
        return ResolveAwaitedType(exprType);
    }

    private TypeInfo CheckDynamicImport(Expr.DynamicImport di)
    {
        TypeInfo pathType = CheckExpr(di.PathExpression);

        // Path must be string, string literal, or any
        bool isValidPath = pathType is TypeInfo.Primitive { Type: TokenType.TYPE_STRING }
                        || pathType is TypeInfo.StringLiteral
                        || pathType is TypeInfo.Any;

        if (!isValidPath)
        {
            throw new Exception($"Type Error: Dynamic import path must be a string, got '{pathType}'.");
        }

        // Dynamic import returns Promise<any> since module type is unknown at compile time
        return new TypeInfo.Promise(new TypeInfo.Any());
    }

    private TypeInfo CheckYield(Expr.Yield yieldExpr)
    {
        if (!_inGeneratorFunction)
        {
            throw new Exception("Type Error: 'yield' is only valid inside a generator function.");
        }

        if (yieldExpr.Value != null)
        {
            TypeInfo valueType = CheckExpr(yieldExpr.Value);

            // For yield*, the expression must be iterable and we yield each element
            if (yieldExpr.IsDelegating)
            {
                // yield* requires an iterable (array, generator, etc.)
                return GetIterableElementType(valueType);
            }

            return valueType;
        }

        // Bare yield returns undefined (void type for simplicity)
        return new TypeInfo.Void();
    }

    /// <summary>
    /// Gets the element type from an iterable type (for yield* delegation).
    /// </summary>
    private TypeInfo GetIterableElementType(TypeInfo type) => type switch
    {
        TypeInfo.Array arr => arr.ElementType,
        TypeInfo.Generator gen => gen.YieldType,
        TypeInfo.Iterator iter => iter.ElementType,
        TypeInfo.Set set => set.ElementType,
        TypeInfo.Map map => new TypeInfo.Tuple([map.KeyType, map.ValueType], 2),  // [K, V] tuples
        TypeInfo.Primitive p when p.Type == Parsing.TokenType.TYPE_STRING => new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING),  // String yields characters
        TypeInfo.StringLiteral => new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING),  // String literal also yields characters
        TypeInfo.Any => new TypeInfo.Any(),
        _ => throw new Exception($"Type Error: Type '{type}' is not iterable for yield*.")
    };

    /// <summary>
    /// Resolves the Awaited&lt;T&gt; type - recursively unwraps Promise types.
    /// Handles Promise<T> → T, Promise<Promise<T>> → T, and distributes over unions.
    /// </summary>
    private TypeInfo ResolveAwaitedType(TypeInfo type) => type switch
    {
        TypeInfo.Promise p => ResolveAwaitedType(p.ValueType),
        TypeInfo.Union u => new TypeInfo.Union(
            u.FlattenedTypes.Select(ResolveAwaitedType).ToList()),
        _ => type
    };

    private TypeInfo CheckTypeAssertion(Expr.TypeAssertion ta)
    {
        TypeInfo sourceType = CheckExpr(ta.Expression);
        TypeInfo targetType = ToTypeInfo(ta.TargetType);

        // Allow any <-> anything (escape hatch)
        if (sourceType is TypeInfo.Any || targetType is TypeInfo.Any)
            return targetType;

        // Check if types are related (either direction)
        if (IsCompatible(targetType, sourceType) || IsCompatible(sourceType, targetType))
            return targetType;

        throw new Exception($"Type Error: Cannot assert type '{sourceType}' to '{targetType}'.");
    }

    private TypeInfo CheckTemplateLiteral(Expr.TemplateLiteral template)
    {
        // Type check all interpolated expressions (any type is allowed)
        foreach (var expr in template.Expressions)
        {
            CheckExpr(expr);
        }
        // Template literals always result in string
        return new TypeInfo.Primitive(TokenType.TYPE_STRING);
    }

    private TypeInfo CheckObject(Expr.ObjectLiteral obj)
    {
        Dictionary<string, TypeInfo> fields = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                // Spread property - merge fields from the spread object
                TypeInfo spreadType = CheckExpr(prop.Value);
                if (spreadType is TypeInfo.Record record)
                {
                    foreach (var kv in record.Fields)
                    {
                        fields[kv.Key] = kv.Value;
                    }
                }
                else if (spreadType is TypeInfo.Instance inst)
                {
                    // Instance fields are dynamic, just accept
                }
                else if (spreadType is TypeInfo.Any)
                {
                    // Any is fine
                }
                else
                {
                    throw new Exception($"Type Error: Spread in object literal requires an object, got '{spreadType}'.");
                }
            }
            else
            {
                TypeInfo valueType = CheckExpr(prop.Value);

                switch (prop.Key)
                {
                    case Expr.IdentifierKey ik:
                        fields[ik.Name.Lexeme] = valueType;
                        break;

                    case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                        fields[(string)lk.Literal.Literal!] = valueType;
                        break;

                    case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                        // Number keys are converted to strings in JS/TS
                        fields[lk.Literal.Literal!.ToString()!] = valueType;
                        numberIndexType = UnifyIndexTypes(numberIndexType, valueType);
                        break;

                    case Expr.ComputedKey ck:
                        TypeInfo keyType = CheckExpr(ck.Expression);
                        // Infer index signature based on key type
                        if (keyType is TypeInfo.Primitive p && p.Type == TokenType.TYPE_STRING)
                            stringIndexType = UnifyIndexTypes(stringIndexType, valueType);
                        else if (keyType is TypeInfo.Primitive n && n.Type == TokenType.TYPE_NUMBER)
                            numberIndexType = UnifyIndexTypes(numberIndexType, valueType);
                        else if (keyType is TypeInfo.Symbol)
                            symbolIndexType = UnifyIndexTypes(symbolIndexType, valueType);
                        else if (keyType is TypeInfo.StringLiteral sl)
                            fields[sl.Value] = valueType;  // Known key at compile time
                        else if (keyType is TypeInfo.NumberLiteral nl)
                            fields[nl.Value.ToString()] = valueType;
                        else if (keyType is TypeInfo.Any)
                            stringIndexType = UnifyIndexTypes(stringIndexType, valueType);
                        else if (keyType is TypeInfo.Union)
                            // Union of string/number types - use string index signature
                            stringIndexType = UnifyIndexTypes(stringIndexType, valueType);
                        else
                            throw new Exception($"Type Error: Computed property key must be string, number, or symbol, got '{keyType}'.");
                        break;
                }
            }
        }
        return new TypeInfo.Record(fields.ToFrozenDictionary(), stringIndexType, numberIndexType, symbolIndexType);
    }

    /// <summary>
    /// Unifies index signature types - creates a union if types differ.
    /// </summary>
    private TypeInfo UnifyIndexTypes(TypeInfo? existing, TypeInfo newType)
    {
        if (existing == null) return newType;
        if (IsCompatible(existing, newType)) return existing;
        if (IsCompatible(newType, existing)) return newType;
        // Create union if incompatible
        return new TypeInfo.Union([existing, newType]);
    }

    private TypeInfo CheckArray(Expr.ArrayLiteral array)
    {
        if (array.Elements.Count == 0) return new TypeInfo.Array(new TypeInfo.Any()); // Empty array is any[]? or generic?

        List<TypeInfo> elementTypes = [];
        foreach (var element in array.Elements)
        {
            TypeInfo elemType;
            if (element is Expr.Spread spread)
            {
                // Spread element - get element type from array or tuple
                TypeInfo spreadType = CheckExpr(spread.Expression);
                if (spreadType is TypeInfo.Array arrType)
                {
                    elemType = arrType.ElementType;
                }
                else if (spreadType is TypeInfo.Tuple tupType)
                {
                    // Spread tuple - add all its element types
                    elementTypes.AddRange(tupType.ElementTypes);
                    if (tupType.RestElementType != null)
                        elementTypes.Add(tupType.RestElementType);
                    continue; // Don't add elemType again since we added multiple
                }
                else if (spreadType is TypeInfo.Any)
                {
                    elemType = new TypeInfo.Any();
                }
                else
                {
                    throw new Exception($"Type Error: Spread expression must be an array or tuple, got '{spreadType}'.");
                }
            }
            else
            {
                elemType = CheckExpr(element);
            }
            elementTypes.Add(elemType);
        }

        // Find common type or create union
        TypeInfo commonType = elementTypes[0];
        bool allCompatible = true;
        for (int i = 1; i < elementTypes.Count; i++)
        {
            if (!IsCompatible(commonType, elementTypes[i]) && !IsCompatible(elementTypes[i], commonType))
            {
                allCompatible = false;
                break;
            }
        }

        if (!allCompatible)
        {
            // Create union of all unique element types
            var uniqueTypes = elementTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            commonType = uniqueTypes.Count == 1 ? uniqueTypes[0] : new TypeInfo.Union(uniqueTypes);
        }

        return new TypeInfo.Array(commonType);
    }

    private TypeInfo CheckSpread(Expr.Spread spread)
    {
        // Spread just passes through to the underlying expression type
        // The actual spread logic is handled by the caller (array literal, call, etc.)
        return CheckExpr(spread.Expression);
    }

    private void CheckArrayLiteralAgainstTuple(Expr.ArrayLiteral arrayLit, TypeInfo.Tuple tupleType, string varName)
    {
        int elemCount = arrayLit.Elements.Count;
        int requiredCount = tupleType.RequiredCount;
        int maxCount = tupleType.MaxLength ?? int.MaxValue;

        // Check element count
        if (elemCount < requiredCount)
        {
            throw new Exception($"Type Error: Tuple requires at least {requiredCount} elements, but got {elemCount} for variable '{varName}'.");
        }
        if (tupleType.RestElementType == null && elemCount > tupleType.ElementTypes.Count)
        {
            throw new Exception($"Type Error: Tuple expects at most {tupleType.ElementTypes.Count} elements, but got {elemCount} for variable '{varName}'.");
        }

        // Check each element type
        for (int i = 0; i < elemCount; i++)
        {
            var element = arrayLit.Elements[i];
            TypeInfo expectedType;

            if (i < tupleType.ElementTypes.Count)
            {
                expectedType = tupleType.ElementTypes[i];
            }
            else if (tupleType.RestElementType != null)
            {
                expectedType = tupleType.RestElementType;
            }
            else
            {
                throw new Exception($"Type Error: Tuple index {i} is out of bounds for variable '{varName}'.");
            }

            // Recursively apply contextual typing for nested array literals with tuple types
            if (expectedType is TypeInfo.Tuple nestedTuple && element is Expr.ArrayLiteral nestedArrayLit)
            {
                CheckArrayLiteralAgainstTuple(nestedArrayLit, nestedTuple, $"{varName}[{i}]");
            }
            else
            {
                TypeInfo elemType = CheckExpr(element);
                if (!IsCompatible(expectedType, elemType))
                {
                    throw new Exception($"Type Error: Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.");
                }
            }
        }
    }

    private TypeInfo CheckArrowFunction(Expr.ArrowFunction arrow)
    {
        // Parse explicit 'this' type if present (for object literal method shorthand)
        // Note: Arrow function expressions shouldn't have 'this' parameter in standard TypeScript,
        // but we support it for object literal method shorthand which is parsed as ArrowFunction.
        TypeInfo? thisType = arrow.ThisType != null ? ToTypeInfo(arrow.ThisType) : null;

        // For object method shorthand, allow 'this' even without explicit type annotation
        // TypeScript infers 'this' as the containing object type, but we use 'any' for simplicity
        if (arrow.IsObjectMethod && thisType == null)
        {
            thisType = new TypeInfo.Any();
        }

        // Build parameter types and check defaults
        List<TypeInfo> paramTypes = [];
        int requiredParams = 0;
        bool seenDefault = false;

        foreach (var param in arrow.Parameters)
        {
            TypeInfo paramType = param.Type != null ? ToTypeInfo(param.Type) : new TypeInfo.Any();
            paramTypes.Add(paramType);

            // Rest parameters are not counted toward required params
            if (param.IsRest)
            {
                continue;
            }

            if (param.DefaultValue != null)
            {
                seenDefault = true;
                TypeInfo defaultType = CheckExpr(param.DefaultValue);
                if (!IsCompatible(paramType, defaultType))
                {
                    throw new Exception($"Type Error: Default value type '{defaultType}' is not assignable to parameter type '{paramType}'.");
                }
            }
            else if (param.IsOptional)
            {
                seenDefault = true; // Optional parameters are like having a default
            }
            else
            {
                if (seenDefault)
                {
                    throw new Exception($"Type Error: Required parameter cannot follow optional parameter.");
                }
                requiredParams++;
            }
        }

        // Determine return type
        TypeInfo returnType = arrow.ReturnType != null
            ? ToTypeInfo(arrow.ReturnType)
            : new TypeInfo.Any();

        // Create new environment with parameters
        TypeEnvironment arrowEnv = new(_environment);
        for (int i = 0; i < arrow.Parameters.Count; i++)
        {
            arrowEnv.Define(arrow.Parameters[i].Name.Lexeme, paramTypes[i]);
        }

        // Save and set context - function bodies are isolated from outer loop/switch/label context
        TypeEnvironment previousEnv = _environment;
        TypeInfo? previousReturn = _currentFunctionReturnType;
        TypeInfo? previousThisType = _currentFunctionThisType;
        bool previousInAsync = _inAsyncFunction;
        int previousLoopDepth = _loopDepth;
        int previousSwitchDepth = _switchDepth;
        var previousActiveLabels = new Dictionary<string, bool>(_activeLabels);

        _environment = arrowEnv;
        _currentFunctionReturnType = returnType;
        _currentFunctionThisType = thisType;
        _inAsyncFunction = arrow.IsAsync;
        _loopDepth = 0;
        _switchDepth = 0;
        _activeLabels.Clear();

        try
        {
            if (arrow.ExpressionBody != null)
            {
                // Expression body - infer return type if not specified
                TypeInfo exprType = CheckExpr(arrow.ExpressionBody);
                if (arrow.ReturnType == null)
                {
                    // For async arrow functions, wrap return type in Promise if not already
                    if (arrow.IsAsync && exprType is not TypeInfo.Promise)
                    {
                        returnType = new TypeInfo.Promise(exprType);
                    }
                    else
                    {
                        returnType = exprType;
                    }
                }
                else
                {
                    // For async arrow functions, the return type is Promise<T> but we can return T directly
                    TypeInfo expectedType = returnType;
                    if (arrow.IsAsync && returnType is TypeInfo.Promise promiseType)
                    {
                        expectedType = promiseType.ValueType;
                    }

                    if (!IsCompatible(expectedType, exprType))
                    {
                        throw new Exception($"Type Error: Arrow function declared to return '{returnType}' but expression evaluates to '{exprType}'.");
                    }
                }
            }
            else if (arrow.BlockBody != null)
            {
                // Block body - check statements
                foreach (var stmt in arrow.BlockBody)
                {
                    CheckStmt(stmt);
                }
            }
        }
        finally
        {
            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
            _currentFunctionThisType = previousThisType;
            _inAsyncFunction = previousInAsync;
            _loopDepth = previousLoopDepth;
            _switchDepth = previousSwitchDepth;
            _activeLabels.Clear();
            foreach (var kvp in previousActiveLabels)
                _activeLabels[kvp.Key] = kvp.Value;
        }

        bool hasRest = arrow.Parameters.Any(p => p.IsRest);
        return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType);
    }

    private TypeInfo CheckAssign(Expr.Assign assign)
    {
        TypeInfo varType = LookupVariable(assign.Name);
        TypeInfo valueType = CheckExpr(assign.Value);

        if (!IsCompatible(varType, valueType))
        {
            throw new Exception($"Type Error: Cannot assign type '{valueType}' to variable '{assign.Name.Lexeme}' of type '{varType}'.");
        }
        return valueType;
    }

    private TypeInfo LookupVariable(Token name)
    {
        if (name.Lexeme == "console") return new TypeInfo.Any();
        if (name.Lexeme == "Math") return new TypeInfo.Any(); // Math is a special global object
        if (name.Lexeme == "Object") return new TypeInfo.Any(); // Object is a special global object
        if (name.Lexeme == "Array") return new TypeInfo.Any(); // Array is a special global object
        if (name.Lexeme == "JSON") return new TypeInfo.Any(); // JSON is a special global object
        if (name.Lexeme == "Promise") return new TypeInfo.Any(); // Promise is a special global object
        if (name.Lexeme == "Number") return new TypeInfo.Any(); // Number is a special global object
        if (name.Lexeme == "Symbol") return new TypeInfo.Any(); // Symbol is a special global object
        if (name.Lexeme == "parseInt") return new TypeInfo.Any(); // Global parseInt function
        if (name.Lexeme == "parseFloat") return new TypeInfo.Any(); // Global parseFloat function
        if (name.Lexeme == "isNaN") return new TypeInfo.Any(); // Global isNaN function
        if (name.Lexeme == "isFinite") return new TypeInfo.Any(); // Global isFinite function

        var type = _environment.Get(name.Lexeme);
        if (type == null)
        {
             throw new Exception($"Type Error: Undefined variable '{name.Lexeme}'.");
        }
        return type;
    }

    private TypeInfo GetLiteralType(object? value)
    {
        if (value is null) return new TypeInfo.Null();
        if (value is int i) return new TypeInfo.NumberLiteral((double)i);
        if (value is double d) return new TypeInfo.NumberLiteral(d);
        if (value is string s) return new TypeInfo.StringLiteral(s);
        if (value is bool b) return new TypeInfo.BooleanLiteral(b);
        if (value is System.Numerics.BigInteger) return new TypeInfo.BigInt();
        return new TypeInfo.Void();
    }
}
