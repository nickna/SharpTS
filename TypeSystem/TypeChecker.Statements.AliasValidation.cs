using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Declaration-time validation of type-alias bodies: infer placement (TS1338), out-of-scope
/// infer references (TS2304), generic-reference type-argument constraints (TS2344), and
/// mapped-type key constraints (TS2322). tsc reports these when it CHECKS the alias
/// declaration; lazy expansion alone never surfaces them.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Declared constraint strings per generic-alias type parameter, keyed by alias name.
    /// <see cref="TypeEnvironment.DefineGenericTypeAlias"/> stores only parameter NAMES, and
    /// reference-site TS2344 validation needs the constraints. Checker-scoped (not
    /// scope-aware) — same-named aliases in different scopes share an entry, acceptable for a
    /// best-effort validation that skips when in doubt.
    /// </summary>
    private Dictionary<string, List<(string? Constraint, TypeNode? ConstraintNode)>>? _aliasParamConstraints;

    private void RecordAliasParamConstraints(Stmt.TypeAlias stmt)
    {
        if (stmt.TypeParameters is not { Count: > 0 } tps) return;
        _aliasParamConstraints ??= new(StringComparer.Ordinal);
        _aliasParamConstraints[stmt.Name.Lexeme] = tps.Select(tp => (tp.Constraint, tp.ConstraintNode)).ToList();
    }

    /// <summary>
    /// Validates an alias body's type-node tree. Runs after the alias is defined so the alias
    /// stays usable when its body is malformed. Throws on the first violation (per-statement
    /// recovery reports it and moves on).
    /// </summary>
    private void ValidateAliasBody(Stmt.TypeAlias stmt)
    {
        if (stmt.TypeDefinitionNode is not { } body) return;

        // Bind the alias's own type parameters (with constraints) so references inside the
        // body resolve to TypeParameter — constraint checks then go through apparent types.
        var aliasEnv = new TypeEnvironment(_environment);
        if (stmt.TypeParameters is { Count: > 0 } tps)
        {
            var previous = _environment;
            _environment = aliasEnv;
            try { BuildGenericTypeParameters(tps, aliasEnv); }
            catch (TypeCheckException) { /* malformed constraint — reported elsewhere */ }
            finally { _environment = previous; }
        }

        var previousEnv = _environment;
        _environment = aliasEnv;
        try
        {
            ValidateAliasBodyNode(body, inferLegal: false, infersOutOfScope: null);
        }
        finally
        {
            _environment = previousEnv;
        }
    }

    private void ValidateAliasBodyNode(TypeNode node, bool inferLegal, HashSet<string>? infersOutOfScope)
    {
        switch (node)
        {
            case InferTypeNode infer when !inferLegal:
                throw new TypeCheckException(
                    " 'infer' declarations are only permitted in the 'extends' clause of a conditional type.",
                    infer.Line, tsCode: "TS1338");

            case NamedTypeNode { TypeArguments: null } bare:
                // A name that is only bound as an infer of an enclosing conditional, referenced
                // from a position where infers are not in scope (check type, false branch).
                // Restricted to KNOWN infer names — general unknown-name TS2304 is parked (the
                // any-fallback is load-bearing in deferred-resolution contexts).
                if (infersOutOfScope is not null && infersOutOfScope.Contains(bare.Name) &&
                    _environment.GetTypeParameter(bare.Name) is null &&
                    _environment.GetTypeAlias(bare.Name) is null &&
                    _environment.GetGenericTypeAlias(bare.Name) is null)
                {
                    throw new TypeCheckException(
                        $" Cannot find name '{bare.Name}'.", bare.Line, tsCode: "TS2304");
                }
                return;

            case NamedTypeNode { TypeArguments: { } argNodes } genericRef:
                ValidateGenericReferenceArguments(genericRef, argNodes);
                foreach (var arg in argNodes)
                    ValidateAliasBodyNode(arg, inferLegal, infersOutOfScope);
                return;

            case MappedTypeNode mapped:
            {
                // The `in` clause's keys must be key-like (tsc TS2322: "Type 'T' is not
                // assignable to type 'string | number | symbol'"). Conditional-type narrowing
                // of the key source is handled by the conditional case re-binding parameters.
                // A type-parameter key is judged by its apparent type: unconstrained = error
                // (inferTypes1 `string extends T ? { [P in T]: void } : T`), undecidable
                // constraint (keyof any) = skip.
                var keys = TryToTypeInfo(mapped.Constraint);
                if (keys is TypeInfo.TypeParameter keyParam)
                    keys = ApparentTypeOf(keyParam) ?? TypeInfo.Unknown.Shared;
                if (keys is not null && !IsGenericKeySourceUndecidable(keys) &&
                    !IsCompatible(KeyLikeUnion, keys))
                {
                    throw new TypeCheckException(
                        $" Type '{TryToTypeInfo(mapped.Constraint)}' is not assignable to type 'string | number | symbol'.",
                        mapped.Line, tsCode: "TS2322");
                }
                foreach (var child in TypeNodeChildren(node))
                    ValidateAliasBodyNode(child, inferLegal, infersOutOfScope);
                return;
            }

            case ConditionalTypeNode conditional:
            {
                List<InferTypeNode>? clauseInfers = null;
                CollectClauseInfers(conditional.ExtendsType, ref clauseInfers);
                var inferNames = clauseInfers?.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

                HashSet<string>? outOfScope = infersOutOfScope;
                if (inferNames is not null)
                {
                    outOfScope = infersOutOfScope is null ? inferNames : [.. infersOutOfScope, .. inferNames];
                }

                // Check type and false branch: infers of THIS clause are not in scope. Legality
                // of `infer` PLACEMENT is inherited, not reset — once inside some enclosing
                // extends clause, a nested conditional's check position may declare infers
                // (inferTypesWithExtends1 X10/X11/X15/X17).
                ValidateAliasBodyNode(conditional.CheckType, inferLegal, outOfScope);
                ValidateAliasBodyNode(conditional.FalseType, inferLegal, outOfScope);

                // Extends clause: infer declarations are legal here.
                ValidateAliasBodyNode(conditional.ExtendsType, inferLegal: true, infersOutOfScope);

                // True branch: this clause's infers are in scope, bound with their positional
                // constraint (the constraint of the parameter slot the infer occupies — tsc
                // checks `T70<U>` against U's inferred constraint, inferTypes1 T73). A naked
                // type-parameter check is narrowed by the extends type (substitution-type
                // semantics), so `T extends string ? { [P in T]: void } : T` passes the
                // mapped-key rule while the un-narrowed form fails it.
                var trueEnv = new TypeEnvironment(_environment);
                if (clauseInfers is not null)
                {
                    var positional = CollectPositionalInferConstraints(conditional.ExtendsType);
                    foreach (var infer in clauseInfers)
                    {
                        TypeInfo? constraint = infer.Constraint is { } cNode ? TryToTypeInfo(cNode) : null;
                        if (constraint is null && positional is not null)
                            positional.TryGetValue(infer.Name, out constraint);
                        trueEnv.DefineTypeParameter(infer.Name, new TypeInfo.TypeParameter(infer.Name, constraint));
                    }
                }
                if (conditional.CheckType is NamedTypeNode { TypeArguments: null } checkRef &&
                    _environment.GetTypeParameter(checkRef.Name) is TypeInfo.TypeParameter checkParam &&
                    TryToTypeInfo(conditional.ExtendsType) is { } extendsResolved &&
                    !ContainsConditionalType(extendsResolved))
                {
                    var narrowed = checkParam.Constraint is { } existing
                        ? SimplifyIntersection([existing, extendsResolved])
                        : extendsResolved;
                    trueEnv.DefineTypeParameter(checkRef.Name, checkParam with { Constraint = narrowed });
                }
                var previousEnv = _environment;
                _environment = trueEnv;
                try
                {
                    ValidateAliasBodyNode(conditional.TrueType, inferLegal, infersOutOfScope);
                }
                finally
                {
                    _environment = previousEnv;
                }
                return;
            }

            default:
                foreach (var child in TypeNodeChildren(node))
                    ValidateAliasBodyNode(child, inferLegal, infersOutOfScope);
                return;
        }
    }

    private static readonly TypeInfo KeyLikeUnion = new TypeInfo.Union(
        [TypeInfo.String.Shared, TypeInfo.Primitive.Number, TypeInfo.Symbol.Shared]);

    /// <summary>
    /// Key sources whose membership can't be decided at declaration time (deferred conditionals,
    /// indexed accesses, keyof over generics) are skipped rather than guessed.
    /// </summary>
    private static bool IsGenericKeySourceUndecidable(TypeInfo keys) => keys switch
    {
        TypeInfo.KeyOf or TypeInfo.IndexedAccess or TypeInfo.ConditionalType
            or TypeInfo.InferredTypeParameter or TypeInfo.MappedType or TypeInfo.RecursiveTypeAlias => true,
        TypeInfo.Union u => u.Types.Any(IsGenericKeySourceUndecidable),
        TypeInfo.Intersection i => i.Types.Any(IsGenericKeySourceUndecidable),
        _ => false
    };

    /// <summary>
    /// Derives the constraint each unconstrained <c>infer</c> inherits from the parameter slot
    /// it occupies in the extends clause: in <c>T extends T72&lt;infer U&gt;</c> where
    /// <c>T72&lt;T extends number&gt;</c>, U's constraint is <c>number</c>. Only generic-reference
    /// argument slots are mined — other positions contribute nothing.
    /// </summary>
    private Dictionary<string, TypeInfo>? CollectPositionalInferConstraints(TypeNode extendsNode)
    {
        Dictionary<string, TypeInfo>? result = null;
        Walk(extendsNode);
        return result;

        void Walk(TypeNode node)
        {
            if (node is NamedTypeNode { TypeArguments: { } args } reference)
            {
                var constraints = GetReferenceParamConstraints(reference.Name);
                for (int i = 0; i < args.Count; i++)
                {
                    if (args[i] is InferTypeNode { Constraint: null } infer &&
                        constraints is not null && i < constraints.Count &&
                        constraints[i] is { } constraint)
                    {
                        if (!IsGenericTypeShape(constraint))
                        {
                            result ??= new(StringComparer.Ordinal);
                            // An infer occupying multiple slots must satisfy ALL of them — the
                            // effective constraint is the intersection (T74<infer U, infer U>
                            // with slots number/string ⇒ never, which satisfies everything;
                            // tsc reports no error there).
                            result[infer.Name] = result.TryGetValue(infer.Name, out var existing)
                                ? SimplifyIntersection([existing, constraint])
                                : constraint;
                        }
                    }
                }
            }
            if (node is ConditionalTypeNode) return; // nested clause = its own scope
            foreach (var child in TypeNodeChildren(node))
                Walk(child);
        }
    }

    /// <summary>Declared constraints for a referenced generic's parameters (resolved), or null.
    /// Alias constraints resolve node-first from their declaration spelling (unresolvable ones
    /// stay null and are skipped); class/interface type-parameter constraints are already
    /// <see cref="TypeInfo"/> and are used directly — no rendered-string round-trip.</summary>
    private List<TypeInfo?>? GetReferenceParamConstraints(string name)
    {
        if (_aliasParamConstraints is not null && _aliasParamConstraints.TryGetValue(name, out var fromAlias))
            return fromAlias.Select(c =>
            {
                try { return ResolveAnnotation(c.Constraint, c.ConstraintNode); }
                catch (TypeCheckException) { return null; }
            }).ToList();
        return _environment.GetTypeBinding(name) switch
        {
            TypeInfo.GenericClass gc => gc.TypeParams.Select(tp => tp.Constraint).ToList(),
            TypeInfo.GenericInterface gi => gi.TypeParams.Select(tp => tp.Constraint).ToList(),
            _ => null
        };
    }

    /// <summary>
    /// TS2344: every type argument of a generic reference must satisfy the referenced
    /// parameter's declared constraint. Skips slots whose constraint (or argument) is still
    /// generic — those can only be judged at instantiation time.
    /// </summary>
    private void ValidateGenericReferenceArguments(NamedTypeNode reference, List<TypeNode> argNodes)
    {
        // Built-in function-shaped utility types: tsc's lib declares
        // `ReturnType<T extends (...args: any) => any>` and the abstract-constructor pair.
        if (reference.Name is "ReturnType" or "Parameters")
        {
            ValidateUtilityArg(reference, argNodes, RequireCallable);
            return;
        }
        if (reference.Name is "InstanceType" or "ConstructorParameters")
        {
            ValidateUtilityArg(reference, argNodes, RequireConstructable);
            return;
        }

        var constraints = GetReferenceParamConstraints(reference.Name);
        if (constraints is null) return;
        for (int i = 0; i < argNodes.Count && i < constraints.Count; i++)
        {
            // A constraint that still references the target's own parameters (T76<T extends T[]>)
            // can't be judged against an outer-scope argument — skip.
            if (constraints[i] is not { } constraint || IsGenericTypeShape(constraint)) continue;

            // Same `Function`-global discrimination as the built-in utilities: it satisfies no
            // concrete call signature, but resolves to a shape that would.
            if (constraint is TypeInfo.Function &&
                argNodes[i] is NamedTypeNode { Name: "Function", TypeArguments: null })
            {
                throw new TypeCheckException(
                    $" Type 'Function' does not satisfy the constraint '{constraint}'.",
                    reference.Line, tsCode: "TS2344");
            }

            if (TryToTypeInfo(argNodes[i]) is not { } arg) continue;
            // Generic arguments are judged by their own constraint when they have one.
            if (arg is TypeInfo.TypeParameter { Constraint: { } argConstraint } argParam)
            {
                if (!IsGenericTypeShape(argConstraint) && !IsCompatible(constraint, argConstraint))
                {
                    throw new TypeCheckException(
                        $" Type '{argParam.Name}' does not satisfy the constraint '{constraint}'.",
                        reference.Line, tsCode: "TS2344");
                }
                continue;
            }
            if (arg is TypeInfo.InferredTypeParameter || IsGenericTypeShape(arg)) continue;
            if (!IsCompatible(constraint, arg))
            {
                throw new TypeCheckException(
                    $" Type '{arg}' does not satisfy the constraint '{constraint}'.",
                    reference.Line, tsCode: "TS2344");
            }
        }
    }

    private void ValidateUtilityArg(NamedTypeNode reference, List<TypeNode> argNodes, Func<TypeInfo, bool> satisfies)
    {
        if (argNodes.Count != 1) return;
        // The global `Function` interface has no concrete call/construct signature — tsc
        // rejects it against both shapes ("Type 'Function' provides no match for the
        // signature"). Our resolution collapses it to a catch-all function type, so the
        // discrimination has to happen on the reference node.
        if (argNodes[0] is NamedTypeNode { Name: "Function", TypeArguments: null })
            ThrowUtilityConstraintError(reference, new TypeInfo.Interface("Function",
                System.Collections.Frozen.FrozenDictionary<string, TypeInfo>.Empty,
                System.Collections.Frozen.FrozenSet<string>.Empty));
        if (TryToTypeInfo(argNodes[0]) is not { } arg) return;
        if (arg is TypeInfo.TypeParameter { Constraint: { } tpConstraint })
        {
            if (!IsGenericTypeShape(tpConstraint) && !satisfies(tpConstraint))
                ThrowUtilityConstraintError(reference, arg);
            return;
        }
        if (arg is TypeInfo.Any or TypeInfo.Never or TypeInfo.InferredTypeParameter) return;
        if (IsGenericTypeShape(arg)) return;
        if (!satisfies(arg)) ThrowUtilityConstraintError(reference, arg);
    }

    private static void ThrowUtilityConstraintError(NamedTypeNode reference, TypeInfo arg) =>
        throw new TypeCheckException(
            $" Type '{arg}' does not satisfy the constraint of '{reference.Name}'.",
            reference.Line, tsCode: "TS2344");

    /// <summary>Shapes acceptable to `T extends (...args: any) => any`.</summary>
    private bool RequireCallable(TypeInfo arg) => arg switch
    {
        TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction => true,
        TypeInfo.Union u => u.FlattenedTypes.All(RequireCallable),
        TypeInfo.Intersection i => i.FlattenedTypes.Any(RequireCallable),
        TypeInfo.Record r => r.IsCallable,
        TypeInfo.Interface itf => itf.CallSignatures is { Count: > 0 },
        _ => false
    };

    /// <summary>Shapes acceptable to `T extends abstract new (...args: any) => any`.</summary>
    private bool RequireConstructable(TypeInfo arg) => arg switch
    {
        TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass => true,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass } => true,
        TypeInfo.Union u => u.FlattenedTypes.All(RequireConstructable),
        TypeInfo.Intersection i => i.FlattenedTypes.Any(RequireConstructable),
        TypeInfo.Record r => r.IsConstructable,
        TypeInfo.Interface itf => itf.ConstructorSignatures is { Count: > 0 },
        _ => false
    };
}
