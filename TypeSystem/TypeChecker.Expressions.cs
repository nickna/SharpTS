using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

/// <summary>
/// Expression type checking - CheckExpr dispatch and basic expression handlers.
/// </summary>
/// <remarks>
/// Contains the main expression dispatch (CheckExpr) via <see cref="IExprVisitor{TResult}"/>
/// and handlers for: literals, arrays, objects, templates, spread, arrow functions, assign,
/// type assertions, and basic helper methods (LookupVariable, GetLiteralType).
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Type-checks an expression and returns its resolved type.
    /// Dispatches to the appropriate Visit* method via <see cref="Expr.Accept{TResult}"/>.
    /// </summary>
    /// <param name="expr">The expression AST node to type-check.</param>
    /// <returns>The resolved TypeInfo for the expression.</returns>
    private TypeInfo CheckExpr(Expr expr)
    {
        TypeInfo result = Expr.Accept(expr, this);
        _typeMap.Set(expr, result);
        return result;
    }

    // IExprVisitor<TypeInfo> implementation - dispatched via Expr.Accept
    // Simple expressions are implemented inline, complex ones delegate to Check* methods

    public TypeInfo VisitLiteral(Expr.Literal expr) => GetLiteralType(expr.Value);
    public TypeInfo VisitVariable(Expr.Variable expr) => LookupVariable(expr.Name);
    public TypeInfo VisitGrouping(Expr.Grouping expr) => CheckExpr(expr.Expression);
    public TypeInfo VisitRegexLiteral(Expr.RegexLiteral expr) => new TypeInfo.RegExp();
    public TypeInfo VisitAwait(Expr.Await expr) => CheckAwait(expr);
    public TypeInfo VisitDynamicImport(Expr.DynamicImport expr) => CheckDynamicImport(expr);
    public TypeInfo VisitImportMeta(Expr.ImportMeta expr) => CheckImportMeta(expr);
    public TypeInfo VisitYield(Expr.Yield expr) => CheckYield(expr);
    public TypeInfo VisitTypeAssertion(Expr.TypeAssertion expr) => CheckTypeAssertion(expr);
    public TypeInfo VisitSatisfies(Expr.Satisfies expr) => CheckSatisfies(expr);
    public TypeInfo VisitNonNullAssertion(Expr.NonNullAssertion expr) => CheckNonNullAssertion(expr);
    public TypeInfo VisitTemplateLiteral(Expr.TemplateLiteral expr) => CheckTemplateLiteral(expr);
    public TypeInfo VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr) => CheckTaggedTemplateLiteral(expr);
    public TypeInfo VisitObjectLiteral(Expr.ObjectLiteral expr) => CheckObject(expr);
    public TypeInfo VisitArrayLiteral(Expr.ArrayLiteral expr) => CheckArray(expr);
    public TypeInfo VisitSpread(Expr.Spread expr) => CheckSpread(expr);
    public TypeInfo VisitArrowFunction(Expr.ArrowFunction expr) => CheckArrowFunction(expr);
    public TypeInfo VisitAssign(Expr.Assign expr) => CheckAssign(expr);
    public TypeInfo VisitClassExpr(Expr.ClassExpr expr) => CheckClassExpression(expr);

    // The following Visit* methods delegate to Check* methods in other partial files
    // (TypeChecker.Properties.cs, TypeChecker.Operators.cs, TypeChecker.Calls.cs)

    // Binary/logical operators (TypeChecker.Operators.cs)
    public TypeInfo VisitBinary(Expr.Binary expr) => CheckBinary(expr);
    public TypeInfo VisitLogical(Expr.Logical expr) => CheckLogical(expr);
    public TypeInfo VisitNullishCoalescing(Expr.NullishCoalescing expr) => CheckNullishCoalescing(expr);
    public TypeInfo VisitTernary(Expr.Ternary expr) => CheckTernary(expr);
    public TypeInfo VisitUnary(Expr.Unary expr) => CheckUnary(expr);
    public TypeInfo VisitDelete(Expr.Delete expr) => CheckDelete(expr);

    // Compound assignment operators (TypeChecker.Operators.cs)
    public TypeInfo VisitCompoundAssign(Expr.CompoundAssign expr) => CheckCompoundAssign(expr);
    public TypeInfo VisitCompoundSet(Expr.CompoundSet expr) => CheckCompoundSet(expr);
    public TypeInfo VisitCompoundSetIndex(Expr.CompoundSetIndex expr) => CheckCompoundSetIndex(expr);
    public TypeInfo VisitLogicalAssign(Expr.LogicalAssign expr) => CheckLogicalAssign(expr);
    public TypeInfo VisitLogicalSet(Expr.LogicalSet expr) => CheckLogicalSet(expr);
    public TypeInfo VisitLogicalSetIndex(Expr.LogicalSetIndex expr) => CheckLogicalSetIndex(expr);
    public TypeInfo VisitPrefixIncrement(Expr.PrefixIncrement expr) => CheckPrefixIncrement(expr);
    public TypeInfo VisitPostfixIncrement(Expr.PostfixIncrement expr) => CheckPostfixIncrement(expr);

    // Function calls (TypeChecker.Calls.cs)
    public TypeInfo VisitCall(Expr.Call expr) => CheckCall(expr);

    // Property access (TypeChecker.Properties.cs)
    public TypeInfo VisitGet(Expr.Get expr) => CheckGet(expr);
    public TypeInfo VisitSet(Expr.Set expr) => CheckSet(expr);
    public TypeInfo VisitGetPrivate(Expr.GetPrivate expr) => CheckGetPrivate(expr);
    public TypeInfo VisitSetPrivate(Expr.SetPrivate expr) => CheckSetPrivate(expr);
    public TypeInfo VisitCallPrivate(Expr.CallPrivate expr) => CheckCallPrivate(expr);
    public TypeInfo VisitThis(Expr.This expr) => CheckThis(expr);
    public TypeInfo VisitNew(Expr.New expr) => CheckNew(expr);
    public TypeInfo VisitSuper(Expr.Super expr) => CheckSuper(expr);

    // Index access (TypeChecker.Properties.Index.cs)
    public TypeInfo VisitGetIndex(Expr.GetIndex expr) => CheckGetIndex(expr);
    public TypeInfo VisitSetIndex(Expr.SetIndex expr) => CheckSetIndex(expr);

    private TypeInfo CheckAwait(Expr.Await awaitExpr)
    {
        if (!_inAsyncFunction)
        {
            throw new TypeCheckException(" 'await' is only valid inside an async function.");
        }

        TypeInfo exprType = CheckExpr(awaitExpr.Expression);
        return ResolveAwaitedType(exprType);
    }

    private TypeInfo CheckImportMeta(Expr.ImportMeta im)
    {
        // import.meta is an object with 'url', 'dirname', and 'filename' properties
        return new TypeInfo.Record(
            new Dictionary<string, TypeInfo>
            {
                ["url"] = new TypeInfo.String(),
                ["dirname"] = new TypeInfo.String(),
                ["filename"] = new TypeInfo.String()
            }.ToFrozenDictionary()
        );
    }

    private TypeInfo CheckDynamicImport(Expr.DynamicImport di)
    {
        TypeInfo pathType = CheckExpr(di.PathExpression);

        // Path must be string, string literal, or any
        bool isValidPath = pathType is TypeInfo.String
                        || pathType is TypeInfo.StringLiteral
                        || pathType is TypeInfo.Any;

        if (!isValidPath)
        {
            throw new TypeCheckException($" Dynamic import path must be a string, got '{pathType}'.");
        }

        // For string literal paths, try to resolve the module and return Promise<Module>
        if (pathType is TypeInfo.StringLiteral literal)
        {
            // Track this path for module discovery (even if resolution fails)
            _dynamicImportPaths.Add(literal.Value);

            // Try to resolve the module and get its exports
            if (_moduleResolver != null && _currentModule != null)
            {
                try
                {
                    string resolvedPath = _moduleResolver.ResolveModulePath(literal.Value, _currentModule.Path);
                    var targetModule = _moduleResolver.GetCachedModule(resolvedPath);

                    if (targetModule != null && targetModule.IsTypeChecked)
                    {
                        // Build module namespace type from exports
                        var moduleType = new TypeInfo.Module(
                            resolvedPath,
                            targetModule.ExportedTypes.ToFrozenDictionary(),
                            targetModule.DefaultExportType
                        );
                        return new TypeInfo.Promise(moduleType);
                    }
                }
                catch
                {
                    // Module resolution failed - fall through to Promise<any>
                }
            }
        }

        // Variable paths or unresolved modules: Promise<any>
        return new TypeInfo.Promise(new TypeInfo.Any());
    }

    private TypeInfo CheckYield(Expr.Yield yieldExpr)
    {
        if (!_inGeneratorFunction)
        {
            throw new TypeCheckException(" 'yield' is only valid inside a generator function.");
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
        TypeInfo.AsyncGenerator asyncGen => asyncGen.YieldType,
        TypeInfo.Iterator iter => iter.ElementType,
        TypeInfo.Set set => set.ElementType,
        TypeInfo.Map map => TypeInfo.Tuple.FromTypes([map.KeyType, map.ValueType], 2),  // [K, V] tuples
        TypeInfo.String => new TypeInfo.String(),  // String yields characters (as strings)
        TypeInfo.StringLiteral => new TypeInfo.String(),  // String literal also yields characters
        TypeInfo.Any => new TypeInfo.Any(),
        _ => throw new TypeCheckException($" Type '{type}' is not iterable for yield*.")
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

        // Handle 'as const' - deep readonly inference with literal types
        if (ta.TargetType == "const")
        {
            return InferConstType(ta.Expression, sourceType);
        }

        TypeInfo targetType = ToTypeInfo(ta.TargetType);

        // Allow any <-> anything (escape hatch)
        if (sourceType is TypeInfo.Any || targetType is TypeInfo.Any)
            return targetType;

        // Check if types are related (either direction)
        if (IsCompatible(targetType, sourceType) || IsCompatible(sourceType, targetType))
            return targetType;

        throw new TypeCheckException($" Cannot assert type '{sourceType}' to '{targetType}'.");
    }

    private TypeInfo CheckSatisfies(Expr.Satisfies sat)
    {
        TypeInfo inferredType = CheckExpr(sat.Expression);
        TypeInfo constraintType = ToTypeInfo(sat.ConstraintType);

        // Escape hatches - any/unknown constraints always pass
        if (constraintType is TypeInfo.Any or TypeInfo.Unknown)
            return inferredType;

        // any value satisfies any constraint
        if (inferredType is TypeInfo.Any)
            return inferredType;

        // One-way validation: inferred must be assignable TO constraint
        if (!IsCompatible(constraintType, inferredType))
        {
            throw new TypeCheckException(
                $"Type '{inferredType}' does not satisfy constraint '{constraintType}'.");
        }

        // Key difference from 'as': return the inferred type, not the constraint type
        return inferredType;
    }

    /// <summary>
    /// Infers the const type for an expression, recursively converting:
    /// - Array literals to tuples with literal element types
    /// - Object literals to records with literal property types
    /// - Primitive literals to their literal types (string literal, number literal, etc.)
    /// </summary>
    private TypeInfo InferConstType(Expr expr, TypeInfo sourceType)
    {
        return expr switch
        {
            Expr.ArrayLiteral arr => InferConstArrayType(arr),
            Expr.ObjectLiteral obj => InferConstObjectType(obj),
            Expr.Literal lit => InferConstLiteralType(lit.Value),
            _ => sourceType // Variables and other expressions keep their inferred type
        };
    }

    /// <summary>
    /// Converts an array literal to a tuple type with literal element types.
    /// </summary>
    private TypeInfo InferConstArrayType(Expr.ArrayLiteral arr)
    {
        var elementTypes = arr.Elements
            .Select(e => InferConstType(e, CheckExpr(e)))
            .ToList();
        return TypeInfo.Tuple.FromTypes(elementTypes, elementTypes.Count);
    }

    /// <summary>
    /// Converts an object literal to a record type with literal property types.
    /// </summary>
    private TypeInfo InferConstObjectType(Expr.ObjectLiteral obj)
    {
        var fields = new Dictionary<string, TypeInfo>();
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                // For spread properties, merge the spread type
                var spreadType = InferConstType(prop.Value, CheckExpr(prop.Value));
                if (spreadType is TypeInfo.Record rec)
                {
                    foreach (var (k, v) in rec.Fields)
                        fields[k] = v;
                }
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter || prop.Kind == Expr.ObjectPropertyKind.Setter)
            {
                // For getters/setters, extract the property type from the function
                string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                TypeInfo fnType = CheckExpr(prop.Value);
                if (prop.Kind == Expr.ObjectPropertyKind.Getter && fnType is TypeInfo.Function fn)
                {
                    fields[name] = fn.ReturnType;
                }
                else if (prop.Kind == Expr.ObjectPropertyKind.Setter && fnType is TypeInfo.Function setterFn && setterFn.ParamTypes.Count > 0)
                {
                    // Only set if not already defined by a getter
                    if (!fields.ContainsKey(name))
                    {
                        fields[name] = setterFn.ParamTypes[0];
                    }
                }
            }
            else if (prop.Key is Expr.IdentifierKey ik)
            {
                fields[ik.Name.Lexeme] = InferConstType(prop.Value, CheckExpr(prop.Value));
            }
            else if (prop.Key is Expr.LiteralKey lk && lk.Literal.Literal is string strKey)
            {
                fields[strKey] = InferConstType(prop.Value, CheckExpr(prop.Value));
            }
            // Computed keys are handled dynamically - use the source type
        }
        return new TypeInfo.Record(fields.ToFrozenDictionary());
    }

    /// <summary>
    /// Converts a literal value to its corresponding literal type.
    /// </summary>
    private static TypeInfo InferConstLiteralType(object? value)
    {
        return value switch
        {
            string s => new TypeInfo.StringLiteral(s),
            double d => new TypeInfo.NumberLiteral(d),
            int i => new TypeInfo.NumberLiteral(i),
            bool b => new TypeInfo.BooleanLiteral(b),
            null => new TypeInfo.Null(),
            Runtime.Types.SharpTSUndefined => new TypeInfo.Undefined(),
            _ => new TypeInfo.Any()
        };
    }

    private TypeInfo CheckTemplateLiteral(Expr.TemplateLiteral template)
    {
        // Type check all interpolated expressions (any type is allowed)
        foreach (var expr in template.Expressions)
        {
            CheckExpr(expr);
        }
        // Template literals always result in string
        return new TypeInfo.String();
    }

    private TypeInfo CheckTaggedTemplateLiteral(Expr.TaggedTemplateLiteral tagged)
    {
        TypeInfo tagType = CheckExpr(tagged.Tag);

        // Check all interpolated expressions
        foreach (var expr in tagged.Expressions)
            CheckExpr(expr);

        // Tag must be callable - return its return type, or any if uncertain
        return tagType switch
        {
            TypeInfo.Function f => f.ReturnType,
            TypeInfo.OverloadedFunction of => of.Implementation.ReturnType,
            TypeInfo.GenericFunction gf => gf.ReturnType,
            TypeInfo.Any => new TypeInfo.Any(),
            TypeInfo.Class => new TypeInfo.Any(), // constructors are callable but shouldn't be used as tag
            _ => throw new TypeCheckException(
                $"Type Error: Tagged template tag must be callable, got '{tagType}'.")
        };
    }

    private TypeInfo CheckObject(Expr.ObjectLiteral obj)
    {
        Dictionary<string, TypeInfo> fields = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        // Track accessor properties for two-pass type inference
        List<Expr.Property> accessorProps = [];
        bool hasAccessors = false;

        // Pass 1: Collect property types without checking accessor bodies
        // For accessors, use type annotations only (don't check bodies yet)
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
                    throw new TypeCheckException($" Spread in object literal requires an object, got '{spreadType}'.");
                }
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter)
            {
                hasAccessors = true;
                accessorProps.Add(prop);

                // Getter - extract return type from annotation only (don't check body yet)
                string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                if (prop.Value is Expr.ArrowFunction arrow && arrow.ReturnType != null)
                {
                    fields[name] = ToTypeInfo(arrow.ReturnType);
                }
                else
                {
                    // No return type annotation - use Any for now (will be inferred on pass 2)
                    fields[name] = new TypeInfo.Any();
                }
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Setter)
            {
                hasAccessors = true;
                accessorProps.Add(prop);

                // Setter - extract parameter type from annotation only
                string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                if (prop.Value is Expr.ArrowFunction arrow && arrow.Parameters.Count > 0 && arrow.Parameters[0].Type != null)
                {
                    // If getter already defined the type, verify compatibility
                    if (!fields.ContainsKey(name))
                    {
                        fields[name] = ToTypeInfo(arrow.Parameters[0].Type!);
                    }
                }
                else if (!fields.ContainsKey(name))
                {
                    // No type annotation - use Any for now
                    fields[name] = new TypeInfo.Any();
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
                        if (keyType is TypeInfo.String)
                            stringIndexType = UnifyIndexTypes(stringIndexType, valueType);
                        else if (keyType is TypeInfo.Primitive n && n.Type == TokenType.TYPE_NUMBER)
                            numberIndexType = UnifyIndexTypes(numberIndexType, valueType);
                        else if (keyType is TypeInfo.Symbol or TypeInfo.UniqueSymbol)
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
                            throw new TypeCheckException($" Computed property key must be string, number, or symbol, got '{keyType}'.");
                        break;
                }
            }
        }

        // Pass 2: If there are accessors, build the object type and re-check accessor bodies with proper 'this'
        if (hasAccessors)
        {
            // Widen literal types for 'this' inference (e.g., 0 -> number, "test" -> string)
            var widenedFields = fields.ToDictionary(
                kv => kv.Key,
                kv => WidenLiteralType(kv.Value)
            );

            // Build the object type for 'this' inference
            var objectType = new TypeInfo.Record(
                widenedFields.ToFrozenDictionary(),
                stringIndexType != null ? WidenLiteralType(stringIndexType) : null,
                numberIndexType != null ? WidenLiteralType(numberIndexType) : null,
                symbolIndexType != null ? WidenLiteralType(symbolIndexType) : null
            );

            // Set contextual 'this' type for accessor bodies
            var previousPendingThis = _pendingObjectThisType;
            _pendingObjectThisType = objectType;

            try
            {
                // Re-check accessor bodies with proper 'this' type
                foreach (var prop in accessorProps)
                {
                    if (prop.Kind == Expr.ObjectPropertyKind.Getter)
                    {
                        string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                        TypeInfo getterType = CheckExpr(prop.Value);

                        // Update the field type with the actual inferred type
                        if (getterType is TypeInfo.Function fn)
                        {
                            fields[name] = fn.ReturnType;
                        }
                        else
                        {
                            fields[name] = getterType;
                        }
                    }
                    else if (prop.Kind == Expr.ObjectPropertyKind.Setter)
                    {
                        string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                        TypeInfo setterType = CheckExpr(prop.Value);

                        // Setter - extract the parameter type (or merge with existing getter type)
                        if (setterType is TypeInfo.Function fn && fn.ParamTypes.Count > 0)
                        {
                            // If getter already defined the type, verify compatibility
                            if (!fields.ContainsKey(name))
                            {
                                fields[name] = fn.ParamTypes[0];
                            }
                        }
                    }
                }
            }
            finally
            {
                _pendingObjectThisType = previousPendingThis;
            }
        }

        return new TypeInfo.Record(fields.ToFrozenDictionary(), stringIndexType, numberIndexType, symbolIndexType);
    }

    /// <summary>
    /// Gets the string name from a property key for type checking.
    /// </summary>
    private static string GetPropertyKeyNameForTypeCheck(Expr.PropertyKey key)
    {
        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            Expr.ComputedKey => "[computed]", // Computed keys need special handling at runtime
            _ => throw new TypeCheckException(" Invalid property key for accessor.")
        };
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

    /// <summary>
    /// Widens literal types to their base types for 'this' type inference.
    /// E.g., 0 -> number, "test" -> string, true -> boolean
    /// </summary>
    private static TypeInfo WidenLiteralType(TypeInfo type)
    {
        return type switch
        {
            TypeInfo.NumberLiteral => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
            TypeInfo.StringLiteral => new TypeInfo.String(),
            TypeInfo.BooleanLiteral => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            TypeInfo.Union u => new TypeInfo.Union(u.FlattenedTypes.Select(WidenLiteralType).ToList()),
            TypeInfo.Array arr => new TypeInfo.Array(WidenLiteralType(arr.ElementType)),
            TypeInfo.Record rec => new TypeInfo.Record(
                rec.Fields.ToDictionary(kv => kv.Key, kv => WidenLiteralType(kv.Value)).ToFrozenDictionary(),
                rec.StringIndexType != null ? WidenLiteralType(rec.StringIndexType) : null,
                rec.NumberIndexType != null ? WidenLiteralType(rec.NumberIndexType) : null,
                rec.SymbolIndexType != null ? WidenLiteralType(rec.SymbolIndexType) : null
            ),
            _ => type
        };
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
                    throw new TypeCheckException($" Spread expression must be an array or tuple, got '{spreadType}'.");
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

        // Check minimum element count
        if (elemCount < requiredCount)
        {
            throw new TypeCheckException($" Tuple requires at least {requiredCount} elements, but got {elemCount} for variable '{varName}'.");
        }

        // Check maximum element count (only for fixed tuples without spread or rest)
        if (tupleType.RestElementType == null && !tupleType.HasSpread && elemCount > tupleType.Elements.Count)
        {
            throw new TypeCheckException($" Tuple expects at most {tupleType.Elements.Count} elements, but got {elemCount} for variable '{varName}'.");
        }

        // Use variadic tuple logic if the tuple has a spread element
        if (tupleType.HasSpread)
        {
            CheckArrayLiteralAgainstVariadicTuple(arrayLit, tupleType, varName);
        }
        else
        {
            CheckArrayLiteralAgainstFixedTuple(arrayLit, tupleType, varName);
        }
    }

    /// <summary>
    /// Checks an array literal against a fixed (non-variadic) tuple type.
    /// </summary>
    private void CheckArrayLiteralAgainstFixedTuple(Expr.ArrayLiteral arrayLit, TypeInfo.Tuple tupleType, string varName)
    {
        int elemCount = arrayLit.Elements.Count;

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
                throw new TypeCheckException($" Tuple index {i} is out of bounds for variable '{varName}'.");
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
                    throw new TypeCheckException($" Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.");
                }
            }
        }
    }

    /// <summary>
    /// Checks an array literal against a variadic tuple type with positional spread matching.
    /// Handles patterns like [E, ...T] or [...T, E] or [A, ...T, B].
    /// </summary>
    private void CheckArrayLiteralAgainstVariadicTuple(Expr.ArrayLiteral arrayLit, TypeInfo.Tuple tupleType, string varName)
    {
        // Find the spread position
        int spreadIdx = tupleType.Elements.FindIndex(e => e.Kind == TupleElementKind.Spread);
        if (spreadIdx < 0)
        {
            // No spread found - shouldn't happen since HasSpread was true, but fallback to fixed tuple logic
            CheckArrayLiteralAgainstFixedTuple(arrayLit, tupleType, varName);
            return;
        }

        var spreadElem = tupleType.Elements[spreadIdx];
        int leadingCount = spreadIdx;
        int trailingCount = tupleType.Elements.Count - spreadIdx - 1;
        int spreadCount = arrayLit.Elements.Count - leadingCount - trailingCount;

        if (spreadCount < 0)
        {
            throw new TypeCheckException($" Not enough elements for variadic tuple: expected at least {leadingCount + trailingCount} elements, got {arrayLit.Elements.Count} for variable '{varName}'.");
        }

        // Check leading elements (before spread)
        for (int i = 0; i < leadingCount; i++)
        {
            var element = arrayLit.Elements[i];
            var expectedType = tupleType.Elements[i].Type;

            if (expectedType is TypeInfo.Tuple nestedTuple && element is Expr.ArrayLiteral nestedArrayLit)
            {
                CheckArrayLiteralAgainstTuple(nestedArrayLit, nestedTuple, $"{varName}[{i}]");
            }
            else
            {
                TypeInfo elemType = CheckExpr(element);
                if (!IsCompatible(expectedType, elemType))
                {
                    throw new TypeCheckException($" Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.");
                }
            }
        }

        // Check spread elements (middle)
        // Get the inner type of the spread element (if it's an array, use element type; otherwise use the type directly)
        TypeInfo spreadInnerType = spreadElem.Type is TypeInfo.Array arr ? arr.ElementType : spreadElem.Type;
        for (int i = 0; i < spreadCount; i++)
        {
            int arrIdx = leadingCount + i;
            var element = arrayLit.Elements[arrIdx];

            if (spreadInnerType is TypeInfo.Tuple nestedTuple && element is Expr.ArrayLiteral nestedArrayLit)
            {
                CheckArrayLiteralAgainstTuple(nestedArrayLit, nestedTuple, $"{varName}[{arrIdx}]");
            }
            else
            {
                TypeInfo elemType = CheckExpr(element);
                if (!IsCompatible(spreadInnerType, elemType))
                {
                    throw new TypeCheckException($" Element at index {arrIdx} has type '{elemType}' but expected '{spreadInnerType}' for variable '{varName}'.");
                }
            }
        }

        // Check trailing elements (after spread)
        for (int i = 0; i < trailingCount; i++)
        {
            int arrIdx = arrayLit.Elements.Count - trailingCount + i;
            int tupleIdx = tupleType.Elements.Count - trailingCount + i;
            var element = arrayLit.Elements[arrIdx];
            var expectedType = tupleType.Elements[tupleIdx].Type;

            if (expectedType is TypeInfo.Tuple nestedTuple && element is Expr.ArrayLiteral nestedArrayLit)
            {
                CheckArrayLiteralAgainstTuple(nestedArrayLit, nestedTuple, $"{varName}[{arrIdx}]");
            }
            else
            {
                TypeInfo elemType = CheckExpr(element);
                if (!IsCompatible(expectedType, elemType))
                {
                    throw new TypeCheckException($" Element at index {arrIdx} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.");
                }
            }
        }
    }

    private TypeInfo CheckArrowFunction(Expr.ArrowFunction arrow, TypeInfo? expectedType = null)
    {
        // Parse explicit 'this' type if present (for object literal method shorthand)
        // Note: Arrow function expressions shouldn't have 'this' parameter in standard TypeScript,
        // but we support it for object literal method shorthand which is parsed as ArrowFunction.
        TypeInfo? thisType = arrow.ThisType != null ? ToTypeInfo(arrow.ThisType) : null;

        // For function expressions and object method shorthand (HasOwnThis=true), allow 'this' even without explicit type annotation
        // TypeScript infers 'this' as the containing object type - use _pendingObjectThisType if available
        if (arrow.HasOwnThis && thisType == null)
        {
            thisType = _pendingObjectThisType ?? new TypeInfo.Any();
        }

        // Extract expected function type for parameter inference
        TypeInfo.Function? expectedFuncType = expectedType switch
        {
            TypeInfo.Function f => f,
            TypeInfo.GenericFunction gf => new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType),
            _ => null
        };

        // Build parameter types and check defaults
        List<TypeInfo> paramTypes = [];
        int requiredParams = 0;
        bool seenDefault = false;

        for (int i = 0; i < arrow.Parameters.Count; i++)
        {
            var param = arrow.Parameters[i];
            TypeInfo paramType;

            if (param.Type != null)
            {
                // Explicit type annotation - use it
                paramType = ToTypeInfo(param.Type);
            }
            else if (expectedFuncType != null && i < expectedFuncType.ParamTypes.Count)
            {
                // Infer from expected type
                paramType = expectedFuncType.ParamTypes[i];
            }
            else
            {
                // No type annotation and no expected type - use Any
                paramType = new TypeInfo.Any();
            }
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
                    throw new TypeCheckException($" Default value type '{defaultType}' is not assignable to parameter type '{paramType}'.");
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
                    throw new TypeCheckException($" Required parameter cannot follow optional parameter.");
                }
                requiredParams++;
            }
        }

        // Determine return type (use expected type if available and no explicit annotation)
        TypeInfo returnType;
        if (arrow.ReturnType != null)
        {
            returnType = ToTypeInfo(arrow.ReturnType);
        }
        else if (expectedFuncType != null)
        {
            returnType = expectedFuncType.ReturnType;
        }
        else
        {
            returnType = new TypeInfo.Any();
        }

        // Build the function type (needed for named function expressions self-reference)
        bool hasRest = arrow.Parameters.Any(p => p.IsRest);
        var funcType = new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType);

        // Create new environment for function body
        TypeEnvironment arrowEnv = new(_environment);

        // For named function expressions, add the function name to the inner scope
        // This enables recursion: const f = function myFunc(n) { return myFunc(n-1); }
        // Note: Parameters can shadow the function name, so define name first
        if (arrow.Name != null)
        {
            arrowEnv.Define(arrow.Name.Lexeme, funcType);
            arrowEnv.MarkAsConst(arrow.Name.Lexeme);  // Function name is read-only in strict mode
        }

        // Define parameters (may shadow function name if same identifier)
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
                    TypeInfo expectedRetType = returnType;
                    if (arrow.IsAsync && returnType is TypeInfo.Promise promiseType)
                    {
                        expectedRetType = promiseType.ValueType;
                    }

                    if (!IsCompatible(expectedRetType, exprType))
                    {
                        throw new TypeCheckException($" Arrow function declared to return '{returnType}' but expression evaluates to '{exprType}'.");
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

        List<string> paramNames = arrow.Parameters.Select(p => p.Name.Lexeme).ToList();
        return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
    }

    private TypeInfo CheckAssign(Expr.Assign assign)
    {
        TypeInfo varType = LookupVariable(assign.Name);
        TypeInfo valueType = CheckExpr(assign.Value);

        if (!IsCompatible(varType, valueType))
        {
            throw new TypeCheckException($" Cannot assign type '{valueType}' to variable '{assign.Name.Lexeme}' of type '{varType}'.");
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
        if (name.Lexeme == "String") return new TypeInfo.Any(); // String is a special global object
        if (name.Lexeme == "Symbol") return new TypeInfo.Any(); // Symbol is a special global object
        if (name.Lexeme == "Buffer") return new TypeInfo.Any(); // Buffer is a global constructor for binary data
        if (name.Lexeme == "parseInt") return new TypeInfo.Any(); // Global parseInt function
        if (name.Lexeme == "parseFloat") return new TypeInfo.Any(); // Global parseFloat function
        if (name.Lexeme == "isNaN") return new TypeInfo.Any(); // Global isNaN function
        if (name.Lexeme == "isFinite") return new TypeInfo.Any(); // Global isFinite function
        if (name.Lexeme == "globalThis") return new TypeInfo.Any(); // globalThis ES2020
        if (name.Lexeme == "undefined") return new TypeInfo.Undefined(); // Global undefined
        if (name.Lexeme == "NaN") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER); // Global NaN
        if (name.Lexeme == "Infinity") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER); // Global Infinity
        if (name.Lexeme == "__dirname") return new TypeInfo.Primitive(TokenType.TYPE_STRING); // Node.js __dirname
        if (name.Lexeme == "__filename") return new TypeInfo.Primitive(TokenType.TYPE_STRING); // Node.js __filename

        var type = _environment.Get(name.Lexeme);
        if (type == null)
        {
             throw new TypeCheckException($" Undefined variable '{name.Lexeme}'.");
        }
        return type;
    }

    private TypeInfo GetLiteralType(object? value)
    {
        if (value is null) return new TypeInfo.Null();
        if (value is Runtime.Types.SharpTSUndefined) return new TypeInfo.Undefined();
        if (value is int i) return new TypeInfo.NumberLiteral((double)i);
        if (value is double d) return new TypeInfo.NumberLiteral(d);
        if (value is string s) return new TypeInfo.StringLiteral(s);
        if (value is bool b) return new TypeInfo.BooleanLiteral(b);
        if (value is System.Numerics.BigInteger) return new TypeInfo.BigInt();
        return new TypeInfo.Void();
    }

    // Counter for generating unique anonymous class expression names
    private int _classExprCounter = 0;

    /// <summary>
    /// Type checks a class expression and returns the class type.
    /// Unlike class declarations, the class is not added to the outer environment.
    /// </summary>
    private TypeInfo CheckClassExpression(Expr.ClassExpr classExpr)
    {
        // Generate name for anonymous classes
        string className = classExpr.Name?.Lexeme ?? $"$ClassExpr_{++_classExprCounter}";

        // Resolve superclass if present
        TypeInfo.Class? superclass = null;
        if (classExpr.Superclass != null)
        {
            TypeInfo superType = LookupVariable(classExpr.Superclass);
            if (superType is TypeInfo.Instance si && si.ClassType is TypeInfo.Class sic)
                superclass = sic;
            else if (superType is TypeInfo.Class sc)
                superclass = sc;
            else
                throw new TypeCheckException("Superclass must be a class");
        }

        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? classTypeParams = null;
        TypeEnvironment classTypeEnv = new(_environment);
        if (classExpr.TypeParams != null && classExpr.TypeParams.Count > 0)
        {
            classTypeParams = [];
            foreach (var tp in classExpr.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                classTypeParams.Add(typeParam);
                classTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Create mutable class early so self-references work
        var mutableClass = new TypeInfo.MutableClass(className)
        {
            Superclass = superclass,
            IsAbstract = classExpr.IsAbstract
        };

        // If named, define the name in class body scope for self-reference
        if (classExpr.Name != null)
        {
            classTypeEnv.Define(classExpr.Name.Lexeme, mutableClass);
        }

        using (new EnvironmentScope(this, classTypeEnv))
        {
            // Helper to build a TypeInfo.Function from a method declaration
            TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
            {
                var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
                    method.Parameters,
                    validateDefaults: true,
                    contextName: $"method '{method.Name.Lexeme}'"
                );

                TypeInfo returnType = method.ReturnType != null
                    ? ToTypeInfo(method.ReturnType)
                    : new TypeInfo.Void();

                return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, null, paramNames);
            }

            // Collect method signatures
            var methodGroups = classExpr.Methods.GroupBy(m => m.Name.Lexeme).ToList();
            foreach (var group in methodGroups)
            {
                string methodName = group.Key;
                var methods = group.ToList();

                var signatures = methods.Where(m => m.Body == null && !m.IsAbstract).ToList();
                var implementations = methods.Where(m => m.Body != null).ToList();

                if (signatures.Count > 0)
                {
                    if (implementations.Count == 0)
                        throw new TypeCheckException($" Overloaded method '{methodName}' has no implementation.");
                    if (implementations.Count > 1)
                        throw new TypeCheckException($" Overloaded method '{methodName}' has multiple implementations.");

                    var implementation = implementations[0];
                    var signatureTypes = signatures.Select(BuildMethodFuncType).ToList();
                    var implType = BuildMethodFuncType(implementation);

                    foreach (var sig in signatureTypes)
                    {
                        if (implType.MinArity > sig.MinArity)
                            throw new TypeCheckException($" Implementation of '{methodName}' requires {implType.MinArity} arguments but overload signature requires only {sig.MinArity}.");
                    }

                    var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, implType);
                    if (implementation.IsStatic)
                        mutableClass.StaticMethods[methodName] = overloadedFunc;
                    else
                        mutableClass.Methods[methodName] = overloadedFunc;
                    mutableClass.MethodAccess[methodName] = implementation.Access;
                }
                else if (implementations.Count == 1)
                {
                    var method = implementations[0];
                    var funcType = BuildMethodFuncType(method);
                    if (method.IsStatic)
                        mutableClass.StaticMethods[methodName] = funcType;
                    else
                        mutableClass.Methods[methodName] = funcType;
                    mutableClass.MethodAccess[methodName] = method.Access;
                }
                else if (implementations.Count > 1)
                {
                    throw new TypeCheckException($" Multiple implementations of method '{methodName}' without overload signatures.");
                }
            }

            // Collect field types
            foreach (var field in classExpr.Fields)
            {
                string fieldName = field.Name.Lexeme;
                TypeInfo fieldType = field.TypeAnnotation != null
                    ? ToTypeInfo(field.TypeAnnotation)
                    : new TypeInfo.Any();

                if (field.IsStatic)
                    mutableClass.StaticProperties[fieldName] = fieldType;
                else
                    mutableClass.FieldTypes[fieldName] = fieldType;

                mutableClass.FieldAccess[fieldName] = field.Access;
                if (field.IsReadonly)
                    mutableClass.ReadonlyFields.Add(fieldName);
            }

            // Collect accessor types
            if (classExpr.Accessors != null)
            {
                foreach (var accessor in classExpr.Accessors)
                {
                    string propName = accessor.Name.Lexeme;
                    if (accessor.Kind.Type == TokenType.GET)
                    {
                        TypeInfo getterRetType = accessor.ReturnType != null
                            ? ToTypeInfo(accessor.ReturnType)
                            : new TypeInfo.Any();
                        mutableClass.Getters[propName] = getterRetType;
                    }
                    else
                    {
                        TypeInfo paramType = accessor.SetterParam?.Type != null
                            ? ToTypeInfo(accessor.SetterParam.Type)
                            : new TypeInfo.Any();
                        mutableClass.Setters[propName] = paramType;
                    }
                }

                // Validate getter/setter type compatibility
                foreach (var propName in mutableClass.Getters.Keys.Intersect(mutableClass.Setters.Keys))
                {
                    if (!IsCompatible(mutableClass.Getters[propName], mutableClass.Setters[propName]))
                        throw new TypeCheckException($" Getter and setter for '{propName}' have incompatible types.");
                }
            }
        }

        // Freeze the mutable class
        // For body checking, always use the frozen MutableClass (which is a TypeInfo.Class)
        // This matches CheckClassDeclaration: generic classes store GenericClass externally but use
        // the frozen MutableClass for body checking since it has the same methods/fields structure.
        TypeInfo.Class classTypeForBody;
        if (classTypeParams != null && classTypeParams.Count > 0)
        {
            var genericClassType = mutableClass.FreezeGeneric(classTypeParams);
            // Store for later lookups - don't add to outer environment
            // For body check, freeze the mutable class (methods/fields have TypeParameter types)
            classTypeForBody = mutableClass.Freeze();
            _typeMap.SetClassType(className, classTypeForBody);
            _typeMap.SetClassExprType(classExpr, classTypeForBody);
            // Class expression returns the GenericClass type
            _ = genericClassType; // Keep for potential future use (return type could be GenericClass)
        }
        else
        {
            TypeInfo.Class classType = mutableClass.Freeze();
            _typeMap.SetClassType(className, classType);
            _typeMap.SetClassExprType(classExpr, classType);
            classTypeForBody = classType;
        }

        // Validate interface implementations (skip for generic - validated at instantiation)
        if (classExpr.Interfaces != null && classTypeParams == null)
        {
            foreach (var interfaceToken in classExpr.Interfaces)
            {
                TypeInfo? itfTypeInfo = _environment.Get(interfaceToken.Lexeme);
                if (itfTypeInfo is not TypeInfo.Interface interfaceType)
                    throw new TypeCheckException($" '{interfaceToken.Lexeme}' is not an interface.");
                ValidateInterfaceImplementation(classTypeForBody, interfaceType, className);
            }
        }

        // Check method bodies
        TypeEnvironment classEnv = new(_environment);
        if (classTypeParams != null)
        {
            foreach (var tp in classTypeParams)
                classEnv.DefineTypeParameter(tp.Name, tp);
        }
        classEnv.Define("this", new TypeInfo.Instance(classTypeForBody));
        if (superclass != null)
            classEnv.Define("super", superclass);

        TypeEnvironment prevEnv = _environment;
        TypeInfo.Class? prevClass = _currentClass;
        _environment = classEnv;
        _currentClass = classTypeForBody is TypeInfo.Class c ? c : mutableClass.Freeze();

        try
        {
            foreach (var method in classExpr.Methods.Where(m => m.Body != null))
            {
                TypeEnvironment methodEnv;
                if (method.IsStatic)
                    methodEnv = new TypeEnvironment(prevEnv);
                else
                    methodEnv = new TypeEnvironment(_environment);

                var declaredMethodType = method.IsStatic
                    ? classTypeForBody.StaticMethods[method.Name.Lexeme]
                    : classTypeForBody.Methods[method.Name.Lexeme];

                TypeInfo.Function methodType = declaredMethodType switch
                {
                    TypeInfo.OverloadedFunction of => of.Implementation,
                    TypeInfo.Function f => f,
                    _ => throw new TypeCheckException($" Unexpected method type for '{method.Name.Lexeme}'.")
                };

                for (int i = 0; i < method.Parameters.Count; i++)
                    methodEnv.Define(method.Parameters[i].Name.Lexeme, methodType.ParamTypes[i]);

                TypeEnvironment previousEnvFunc = _environment;
                TypeInfo? previousReturnFunc = _currentFunctionReturnType;
                bool previousInStatic = _inStaticMethod;
                bool previousInAsyncFunc = _inAsyncFunction;
                bool previousInGeneratorFunc = _inGeneratorFunction;
                int previousLoopDepthFunc = _loopDepth;
                int previousSwitchDepthFunc = _switchDepth;
                var previousActiveLabelsFunc = new Dictionary<string, bool>(_activeLabels);

                _environment = methodEnv;
                _currentFunctionReturnType = methodType.ReturnType;
                _inStaticMethod = method.IsStatic;
                _inAsyncFunction = method.IsAsync;
                _inGeneratorFunction = method.IsGenerator;
                _loopDepth = 0;
                _switchDepth = 0;
                _activeLabels.Clear();

                try
                {
                    if (method.Body != null)
                    {
                        foreach (var bodyStmt in method.Body)
                            CheckStmt(bodyStmt);
                    }
                }
                finally
                {
                    _environment = previousEnvFunc;
                    _currentFunctionReturnType = previousReturnFunc;
                    _inStaticMethod = previousInStatic;
                    _inAsyncFunction = previousInAsyncFunc;
                    _inGeneratorFunction = previousInGeneratorFunc;
                    _loopDepth = previousLoopDepthFunc;
                    _switchDepth = previousSwitchDepthFunc;
                    _activeLabels.Clear();
                    foreach (var kvp in previousActiveLabelsFunc)
                        _activeLabels[kvp.Key] = kvp.Value;
                }
            }

            // Check accessor bodies
            if (classExpr.Accessors != null)
            {
                foreach (var accessor in classExpr.Accessors)
                {
                    TypeEnvironment accessorEnv = new TypeEnvironment(_environment);
                    TypeInfo accessorReturnType;

                    if (accessor.Kind.Type == TokenType.GET)
                    {
                        accessorReturnType = classTypeForBody.Getters[accessor.Name.Lexeme];
                    }
                    else
                    {
                        accessorReturnType = new TypeInfo.Void();
                        if (accessor.SetterParam != null)
                        {
                            TypeInfo setterParamType = classTypeForBody.Setters[accessor.Name.Lexeme];
                            accessorEnv.Define(accessor.SetterParam.Name.Lexeme, setterParamType);
                        }
                    }

                    TypeEnvironment previousEnvAcc = _environment;
                    TypeInfo? previousReturnAcc = _currentFunctionReturnType;
                    int previousLoopDepthAcc = _loopDepth;
                    int previousSwitchDepthAcc = _switchDepth;
                    var previousActiveLabelsAcc = new Dictionary<string, bool>(_activeLabels);

                    _environment = accessorEnv;
                    _currentFunctionReturnType = accessorReturnType;
                    _loopDepth = 0;
                    _switchDepth = 0;
                    _activeLabels.Clear();

                    try
                    {
                        foreach (var bodyStmt in accessor.Body)
                            CheckStmt(bodyStmt);
                    }
                    finally
                    {
                        _environment = previousEnvAcc;
                        _currentFunctionReturnType = previousReturnAcc;
                        _loopDepth = previousLoopDepthAcc;
                        _switchDepth = previousSwitchDepthAcc;
                        _activeLabels.Clear();
                        foreach (var kvp in previousActiveLabelsAcc)
                            _activeLabels[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Check field initializers
            foreach (var field in classExpr.Fields.Where(f => f.Initializer != null))
            {
                TypeInfo initType = CheckExpr(field.Initializer!);
                TypeInfo fieldDeclaredType = field.IsStatic
                    ? classTypeForBody.StaticProperties[field.Name.Lexeme]
                    : classTypeForBody.FieldTypes[field.Name.Lexeme];

                if (!IsCompatible(fieldDeclaredType, initType))
                    throw new TypeCheckException($" Cannot assign type '{initType}' to field '{field.Name.Lexeme}' of type '{fieldDeclaredType}'.");
            }
        }
        finally
        {
            _environment = prevEnv;
            _currentClass = prevClass;
        }

        // Return the class type (not an instance)
        return classTypeForBody;
    }
}
