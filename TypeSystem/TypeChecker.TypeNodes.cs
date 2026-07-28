using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Resolution of syntactic <see cref="TypeNode"/>s to semantic <see cref="TypeInfo"/> — the
/// node-first half of the type-AST migration (docs/plans/type-ast-design.md).
/// </summary>
/// <remarks>
/// Returns null for node kinds (or compositions) this slice doesn't resolve; callers fall back
/// to the authoritative string path. Resolution semantics deliberately REUSE the string path's
/// machinery where names are involved (a bare name has no string-scanning hazards), so the two
/// paths cannot diverge on lookup order, alias expansion, or scoping.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Resolves a type node to a <see cref="TypeInfo"/>, or null when the node (or a component)
    /// has no node-path resolution yet.
    /// </summary>
    internal TypeInfo? TryToTypeInfo(TypeNode node)
    {
        switch (node)
        {
            // A bare name resolves through the shared single-name resolver — type parameters,
            // aliases (node-first), primitives, classes, interfaces, the hot lib globals:
            // identical semantics by construction, with none of the scanning hazards strings
            // have for COMPOSITE types. Never routes back through ToTypeInfo(string).
            case NamedTypeNode { TypeArguments: null } named:
                BindTypeUse(named.NameToken, named.Name);
                return ResolveTypeName(named.Name);

            // Generic references resolve their argument nodes and hand off to the shared
            // instantiation machinery (built-in generics, utility types, generic alias node
            // expansion, generic classes/interfaces/functions — including its TS2314 arity
            // errors). ResolveGenericType owns the built-ins-before-aliases shadowing precedence.
            case NamedTypeNode { TypeArguments: { } argNodes } named:
            {
                BindTypeUse(named.NameToken, named.Name);
                List<TypeInfo> typeArgs = new(argNodes.Count);
                foreach (var argNode in argNodes)
                {
                    if (TryToTypeInfo(argNode) is not { } arg) return null;
                    typeArgs.Add(arg);
                }
                return ResolveGenericType(named.Name, typeArgs);
            }

            case LiteralTypeNode lit:
                return lit.Value switch
                {
                    string str => new TypeInfo.StringLiteral(str),
                    double num => new TypeInfo.NumberLiteral(num),
                    bool b => new TypeInfo.BooleanLiteral(b),
                    System.Numerics.BigInteger bi => new TypeInfo.BigIntLiteral(bi),
                    _ => null,
                };

            // Standalone `unique symbol` is only valid on const declarations initialized with
            // Symbol() — VisitConst special-cases that annotation BEFORE resolution ever runs.
            // Reaching resolution therefore means an invalid position: same TS1331 as the string
            // path's ToTypeInfoCore.
            case UniqueSymbolTypeNode:
                throw new TypeCheckException(
                    "'unique symbol' type is only valid on const declarations initialized with Symbol().", tsCode: "TS1331");

            case ArrayTypeNode arr:
                return TryToTypeInfo(arr.ElementType) is { } elem ? new TypeInfo.Array(elem) : null;

            case UnionTypeNode union:
            {
                List<TypeInfo> members = new(union.Members.Count);
                foreach (var member in union.Members)
                {
                    if (TryToTypeInfo(member) is not { } resolved) return null;
                    members.Add(resolved);
                }
                // Same normalization as the string path's union split: any absorbs, never drops.
                return members.Aggregate(CreateUnion);
            }

            case IntersectionTypeNode intersection:
            {
                List<TypeInfo> members = new(intersection.Members.Count);
                foreach (var member in intersection.Members)
                {
                    if (TryToTypeInfo(member) is not { } resolved) return null;
                    members.Add(resolved);
                }
                // Identical merge rules (primitive conflicts, object-member union, never/any
                // absorption) as the string path's "A & B" split.
                return SimplifyIntersection(members);
            }

            case KeyofTypeNode keyof:
                return TryToTypeInfo(keyof.Operand) is { } operand ? new TypeInfo.KeyOf(operand) : null;

            case IndexedAccessTypeNode indexed:
            {
                if (TryToTypeInfo(indexed.ObjectType) is not { } objectType) return null;
                if (TryToTypeInfo(indexed.IndexType) is not { } indexType) return null;
                // Chained T[K][J] is already nested structurally by the parser, so a single
                // IndexedAccess per node mirrors the string path's iterative suffix consumption.
                // A fully concrete access (`Part[][number]`, `Foo["bar"]`) simplifies to the
                // member type NOW, exactly like the string path (#365) — property access on a
                // raw IndexedAccess reads as `any`. A generic-shaped side stays deferred
                // (SimplifyConcreteIndexedAccess self-defers).
                return SimplifyConcreteIndexedAccess(objectType, indexType);
            }

            // Deferred form — distribution and `infer` inference run later in
            // EvaluateConditionalType, exactly as for a string-built ConditionalType.
            case ConditionalTypeNode conditional:
            {
                if (TryToTypeInfo(conditional.CheckType) is not { } checkType) return null;

                // Infer variables declared in the extends clause are in scope for the TRUE branch
                // only (tsc). Bind each name to the inferred-type-parameter it denotes so a
                // reference like the `U` in `T extends ... infer U ? U : ...` resolves to that
                // placeholder — EvaluateConditionalType then substitutes it with the matched type.
                // Without this the reference falls back to `any`, silently collapsing the true
                // branch to `any` (#316). Binding to an InferredTypeParameter (not a plain type
                // parameter) keeps the established leniency when matching fails to bind it: an
                // unresolved infer placeholder stays uncomparable rather than surfacing as a stray
                // type parameter. The extends clause is resolved in the same scope, harmlessly:
                // `infer U` itself resolves via InferTypeNode, not this name binding.
                List<InferTypeNode>? clauseInfers = null;
                CollectClauseInfers(conditional.ExtendsType, ref clauseInfers);

                TypeInfo? extendsType, trueType;
                if (clauseInfers is { Count: > 0 })
                {
                    var inferEnv = new TypeEnvironment(_environment);
                    foreach (var inf in clauseInfers)
                        inferEnv.DefineTypeParameter(inf.Name, new TypeInfo.InferredTypeParameter(inf.Name));
                    using (new EnvironmentScope(this, inferEnv))
                    {
                        extendsType = TryToTypeInfo(conditional.ExtendsType);
                        trueType = TryToTypeInfo(conditional.TrueType);
                    }
                }
                else
                {
                    extendsType = TryToTypeInfo(conditional.ExtendsType);
                    trueType = TryToTypeInfo(conditional.TrueType);
                }

                if (extendsType is null || trueType is null) return null;
                if (TryToTypeInfo(conditional.FalseType) is not { } falseType) return null;
                // Distributivity comes from the DECLARED check node: a bare name that refers to
                // a type parameter, whether still unbound (resolves to TypeParameter) or already
                // bound to its argument by an alias instantiation scope. A bare name referring
                // to a concrete alias (`type Y = Letters extends "a" ? ...`) does NOT distribute.
                bool distributive = checkType is TypeInfo.TypeParameter ||
                    (conditional.CheckType is NamedTypeNode { TypeArguments: null } checkRef &&
                     _environment.GetTypeParameter(checkRef.Name) is not null);
                return new TypeInfo.ConditionalType(checkType, extendsType, trueType, falseType)
                    { IsDistributive = distributive };
            }

            case InferTypeNode infer:
                if (infer.Constraint is null)
                    return new TypeInfo.InferredTypeParameter(infer.Name);
                return TryToTypeInfo(infer.Constraint) is { } inferConstraint
                    ? new TypeInfo.InferredTypeParameter(infer.Name, inferConstraint)
                    : null;

            // `readonly T[]` / `readonly [A, B]` — mark the resolved array/tuple readonly; any other
            // inner type ignores the modifier (mirrors ToTypeInfoCore's readonly branch).
            case ReadonlyTypeNode ro:
                return TryToTypeInfo(ro.Inner) switch
                {
                    null => null,
                    TypeInfo.Array arr => arr with { IsReadonly = true },
                    TypeInfo.Tuple tup => tup with { IsReadonly = true },
                    { } inner => inner,
                };

            case TypePredicateNode predicate:
                return TryToTypeInfo(predicate.PredicateType) is { } predType
                    ? new TypeInfo.TypePredicate(predicate.ParameterName, predType, predicate.IsAssertion)
                    : null;

            case AssertsNonNullTypeNode asserts:
                return new TypeInfo.AssertsNonNull(asserts.ParameterName);

            // typeof resolves through the same evaluator as the string path; the node only spares
            // the top-level scan that splits unions/intersections before `typeof` (the parser
            // already separated them into sibling nodes).
            case TypeQueryNode query:
                return EvaluateTypeOf(query.EntityPath);

            case GenericFunctionTypeNode genericFn:
            {
                if (TryResolveGenericSignature(genericFn.TypeParameters, genericFn.Body) is not ({ } typeParams, { } func))
                    return null;
                return new TypeInfo.GenericFunction(
                    typeParams, func.ParamTypes, func.ReturnType, func.RequiredParams, func.HasRestParam,
                    func.ThisType, func.ParamNames);
            }

            // `new <T>(…) => R` — an object type with a single GENERIC construct signature, mirroring
            // the string path's "{ new <T>(…) => R }" rendering resolved via ResolveSignature.
            case GenericConstructorTypeNode genericCtor:
            {
                if (TryResolveGenericSignature(genericCtor.TypeParameters, genericCtor.Body) is not ({ } typeParams, { } func))
                    return null;
                var signature = new TypeInfo.ConstructorSignature(
                    typeParams, func.ParamTypes, func.ReturnType, func.RequiredParams, func.HasRestParam, func.ParamNames);
                return new TypeInfo.Record(
                    FrozenDictionary<string, TypeInfo>.Empty,
                    ConstructorSignatures: [signature]);
            }

            case TemplateLiteralTypeNode template:
            {
                List<TypeInfo> interpolated = new(template.InterpolatedTypes.Count);
                foreach (var part in template.InterpolatedTypes)
                {
                    if (TryToTypeInfo(part) is not { } resolved) return null;
                    interpolated.Add(resolved);
                }
                // Same normalization the string path applies: all-concrete → union of string
                // literals; a string-primitive part → pattern TemplateLiteralType.
                return NormalizeTemplateLiteralType(template.Strings, interpolated);
            }

            case FunctionTypeNode fn:
            {
                TypeInfo? thisType = null;
                if (fn.ThisType is { } thisNode)
                {
                    if (TryToTypeInfo(thisNode) is not { } resolvedThis) return null;
                    thisType = resolvedThis;
                }
                if (!TryResolveParameters(fn.Parameters, out var paramTypes, out int requiredParams, out bool hasRestParam))
                    return null;
                if (TryToTypeInfo(fn.ReturnType) is not { } returnType) return null;
                return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRestParam, thisType,
                    InstantiatedTypeParamPositions: MarkInstantiatedParamPositions(fn.Parameters));
            }

            // `new (…) => R` models as an object type carrying a single construct signature —
            // the same shape the string path produces for its "{ new (…) => R }" rendering.
            case ConstructorTypeNode ctor:
            {
                // A `this: X` pseudo-parameter resolves (bad names fail identically) but is not
                // carried: ConstructorSignature has no this-type slot, and the string path's
                // ConvertConstructSignatures drops it the same way.
                if (ctor.ThisType is { } ctorThisNode && TryToTypeInfo(ctorThisNode) is null)
                    return null;
                if (!TryResolveParameters(ctor.Parameters, out var paramTypes, out int requiredParams, out bool hasRestParam))
                    return null;
                if (TryToTypeInfo(ctor.ReturnType) is not { } returnType) return null;
                var signature = new TypeInfo.ConstructorSignature(null, paramTypes, returnType, requiredParams, hasRestParam);
                return new TypeInfo.Record(
                    FrozenDictionary<string, TypeInfo>.Empty,
                    ConstructorSignatures: [signature]);
            }

            case ObjectTypeNode obj:
                return TryResolveObjectType(obj);

            case MappedTypeNode mapped:
                return TryResolveMappedType(mapped);

            case TupleTypeNode tuple:
                return TryResolveTupleType(tuple);

            default:
                return null;
        }
    }

    /// <summary>
    /// Mirror of the string path's <c>ParseInlineObjectTypeInfo</c>: fields with optional/method
    /// markers, index signatures by key kind, call/construct signatures, the
    /// pure-single-call-signature→Function rule, and the same Record shape otherwise.
    /// </summary>
    private TypeInfo? TryResolveObjectType(ObjectTypeNode obj)
    {
        Dictionary<string, TypeInfo> fields = [];
        HashSet<string> optionalFields = [];
        HashSet<string> methodMembers = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;
        List<TypeNode> callSignatures = [];
        List<TypeNode> constructSignatures = [];

        foreach (var member in obj.Members)
        {
            switch (member)
            {
                case PropertyMemberNode prop:
                    if (TryToTypeInfo(prop.Type) is not { } propType) return null;
                    if (prop.IsMethod) methodMembers.Add(prop.Name);
                    if (prop.IsOptional) optionalFields.Add(prop.Name);
                    fields[prop.Name] = propType;
                    break;

                case IndexSignatureNode index:
                    if (TryToTypeInfo(index.ValueType) is not { } valueType) return null;
                    switch (index.KeyKind)
                    {
                        case "string": stringIndexType = valueType; break;
                        case "number": numberIndexType = valueType; break;
                        case "symbol": symbolIndexType = valueType; break;
                    }
                    break;

                case CallSignatureMemberNode call:
                    callSignatures.Add(call.Signature);
                    break;

                case ConstructSignatureMemberNode ctor:
                    constructSignatures.Add(ctor.Signature);
                    break;

                default:
                    return null;
            }
        }

        // A pure single call-signature object type is structurally a plain function type —
        // same rule (and same this-type retention) as the string path.
        if (callSignatures.Count == 1 && constructSignatures.Count == 0 && fields.Count == 0
            && stringIndexType == null && numberIndexType == null && symbolIndexType == null)
        {
            return TryToTypeInfo(callSignatures[0]);
        }

        List<TypeInfo.CallSignature>? recCallSigs = null;
        foreach (var signature in callSignatures)
        {
            // The string path's CallSignature copies drop a `this` type; mirror via the parts.
            // Generic overloads carry their type parameters (resolved through the shared scope);
            // non-generic signatures bind a null tps.
            if (ResolveSignatureNode(signature) is not (var tps, { } f)) return null;
            (recCallSigs ??= []).Add(new TypeInfo.CallSignature(tps, f.ParamTypes, f.ReturnType, f.RequiredParams, f.HasRestParam, f.ParamNames));
        }
        List<TypeInfo.ConstructorSignature>? recCtorSigs = null;
        foreach (var signature in constructSignatures)
        {
            if (ResolveSignatureNode(signature) is not (var tps, { } f)) return null;
            (recCtorSigs ??= []).Add(new TypeInfo.ConstructorSignature(tps, f.ParamTypes, f.ReturnType, f.RequiredParams, f.HasRestParam, f.ParamNames));
        }

        return new TypeInfo.Record(
            fields.ToFrozenDictionary(),
            stringIndexType,
            numberIndexType,
            symbolIndexType,
            optionalFields.Count > 0 ? optionalFields.ToFrozenSet() : null,
            CallSignatures: recCallSigs,
            ConstructorSignatures: recCtorSigs,
            MethodMembers: methodMembers.Count > 0 ? methodMembers.ToFrozenSet() : null);
    }

    /// <summary>
    /// Mirror of the string path's <c>ParseTupleTypeInfo</c>: same element kinds, the same
    /// trailing-rest rule (a last <c>...T[]</c> becomes the rest type; any other spread is a
    /// variadic element), and the same TS1257 required-after-optional rejection.
    /// </summary>
    private TypeInfo? TryResolveTupleType(TupleTypeNode tuple)
    {
        List<TypeInfo.TupleElement> elements = [];
        int requiredCount = 0;
        bool seenOptional = false;
        bool seenSpread = false;
        TypeInfo? restType = null;

        for (int i = 0; i < tuple.Elements.Count; i++)
        {
            var element = tuple.Elements[i];

            if (element.IsRest)
            {
                // Trailing ...T[] is the tuple's rest type; the parser only carries a node for a
                // spread when array-ness agrees between the string and structured views.
                if (i == tuple.Elements.Count - 1 && element.Type is ArrayTypeNode arr)
                {
                    if (TryToTypeInfo(arr.ElementType) is not { } rest) return null;
                    restType = rest;
                    break;
                }
                if (TryToTypeInfo(element.Type) is not { } spreadInner) return null;
                elements.Add(new TypeInfo.TupleElement(spreadInner, TupleElementKind.Spread, null));
                seenSpread = true;
                continue;
            }

            if (element.IsOptional)
            {
                seenOptional = true;
            }
            else if (seenOptional && !seenSpread)
            {
                throw new TypeCheckException("Required element cannot follow optional element in tuple.", tsCode: "TS1257");
            }

            if (TryToTypeInfo(element.Type) is not { } elementType) return null;
            elements.Add(new TypeInfo.TupleElement(
                elementType,
                element.IsOptional ? TupleElementKind.Optional : TupleElementKind.Required,
                element.Name));
            if (!element.IsOptional) requiredCount++;
        }

        return new TypeInfo.Tuple(elements, requiredCount, restType);
    }

    /// <summary>
    /// Resolves a call/construct signature node to its (optional) type parameters and function shape.
    /// A <see cref="GenericFunctionTypeNode"/> resolves through the shared generic-signature scope; a
    /// plain <see cref="FunctionTypeNode"/> yields null type parameters. Returns null (fallback) for
    /// any other node or an unresolvable body.
    /// </summary>
    private (List<TypeInfo.TypeParameter>? TypeParams, TypeInfo.Function Func)? ResolveSignatureNode(TypeNode signature)
    {
        switch (signature)
        {
            case GenericFunctionTypeNode generic:
                return TryResolveGenericSignature(generic.TypeParameters, generic.Body);
            case FunctionTypeNode:
                return TryToTypeInfo(signature) is TypeInfo.Function f ? (null, f) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a generic signature's type parameters and body, shared by generic function types and
    /// generic constructor types. Mirror of the string path's <c>TryParseGenericFunctionTypeInfo</c> /
    /// <c>ResolveSignature</c>: every type-parameter name is defined unconstrained first (so a
    /// constraint/default may forward-reference a later parameter), constraints/defaults then resolve
    /// in that scope, and the body <see cref="FunctionTypeNode"/> resolves with the parameters in
    /// scope so its <c>T</c>s bind to them. Constraints/defaults come from the <see cref="TypeParam"/>
    /// strings (resolved via the shared single-name path), so they cannot diverge from the string
    /// path. Null (e.g. a body component without a node) signals fallback.
    /// </summary>
    private (List<TypeInfo.TypeParameter> TypeParams, TypeInfo.Function Func)? TryResolveGenericSignature(
        List<TypeParam> typeParameters, FunctionTypeNode body)
    {
        var typeParamEnv = new TypeEnvironment(_environment);

        // First pass: declare every name unconstrained.
        foreach (var tp in typeParameters)
            typeParamEnv.DefineTypeParameter(tp.Name.Lexeme, new TypeInfo.TypeParameter(tp.Name.Lexeme));

        var typeParams = new List<TypeInfo.TypeParameter>();
        TypeInfo? bodyType;
        using (new EnvironmentScope(this, typeParamEnv))
        {
            // Second pass: resolve constraints/defaults now that all names are in scope.
            foreach (var tp in typeParameters)
            {
                TypeInfo? constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
                TypeInfo? defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
                var resolved = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType);
                typeParams.Add(resolved);
                typeParamEnv.DefineTypeParameter(tp.Name.Lexeme, resolved);
            }

            bodyType = TryToTypeInfo(body);
        }

        return bodyType is TypeInfo.Function func ? (typeParams, func) : null;
    }

    /// <summary>
    /// Mirror of the string path's <c>ParseMappedTypeInfo</c>: the constraint resolves first, then
    /// the mapped parameter is registered as an open type variable (so the as-clause and value type
    /// build the same deferred forms <c>ExpandMappedType</c> substitutes per key) before those
    /// resolve. The modifier flags translate 1:1 to <see cref="MappedTypeModifiers"/>.
    /// </summary>
    private TypeInfo? TryResolveMappedType(MappedTypeNode mapped)
    {
        MappedTypeModifiers modifiers = MappedTypeModifiers.None;
        if (mapped.AddReadonly) modifiers |= MappedTypeModifiers.AddReadonly;
        if (mapped.RemoveReadonly) modifiers |= MappedTypeModifiers.RemoveReadonly;
        if (mapped.AddOptional) modifiers |= MappedTypeModifiers.AddOptional;
        if (mapped.RemoveOptional) modifiers |= MappedTypeModifiers.RemoveOptional;

        if (TryToTypeInfo(mapped.Constraint) is not { } constraint) return null;

        _openTypeVariablesInScope ??= new HashSet<string>(StringComparer.Ordinal);
        bool openVarAdded = _openTypeVariablesInScope.Add(mapped.ParamName);
        try
        {
            TypeInfo? asClause = null;
            if (mapped.AsClause is { } asNode)
            {
                if (TryToTypeInfo(asNode) is not { } resolvedAs) return null;
                asClause = resolvedAs;
            }
            if (TryToTypeInfo(mapped.ValueType) is not { } valueType) return null;
            return new TypeInfo.MappedType(mapped.ParamName, constraint, valueType, modifiers, asClause);
        }
        finally
        {
            if (openVarAdded) _openTypeVariablesInScope.Remove(mapped.ParamName);
        }
    }

    /// <summary>
    /// Expands a generic alias from its definition node: the type parameters are bound to the
    /// (already-resolved) arguments in a child scope and the definition resolves node-first —
    /// no argument-string substitution, no definition re-parse. Carries the retired string
    /// branch's guards exactly: TS2314 arity, open-type-variable deferral, the TS2589 depth
    /// limit, the recursion placeholder (same instantiation key derivation), the deferred-key
    /// mapped guard, and the same post-expansion passes. Null (a definition component the node
    /// path cannot resolve) makes the caller resolve permissively.
    /// </summary>
    private TypeInfo? TryExpandGenericAliasFromNode(
        string baseName, TypeNode definitionNode, List<string> typeParamNames, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != typeParamNames.Count)
        {
            throw new TypeCheckException(
                $" Type alias '{baseName}' requires {typeParamNames.Count} type argument(s), got {typeArgs.Count}.",
                tsCode: "TS2314");
        }

        // Open type variables (a mapped-type parameter mid-parse) defer instantiation, exactly
        // like the string path (#185).
        if (typeArgs.Any(ContainsOpenTypeVariable))
            return new TypeInfo.RecursiveTypeAlias(baseName, typeArgs);

        var typeArgStrings = typeArgs.Select(TypeInfoToString).ToList();
        string aliasKey = $"{baseName}<{string.Join(",", typeArgStrings)}>";
        _typeAliasExpansionStack ??= new HashSet<string>(StringComparer.Ordinal);

        if (_typeAliasExpansionStack.Count >= MaxTypeAliasExpansionDepth)
        {
            throw new TypeCheckException(
                " Type instantiation is excessively deep and possibly infinite.",
                tsCode: "TS2589");
        }

        if (_typeAliasExpansionStack.Contains(aliasKey))
            return new TypeInfo.RecursiveTypeAlias(baseName, typeArgs);

        _typeAliasExpansionStack.Add(aliasKey);
        try
        {
            var aliasEnv = new TypeEnvironment(_environment);
            for (int i = 0; i < typeParamNames.Count; i++)
                aliasEnv.DefineTypeParameter(typeParamNames[i], typeArgs[i]);

            TypeInfo? result;
            using (new EnvironmentScope(this, aliasEnv))
            {
                result = TryToTypeInfo(definitionNode);
            }
            if (result is null) return null;

            // A nested alias may have expanded via the string path and produced a deferred
            // conditional/mapped form — apply the same post-expansion passes as the string path.
            if (result is TypeInfo.ConditionalType condResult && !ContainsOpenTypeVariable(condResult))
                result = EvaluateConditionalType(condResult);
            // A mapped type over a DEFERRED key domain (a generic key-filter such as
            // Pick<T, FunctionPropertyNames<T>>) must stay a MappedType node: enumerating it now
            // would yield an empty object and lose the key filter, so it has to reach the relation
            // rules deferred (#337 item 1). Same guard as the string path's alias branch — without
            // it, the conditionalTypes1 f7/f8 assignability verdicts flip.
            if (result is TypeInfo.MappedType mappedResult && !ContainsOpenTypeVariable(mappedResult)
                && !IsDeferredKeyDomain(ResolveMappedKeyDomain(mappedResult)))
                result = ExpandMappedType(mappedResult);

            result = FlattenTupleSpreads(result);
            ValidateSpreadConstraints(result);
            return result;
        }
        finally
        {
            _typeAliasExpansionStack.Remove(aliasKey);
        }
    }

    /// <summary>
    /// Parameter positions whose node is a bare type-parameter reference currently bound to a
    /// CONCRETE type — i.e. the position is being instantiated right now (alias expansion binds
    /// arguments via DefineTypeParameter). The structural equivalent of
    /// <see cref="MarkInstantiatedParamPositions(TypeInfo.Function, Dictionary{string, TypeInfo})"/>
    /// for node-path resolution, where substitution happens by scope binding instead of a
    /// rewrite. In ordinary generic-declaration scopes the binding is itself a TypeParameter,
    /// so nothing marks.
    /// </summary>
    private FrozenSet<int>? MarkInstantiatedParamPositions(List<ParameterTypeNode> parameters)
    {
        HashSet<int>? marks = null;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Type is NamedTypeNode { TypeArguments: null } named &&
                _environment.GetTypeParameter(named.Name) is { } bound &&
                bound is not TypeInfo.TypeParameter)
            {
                (marks ??= []).Add(i);
            }
        }
        return marks?.ToFrozenSet();
    }

    /// <summary>
    /// Resolves a function/constructor type node's parameter list with the string path's arity
    /// accounting: optional/rest parameters are not required, and nothing after the first
    /// optional/rest parameter counts as required. False when any parameter type lacks a node-path
    /// resolution (the whole signature then falls back to the string).
    /// </summary>
    private bool TryResolveParameters(
        List<ParameterTypeNode> parameters,
        out List<TypeInfo> paramTypes,
        out int requiredParams,
        out bool hasRestParam)
    {
        paramTypes = new List<TypeInfo>(parameters.Count);
        requiredParams = 0;
        hasRestParam = false;
        bool sawOptionalOrRest = false;

        foreach (var parameter in parameters)
        {
            if (TryToTypeInfo(parameter.Type) is not { } paramType) return false;
            paramTypes.Add(paramType);

            if (parameter.IsRest)
            {
                hasRestParam = true;
                sawOptionalOrRest = true;
            }
            else if (parameter.IsOptional)
            {
                sawOptionalOrRest = true;
            }
            else if (!sawOptionalOrRest)
            {
                requiredParams++;
            }
        }
        return true;
    }

    /// <summary>
    /// Resolves a variable annotation node-first with string fallback, recording coverage stats.
    /// </summary>
    private TypeInfo? ResolveAnnotation(string? annotation, TypeNode? annotationNode)
    {
        if (annotationNode is not null && TryToTypeInfo(annotationNode) is { } fromNode)
        {
            TypeNodeStats.NodeHits++;
            return fromNode;
        }
        if (annotation is null) return null;
        TypeNodeStats.StringFallbacks++;
        return ToTypeInfo(annotation);
    }

    /// <summary>
    /// Resolves the i-th explicit type argument of a call/new/heritage clause node-first
    /// (type-AST migration): the node twin when the parser produced one, string fallback
    /// otherwise. The node list, when non-null, is index-aligned with the string list.
    /// Callers only invoke inside count-guarded branches, so the string list is never null.
    /// </summary>
    private TypeInfo ResolveTypeArg(List<string>? typeArgs, List<TypeNode?>? typeArgNodes, int i) =>
        ResolveAnnotation(typeArgs![i], typeArgNodes is { } nodes && i < nodes.Count ? nodes[i] : null)!;
}
