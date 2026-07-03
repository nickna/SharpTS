using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Generic type handling - core instantiation and type argument resolution.
/// </summary>
/// <remarks>
/// Contains core generic type methods:
/// ParseGenericTypeReference, ResolveGenericType, SplitTypeArguments, SubstituteTypeParamInString,
/// InstantiateGenericClass, InstantiateGenericInterface, InstantiateGenericFunction,
/// ResolveTypeArgumentsWithDefaults.
///
/// Related partial files:
/// - TypeChecker.Generics.Substitution.cs: Type parameter substitution and tuple flattening
/// - TypeChecker.Generics.Inference.cs: Type argument inference from call arguments
/// - TypeChecker.Generics.MappedTypes.cs: KeyOf evaluation and mapped type expansion
/// - TypeChecker.Generics.UtilityTypes.cs: Built-in utility type expansions (Partial, Required, etc.)
/// - TypeChecker.Generics.Conditional.cs: Conditional type evaluation and infer patterns
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Parses a generic type reference like "Box&lt;number&gt;" or "Map&lt;string, number&gt;".
    /// Also handles array suffixes: "Partial&lt;T&gt;[]", "Box&lt;number&gt;[][]".
    /// </summary>
    private TypeInfo ParseGenericTypeReference(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        string baseName = typeName[..openAngle];

        // Find matching closing '>' respecting nested angle brackets
        // Skip `>` that is part of `=>` (arrow function syntax)
        int angleDepth = 0;
        int closeAngle = -1;
        for (int i = openAngle; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '<') angleDepth++;
            else if (c == '>')
            {
                // Skip `>` that is part of `=>` (arrow function return type)
                if (i > 0 && typeName[i - 1] == '=')
                    continue;

                angleDepth--;
                if (angleDepth == 0)
                {
                    closeAngle = i;
                    break;
                }
            }
        }

        string argsStr = typeName[(openAngle + 1)..closeAngle];
        string suffix = typeName[(closeAngle + 1)..];

        // Split type arguments respecting nesting
        var typeArgStrings = SplitTypeArguments(argsStr);
        var typeArgs = typeArgStrings.Select(ToTypeInfo).ToList();

        return ResolveGenericType(baseName, typeArgs, suffix);
    }

    /// <summary>
    /// Resolves a generic type with pre-parsed TypeInfo arguments.
    /// </summary>
    /// <param name="baseName">The generic type name (e.g., "Promise", "Box").</param>
    /// <param name="typeArgs">The type arguments as TypeInfo objects.</param>
    /// <param name="suffix">Optional array suffix (e.g., "[][]") — string-path callers only.</param>
    /// <returns>The resolved type.</returns>
    private TypeInfo ResolveGenericType(string baseName, List<TypeInfo> typeArgs, string suffix = "")
    {
        TypeInfo result;

        // Handle built-in generic types
        if (baseName is "Array" or "ReadonlyArray")
        {
            // Array<T> is the generic spelling of T[] — without this it would fall through the
            // user-generics lookup to `any`, making every Array<T>-typed position vacuously
            // compatible. ReadonlyArray<T> carries the readonly flag so an interface extending it
            // (DeepReadonlyArray) gets a read-only numeric index signature (#337 item 2).
            if (typeArgs.Count != 1)
            {
                throw new TypeCheckException($" {baseName} requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            }
            result = new TypeInfo.Array(typeArgs[0], IsReadonly: baseName == "ReadonlyArray");
        }
        else if (baseName == "Promise")
        {
            if (typeArgs.Count != 1)
            {
                throw new TypeCheckException($" Promise requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            }
            // Flatten nested Promises: Promise<Promise<T>> -> Promise<T>
            TypeInfo valueType = typeArgs[0];
            while (valueType is TypeInfo.Promise nested)
            {
                valueType = nested.ValueType;
            }
            result = new TypeInfo.Promise(valueType);
        }
        // The lib signatures are Generator<T = unknown, TReturn = any, TNext = unknown> and
        // AsyncGenerator<T = unknown, TReturn = any, TNext = unknown> — TReturn/TNext are defaulted, so a
        // reference may carry 1–3 arguments. SharpTS models only the yield type (T), so the extra two are
        // accepted and dropped, exactly like the Iterator arm below. Out-of-range is TS2707 (the code tsc
        // uses for a defaulted-arity range), not the exact-count TS2314 (#487).
        else if (baseName == "Generator")
        {
            if (typeArgs.Count is < 1 or > 3)
            {
                throw new TypeCheckException($" Generator requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            }
            result = new TypeInfo.Generator(typeArgs[0]);
        }
        else if (baseName == "AsyncGenerator")
        {
            if (typeArgs.Count is < 1 or > 3)
            {
                throw new TypeCheckException($" AsyncGenerator requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            }
            result = new TypeInfo.AsyncGenerator(typeArgs[0]);
        }
        // The built-in collections carry dedicated TypeInfo records (with IsCompatible + member-type
        // support) but were previously only constructed from `new Map()`/`new Set()` expressions — a
        // type REFERENCE `Map<K, V>` fell through to the `any` fallback below, so annotations and
        // conditional-type check sides lost their element types (#347, the `Map<…, infer V>` example).
        else if (baseName == "Map")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Map requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.Map(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Set")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Set requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.Set(typeArgs[0]);
        }
        else if (baseName == "WeakMap")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" WeakMap requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.WeakMap(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "WeakSet")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" WeakSet requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.WeakSet(typeArgs[0]);
        }
        // IterableIterator<T> / Iterator<T> references resolve to the dedicated Iterator record (the
        // same one .keys()/.values()/.entries() already produce) instead of the `any` fallback, so the
        // annotations are strongly typed and conditional-type check sides keep their element type
        // (#456). The lib signature is Iterator<T, TReturn = any, TNext = any> — SharpTS models only the
        // element type, so 1–3 arguments are accepted and TReturn/TNext are dropped, matching how the
        // Generator arm above keeps only its yield type.
        else if (baseName is "Iterator" or "IterableIterator")
        {
            if (typeArgs.Count is < 1 or > 3)
                throw new TypeCheckException($" {baseName} requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            result = new TypeInfo.Iterator(typeArgs[0]);
        }
        // The async iterator-protocol references mirror their sync counterparts (#483, async parallel of
        // #456): AsyncIterator<T> / AsyncIterableIterator<T> collapse onto the dedicated AsyncIterator
        // record (as Iterator/IterableIterator collapse onto Iterator), and AsyncIterable<T> resolves to
        // the AsyncIterable record — all previously degraded to `any`. The lib signatures default
        // TReturn/TNext (AsyncIterator<T, TReturn = any, TNext = any>), so 1–3 arguments are accepted and
        // only the element type is kept; out-of-range is TS2707.
        else if (baseName is "AsyncIterator" or "AsyncIterableIterator")
        {
            if (typeArgs.Count is < 1 or > 3)
                throw new TypeCheckException($" {baseName} requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            result = new TypeInfo.AsyncIterator(typeArgs[0]);
        }
        else if (baseName == "AsyncIterable")
        {
            if (typeArgs.Count is < 1 or > 3)
                throw new TypeCheckException($" AsyncIterable requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            result = new TypeInfo.AsyncIterable(typeArgs[0]);
        }
        // Iterable<T> references resolve to the dedicated Iterable record so the annotation is element-typed
        // (for...of/spread/yield* and assignment) rather than degrading to `any` (#485). The newer lib
        // signature is Iterable<T, TReturn = void, TNext = undefined> — only the element type is modeled, so
        // 1–3 arguments are accepted and the rest are dropped, mirroring the Iterator arm above. (The async
        // parallel — AsyncIterable/AsyncIterator/AsyncIterableIterator — is handled by the arms above, #483.)
        else if (baseName == "Iterable")
        {
            if (typeArgs.Count is < 1 or > 3)
                throw new TypeCheckException($" Iterable requires between 1 and 3 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            result = new TypeInfo.Iterable(typeArgs[0]);
        }
        // IteratorResult<T, TReturn = any> and its IteratorYieldResult/IteratorReturnResult arms are
        // structural types in TS ({ value; done }); SharpTS models them as the structural record
        // { value: T; done?: boolean } so a `next(): IteratorResult<T>` annotation is element-typed and a
        // hand-written { value, done } object literal satisfies it (#485). Only the value type is kept
        // (matching the single-type-param convention used for Iterator/Generator).
        else if (baseName == "IteratorResult")
        {
            if (typeArgs.Count is < 1 or > 2)
                throw new TypeCheckException($" IteratorResult requires 1 or 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2707");
            result = BuildIteratorResultType(typeArgs[0]);
        }
        else if (baseName is "IteratorYieldResult" or "IteratorReturnResult")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" {baseName} requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = BuildIteratorResultType(typeArgs[0]);
        }
        else if (baseName == "WeakRef")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" WeakRef requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.WeakRef(typeArgs[0]);
        }
        else if (baseName == "FinalizationRegistry")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" FinalizationRegistry requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = new TypeInfo.FinalizationRegistry(typeArgs[0]);
        }
        // Handle built-in utility types
        else if (baseName == "Partial")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Partial<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandPartial(typeArgs[0]);
        }
        else if (baseName == "Required")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Required<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandRequired(typeArgs[0]);
        }
        else if (baseName == "Readonly")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Readonly<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandReadonly(typeArgs[0]);
        }
        else if (baseName == "Record")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Record<K, V> requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandRecordType(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Pick")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Pick<T, K> requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandPick(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Omit")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Omit<T, K> requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandOmit(typeArgs[0], typeArgs[1]);
        }
        // Additional utility types
        else if (baseName == "ReturnType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ReturnType<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandReturnType(typeArgs[0]);
        }
        else if (baseName == "Parameters")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Parameters<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandParameters(typeArgs[0]);
        }
        else if (baseName == "ConstructorParameters")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ConstructorParameters<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandConstructorParameters(typeArgs[0]);
        }
        else if (baseName == "InstanceType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" InstanceType<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandInstanceType(typeArgs[0]);
        }
        else if (baseName == "ThisType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ThisType<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            // ThisType<T> is a marker type - it just wraps T for this-context typing
            result = typeArgs[0];
        }
        else if (baseName == "Awaited")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Awaited<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandAwaited(typeArgs[0]);
        }
        else if (baseName == "NonNullable")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" NonNullable<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandNonNullable(typeArgs[0]);
        }
        else if (baseName == "Extract")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Extract<T, U> requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandExtract(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Exclude")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Exclude<T, U> requires exactly 2 type arguments, got {typeArgs.Count}.", tsCode: "TS2314");
            result = ExpandExclude(typeArgs[0], typeArgs[1]);
        }
        else if (baseName is "Uppercase" or "Lowercase" or "Capitalize" or "Uncapitalize")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" {baseName}<T> requires exactly 1 type argument, got {typeArgs.Count}.", tsCode: "TS2314");

            var operation = baseName switch
            {
                "Uppercase" => StringManipulation.Uppercase,
                "Lowercase" => StringManipulation.Lowercase,
                "Capitalize" => StringManipulation.Capitalize,
                "Uncapitalize" => StringManipulation.Uncapitalize,
                _ => throw new InvalidOperationException()
            };
            result = EvaluateIntrinsicStringType(typeArgs[0], operation);
        }
        else
        {
            // Check for generic type alias first
            var genericAlias = _environment.GetGenericTypeAlias(baseName);
            if (genericAlias != null)
            {
                // Node-based expansion is the ONLY generic-alias expander: the type parameters
                // bind to the resolved arguments in a child scope and the stored definition node
                // resolves directly — no argument-string substitution, no definition re-parse.
                // TryExpandGenericAliasFromNode carries the string branch's guards (TS2314 arity,
                // open-type-variable deferral, TS2589 depth, recursion placeholder, deferred-key
                // mapped guard) and its post-expansion passes; deferral placeholders flow to the
                // shared suffix wrap below. Aliases without a definition node cannot exist once
                // the parser produces nodes for every construct; if one slips through, resolve
                // permissively rather than crash.
                result = genericAlias.Value.DefinitionNode is { } definitionNode
                    ? TryExpandGenericAliasFromNode(baseName, definitionNode, genericAlias.Value.TypeParams, typeArgs)
                        ?? new TypeInfo.Any()
                    : new TypeInfo.Any();
            }
            else
            {
                // Look up the generic definition
                TypeInfo? genericDef = _environment.Get(baseName);

                result = genericDef switch
                {
                    TypeInfo.GenericClass gc => new TypeInfo.Instance(InstantiateGenericClass(gc, typeArgs)),
                    TypeInfo.GenericInterface gi => InstantiateGenericInterface(gi, typeArgs),
                    TypeInfo.GenericFunction gf => InstantiateGenericFunction(gf, typeArgs),
                    _ => new TypeInfo.Any() // Unknown generic type - fallback to any
                };
            }
        }

        // Handle array suffix(es) after the generic type
        while (suffix.StartsWith("[]"))
        {
            result = new TypeInfo.Array(result);
            suffix = suffix[2..];
        }

        return result;
    }

    /// <summary>
    /// True when the type mentions a type variable that is currently open — a mapped-type
    /// parameter whose owning body is mid-parse (see <c>_openTypeVariablesInScope</c>).
    /// Such a type is not yet instantiable; generic alias references over it are deferred (#185).
    /// </summary>
    private static bool ContainsOpenTypeVariable(TypeInfo type)
    {
        if (_openTypeVariablesInScope is not { Count: > 0 }) return false;
        return Walk(type);

        static bool Walk(TypeInfo t) => t switch
        {
            TypeInfo.TypeParameter tp => _openTypeVariablesInScope!.Contains(tp.Name),
            TypeInfo.Array a => Walk(a.ElementType),
            TypeInfo.Union u => u.Types.Any(Walk),
            TypeInfo.Intersection i => i.Types.Any(Walk),
            TypeInfo.IndexedAccess ia => Walk(ia.ObjectType) || Walk(ia.IndexType),
            TypeInfo.KeyOf k => Walk(k.SourceType),
            TypeInfo.RecursiveTypeAlias rta => rta.TypeArguments?.Any(Walk) ?? false,
            TypeInfo.ConditionalType c => Walk(c.CheckType) || Walk(c.ExtendsType) || Walk(c.TrueType) || Walk(c.FalseType),
            TypeInfo.Promise p => Walk(p.ValueType),
            TypeInfo.Tuple tup => tup.Elements.Any(e => Walk(e.Type)) || (tup.RestElementType is { } rest && Walk(rest)),
            TypeInfo.Record r => r.Fields.Values.Any(Walk)
                || (r.StringIndexType is { } sit && Walk(sit))
                || (r.NumberIndexType is { } nit && Walk(nit))
                || (r.SymbolIndexType is { } yit && Walk(yit)),
            TypeInfo.Function f => f.ParamTypes.Any(Walk) || Walk(f.ReturnType),
            TypeInfo.MappedType m => Walk(m.Constraint) || Walk(m.ValueType) || (m.AsClause is { } asc && Walk(asc)),
            TypeInfo.IntrinsicStringType ist => Walk(ist.Inner),
            TypeInfo.TemplateLiteralType tlt => tlt.InterpolatedTypes.Any(Walk),
            _ => false
        };
    }

    /// <summary>
    /// Splits type arguments respecting nested angle brackets.
    /// </summary>
    private List<string> SplitTypeArguments(string argsStr)
    {
        List<string> args = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];
            // Track all bracket types to handle tuples and function types in type arguments
            if (c == '<' || c == '[' || c == '(') depth++;
            else if (c == '>' || c == ']' || c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(argsStr[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < argsStr.Length)
        {
            args.Add(argsStr[start..].Trim());
        }

        return args;
    }

    /// <summary>
    /// Instantiates a generic class with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericClass(TypeInfo.GenericClass generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, generic.Name);

        // Build substitution map first (needed for recursive constraints)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    if (ReportOrThrowConstraintViolation(resolvedTypeArgs[i], substitutedConstraint, tp))
                        continue;
                }
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, resolvedTypeArgs);
    }

    /// <summary>
    /// A type argument failed its constraint (TS2344). While resolving an <c>extends</c>/superclass
    /// clause (<see cref="_extendsClauseConstraintLine"/> set) this is RECORDED at the declaration's
    /// line and the caller keeps instantiating with the offending argument — tsc reports TS2344 and
    /// continues, so sibling declarations and the declaration's own index-signature checks still run
    /// (#895). Everywhere else the violation throws, as before. Returns true when recorded (caller
    /// should <c>continue</c>); never returns when it throws.
    /// </summary>
    private bool ReportOrThrowConstraintViolation(TypeInfo typeArg, TypeInfo constraint, TypeInfo.TypeParameter tp)
    {
        var msg = $" Type '{typeArg}' does not satisfy constraint '{constraint}' for type parameter '{tp.Name}'.";
        if (_extendsClauseConstraintLine is int extLine)
        {
            RecordTypeError(new TypeCheckException(msg, line: extLine, tsCode: "TS2344"));
            return true;
        }
        throw new TypeCheckException(msg, tsCode: "TS2344");
    }

    /// <summary>
    /// Instantiates a generic interface with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericInterface(TypeInfo.GenericInterface generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, generic.Name);

        // Build substitution map first (needed for recursive constraints like T extends TreeNode<T>)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                // Substitute type parameters in the constraint (e.g., TreeNode<T> becomes TreeNode<MyNode>)
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    if (ReportOrThrowConstraintViolation(resolvedTypeArgs[i], substitutedConstraint, tp))
                        continue;
                }
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, resolvedTypeArgs);
    }

    /// <summary>
    /// Instantiates a generic function with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericFunction(TypeInfo.GenericFunction generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, "function");

        // Create substitution map first (needed for recursive constraints)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    if (ReportOrThrowConstraintViolation(resolvedTypeArgs[i], substitutedConstraint, tp))
                        continue;
                }
            }
        }

        // Substitute type parameters in the function signature
        var substitutedParams = generic.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
        var substitutedReturn = Substitute(generic.ReturnType, substitutions);

        return new TypeInfo.Function(substitutedParams, substitutedReturn, generic.RequiredParams, generic.HasRestParam);
    }

    /// <summary>
    /// Resolves type arguments, filling in defaults for missing arguments.
    /// </summary>
    /// <param name="typeParams">The type parameter definitions (with potential defaults).</param>
    /// <param name="typeArgs">The provided type arguments.</param>
    /// <param name="contextName">Name for error messages (e.g., class name).</param>
    /// <returns>Complete list of type arguments with defaults filled in.</returns>
    private List<TypeInfo> ResolveTypeArgumentsWithDefaults(
        List<TypeInfo.TypeParameter> typeParams,
        List<TypeInfo> typeArgs,
        string contextName)
    {
        // Count required type parameters (those without defaults)
        int requiredCount = typeParams.TakeWhile(tp => tp.Default == null).Count();

        if (typeArgs.Count < requiredCount)
        {
            throw new TypeCheckException($" Generic '{contextName}' requires at least {requiredCount} type argument(s), got {typeArgs.Count}.", tsCode: "TS2314");
        }

        if (typeArgs.Count > typeParams.Count)
        {
            throw new TypeCheckException($" Generic '{contextName}' has {typeParams.Count} type parameter(s), but got {typeArgs.Count} type argument(s).", tsCode: "TS2314");
        }

        // Build the resolved list
        List<TypeInfo> resolved = new(typeParams.Count);
        Dictionary<string, TypeInfo> substitutions = [];

        for (int i = 0; i < typeParams.Count; i++)
        {
            TypeInfo argType;
            if (i < typeArgs.Count)
            {
                // Use provided type argument
                argType = typeArgs[i];
            }
            else if (typeParams[i].Default != null)
            {
                // Use default, substituting any already-resolved type parameters
                argType = Substitute(typeParams[i].Default!, substitutions);
            }
            else
            {
                // Should not happen due to requiredCount check, but handle gracefully
                throw new TypeCheckException($" Missing type argument for type parameter '{typeParams[i].Name}' in generic '{contextName}'.", tsCode: "TS2314");
            }

            resolved.Add(argType);
            substitutions[typeParams[i].Name] = argType;
        }

        return resolved;
    }
}
