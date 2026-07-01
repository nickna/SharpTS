using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Expression type checking - CheckExpr dispatch and basic expression handlers.
/// </summary>
/// <remarks>
/// Contains the main expression dispatch (CheckExpr) and handlers for: literals, arrays,
/// objects, templates, spread, arrow functions, assign, type assertions, and basic helper
/// methods (LookupVariable, GetLiteralType).
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Type-checks an expression and returns its resolved type.
    /// Dispatches to the appropriate Visit* method via the registry.
    /// </summary>
    /// <param name="expr">The expression AST node to type-check.</param>
    /// <returns>The resolved TypeInfo for the expression.</returns>
    private TypeInfo CheckExpr(Expr expr)
    {
        TypeInfo result = _registry.DispatchExpr(expr, this);
        _typeMap.Set(expr, result);
        return result;
    }

    /// <summary>
    /// Type-checks an expression under a contextual type (a narrow slice of tsc's
    /// checkExpressionWithContextualType). The context is consumed only by the node
    /// kinds that use it: array literals against a tuple (or tuple-containing union)
    /// context infer a tuple instead of an array, ternaries propagate the context
    /// into their branches, and groupings unwrap. Everything else — including a null
    /// context — falls through to plain <see cref="CheckExpr"/>.
    /// </summary>
    private TypeInfo CheckExprWithContext(Expr expr, TypeInfo? contextualType)
    {
        if (contextualType is null) return CheckExpr(expr);

        switch (expr)
        {
            case Expr.Grouping grouping:
            {
                TypeInfo inner = CheckExprWithContext(grouping.Expression, contextualType);
                _typeMap.Set(expr, inner);
                return inner;
            }
            case Expr.Ternary ternary:
            {
                TypeInfo result = CheckTernary(ternary, contextualType);
                _typeMap.Set(expr, result);
                return result;
            }
            case Expr.ArrayLiteral arrayLit when TryCheckArrayLiteralAsTuple(arrayLit, contextualType, out var tupleType):
            {
                _typeMap.Set(expr, tupleType);
                return tupleType;
            }
            // The destructuring desugarer wraps the source in `__arrayDestructure(src)` (#685). Re-thread
            // the contextual shape to the wrapped source so a mixed array literal infers as a tuple, then
            // normalize as the plain call branch does (#783).
            case Expr.Call { Callee: Expr.Variable { Name.Lexeme: BuiltInNames.ArrayDestructure }, Arguments: [var src] }:
            {
                var sourceType = NormalizeArrayDestructureSourceType(CheckExprWithContext(src, contextualType));
                _typeMap.Set(expr, sourceType);
                return sourceType;
            }
            default:
                return CheckExpr(expr);
        }
    }

    /// <summary>
    /// Contextually types an array literal as a tuple when the context contains tuple
    /// constituents whose arity the literal can satisfy. Element expressions are checked
    /// under the union of the candidates' types at that position, so nested literals and
    /// ternaries keep contextual typing. Returns false (no tuple inference) when the
    /// context has no arity-compatible tuple constituent or the literal uses spreads.
    /// </summary>
    private bool TryCheckArrayLiteralAsTuple(Expr.ArrayLiteral array, TypeInfo contextualType, out TypeInfo result)
    {
        result = null!;
        if (array.Elements.Any(e => e is Expr.Spread)) return false;

        IEnumerable<TypeInfo.Tuple> tupleConstituents = contextualType switch
        {
            TypeInfo.Tuple tuple => [tuple],
            TypeInfo.Union union => union.FlattenedTypes.OfType<TypeInfo.Tuple>(),
            _ => []
        };

        var candidates = tupleConstituents
            .Where(t => !t.HasSpread
                && array.Elements.Count >= t.RequiredCount
                && (t.RestElementType != null || array.Elements.Count <= t.Elements.Count))
            .ToList();
        if (candidates.Count == 0) return false;

        var elements = new List<TypeInfo.TupleElement>(array.Elements.Count);
        for (int i = 0; i < array.Elements.Count; i++)
        {
            var positionContexts = new List<TypeInfo>();
            foreach (var candidate in candidates)
            {
                TypeInfo? at = i < candidate.Elements.Count
                    ? candidate.Elements[i].Type
                    : candidate.RestElementType;
                if (at != null && !positionContexts.Any(p => TypeInfoEqualityComparer.Instance.Equals(p, at)))
                    positionContexts.Add(at);
            }
            TypeInfo? elementContext = positionContexts.Count == 0 ? null :
                positionContexts.Count == 1 ? positionContexts[0] :
                new TypeInfo.Union(positionContexts);

            TypeInfo elementType = CheckExprWithContext(array.Elements[i], elementContext);
            elements.Add(new TypeInfo.TupleElement(elementType, TupleElementKind.Required));
        }

        result = new TypeInfo.Tuple(elements, elements.Count);
        return true;
    }

    // Expression handlers - called by the registry
    // Simple expressions are implemented inline, complex ones delegate to Check* methods

    internal TypeInfo VisitLiteral(Expr.Literal expr) => GetLiteralType(expr.Value);
    internal TypeInfo VisitVariable(Expr.Variable expr) => LookupVariable(expr.Name);
    internal TypeInfo VisitGrouping(Expr.Grouping expr) => CheckExpr(expr.Expression);
    internal TypeInfo VisitRegexLiteral(Expr.RegexLiteral expr) => new TypeInfo.RegExp();
    internal TypeInfo VisitAwait(Expr.Await expr) => CheckAwait(expr);
    internal TypeInfo VisitDynamicImport(Expr.DynamicImport expr) => CheckDynamicImport(expr);
    internal TypeInfo VisitImportMeta(Expr.ImportMeta expr) => CheckImportMeta(expr);
    internal TypeInfo VisitYield(Expr.Yield expr) => CheckYield(expr);
    internal TypeInfo VisitTypeAssertion(Expr.TypeAssertion expr) => CheckTypeAssertion(expr);
    internal TypeInfo VisitSatisfies(Expr.Satisfies expr) => CheckSatisfies(expr);
    internal TypeInfo VisitNonNullAssertion(Expr.NonNullAssertion expr) => CheckNonNullAssertion(expr);
    internal TypeInfo VisitTemplateLiteral(Expr.TemplateLiteral expr) => CheckTemplateLiteral(expr);
    internal TypeInfo VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr) => CheckTaggedTemplateLiteral(expr);
    internal TypeInfo VisitObjectLiteral(Expr.ObjectLiteral expr) => CheckObject(expr);
    internal TypeInfo VisitArrayLiteral(Expr.ArrayLiteral expr) => CheckArray(expr);
    internal TypeInfo VisitSpread(Expr.Spread expr) => CheckSpread(expr);
    internal TypeInfo VisitArrowFunction(Expr.ArrowFunction expr) => CheckArrowFunction(expr);
    internal TypeInfo VisitAssign(Expr.Assign expr) => CheckAssign(expr);
    internal TypeInfo VisitClassExpr(Expr.ClassExpr expr) => CheckClassExpression(expr);

    // The following Visit* methods delegate to Check* methods in other partial files
    // (TypeChecker.Properties.cs, TypeChecker.Operators.cs, TypeChecker.Calls.cs)

    // Comma (sequence) operator - evaluates all, returns type of last
    internal TypeInfo VisitComma(Expr.Comma expr) { CheckExpr(expr.Left); return CheckExpr(expr.Right); }

    // Assignment destructuring (#754): check the lowered statements (which declare the rhs temp and
    // validate each target write — e.g. assigning a number element to a `string` target raises TS2322),
    // then report the result type as the rhs's, since the expression evaluates to its right-hand side.
    internal TypeInfo VisitDestructuringAssign(Expr.DestructuringAssign expr)
    {
        foreach (var stmt in expr.Assignments)
            CheckStmt(stmt);

        // The lowered spill temps live inside an EXPRESSION, so they escape the whole-body
        // numeric-slot taint pass (MarkUndefinedReachableNumericSlots) that the declaration
        // desugaring's Stmt.Sequence goes through. A defaulted element/property whose source value
        // is absent (`[a = 5] = arr`, `({c = 5} = obj)`) leaves the spill holding the runtime
        // `undefined` sentinel; an unboxed `double` slot would coerce it to NaN at the store. Widen
        // every synthesized temp back to an object slot (a no-op for the non-numeric source temps). #784
        foreach (var stmt in expr.Assignments)
        {
            if (stmt is Stmt.Var { Initializer: { } init } varStmt)
            {
                _typeMap.MarkUndefinedReachableNumericLocal(varStmt);
                _typeMap.MarkUndefinedReachableNumericLocal(init);
            }
        }

        return CheckExpr(expr.ResultValue);
    }

    // Binary/logical operators (TypeChecker.Operators.cs)
    internal TypeInfo VisitBinary(Expr.Binary expr) => CheckBinary(expr);
    internal TypeInfo VisitLogical(Expr.Logical expr) => CheckLogical(expr);
    internal TypeInfo VisitNullishCoalescing(Expr.NullishCoalescing expr) => CheckNullishCoalescing(expr);
    internal TypeInfo VisitTernary(Expr.Ternary expr) => CheckTernary(expr);
    internal TypeInfo VisitUnary(Expr.Unary expr) => CheckUnary(expr);
    internal TypeInfo VisitDelete(Expr.Delete expr) => CheckDelete(expr);

    // Compound assignment operators (TypeChecker.Operators.cs)
    internal TypeInfo VisitCompoundAssign(Expr.CompoundAssign expr) => CheckCompoundAssign(expr);
    internal TypeInfo VisitCompoundSet(Expr.CompoundSet expr) => CheckCompoundSet(expr);
    internal TypeInfo VisitCompoundSetIndex(Expr.CompoundSetIndex expr) => CheckCompoundSetIndex(expr);
    internal TypeInfo VisitLogicalAssign(Expr.LogicalAssign expr) => CheckLogicalAssign(expr);
    internal TypeInfo VisitLogicalSet(Expr.LogicalSet expr) => CheckLogicalSet(expr);
    internal TypeInfo VisitLogicalSetIndex(Expr.LogicalSetIndex expr) => CheckLogicalSetIndex(expr);
    internal TypeInfo VisitPrefixIncrement(Expr.PrefixIncrement expr) => CheckPrefixIncrement(expr);
    internal TypeInfo VisitPostfixIncrement(Expr.PostfixIncrement expr) => CheckPostfixIncrement(expr);

    // Function calls (TypeChecker.Calls.cs)
    internal TypeInfo VisitCall(Expr.Call expr) => CheckCall(expr);

    // Property access (TypeChecker.Properties.cs)
    internal TypeInfo VisitGet(Expr.Get expr) => CheckGet(expr);
    internal TypeInfo VisitSet(Expr.Set expr) => CheckSet(expr);
    internal TypeInfo VisitGetPrivate(Expr.GetPrivate expr) => CheckGetPrivate(expr);
    internal TypeInfo VisitSetPrivate(Expr.SetPrivate expr) => CheckSetPrivate(expr);
    internal TypeInfo VisitCallPrivate(Expr.CallPrivate expr) => CheckCallPrivate(expr);
    internal TypeInfo VisitThis(Expr.This expr) => CheckThis(expr);
    internal TypeInfo VisitNew(Expr.New expr) => CheckNew(expr);
    internal TypeInfo VisitSuper(Expr.Super expr) => CheckSuper(expr);

    // Index access (TypeChecker.Properties.Index.cs)
    internal TypeInfo VisitGetIndex(Expr.GetIndex expr) => CheckGetIndex(expr);
    internal TypeInfo VisitSetIndex(Expr.SetIndex expr) => CheckSetIndex(expr);

    private TypeInfo CheckAwait(Expr.Await awaitExpr)
    {
        if (!_inAsyncFunction)
        {
            throw new TypeCheckException("'await' is only valid inside an async function.", tsCode: "TS1308");
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
            throw new TypeCheckException($" Dynamic import path must be a string, got '{pathType}'.", tsCode: "TS2345");
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
            throw new TypeCheckException("'yield' is only valid inside a generator function.", tsCode: "TS1163");
        }

        if (yieldExpr.Value != null)
        {
            TypeInfo valueType = CheckExpr(yieldExpr.Value);

            // For yield*, the expression must be iterable and we yield each element
            if (yieldExpr.IsDelegating)
            {
                // yield* requires an iterable (array, generator, etc.)
                TypeInfo delegatedElement = GetIterableElementType(valueType);
                // The delegated iterable's element type flows into the enclosing generator's yield type
                // (#548): `function* g() { yield* [1, 2]; }` yields `number`, so it must infer
                // Generator<number>, not Generator<void>. Collected only while that type is being inferred.
                _inferredYieldTypes?.Add(delegatedElement);
                return delegatedElement;
            }

            _inferredYieldTypes?.Add(valueType);
            return valueType;
        }

        // Bare `yield` yields `undefined`; it contributes `undefined` to the inferred yield type (#548).
        _inferredYieldTypes?.Add(new TypeInfo.Undefined());
        // The yield EXPRESSION still evaluates to the (modeled) void type for an operand-less yield.
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
        TypeInfo.Iterable iterable => iterable.ElementType,
        TypeInfo.Set set => set.ElementType,
        TypeInfo.Map map => TypeInfo.Tuple.FromTypes([map.KeyType, map.ValueType], 2),  // [K, V] tuples
        TypeInfo.String => new TypeInfo.String(),  // String yields characters (as strings)
        TypeInfo.StringLiteral => new TypeInfo.String(),  // String literal also yields characters
        TypeInfo.Any => new TypeInfo.Any(),
        // A hand-written object exposing [Symbol.iterator] is iterable structurally (#485); dedicated
        // records are handled above, so only genuine structural objects reach here.
        _ when TryGetStructuralIterableElement(type, out var structuralElem) => structuralElem,
        _ => throw new TypeCheckException($" Type '{type}' is not iterable for yield*.", tsCode: "TS2488")
    };

    /// <summary>
    /// Tries to get the element type from a spreadable/iterable type.
    /// Returns true if the type is spreadable, with the element type in the out parameter.
    /// </summary>
    private bool TryGetSpreadElementType(TypeInfo type, out TypeInfo elementType)
    {
        switch (type)
        {
            case TypeInfo.Array arr:
                elementType = arr.ElementType;
                return true;
            case TypeInfo.Iterator iter:
                elementType = iter.ElementType;
                return true;
            case TypeInfo.Iterable iterable:
                elementType = iterable.ElementType;
                return true;
            case TypeInfo.Generator gen:
                elementType = gen.YieldType;
                return true;
            case TypeInfo.Set set:
                elementType = set.ElementType;
                return true;
            case TypeInfo.Map map:
                elementType = TypeInfo.Tuple.FromTypes([map.KeyType, map.ValueType], 2);
                return true;
            case TypeInfo.String:
            case TypeInfo.StringLiteral:
                elementType = new TypeInfo.String();
                return true;
            case TypeInfo.Any:
                elementType = new TypeInfo.Any();
                return true;
            case TypeInfo.TypedArray typed:
                elementType = typed.ElementType.StartsWith("Big")
                    ? (TypeInfo)new TypeInfo.BigInt()
                    : new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
                return true;
            default:
                // A hand-written object exposing [Symbol.iterator] is spreadable structurally (#485).
                return TryGetStructuralIterableElement(type, out elementType);
        }
    }

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

        // Resolve the target node-first: a composite target such as a conditional type whose
        // extends clause is a function type (`X extends () => infer U ? U : V`) is mis-scanned by
        // the string resolver (the '>' of '=>' reads as a closing bracket, so the conditional's
        // '?' is missed and the type garbles). The node path resolves it structurally; the string
        // path stays the fallback for any node the migration doesn't cover yet (#346).
        TypeInfo targetType = (ta.TargetTypeNode is { } targetNode ? TryToTypeInfo(targetNode) : null)
            ?? ToTypeInfo(ta.TargetType);

        // Allow any <-> anything (escape hatch)
        if (sourceType is TypeInfo.Any || targetType is TypeInfo.Any)
            return targetType;

        // Check if types are related (either direction)
        if (IsCompatible(targetType, sourceType) || IsCompatible(sourceType, targetType))
            return targetType;

        throw new TypeCheckException($" Cannot assert type '{sourceType}' to '{targetType}'.", tsCode: "TS2352");
    }

    private TypeInfo CheckSatisfies(Expr.Satisfies sat)
    {
        TypeInfo inferredType = CheckExpr(sat.Expression);
        // Node-first, mirroring CheckTypeAssertion (#346): a composite constraint such as a
        // conditional with a function-type extends clause garbles in the string resolver.
        TypeInfo constraintType = (sat.ConstraintTypeNode is { } constraintNode ? TryToTypeInfo(constraintNode) : null)
            ?? ToTypeInfo(sat.ConstraintType);

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
                $"Type '{inferredType}' does not satisfy constraint '{constraintType}'.", tsCode: "TS2344");
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
    /// Converts an array literal to a readonly tuple type with literal element types.
    /// The tuple is marked <see cref="TypeInfo.Tuple.IsReadonly"/> because <c>as const</c> produces
    /// readonly literal-typed elements; this both rejects element writes and protects the literal
    /// element types from <c>const</c>-initializer widening (#493 — see <see cref="WidenLiteralType"/>).
    /// </summary>
    private TypeInfo InferConstArrayType(Expr.ArrayLiteral arr)
    {
        var elementTypes = arr.Elements
            .Select(e => InferConstType(e, CheckExpr(e)))
            .ToList();
        return TypeInfo.Tuple.FromTypes(elementTypes, elementTypes.Count, isReadonly: true);
    }

    /// <summary>
    /// Converts an object literal to a readonly record type with literal property types.
    /// The record is marked <see cref="TypeInfo.Record.IsReadonly"/> because <c>as const</c> produces
    /// readonly literal-typed members; this both rejects member writes with TS2540 and protects the
    /// literal member types from <c>const</c>-initializer widening (#493 — see <see cref="WidenLiteralType"/>).
    /// </summary>
    private TypeInfo InferConstObjectType(Expr.ObjectLiteral obj)
    {
        var fields = new Dictionary<string, TypeInfo>();
        var getters = new HashSet<string>();
        var setters = new HashSet<string>();
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
                    getters.Add(name);
                }
                else if (prop.Kind == Expr.ObjectPropertyKind.Setter && fnType is TypeInfo.Function setterFn && setterFn.ParamTypes.Count > 0)
                {
                    setters.Add(name);
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
        // Properties with a getter but no setter are getter-only
        var getterOnly = getters.Except(setters).ToFrozenSet();
        return new TypeInfo.Record(fields.ToFrozenDictionary(),
            IsReadonly: true,
            GetterOnlyFields: getterOnly.Count > 0 ? getterOnly : null);
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
                $"Type Error: Tagged template tag must be callable, got '{tagType}'.", tsCode: "TS2349")
        };
    }

    /// <summary>
    /// Merges the known fields of a spread source (<c>{ ...source }</c>) into <paramref name="fields"/>.
    /// Spread accepts any object-like type per TS, not just object literals. Returns false for
    /// non-object types (primitives, void, etc.) which should be reported as TS2698.
    /// Class-like and dynamic sources (Instance/Class/Any/Unknown) are accepted without merging
    /// concrete fields, mirroring the pre-existing Instance behavior.
    /// </summary>
    private bool TryMergeSpreadFields(TypeInfo spreadType, Dictionary<string, TypeInfo> fields)
    {
        switch (spreadType)
        {
            case TypeInfo.Record record:
                foreach (var kv in record.Fields)
                {
                    fields[kv.Key] = kv.Value;
                }
                return true;

            case TypeInfo.Interface iface:
                foreach (var kv in iface.GetAllMembers())
                {
                    fields[kv.Key] = kv.Value;
                }
                return true;

            case TypeInfo.Intersection intersection:
                // Spread of an intersection contributes the fields of every object-like constituent.
                // Reject only if no constituent is object-like (e.g. `string & number`).
                bool anyObjectLike = false;
                foreach (var part in intersection.Types)
                {
                    if (TryMergeSpreadFields(part, fields))
                    {
                        anyObjectLike = true;
                    }
                }
                return anyObjectLike;

            // Class-like / dynamic sources: accept but don't enumerate concrete fields here.
            case TypeInfo.Instance:
            case TypeInfo.Class:
            case TypeInfo.GenericClass:
            case TypeInfo.InstantiatedGeneric:
            case TypeInfo.Any:
            case TypeInfo.Unknown:
            case TypeInfo.Object:
                return true;

            default:
                return false;
        }
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
        // Track getter/setter names to identify getter-only properties
        HashSet<string> getterNames = [];
        HashSet<string> setterNames = [];

        // Pass 1: Collect property types without checking accessor bodies
        // For accessors, use type annotations only (don't check bodies yet)
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                // Spread property - merge fields from the spread object
                TypeInfo spreadType = CheckExpr(prop.Value);
                if (!TryMergeSpreadFields(spreadType, fields))
                {
                    throw new TypeCheckException($" Spread in object literal requires an object, got '{spreadType}'.", tsCode: "TS2698");
                }
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Getter)
            {
                hasAccessors = true;
                accessorProps.Add(prop);

                // Getter - extract return type from annotation only (don't check body yet)
                string name = GetPropertyKeyNameForTypeCheck(prop.Key!);
                getterNames.Add(name);
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
                setterNames.Add(name);
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
                if (prop.IsShorthandDefault)
                {
                    // A `{ a = 5 }` CoverInitializedName reaching CheckObject was used as a plain object
                    // literal expression, not a destructuring target (assignment-destructuring patterns are
                    // consumed by BuildDestructuringAssignment before type-checking). tsc rejects it (#780).
                    throw new TypeCheckException(
                        " '=' can only be used in an object literal property inside a destructuring assignment.",
                        tsCode: "TS1312");
                }

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
                            throw new TypeCheckException($" Computed property key must be string, number, or symbol, got '{keyType}'.", tsCode: "TS1170");
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

        // Properties with a getter but no setter are getter-only
        var getterOnly = getterNames.Except(setterNames).ToFrozenSet();
        return new TypeInfo.Record(fields.ToFrozenDictionary(), stringIndexType, numberIndexType, symbolIndexType,
            GetterOnlyFields: getterOnly.Count > 0 ? getterOnly : null);
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
            _ => throw new TypeCheckException("Invalid property key for accessor.", tsCode: "TS1170")
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
            // `as const` (and other readonly) arrays/tuples/records keep their literal element/member
            // types — readonly literal types are never widened (#493). Without this, an `as const`
            // sitting as an array *element* or a spread *member* (positions where `const`-initializer
            // widening recurses through a fresh literal) would be widened away.
            TypeInfo.Array { IsReadonly: true } => type,
            TypeInfo.Tuple { IsReadonly: true } => type,
            TypeInfo.Record { IsReadonly: true } => type,
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

    /// <summary>
    /// Widens the inferred type of an un-annotated <c>const</c> initializer to match TypeScript.
    /// A <c>const</c> binding freezes the <em>binding</em>, not the mutability of an object/array
    /// literal's members, so a fresh object- or array-literal initializer has its property/element
    /// types widened to their base types (<c>const o = { n: 1 }</c> ⇒ <c>{ n: number }</c>), which
    /// keeps a later <c>o.n = 9</c> legal. A bare primitive literal keeps its literal type
    /// (<c>const x = 1</c> ⇒ <c>1</c>), and <c>as const</c> assertions keep their readonly literal
    /// types — including an <c>as const</c> nested inside a plain object literal, sitting as an array
    /// element, or spread into a plain object literal (#493). Non-literal initializers (variables,
    /// calls, …) are not "fresh" and pass through unchanged.
    /// </summary>
    private TypeInfo WidenConstInitializerType(Expr initializer, TypeInfo inferredType)
        => WidenFreshLiteralsForConst(initializer, inferredType, topLevel: true);

    /// <summary>
    /// Recursively widens the literal types produced by <em>fresh</em> object/array literals for a
    /// <c>const</c> binding, while preserving <c>as const</c> assertions and non-literal
    /// expressions. At the binding top level a bare primitive literal is preserved
    /// (<c>const x = 1</c> ⇒ <c>1</c>); inside an object literal it is widened
    /// (<c>{ a: 1 }</c> ⇒ <c>{ a: number }</c>), mirroring the <c>let</c> path.
    /// </summary>
    private TypeInfo WidenFreshLiteralsForConst(Expr expr, TypeInfo type, bool topLevel)
    {
        // `satisfies` and parentheses are transparent to widening:
        // `const o = ({ n: 1 }) satisfies T` widens exactly like `const o = { n: 1 }`.
        Expr inner = UnwrapTransparentForWidening(expr);

        switch (inner)
        {
            // `as const` keeps its readonly literal property/element types.
            case Expr.TypeAssertion { TargetType: "const" }:
                return type;

            // A fresh object literal widens each member; a member that is itself `as const`, or a
            // spread of a non-fresh source, is preserved per-member (see WidenConstObjectLiteralFields).
            case Expr.ObjectLiteral obj when type is TypeInfo.Record rec:
                return WidenConstObjectLiteralFields(obj, rec);

            // A fresh array literal widens its element type. An `as const` *element* produced a
            // readonly record/tuple, which WidenLiteralType leaves intact, so it survives (#493).
            case Expr.ArrayLiteral when type is TypeInfo.Array arr:
                return new TypeInfo.Array(WidenLiteralType(arr.ElementType));

            // A bare primitive literal: preserved at the binding top level, widened inside a literal.
            case Expr.Literal:
                return topLevel ? type : WidenLiteralType(type);

            // Variables, calls, non-const assertions, …: not fresh — preserve as-is.
            default:
                return type;
        }
    }

    /// <summary>
    /// Widens the fields of a fresh object-literal <see cref="TypeInfo.Record"/> for a <c>const</c>
    /// binding, mirroring TypeScript's freshness rules. A direct <c>name: value</c> member is fresh:
    /// its value expression is widened (recursing so a nested <c>as const</c> survives). A spread
    /// member contributes its source's fields: spreading a <em>fresh</em> inline object literal
    /// widens them (recursively), while spreading a <em>non-fresh</em> source (an <c>as const</c>, a
    /// variable, a call) preserves their already-fixed literal types verbatim — this is what keeps
    /// <c>const o = { ...({ a: 1 } as const) }</c> at <c>{ a: 1 }</c> (#493). Members are processed in
    /// source order so a later member overrides an earlier one (JS spread/override order). A field not
    /// resolved to any member (a computed key, or a non-enumerable spread source) widens
    /// conservatively, and other record metadata (optional/getter-only/index signatures, …) is preserved.
    /// </summary>
    private TypeInfo WidenConstObjectLiteralFields(Expr.ObjectLiteral obj, TypeInfo.Record rec)
    {
        // Resolve, per final field name, the widened member type by walking members in source order
        // so the last writer for a name wins — matching how `rec` itself was merged during inference.
        var resolved = new Dictionary<string, TypeInfo>();
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                Expr source = UnwrapTransparentForWidening(prop.Value);
                // The spread source was already type-checked while inferring this object literal;
                // re-checking an r-value here is idempotent and surfaces no new diagnostics.
                TypeInfo sourceType = CheckExpr(prop.Value);
                if (source is Expr.ObjectLiteral innerObj && sourceType is TypeInfo.Record innerRec)
                {
                    // Fresh inline object-literal spread: widen its members recursively.
                    var widenedInner = (TypeInfo.Record)WidenConstObjectLiteralFields(innerObj, innerRec);
                    foreach (var (k, v) in widenedInner.Fields)
                        resolved[k] = v;
                }
                else
                {
                    // Non-fresh spread (`as const`, a variable, a call, …): preserve the source's
                    // already-fixed field types verbatim, so spread `as const` literals survive.
                    foreach (var k in EnumerateRecordLikeFieldNames(sourceType))
                        if (rec.Fields.TryGetValue(k, out var preserved))
                            resolved[k] = preserved;
                }
            }
            else if (prop.Kind == Expr.ObjectPropertyKind.Value &&
                     TryGetStaticFieldNameForWidening(prop.Key, out var name) &&
                     rec.Fields.TryGetValue(name, out var directType))
            {
                resolved[name] = WidenFreshLiteralsForConst(prop.Value, directType, topLevel: false);
            }
        }

        var widenedFields = new Dictionary<string, TypeInfo>(rec.Fields.Count);
        foreach (var (name, fieldType) in rec.Fields)
            widenedFields[name] = resolved.TryGetValue(name, out var w) ? w : WidenLiteralType(fieldType);

        return rec with
        {
            Fields = widenedFields.ToFrozenDictionary(),
            StringIndexType = rec.StringIndexType != null ? WidenLiteralType(rec.StringIndexType) : null,
            NumberIndexType = rec.NumberIndexType != null ? WidenLiteralType(rec.NumberIndexType) : null,
            SymbolIndexType = rec.SymbolIndexType != null ? WidenLiteralType(rec.SymbolIndexType) : null,
        };
    }

    /// <summary>
    /// Unwraps the widening-transparent wrappers (<c>satisfies</c> and parentheses) so widening sees
    /// the underlying literal/assertion shape.
    /// </summary>
    private static Expr UnwrapTransparentForWidening(Expr expr)
    {
        while (true)
        {
            if (expr is Expr.Satisfies sat) { expr = sat.Expression; continue; }
            if (expr is Expr.Grouping grp) { expr = grp.Expression; continue; }
            return expr;
        }
    }

    /// <summary>Enumerates the statically known field names a spread source contributes.</summary>
    private static IEnumerable<string> EnumerateRecordLikeFieldNames(TypeInfo sourceType) => sourceType switch
    {
        TypeInfo.Record rec => rec.Fields.Keys,
        TypeInfo.Interface iface => iface.GetAllMembers().Select(m => m.Key),
        _ => [],
    };

    /// <summary>Resolves the static field name of a <c>name: value</c> object-literal member key.</summary>
    private static bool TryGetStaticFieldNameForWidening(Expr.PropertyKey? key, out string name)
    {
        name = key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey { Literal.Literal: string s } => s,
            _ => null!,
        };
        return name != null;
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
                // Spread element - get element type from any iterable type
                TypeInfo spreadType = CheckExpr(spread.Expression);
                if (spreadType is TypeInfo.Tuple tupType)
                {
                    // Spread tuple - add all its element types
                    elementTypes.AddRange(tupType.ElementTypes);
                    if (tupType.RestElementType != null)
                        elementTypes.Add(tupType.RestElementType);
                    continue; // Don't add elemType again since we added multiple
                }
                else if (TryGetSpreadElementType(spreadType, out var spreadElemType))
                {
                    elemType = spreadElemType;
                }
                else
                {
                    throw new TypeCheckException($" Spread expression must be an iterable type (array, iterator, set, map, string, or generator), got '{spreadType}'.", tsCode: "TS2488");
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
            throw new TypeCheckException($" Tuple requires at least {requiredCount} elements, but got {elemCount} for variable '{varName}'.", tsCode: "TS2741");
        }

        // Check maximum element count (only for fixed tuples without spread or rest)
        if (tupleType.RestElementType == null && !tupleType.HasSpread && elemCount > tupleType.Elements.Count)
        {
            throw new TypeCheckException($" Tuple expects at most {tupleType.Elements.Count} elements, but got {elemCount} for variable '{varName}'.", tsCode: "TS2322");
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
                throw new TypeCheckException($" Tuple index {i} is out of bounds for variable '{varName}'.", tsCode: "TS2493");
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
                    throw new TypeCheckException($" Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.", tsCode: "TS2322");
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
            throw new TypeCheckException($" Not enough elements for variadic tuple: expected at least {leadingCount + trailingCount} elements, got {arrayLit.Elements.Count} for variable '{varName}'.", tsCode: "TS2741");
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
                    throw new TypeCheckException($" Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.", tsCode: "TS2322");
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
                    throw new TypeCheckException($" Element at index {arrIdx} has type '{elemType}' but expected '{spreadInnerType}' for variable '{varName}'.", tsCode: "TS2322");
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
                    throw new TypeCheckException($" Element at index {arrIdx} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.", tsCode: "TS2322");
                }
            }
        }
    }

    private TypeInfo CheckArrowFunction(Expr.ArrowFunction arrow, TypeInfo? expectedType = null)
    {
        // Extract expected function type for parameter inference
        TypeInfo.Function? expectedFuncType = expectedType switch
        {
            TypeInfo.Function f => f,
            TypeInfo.GenericFunction gf => new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType),
            _ => null
        };

        // Set up generic type parameters (if any)
        TypeEnvironment typeParamEnv = _environment;
        List<TypeInfo.TypeParameter>? typeParams = null;

        if (arrow.TypeParams != null && arrow.TypeParams.Count > 0)
        {
            typeParamEnv = new TypeEnvironment(_environment);

            // First pass: define all type parameters without constraints
            foreach (var tp in arrow.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null, tp.IsConst, tp.Variance);
                typeParamEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }

            // Second pass: parse constraints/defaults (can reference other type parameters)
            using (new EnvironmentScope(this, typeParamEnv))
            {
                typeParams = [];
                foreach (var tp in arrow.TypeParams)
                {
                    TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                    TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                    typeParams.Add(typeParam);
                    typeParamEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }
        }

        // Parse 'this' type, parameter types, and return type in the type-parameter scope
        TypeInfo? thisType;
        List<TypeInfo> paramTypes = [];
        int requiredParams = 0;
        bool seenDefault = false;
        TypeInfo returnType;

        using (new EnvironmentScope(this, typeParamEnv))
        {
            // Parse explicit 'this' type if present (for object literal method shorthand)
            // Note: Arrow function expressions shouldn't have 'this' parameter in standard TypeScript,
            // but we support it for object literal method shorthand which is parsed as ArrowFunction.
            thisType = arrow.ThisType != null ? ToTypeInfo(arrow.ThisType) : null;

            // For function expressions and object method shorthand (HasOwnThis=true), allow 'this' even without explicit type annotation
            // TypeScript infers 'this' as the containing object type - use _pendingObjectThisType if available
            if (arrow.HasOwnThis && thisType == null)
            {
                thisType = _pendingObjectThisType ?? new TypeInfo.Any();
            }

            // A default value may reference any PRECEDING parameter (`(a, b = a * 2)`), so each
            // parameter is progressively defined in a dedicated scope and later defaults are checked
            // against it. Each parameter is defined AFTER its own default is checked, so a
            // self-reference resolves to an outer binding or errors. Mirrors BuildFunctionSignature,
            // which the function-declaration/method path already uses (#698).
            var paramScope = new TypeEnvironment(_environment);

            // Build parameter types and check defaults
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
                    paramScope.Define(param.Name.Lexeme, paramType);
                    continue;
                }

                if (param.DefaultValue != null)
                {
                    seenDefault = true;
                    TypeInfo defaultType;
                    using (new EnvironmentScope(this, paramScope))
                    {
                        defaultType = CheckExpr(param.DefaultValue);
                    }
                    if (!IsCompatible(paramType, defaultType))
                    {
                        throw new TypeCheckException($" Default value type '{defaultType}' is not assignable to parameter type '{paramType}'.", tsCode: "TS2322");
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
                        throw new TypeCheckException($" Required parameter cannot follow optional parameter.", tsCode: "TS1016");
                    }
                    requiredParams++;
                }

                paramScope.Define(param.Name.Lexeme, paramType);
            }

            // Determine return type (use expected type if available and no explicit annotation)
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
                returnType = new TypeInfo.Inferred();
            }
        }

        // Build the function type (needed for named function expressions self-reference)
        bool hasRest = arrow.Parameters.Any(p => p.IsRest);
        List<string> paramNames = arrow.Parameters.Select(p => p.Name.Lexeme).ToList();
        TypeInfo funcType = (typeParams != null && typeParams.Count > 0)
            ? new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames)
            : new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);

        // Create new environment for function body
        TypeEnvironment arrowEnv = new(typeParamEnv);

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
        var previousInferredArrow = _inferredReturnTypes;
        TypeInfo? previousThisType = _currentFunctionThisType;
        bool previousInAsync = _inAsyncFunction;
        bool previousInGenerator = _inGeneratorFunction;
        var previousInferredYieldTypes = _inferredYieldTypes;
        int previousLoopDepth = _loopDepth;
        int previousSwitchDepth = _switchDepth;
        var previousActiveLabels = new Dictionary<string, bool>(_activeLabels);

        bool inferringArrowReturn = returnType is TypeInfo.Inferred;
        _environment = arrowEnv;
        if (inferringArrowReturn)
        {
            _inferredReturnTypes = new List<TypeInfo>();
            _currentFunctionReturnType = new TypeInfo.Inferred();
        }
        else
        {
            _currentFunctionReturnType = returnType;
        }
        _currentFunctionThisType = thisType;
        _inAsyncFunction = arrow.IsAsync;
        // A generator function EXPRESSION establishes its own generator context so `yield` is valid in
        // its body and its element type is inferred from the yields. Reached only for generator arrows
        // the GeneratorArrowLifter intentionally leaves in place — i.e. those closing over a block-scoped
        // binding (#678); all other generator expressions are lifted to declarations before type-check.
        _inGeneratorFunction = arrow.IsGenerator;
        // Collect yield operand types only while inferring a generator's type (mirrors the declaration
        // path, #548). Null otherwise so a nested explicitly-typed generator's yields cannot leak into an
        // enclosing inferred one.
        _inferredYieldTypes = inferringArrowReturn && arrow.IsGenerator ? new List<TypeInfo>() : null;
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
                        throw new TypeCheckException($" Arrow function declared to return '{returnType}' but expression evaluates to '{exprType}'.", tsCode: "TS2322");
                    }

                    // Expression-bodied arrows have no Stmt.Return; the body expression is
                    // the return value. Flag it the same way block returns are flagged (#344).
                    MarkIfUndefinedReachableNumericReturn(arrow.ExpressionBody, exprType);
                }
            }
            else if (arrow.BlockBody != null)
            {
                // Hoist inner function declarations so forward references within
                // an arrow body (incl. IIFEs like `(function runInContext() {…})()`)
                // resolve. Matches the behavior in CheckFunctionDeclaration.
                HoistFunctionDeclarations(arrow.BlockBody);

                // Likewise hoist the body's own let/const (as `any`) so an inner function declared
                // before a later block-scoped binding in the same body can forward-reference it (#533).
                HoistLexicalDeclarations(arrow.BlockBody);

                // Block body - check statements
                CheckStmtList(arrow.BlockBody);

                // #367/#372: object-slot number/boolean-typed locals, parameters, and returns left
                // holding the undefined sentinel by an `any`/`undefined` assignment. Expression-bodied
                // arrows have no body statements that could reassign a slot, so only block bodies need this.
                MarkUndefinedReachableNumericSlots(arrow.BlockBody, arrow.Parameters);

                // Resolve inferred return type for block-body arrows
                if (inferringArrowReturn)
                {
                    var collected = _inferredReturnTypes!;
                    _inferredReturnTypes = null;

                    if (arrow.IsGenerator)
                    {
                        // A generator's element type is its YIELD type (#548), not the return-derived
                        // type; build Generator<Y> (or AsyncGenerator<Y> for an async generator).
                        returnType = BuildInferredGeneratorType(_inferredYieldTypes!, arrow.IsAsync);
                    }
                    else if (collected.Count == 0)
                    {
                        returnType = new TypeInfo.Void();
                    }
                    else
                    {
                        var distinct = collected.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                        returnType = CollapseOrCreateUnion(distinct);
                    }

                    if (arrow.IsAsync && !arrow.IsGenerator && returnType is not TypeInfo.Void)
                        returnType = new TypeInfo.Promise(returnType);
                }
            }
        }
        finally
        {
            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
            _inferredReturnTypes = previousInferredArrow;
            _currentFunctionThisType = previousThisType;
            _inAsyncFunction = previousInAsync;
            _inGeneratorFunction = previousInGenerator;
            _inferredYieldTypes = previousInferredYieldTypes;
            _loopDepth = previousLoopDepth;
            _switchDepth = previousSwitchDepth;
            _activeLabels.Clear();
            foreach (var kvp in previousActiveLabels)
                _activeLabels[kvp.Key] = kvp.Value;
        }

        return typeParams != null && typeParams.Count > 0
            ? new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames)
            : new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
    }

    private TypeInfo CheckAssign(Expr.Assign assign)
    {
        // For assignment, check against the DECLARED type, not the narrowed type
        // This allows reassigning within narrowed scopes (e.g., x = "found" when x was narrowed to null)
        // Use GetDeclaredType to get the original declared type, not the potentially narrowed type
        // tsc: assigning to `undefined` is TS2539 ("Cannot assign to 'undefined' because it is
        // not a variable") — the parser lets it through as an identifier-shaped target.
        if (assign.Name.Lexeme == "undefined")
        {
            throw new TypeCheckException($" Cannot assign to 'undefined' because it is not a variable.", line: assign.Name.Line, tsCode: "TS2539");
        }

        var declaredType = GetDeclaredType(assign.Name.Lexeme);
        if (declaredType == null)
        {
            throw new TypeCheckException($" Undefined variable '{assign.Name.Lexeme}'.", tsCode: "TS2304");
        }

        // VarHoister synthesizes a self-assignment (`z = z`) carrying an explicit annotation when a
        // later `var` re-declares an existing name with only a type annotation (no initializer).
        // tsc's TS2403 requires subsequent declarations to have the SAME type — structural identity,
        // not mere assignability — so compare the established declared type against the re-declared
        // annotation with TypeInfoEqualityComparer, mirroring CheckVarRedeclaration. The synthesized
        // value (`z`) is irrelevant to the check and is not type-checked.
        if (assign.RedeclarationTypeAnnotation is not null || assign.RedeclarationTypeAnnotationNode is not null)
        {
            // `Any` covers the var-hoisting placeholder and explicit any-typed vars — neither participates.
            if (declaredType is TypeInfo.Any) return declaredType;
            var redeclaredType = ResolveAnnotation(assign.RedeclarationTypeAnnotation, assign.RedeclarationTypeAnnotationNode) ?? new TypeInfo.Any();
            if (!TypeInfoEqualityComparer.Instance.Equals(declaredType, redeclaredType))
            {
                throw new TypeCheckException(
                    $" Subsequent variable declarations must have the same type. Variable '{assign.Name.Lexeme}' must be of type '{declaredType}', but here has type '{redeclaredType}'.",
                    line: assign.Name.Line, tsCode: "TS2403");
            }
            return declaredType;
        }

        TypeInfo valueType = CheckExpr(assign.Value);

        if (!IsCompatible(declaredType, valueType))
        {
            // An assignment synthesized from a duplicate `var` declaration (VarHoister) that
            // fails against the established type is tsc's TS2403: subsequent variable
            // declarations must have the same type.
            if (assign.IsVarRedeclaration)
            {
                throw new TypeCheckException(
                    $" Subsequent variable declarations must have the same type. Variable '{assign.Name.Lexeme}' must be of type '{declaredType}', but here has type '{valueType}'.",
                    tsCode: "TS2403");
            }
            throw new TypeCheckException($" Cannot assign type '{valueType}' to variable '{assign.Name.Lexeme}' of type '{declaredType}'.", tsCode: AssignmentDiagnosticCode(declaredType, valueType));
        }

        // A reassignment widens the variable's OWN narrowing only when the assigned value falls
        // OUTSIDE it: tsc keeps `x` narrowed after `x = stillNarrowValue` (a value the guard still
        // admits) but widens it after a value the guard excluded. The variable's PROPERTY narrowings,
        // however, describe the now-replaced value and are always stale, so they are dropped either
        // way. (#570 — and incidentally the loop over-invalidation where a reassign-to-narrow widened
        // a still-valid loop-condition narrowing.)
        var assignedPath = new Narrowing.NarrowingPath.Variable(assign.Name.Lexeme);
        var currentNarrowedType = GetNarrowing(assignedPath) ?? _environment.Get(assign.Name.Lexeme) ?? declaredType;
        if (IsCompatible(currentNarrowedType, valueType))
        {
            InvalidatePropertyNarrowingsFor(assignedPath);
        }
        else
        {
            // The value escapes the narrowing: drop the variable's own (context-stack) narrowing and
            // its descendants, AND widen any enclosing lexical narrowing the context stack can't reach.
            // `if`-guard variable narrowing lives in a child TypeEnvironment that a nested block's own
            // env shadows-then-discards, so without WidenEnclosingNarrowing a reassignment inside a
            // nested branch leaves the outer guard's narrowing in place for later statements (#570/#654).
            InvalidateNarrowingsFor(assignedPath);
            WidenEnclosingNarrowing(assign.Name.Lexeme, declaredType);
        }

        // Restore the environment binding, undoing any control-flow narrowing applied via
        // TypeEnvironment.Define(). For a tracked (function-local/parameter) variable, restore it to
        // the post-write flow-narrowed type (#653): subsequent reads see the declared type filtered to
        // the members the RHS can be, mirroring the property-write narrowing of #48 (`o.x = "s"`
        // narrows `o.x` to `string`). The narrowed binding lives in the current lexical scope and is
        // discarded at its block's join, so it does not leak a too-narrow type past a conditional.
        //
        // One guard keeps this sound and non-regressive: only TRACKED (function-local/parameter)
        // variables narrow. Module/top-level variables are not in the declared-type stack, so
        // GetDeclaredType falls back to the environment for them — narrowing the binding would then
        // corrupt the declared type a later assignment checks against.
        //
        // A purely-nullish narrowed slot (`null`/`undefined`) IS installed: `x = undefined` narrows
        // to `undefined` (parity with the property-write narrowing of #48). A later `x.length` is now
        // flagged directly on the bare nullish type (ResolveMemberType, #742), so narrowing no longer
        // risks dropping the "possibly null/undefined" diagnostic.
        var postAssignType = declaredType;
        if (IsDeclaredTypeTracked(assign.Name.Lexeme)
            && NarrowToDeclaredSlot(declaredType, valueType) is { } narrowedSlot)
        {
            postAssignType = narrowedSlot;
        }
        _environment.Define(assign.Name.Lexeme, postAssignType);

        // tsc narrows a reference whose declared type is a bare type parameter by assignment:
        // the constraint is the narrowing domain, so after `x = y` the reference reads as the
        // assigned value's base (apparent) type. This is what makes
        //   function f2<T extends string | undefined>(x: T, y: NonNullable<T>) {
        //       x = y; let s: string = x;  // ok — x reads as `string` here
        //   }
        // type-check, while leaving an *unconstrained* `T` (no derivable base type) at `T` so its
        // assignments still error. Only install when a concrete narrower type can be derived.
        if (declaredType is TypeInfo.TypeParameter &&
            AssignmentNarrowedBaseType(valueType) is { } narrowed &&
            narrowed is not (TypeInfo.Any or TypeInfo.Unknown) &&
            !TypeInfoEqualityComparer.Instance.Equals(narrowed, declaredType))
        {
            AddNarrowing(assignedPath, narrowed);
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
        if (name.Lexeme == "Boolean") return new TypeInfo.Any(); // Boolean is a special global object
        if (name.Lexeme == "Symbol") return new TypeInfo.Any(); // Symbol is a special global object
        if (name.Lexeme == "Function") return new TypeInfo.Any(); // Function is the Function constructor
        if (name.Lexeme == "Proxy") return new TypeInfo.Any(); // Proxy is a special global object
        if (name.Lexeme == "Buffer") return new TypeInfo.Any(); // Buffer is a global constructor for binary data
        if (name.Lexeme == "parseInt") return new TypeInfo.Any(); // Global parseInt function
        if (name.Lexeme == "parseFloat") return new TypeInfo.Any(); // Global parseFloat function
        if (name.Lexeme == "isNaN") return new TypeInfo.Any(); // Global isNaN function
        if (name.Lexeme == "isFinite") return new TypeInfo.Any(); // Global isFinite function
        if (name.Lexeme == "eval") return new TypeInfo.Any(); // Global eval(): (s: string) => any
        if (name.Lexeme == "globalThis") return new TypeInfo.Any(); // globalThis ES2020
        if (name.Lexeme == "fetch") return new TypeInfo.Any(); // fetch() global function
        if (name.Lexeme == "setTimeout") return new TypeInfo.Any(); // setTimeout() global function
        if (name.Lexeme == "setInterval") return new TypeInfo.Any(); // setInterval() global function
        if (name.Lexeme == "clearTimeout") return new TypeInfo.Any(); // clearTimeout() global function
        if (name.Lexeme == "clearInterval") return new TypeInfo.Any(); // clearInterval() global function
        if (name.Lexeme == "queueMicrotask") return new TypeInfo.Any(); // queueMicrotask() global function
        if (name.Lexeme == "encodeURIComponent") return new TypeInfo.Any(); // URI encoding global
        if (name.Lexeme == "decodeURIComponent") return new TypeInfo.Any(); // URI decoding global
        // Base64 globals (also exported by the 'buffer' module): (data: string) => string
        if (name.Lexeme == "atob" || name.Lexeme == "btoa")
            return new TypeInfo.Function([new TypeInfo.String()], new TypeInfo.String());
        if (name.Lexeme == "undefined") return new TypeInfo.Undefined(); // Global undefined
        if (name.Lexeme == "NaN") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER); // Global NaN
        if (name.Lexeme == "Infinity") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER); // Global Infinity
        if (name.Lexeme == "__dirname") return new TypeInfo.String(); // Node.js __dirname
        if (name.Lexeme == "__filename") return new TypeInfo.String(); // Node.js __filename
        // CommonJS globals — bound by the CJS module wrapper at runtime. Treated as `any` because
        // CJS modules don't carry static type info for module.exports.
        if (name.Lexeme == "require") return new TypeInfo.Any();
        if (_currentModule?.IsCommonJs == true)
        {
            if (name.Lexeme == "module" || name.Lexeme == "exports" || name.Lexeme == "global")
                return new TypeInfo.Any();
        }
        // Worker-scoped globals — only valid inside a worker_threads worker script,
        // where SharpTSWorker.SetupWorkerGlobals binds them at runtime. Gated on
        // worker context so they stay undefined (TS2304) on the main thread, matching
        // Node (these are not main-thread globals).
        if (_isWorkerContext && name.Lexeme is "parentPort" or "postMessage"
            or "workerData" or "threadId" or "isMainThread")
            return new TypeInfo.Any();
        // Worker Threads globals
        if (name.Lexeme == "structuredClone") return new TypeInfo.Any(); // structuredClone() global function
        if (name.Lexeme == "SharedArrayBuffer") return new TypeInfo.Any(); // SharedArrayBuffer constructor
        if (name.Lexeme == "ArrayBuffer") return new TypeInfo.Any(); // ArrayBuffer constructor
        if (name.Lexeme == "Atomics") return new TypeInfo.Any(); // Atomics static object
        if (name.Lexeme == "MessageChannel") return new TypeInfo.Any(); // MessageChannel constructor
        if (name.Lexeme == "DataView") return new TypeInfo.Any(); // DataView constructor
        if (name.Lexeme == "AbortController") return new TypeInfo.Any(); // AbortController constructor
        if (name.Lexeme == "AbortSignal") return new TypeInfo.Any(); // AbortSignal static namespace
        if (name.Lexeme == "Headers") return new TypeInfo.Any(); // Headers constructor
        if (name.Lexeme == "Request") return new TypeInfo.Any(); // Request constructor
        if (name.Lexeme == "Response") return new TypeInfo.Any(); // Response constructor/namespace
        if (name.Lexeme == "Iterator") return new TypeInfo.Any(); // Iterator namespace (ES2025)
        if (name.Lexeme == "Intl") return new TypeInfo.Any(); // Intl namespace
        // URL / URLSearchParams — migrated to stdlib/node/url.ts; no longer
        // implicit globals. Resolved through normal import lookup.
        if (name.Lexeme is "ReadableStream" or "WritableStream" or "TransformStream"
            or "ByteLengthQueuingStrategy" or "CountQueuingStrategy")
            return new TypeInfo.Any(); // Web Streams constructors
        if (name.Lexeme is "Blob" or "File") return new TypeInfo.Any(); // Blob/File constructors
        // TypedArray constructors
        if (name.Lexeme is "Int8Array" or "Uint8Array" or "Uint8ClampedArray"
            or "Int16Array" or "Uint16Array" or "Int32Array" or "Uint32Array"
            or "Float32Array" or "Float64Array" or "BigInt64Array" or "BigUint64Array")
            return new TypeInfo.Any(); // TypedArray constructor
        // Error constructors (Error, TypeError, RangeError, etc.)
        if (BuiltInNames.IsErrorTypeName(name.Lexeme))
            return new TypeInfo.Any();
        // Built-in constructors that can be referenced as variables.
        // Exposing these as Any lets code like `value instanceof Promise`,
        // `new TextEncoder()`, and `typeof Buffer === 'function'` type-check
        // without a dedicated declaration, mirroring the compile-mode handling
        // in ILEmitter.TryEmitBuiltInClassType.
        if (name.Lexeme is "Map" or "Set" or "WeakMap" or "WeakSet" or "WeakRef"
            or "Date" or "RegExp" or "Promise" or "Buffer"
            or "TextEncoder" or "TextDecoder"
            or "FinalizationRegistry" or "Proxy" or "BroadcastChannel")
            return new TypeInfo.Any();
        // `arguments` is a JS function-scoped array-like, bound at call time by
        // the runtime (SharpTSFunction / ILEmitter's function prologue). The
        // type checker doesn't track function-vs-module context, so we accept
        // it as Any everywhere; runtime throws a ReferenceError if referenced
        // outside a non-arrow function, matching JS semantics.
        if (name.Lexeme == "arguments")
            return new TypeInfo.Any();

        var type = _environment.Get(name.Lexeme);
        if (type == null)
        {
             throw new TypeCheckException($" Undefined variable '{name.Lexeme}'.", tsCode: "TS2304");
        }

        // Check for variable narrowing in the narrowing context
        var path = new Narrowing.NarrowingPath.Variable(name.Lexeme);
        var narrowedType = GetNarrowing(path);
        if (narrowedType != null)
        {
            return narrowedType;
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

        // Resolve superclass if present. Stored as TypeInfo so a MutableClass
        // placeholder (for `any`-typed supers like CJS-imported classes) can
        // also sit in this slot — CheckSuper special-cases MutableClass.
        TypeInfo? superclass = null;
        if (classExpr.SuperclassExpr != null)
        {
            TypeInfo superType = CheckExpr(classExpr.SuperclassExpr);
            if (superType is TypeInfo.Instance si && si.ClassType is TypeInfo.Class sic)
                superclass = sic;
            else if (superType is TypeInfo.Class sc)
                superclass = sc;
            else if (superType is TypeInfo.Any)
            {
                // Extending an `any`-typed expression (e.g. CJS-imported
                // class `orig.Minimatch`). Use a placeholder MutableClass as
                // the superclass so that `super(...)` calls inside this
                // class body type-check as `any` instead of erroring with
                // "does not have a superclass".
                superclass = new TypeInfo.MutableClass("$AnySuperclass");
            }
            else
                throw new TypeCheckException("Superclass must be a class", tsCode: "TS2507");
        }

        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? classTypeParams = null;
        TypeEnvironment classTypeEnv = new(_environment);
        if (classExpr.TypeParams != null && classExpr.TypeParams.Count > 0)
        {
            // Multi-pass under classTypeEnv so a constraint referencing a later parameter resolves
            // (`class<T extends U, U>`). Mirrors the class-declaration path.
            using (new EnvironmentScope(this, classTypeEnv))
                classTypeParams = BuildGenericTypeParameters(classExpr.TypeParams, classTypeEnv);
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
                    : new TypeInfo.Inferred();

                // Wrap return type for generator/async generator methods (skip when inferring).
                // An un-annotated generator method stays a plain <inferred> placeholder so the
                // inferred-return resolution pass below turns it into Generator<yieldType> (#793,
                // mirroring CheckClassDeclaration).
                if (method.ReturnType != null && method.IsGenerator)
                {
                    if (method.IsAsync && returnType is not TypeInfo.AsyncGenerator)
                    {
                        returnType = new TypeInfo.AsyncGenerator(returnType);
                    }
                    else if (!method.IsAsync && returnType is not TypeInfo.Generator)
                    {
                        returnType = new TypeInfo.Generator(returnType);
                    }
                }

                return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, null, paramNames);
            }

            // Computed symbol-keyed methods (`[Symbol.iterator]() {...}`) are modeled under their
            // canonical @@name (@@iterator, …) so structural iterability sees them (#592). They carry the
            // synthetic `<computed>` name, so they're pulled out of the name-keyed overload grouping.
            // An iterator factory's explicit return type is used as-is (not generator-wrapped), so the
            // structural probe reads the right element type.
            foreach (var method in classExpr.Methods.Where(m => m.ComputedKey != null && m.Body != null))
            {
                if (TryGetWellKnownSymbolMemberName(method.ComputedKey) is not { } memberName)
                    continue;
                var (cParamTypes, cRequired, cHasRest, cParamNames) = BuildFunctionSignature(
                    method.Parameters, validateDefaults: true, contextName: $"method '{memberName}'");
                TypeInfo factoryReturn = method.ReturnType != null ? ToTypeInfo(method.ReturnType) : new TypeInfo.Inferred();
                var computedFunc = new TypeInfo.Function(cParamTypes, factoryReturn, cRequired, cHasRest, null, cParamNames);
                if (method.IsStatic)
                    mutableClass.StaticMethods[memberName] = computedFunc;
                else
                    mutableClass.Methods[memberName] = computedFunc;
                (method.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[memberName] = method.Access;
            }

            // Collect method signatures (computed-key methods handled above)
            var methodGroups = classExpr.Methods.Where(m => m.ComputedKey == null).GroupBy(m => (m.IsStatic, Name: m.Name.Lexeme)).ToList();
            foreach (var group in methodGroups)
            {
                string methodName = group.Key.Name;
                var methods = group.ToList();

                var signatures = methods.Where(m => m.Body == null && !m.IsAbstract).ToList();
                var implementations = methods.Where(m => m.Body != null).ToList();

                if (signatures.Count > 0)
                {
                    if (implementations.Count == 0)
                        throw new TypeCheckException($" Overloaded method '{methodName}' has no implementation.", tsCode: "TS2391");
                    if (implementations.Count > 1)
                        throw new TypeCheckException($" Overloaded method '{methodName}' has multiple implementations.", tsCode: "TS2393");

                    var implementation = implementations[0];
                    var signatureTypes = signatures.Select(BuildMethodFuncType).ToList();
                    var implType = BuildMethodFuncType(implementation);

                    foreach (var sig in signatureTypes)
                    {
                        if (implType.MinArity > sig.MinArity)
                            throw new TypeCheckException($" Implementation of '{methodName}' requires {implType.MinArity} arguments but overload signature requires only {sig.MinArity}.", tsCode: "TS2394");
                    }

                    var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, implType);
                    if (implementation.IsStatic)
                        mutableClass.StaticMethods[methodName] = overloadedFunc;
                    else
                        mutableClass.Methods[methodName] = overloadedFunc;
                    (implementation.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = implementation.Access;
                }
                else if (implementations.Count == 1)
                {
                    var method = implementations[0];
                    var funcType = BuildMethodFuncType(method);
                    if (method.IsStatic)
                        mutableClass.StaticMethods[methodName] = funcType;
                    else
                        mutableClass.Methods[methodName] = funcType;
                    (method.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = method.Access;
                }
                else if (implementations.Count > 1)
                {
                    throw new TypeCheckException($" Multiple implementations of method '{methodName}' without overload signatures.", tsCode: "TS2393");
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

                (field.IsStatic ? mutableClass.StaticFieldAccess : mutableClass.FieldAccess)[fieldName] = field.Access;
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
                            ? ResolveAnnotation(accessor.ReturnType, accessor.ReturnTypeNode)!
                            : new TypeInfo.Any();
                        mutableClass.Getters[propName] = getterRetType;
                    }
                    else
                    {
                        TypeInfo paramType = accessor.SetterParam?.Type != null
                            ? ResolveAnnotation(accessor.SetterParam.Type, accessor.SetterParam.TypeAnnotationNode)!
                            : new TypeInfo.Any();
                        mutableClass.Setters[propName] = paramType;
                    }
                }

                // Validate getter/setter type compatibility
                foreach (var propName in mutableClass.Getters.Keys.Intersect(mutableClass.Setters.Keys))
                {
                    if (!IsCompatible(mutableClass.Getters[propName], mutableClass.Setters[propName]))
                        throw new TypeCheckException($" Getter and setter for '{propName}' have incompatible types.", tsCode: "TS2380");
                }
            }
        }

        // Freeze the mutable class
        // For body checking, always use the frozen MutableClass (which is a TypeInfo.Class)
        // This matches CheckClassDeclaration: generic classes store GenericClass externally but use
        // the frozen MutableClass for body checking since it has the same methods/fields structure.
        // `classExprResultType` is the value the expression evaluates to (the binding's type): for a
        // generic class expression that is the GenericClass, so `new <expr>(...)` runs the same
        // type-argument inference as a generic class declaration (issue #291).
        TypeInfo.Class classTypeForBody;
        TypeInfo classExprResultType;
        if (classTypeParams != null && classTypeParams.Count > 0)
        {
            var genericClassType = mutableClass.FreezeGeneric(classTypeParams);
            // Store for later lookups - don't add to outer environment
            // For body check, freeze the mutable class (methods/fields have TypeParameter types)
            classTypeForBody = mutableClass.Freeze();
            _typeMap.SetClassType(className, classTypeForBody);
            _typeMap.SetClassExprType(classExpr, classTypeForBody);
            classExprResultType = genericClassType;
        }
        else
        {
            TypeInfo.Class classType = mutableClass.Freeze();
            _typeMap.SetClassType(className, classType);
            _typeMap.SetClassExprType(classExpr, classType);
            classTypeForBody = classType;
            classExprResultType = classType;
        }

        // Validate interface implementations (skip for generic - validated at instantiation)
        if (classExpr.Interfaces != null && classTypeParams == null)
        {
            for (int i = 0; i < classExpr.Interfaces.Count; i++)
            {
                var interfaceToken = classExpr.Interfaces[i];
                TypeInfo? itfTypeInfo = _environment.Get(interfaceToken.Lexeme);
                if (itfTypeInfo is TypeInfo.Interface interfaceType)
                {
                    ValidateInterfaceImplementation(classTypeForBody, interfaceType, className, classExpr.Name?.Line);
                }
                else if (itfTypeInfo == null && TryResolveIterableProtocolInterface(
                             interfaceToken.Lexeme,
                             classExpr.InterfaceTypeArgs != null && i < classExpr.InterfaceTypeArgs.Count ? classExpr.InterfaceTypeArgs[i] : null,
                             out var protocolType))
                {
                    // Built-in iterable-protocol interface (Iterable<T>, AsyncIterable<T>, …) — not a
                    // user-declared interface, so validate the class structurally implements it (#756).
                    ValidateProtocolInterfaceImplementation(classTypeForBody, protocolType, className);
                }
                else
                {
                    throw new TypeCheckException($" '{interfaceToken.Lexeme}' is not an interface.", tsCode: "TS2304");
                }
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

        // Set when a method's inferred (un-annotated) return type is resolved during the body
        // pass below. The class was frozen with <inferred> placeholders before this pass, so when
        // this is true the frozen class is rebuilt and re-published afterwards (#793; mirrors the
        // class-declaration path #658/#661).
        bool anyInferredMethodReturnResolved = false;

        try
        {
            foreach (var method in classExpr.Methods.Where(m => m.Body != null))
            {
                TypeEnvironment methodEnv;
                if (method.IsStatic)
                    methodEnv = new TypeEnvironment(prevEnv);
                else
                    methodEnv = new TypeEnvironment(_environment);

                TypeInfo declaredMethodType;
                if (method.ComputedKey != null)
                {
                    // Computed methods carry the `<computed>` name; well-known ones are keyed by their
                    // @@name, arbitrary ones aren't modeled — build a signature inline to bind params.
                    string? memberName = TryGetWellKnownSymbolMemberName(method.ComputedKey);
                    var computedDict = method.IsStatic ? classTypeForBody.StaticMethods : classTypeForBody.Methods;
                    if (memberName != null && computedDict.TryGetValue(memberName, out var ct))
                    {
                        declaredMethodType = ct;
                    }
                    else
                    {
                        var (cpt, creq, chr, cpn) = BuildFunctionSignature(
                            method.Parameters, validateDefaults: true, contextName: "computed method");
                        TypeInfo cr = method.ReturnType != null ? ToTypeInfo(method.ReturnType) : new TypeInfo.Inferred();
                        declaredMethodType = new TypeInfo.Function(cpt, cr, creq, chr, null, cpn);
                    }
                }
                else
                {
                    declaredMethodType = method.IsStatic
                        ? classTypeForBody.StaticMethods[method.Name.Lexeme]
                        : classTypeForBody.Methods[method.Name.Lexeme];
                }

                TypeInfo.Function methodType = declaredMethodType switch
                {
                    TypeInfo.OverloadedFunction of => of.Implementation,
                    TypeInfo.Function f => f,
                    // SharpTS-only: internal invariant
                    _ => throw new TypeCheckException($" Unexpected method type for '{method.Name.Lexeme}'.")
                };

                for (int i = 0; i < method.Parameters.Count; i++)
                    methodEnv.Define(method.Parameters[i].Name.Lexeme, methodType.ParamTypes[i]);

                TypeEnvironment previousEnvFunc = _environment;
                TypeInfo? previousReturnFunc = _currentFunctionReturnType;
                var previousInferredFunc = _inferredReturnTypes;
                var previousInferredYieldFunc = _inferredYieldTypes;
                bool previousInStatic = _inStaticMethod;
                bool previousInAsyncFunc = _inAsyncFunction;
                bool previousInGeneratorFunc = _inGeneratorFunction;
                int previousLoopDepthFunc = _loopDepth;
                int previousSwitchDepthFunc = _switchDepth;
                var previousActiveLabelsFunc = new Dictionary<string, bool>(_activeLabels);

                bool inferringMethodReturn = methodType.ReturnType is TypeInfo.Inferred;
                _environment = methodEnv;
                if (inferringMethodReturn)
                {
                    _inferredReturnTypes = new List<TypeInfo>();
                    _currentFunctionReturnType = new TypeInfo.Inferred();
                }
                else
                {
                    _currentFunctionReturnType = methodType.ReturnType;
                }
                // Collect yield operand types only while inferring a generator method's type (#548).
                _inferredYieldTypes = inferringMethodReturn && method.IsGenerator ? new List<TypeInfo>() : null;
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

                        // #367/#372: object-slot number/boolean-typed locals, parameters, and returns
                        // that may hold the undefined sentinel.
                        MarkUndefinedReachableNumericSlots(method.Body, method.Parameters);
                    }

                    // Resolve inferred method return type (#793; mirrors CheckClassDeclaration).
                    if (inferringMethodReturn && method.Body != null)
                    {
                        var collected = _inferredReturnTypes!;
                        _inferredReturnTypes = null;

                        TypeInfo inferredReturn;
                        if (collected.Count == 0)
                        {
                            inferredReturn = new TypeInfo.Void();
                        }
                        else
                        {
                            var distinct = collected.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                            inferredReturn = CollapseOrCreateUnion(distinct);
                        }

                        // A generator method's type argument is its YIELD type (#548), not the
                        // `return`-derived inferredReturn; a non-generator async method wraps in Promise.
                        // The resolved type is re-published below (anyInferredMethodReturnResolved), so
                        // `new C().m()` reads the real Generator<…>/Promise<…> at the call site rather
                        // than the `<inferred>` placeholder.
                        if (method.IsGenerator)
                            inferredReturn = BuildInferredGeneratorType(_inferredYieldTypes!, method.IsAsync);
                        else if (method.IsAsync && inferredReturn is not TypeInfo.Void)
                            inferredReturn = new TypeInfo.Promise(inferredReturn);

                        // Update the method type in the class. Computed symbol-keyed methods are keyed
                        // by their @@name (e.g. @@iterator); an arbitrary computed key (no well-known
                        // @@name) carries no static member to update.
                        var updatedMethodType = new TypeInfo.Function(methodType.ParamTypes, inferredReturn, methodType.RequiredParams, methodType.HasRestParam, methodType.ThisType, methodType.ParamNames);
                        string? mName = method.ComputedKey != null
                            ? TryGetWellKnownSymbolMemberName(method.ComputedKey)
                            : method.Name.Lexeme;
                        if (mName != null)
                        {
                            if (method.IsPrivate)
                            {
                                if (method.IsStatic) mutableClass.StaticPrivateMethods[mName] = updatedMethodType;
                                else mutableClass.PrivateMethods[mName] = updatedMethodType;
                            }
                            else
                            {
                                if (method.IsStatic) mutableClass.StaticMethods[mName] = updatedMethodType;
                                else mutableClass.Methods[mName] = updatedMethodType;
                            }
                            anyInferredMethodReturnResolved = true;
                        }
                    }
                }
                finally
                {
                    _environment = previousEnvFunc;
                    _currentFunctionReturnType = previousReturnFunc;
                    _inferredReturnTypes = previousInferredFunc;
                    _inferredYieldTypes = previousInferredYieldFunc;
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
                    bool previousInStaticAcc = _inStaticMethod;

                    _environment = accessorEnv;
                    _currentFunctionReturnType = accessorReturnType;
                    _loopDepth = 0;
                    _switchDepth = 0;
                    _activeLabels.Clear();
                    _inStaticMethod = accessor.IsStatic;

                    try
                    {
                        foreach (var bodyStmt in accessor.Body)
                            CheckStmt(bodyStmt);

                        // #367/#372: object-slot number/boolean-typed locals and returns that may hold
                        // the undefined sentinel. The setter parameter always uses an object slot.
                        MarkUndefinedReachableNumericSlots(accessor.Body);
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
                        _inStaticMethod = previousInStaticAcc;
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
                    throw new TypeCheckException($" Cannot assign type '{initType}' to field '{field.Name.Lexeme}' of type '{fieldDeclaredType}'.", tsCode: "TS2322");
            }
        }
        finally
        {
            _environment = prevEnv;
            _currentClass = prevClass;
        }

        // Publish method return types inferred during the body pass. The class was frozen with
        // <inferred> placeholders before the body could be checked, so the frozen Class the binding
        // (`const C = class …`) and the TypeMap (read by the compiler) hold still carry the
        // placeholder for every un-annotated method. Rebuild the frozen form from the now-resolved
        // mutable state, re-register the TypeMap, and recompute the result type returned below.
        // Unlike a class declaration, a class expression publishes nothing to the outer environment —
        // its resolved type flows out solely through the return value and the TypeMap (#793).
        if (anyInferredMethodReturnResolved)
        {
            mutableClass.ResetFrozenCache();
            if (classTypeParams != null && classTypeParams.Count > 0)
            {
                var refrozenCore = mutableClass.Freeze();
                _typeMap.SetClassType(className, refrozenCore);
                _typeMap.SetClassExprType(classExpr, refrozenCore);
                classExprResultType = mutableClass.FreezeGeneric(classTypeParams);
            }
            else
            {
                var refrozen = mutableClass.Freeze();
                _typeMap.SetClassType(className, refrozen);
                _typeMap.SetClassExprType(classExpr, refrozen);
                classExprResultType = refrozen;
            }
            // Structural compatibility results cache on CacheKey() (carries the stable DeclarationId),
            // so any comparison made against the placeholder during the body pass must not be reused.
            _compatibilityCache = null;
            _identityCompatibilityCache = null;
        }

        // Return the class type (not an instance). For a generic class expression this is the
        // GenericClass so callers (e.g. `new`) can instantiate/infer its type arguments (#291).
        return classExprResultType;
    }
}
