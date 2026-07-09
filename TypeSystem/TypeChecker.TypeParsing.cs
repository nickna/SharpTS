using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type-name resolution and the string entry point for annotation resolution.
/// </summary>
/// <remarks>
/// The char-scanning string parser this file used to hold is gone (type-AST migration,
/// docs/plans/type-ast-design.md slice 6): annotations resolve from parser-built
/// <see cref="TypeNode"/>s (TypeChecker.TypeNodes.cs). What remains here:
/// <see cref="ResolveTypeName"/> — the shared single-name resolver both paths use;
/// <see cref="ToTypeInfo(string)"/> — the string entry (ResolveAnnotation's defensive fallback
/// and the REPL/embedding surface), reimplemented as parse-to-node + convert;
/// <see cref="SimplifyIntersection"/>, the template-literal normalization family, and the
/// <see cref="EvaluateTypeOf"/> family — shared semantic helpers the node path resolves through.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Resolves a type annotation STRING. Bare names skip straight to
    /// <see cref="ResolveTypeName"/>; anything structural is lexed and parsed to its
    /// <see cref="TypeNode"/> by the real parser (<see cref="Parser.TryParseTypeFragment"/>)
    /// and resolved node-first. Unparseable text resolves to <c>any</c> — the same verdict the
    /// retired scanner's unknown-name tail produced. Resolution errors (TS2456 alias cycles,
    /// TS2314 arity, TS1331, …) propagate, exactly as before.
    /// </summary>
    private TypeInfo ToTypeInfo(string typeName)
    {
        if (IsBareTypeName(typeName))
            return ResolveTypeName(typeName);
        return Parser.TryParseTypeFragment(typeName) is { } node
            ? TryToTypeInfo(node) ?? new TypeInfo.Any()
            : new TypeInfo.Any();
    }

    /// <summary>
    /// Identifier or dotted-identifier chain — a name with no type STRUCTURE, safe to hand to
    /// <see cref="ResolveTypeName"/> without a parse. <c>true</c>/<c>false</c> are excluded:
    /// identifier-shaped but literal-meaning (the parse route resolves them to
    /// <c>BooleanLiteral</c>, matching source annotations).
    /// </summary>
    private static bool IsBareTypeName(string s)
    {
        if (s.Length == 0 || s is "true" or "false") return false;
        int start = 0;
        for (int i = 0; i <= s.Length; i++)
        {
            if (i == s.Length || s[i] == '.')
            {
                if (!IsValidIdentifier(s[start..i])) return false;
                start = i + 1;
            }
        }
        return true;
    }

    /// <summary>
    /// Resolves a bare type NAME — type parameters in scope, alias expansion (node-first),
    /// primitives/keywords, the hot lib globals, typed-array and Error names, open mapped-type
    /// variables, and finally the environment (classes/interfaces/enums), falling through to
    /// <c>any</c>. This is the shared name tail of both resolution paths: the node path's
    /// <c>NamedTypeNode</c> case enters here directly, and the string scanner delegates to it
    /// after its composite branches. Never lexes or scans — names have no structure.
    /// </summary>
    private TypeInfo ResolveTypeName(string typeName)
    {
        // Backstop against runaway recursion through type resolution (e.g. self-referential
        // mapped/indexed-access/generic types). Every unbounded resolution cycle traverses a
        // name hop (alias/generic reference), so guarding here covers both the node path and
        // the string scanner. Throwing a catchable exception prevents an uncatchable
        // StackOverflowException from tearing down the whole process.
        if (++_typeResolutionDepth > MaxTypeResolutionDepth)
        {
            _typeResolutionDepth--;
            throw new TypeCheckException(
                $"Type '{typeName}' is too deeply nested or circularly references itself.", tsCode: "TS2456");
        }
        try
        {
            return ResolveTypeNameCore(typeName);
        }
        finally
        {
            _typeResolutionDepth--;
        }
    }

    /// <summary>Body of <see cref="ResolveTypeName"/> (which wraps it in the recursion guard).</summary>
    private TypeInfo ResolveTypeNameCore(string typeName)
    {
        // Check for type parameter in current scope first
        var typeParam = _environment.GetTypeParameter(typeName);
        if (typeParam != null)
        {
            return typeParam;
        }

        // Check for type alias
        if (_environment.GetTypeAlias(typeName) is { } aliasEntry)
        {
            // Check cache first - reusing the same TypeInfo object enables identity-based caching.
            // Keyed by name AND definition STRING: two same-named aliases in different scopes (e.g.
            // namespace-local `type T = …` redeclarations) must not share an expansion — the
            // name alone served one namespace's T for another's. The string key also stays stable
            // across the node-first expansion below (nodes have no canonical rendering).
            string aliasCacheKey = $"{typeName}={aliasEntry.Definition}";
            _expandedTypeAliasCache ??= new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
            if (_expandedTypeAliasCache.TryGetValue(aliasCacheKey, out var cached))
            {
                return cached;
            }

            _typeAliasExpansionStack ??= new HashSet<string>(StringComparer.Ordinal);

            // Recursive reference detected - return deferred placeholder
            if (_typeAliasExpansionStack.Contains(typeName))
            {
                return new TypeInfo.RecursiveTypeAlias(typeName);
            }

            _typeAliasExpansionStack.Add(typeName);
            try
            {
                if (++_typeAliasExpansionDepth > MaxTypeAliasExpansionDepth)
                {
                    throw new TypeCheckException(
                        $"Type alias '{typeName}' circularly references itself.", tsCode: "TS2456");
                }

                // Node-first: resolve the stored definition node; the definition string is the
                // fallback for any construct the node path can't yet resolve.
                var expanded = aliasEntry.DefinitionNode is { } definitionNode
                    ? TryToTypeInfo(definitionNode) ?? ToTypeInfo(aliasEntry.Definition)
                    : ToTypeInfo(aliasEntry.Definition);

                // Validate: direct self-reference without indirection is illegal
                if (IsDirectCircularReference(expanded, typeName))
                {
                    throw new TypeCheckException(
                        $"Type alias '{typeName}' circularly references itself.", tsCode: "TS2456");
                }

                // Cache the expanded type for future use
                _expandedTypeAliasCache[aliasCacheKey] = expanded;

                return expanded;
            }
            finally
            {
                _typeAliasExpansionStack.Remove(typeName);
                _typeAliasExpansionDepth--;
            }
        }

        if (typeName == "string") return new TypeInfo.String();
        if (typeName == "number") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (typeName == "boolean") return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (typeName == "symbol") return new TypeInfo.Symbol();
        if (typeName == "bigint") return new TypeInfo.BigInt();
        // The global Function type: any callable, i.e. (...args: any[]) => any. Parsing it to
        // Any made `T[K] extends Function` filters (FunctionPropertyNames et al.) match every
        // property — `X extends any` is always true — emptying the mapped result (#185).
        if (typeName == "Function") return new TypeInfo.Function(
            [new TypeInfo.Array(new TypeInfo.Any())], new TypeInfo.Any(), RequiredParams: 0, HasRestParam: true);
        if (typeName == "void") return new TypeInfo.Void();
        if (typeName == "null") return new TypeInfo.Null();
        if (typeName == "undefined") return new TypeInfo.Undefined();
        if (typeName == "unknown") return new TypeInfo.Unknown();
        if (typeName == "never") return new TypeInfo.Never();
        if (typeName == "object") return new TypeInfo.Object();
        if (typeName == "Buffer") return new TypeInfo.Buffer();
        // Hot lib globals in TYPE position. Without these they fell through the user-type lookup
        // to `any`, making every Object/Date/wrapper-typed position vacuously compatible (same
        // failure mode as the `Function` mapping above). `Object` ≈ `{}` — everything non-nullish
        // is assignable to it, and it is assignable to nothing specific; the String/Number/Boolean
        // wrappers approximate their primitives (assignability matches in the directions the
        // corpus exercises); Date/RegExp map to their dedicated TypeInfos.
        if (typeName == "Object") return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
        if (typeName == "Date") return new TypeInfo.Date();
        if (typeName == "RegExp") return new TypeInfo.RegExp();
        if (typeName == "String") return new TypeInfo.String();
        if (typeName == "Number") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (typeName == "Boolean") return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        // Typed-array names in annotation position (`Int32Array`, `Float64Array`, …) resolve to
        // TypeInfo.TypedArray — the SAME type `new Int32Array(...)` produces — so element access on an
        // annotated typed array is typed `number` and the compiled unboxed fast paths fire. Without
        // this they fell through to `any`, leaving every annotated typed array (the common case, and
        // every benchmark/real program) on the boxed GetIndex/SetIndex path. The element prefix is the
        // name minus "Array"; BigInt64/BigUint64 carry bigint elements (the compiled side keeps those
        // on the boxed path).
        if (typeName is "Int8Array" or "Uint8Array" or "Uint8ClampedArray"
            or "Int16Array" or "Uint16Array" or "Int32Array" or "Uint32Array"
            or "Float32Array" or "Float64Array" or "BigInt64Array" or "BigUint64Array")
        {
            return new TypeInfo.TypedArray(typeName[..^"Array".Length]);
        }
        // Built-in Error type references (Error, TypeError, RangeError, …) resolve to their structured
        // TypeInfo.Error — like Date/RegExp above — instead of degrading to `any`. This types member
        // access (`e.message: string`, so `const n: number = e.message` is a TS2322 error) and aligns
        // the annotation with the value `new Error("x")`, which already produces TypeInfo.Error (#528).
        if (Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(typeName)) return new TypeInfo.Error(typeName);

        // A mapped-type parameter in scope (e.g. P in `{ [P in K]: DeepReadonly<T[P]> }`)
        // parses to a TypeParameter so the body builds deferred forms (IndexedAccess,
        // deferred alias references) that ExpandMappedType substitutes per key — instead
        // of dissolving to Any or eagerly instantiating with an open argument (#185).
        if (_openTypeVariablesInScope is { Count: > 0 } && _openTypeVariablesInScope.Contains(typeName))
        {
            return new TypeInfo.TypeParameter(typeName);
        }

        TypeInfo? type = _environment.Get(typeName);
        if (type is TypeInfo.MutableClass mutableClass)
        {
            // MutableClass is used during signature collection for self-references.
            // Instance wraps it; resolution to frozen class happens lazily via Instance.ResolvedClassType.
            return new TypeInfo.Instance(mutableClass);
        }
        if (type is TypeInfo.Class classType)
        {
            return new TypeInfo.Instance(classType);
        }
        if (type is TypeInfo.Interface itfType)
        {
            return itfType;
        }
        if (type is TypeInfo.Enum enumType)
        {
            return enumType;
        }

        // #99 lib-type seam: ambient lib.d.ts TYPE-position names SharpTS doesn't load from the
        // .d.ts files yet (checked AFTER user declarations above, so a user interface of the same
        // name wins). A later increment backs this with a real lib.d.ts loader.
        if (TryResolveLibType(typeName) is { } libType)
        {
            return libType;
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// The #99 lib-type seam: resolves the ambient TYPE-position names that lib.d.ts declares but
    /// SharpTS doesn't yet parse from the vendored .d.ts files, to their modeled <see cref="TypeInfo"/>.
    /// Consulted by both <see cref="ResolveTypeNameCore"/> (for the type) and
    /// <c>ReportUnknownTypeName</c> (so these names aren't flagged TS2304). Increment 1 covers the
    /// Symbol family; a later increment replaces this switch with content loaded from lib.d.ts.
    /// </summary>
    private static TypeInfo? TryResolveLibType(string name) => name switch
    {
        "SymbolConstructor" => WellKnownSymbolTypes.SymbolConstructor,
        "Symbol" => WellKnownSymbolTypes.SymbolWrapper,
        _ => null
    };

    /// <summary>
    /// Simplifies an intersection type according to TypeScript semantics:
    /// - Conflicting primitives (string &amp; number) = never
    /// - never &amp; T = never
    /// - any &amp; T = any
    /// - unknown &amp; T = T
    /// - Object types are merged with property combination
    /// </summary>
    private TypeInfo SimplifyIntersection(List<TypeInfo> types)
    {
        // Handle empty or single type
        if (types.Count == 0) return new TypeInfo.Unknown();
        if (types.Count == 1) return types[0];

        // Flatten nested intersections so their object constituents participate in the member
        // merge below — `(T & { foo }) & { bar }` must merge the two records, not hide the first
        // inside an opaque inner intersection.
        if (types.Any(t => t is TypeInfo.Intersection))
            types = types.SelectMany(t => t is TypeInfo.Intersection nested ? nested.FlattenedTypes : [t]).ToList();

        // Check for never (absorbs everything)
        if (types.Any(t => t is TypeInfo.Never))
            return new TypeInfo.Never();

        // Check for any (absorbs in intersection)
        if (types.Any(t => t is TypeInfo.Any))
            return new TypeInfo.Any();

        // Remove unknown (identity element)
        types = types.Where(t => t is not TypeInfo.Unknown).ToList();
        if (types.Count == 0) return new TypeInfo.Unknown();
        if (types.Count == 1) return types[0];

        // Check for conflicting primitives (e.g., string & number = never)
        // Count each primitive type (string, number, boolean are all incompatible with each other)
        bool hasString = types.Any(t => t is TypeInfo.String or TypeInfo.StringLiteral);
        bool hasNumber = types.Any(t => t is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral);
        bool hasBoolean = types.Any(t => t is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral);
        bool hasNull = types.Any(t => t is TypeInfo.Null);
        bool hasUndefined = types.Any(t => t is TypeInfo.Undefined);
        bool hasSymbol = types.Any(t => t is TypeInfo.Symbol);
        bool hasBigInt = types.Any(t => t is TypeInfo.BigInt or TypeInfo.BigIntLiteral);

        // Count how many different primitive categories are present
        int primitiveCount = (hasString ? 1 : 0) + (hasNumber ? 1 : 0) + (hasBoolean ? 1 : 0)
                           + (hasNull ? 1 : 0) + (hasUndefined ? 1 : 0) + (hasSymbol ? 1 : 0) + (hasBigInt ? 1 : 0);

        // If more than one primitive category is present, it's a conflict
        if (primitiveCount > 1)
            return new TypeInfo.Never();  // Conflicting primitives

        // Collect object-like types for merging
        var records = types.OfType<TypeInfo.Record>().ToList();
        var interfaces = types.OfType<TypeInfo.Interface>().ToList();
        var classes = types.OfType<TypeInfo.Class>().ToList();
        var instances = types.OfType<TypeInfo.Instance>().ToList();

        if (records.Count > 0 || interfaces.Count > 0 || classes.Count > 0 || instances.Count > 0)
        {
            // Merge all object-like types
            Dictionary<string, TypeInfo> mergedFields = [];
            HashSet<string> optionalFields = [];
            HashSet<string> requiredInAny = []; // Track if property is required in any type
            List<TypeInfo> nonObjectTypes = [];

            foreach (var type in types)
            {
                IReadOnlyDictionary<string, TypeInfo>? fields = type switch
                {
                    TypeInfo.Record r => r.Fields,
                    TypeInfo.Interface i => i.Members,
                    TypeInfo.Class c => c.FieldTypes,
                    TypeInfo.Instance inst => inst.ClassType switch
                    {
                        TypeInfo.Class c => c.FieldTypes,
                        _ => null
                    },
                    _ => null
                };

                IReadOnlySet<string>? optionals = type switch
                {
                    TypeInfo.Interface i => i.OptionalMembers,
                    TypeInfo.Record r => r.OptionalFields,
                    _ => null
                };

                if (fields == null || fields.Count == 0)
                {
                    // For classes/instances without explicit field types, keep as non-object type
                    // so the intersection is preserved
                    if (type is TypeInfo.Class || type is TypeInfo.Instance)
                    {
                        nonObjectTypes.Add(type);
                    }
                    else if (fields == null)
                    {
                        nonObjectTypes.Add(type);
                    }
                    continue;
                }

                foreach (var (name, fieldType) in fields)
                {
                    bool isOptionalInThisType = optionals?.Contains(name) ?? false;

                    if (mergedFields.TryGetValue(name, out var existingType))
                    {
                        // Check for property type conflict
                        if (!IsCompatible(existingType, fieldType) && !IsCompatible(fieldType, existingType))
                        {
                            // Conflicting types - property becomes never
                            mergedFields[name] = new TypeInfo.Never();
                        }
                        // If compatible, keep the more specific type (or the first one)

                        // If required in any type, mark as required
                        if (!isOptionalInThisType)
                        {
                            requiredInAny.Add(name);
                        }
                    }
                    else
                    {
                        mergedFields[name] = fieldType;
                        // Initially mark optional if optional in this type
                        if (isOptionalInThisType)
                        {
                            optionalFields.Add(name);
                        }
                        else
                        {
                            requiredInAny.Add(name);
                        }
                    }
                }
            }

            // A property is optional in the intersection only if it's optional in ALL types that have it
            // (or if it only appears in types where it's optional)
            optionalFields.ExceptWith(requiredInAny);

            // If all types were object-like, return merged interface (to preserve optional info)
            if (nonObjectTypes.Count == 0)
            {
                // Use Interface if we have optional fields, otherwise Record
                if (optionalFields.Count > 0)
                {
                    return new TypeInfo.Interface("", mergedFields.ToFrozenDictionary(), optionalFields.ToFrozenSet());
                }
                return new TypeInfo.Record(mergedFields.ToFrozenDictionary());
            }

            // Otherwise, return intersection with merged record/interface
            var resultTypes = new List<TypeInfo>(nonObjectTypes) { new TypeInfo.Record(mergedFields.ToFrozenDictionary()) };
            return new TypeInfo.Intersection(resultTypes);
        }

        // Return intersection for other cases (e.g., class instances)
        return new TypeInfo.Intersection(types);
    }

    /// <summary>
    /// Normalizes a template literal type, expanding to union of string literals when possible.
    /// </summary>
    private TypeInfo NormalizeTemplateLiteralType(List<string> strings, List<TypeInfo> types)
    {
        // No interpolations → simple string literal
        if (types.Count == 0)
            return new TypeInfo.StringLiteral(strings[0]);

        // All concrete → expand to union of string literals
        if (types.All(IsConcreteStringType))
            return ExpandTemplateLiteral(strings, types);

        // Contains string primitive → keep as pattern type
        return new TypeInfo.TemplateLiteralType(strings, types);
    }

    /// <summary>
    /// Checks if a type can be expanded to concrete string literals.
    /// </summary>
    private static bool IsConcreteStringType(TypeInfo type) => type switch
    {
        TypeInfo.StringLiteral => true,
        TypeInfo.NumberLiteral => true,  // Numbers can be stringified
        TypeInfo.BooleanLiteral => true,  // Booleans can be stringified
        TypeInfo.BigIntLiteral => true,  // Bigints stringify without the 'n' suffix
        TypeInfo.Union u => u.FlattenedTypes.All(IsConcreteStringType),
        _ => false
    };

    /// <summary>
    /// Expands a template literal with all concrete parts to a union of string literals.
    /// Uses Cartesian product for unions.
    /// </summary>
    private TypeInfo ExpandTemplateLiteral(List<string> strings, List<TypeInfo> types)
    {
        // Convert each type to list of string values
        List<List<string>> valueOptions = types.Select(GetStringLiteralValues).ToList();

        // Generate all combinations (Cartesian product)
        List<string> combinations = GenerateTemplateCombinations(strings, valueOptions);

        // Limit check (TypeScript caps at ~10000)
        if (combinations.Count > 10000)
            // SharpTS-only: implementation limit (TS uses TS2590 for similar "too complex" cases)
            throw new TypeCheckException("Template literal type produces too many combinations (limit: 10000).");

        // Convert to string literal types
        var literalTypes = combinations.Select(s => (TypeInfo)new TypeInfo.StringLiteral(s)).ToList();

        // Return single literal or union
        return literalTypes.Count == 1
            ? literalTypes[0]
            : new TypeInfo.Union(literalTypes);
    }

    /// <summary>
    /// Extracts string values from a concrete type (literal or union of literals).
    /// </summary>
    private static List<string> GetStringLiteralValues(TypeInfo type) => type switch
    {
        TypeInfo.StringLiteral sl => [sl.Value],
        TypeInfo.NumberLiteral nl => [nl.Value.ToString()],
        TypeInfo.BooleanLiteral bl => [bl.Value ? "true" : "false"],
        TypeInfo.BigIntLiteral bil => [bil.Value.ToString()],  // `${1n}` is "1" — no 'n' suffix
        TypeInfo.Union u => u.FlattenedTypes.SelectMany(GetStringLiteralValues).ToList(),
        _ => throw new InvalidOperationException($"Expected concrete string type, got {type}")
    };

    /// <summary>
    /// Generates all combinations of template literal parts using Cartesian product.
    /// </summary>
    private static List<string> GenerateTemplateCombinations(List<string> strings, List<List<string>> valueOptions)
    {
        // Start with the first static string
        List<string> results = [strings[0]];

        for (int i = 0; i < valueOptions.Count; i++)
        {
            var newResults = new List<string>();
            foreach (var current in results)
            {
                foreach (var value in valueOptions[i])
                {
                    newResults.Add(current + value + strings[i + 1]);
                }
            }
            results = newResults;
        }

        return results;
    }

    // ============== TYPEOF EVALUATION ==============

    /// <summary>
    /// Accessor kind for typeof path parsing.
    /// </summary>
    private enum TypeOfAccessorKind { Property, NumericIndex, StringIndex }

    /// <summary>
    /// Represents a single accessor in a typeof path.
    /// </summary>
    private record TypeOfAccessor(string Name, TypeOfAccessorKind Kind);

    /// <summary>
    /// Evaluates typeof path to extract the static type of a variable/expression.
    /// </summary>
    private TypeInfo EvaluateTypeOf(string path)
    {
        // Parse path into segments, handling both dot access and bracket access
        // Examples: "obj.prop", "arr[0]", "obj[\"key\"]", "obj.nested[0].value"
        var accessors = ParseTypeOfPath(path);

        if (accessors.Count == 0)
            // SharpTS-only: malformed typeof query path
            throw new TypeCheckException($"Invalid typeof expression: '{path}'");

        // Look up first identifier in environment
        string firstName = accessors[0].Name;
        // `typeof undefined` — the global undefined has no environment binding.
        if (firstName == "undefined" && accessors.Count == 1)
            return new TypeInfo.Undefined();
        TypeInfo? currentType = _environment.Get(firstName);

        // `typeof globalThis` — the global object has no environment binding.
        // SharpTS models globals as `any`, so it (and any member access off it)
        // resolves to `any`. Other bare globals in typeof-type position remain a
        // gap until lib.d.ts globals are loaded (#99).
        if (currentType == null && firstName == "globalThis")
            currentType = new TypeInfo.Any();

        if (currentType == null)
            throw new TypeCheckException($"Cannot find name '{firstName}' for typeof.", tsCode: "TS2304");

        // Resolve what typeof returns for the value
        currentType = ResolveTypeOfValue(currentType);

        // Traverse access path
        for (int i = 1; i < accessors.Count; i++)
        {
            var accessor = accessors[i];
            currentType = accessor.Kind switch
            {
                TypeOfAccessorKind.Property => GetPropertyTypeForTypeOf(currentType, accessor.Name),
                TypeOfAccessorKind.NumericIndex => GetIndexedTypeForTypeOf(currentType, int.Parse(accessor.Name)),
                TypeOfAccessorKind.StringIndex => GetPropertyTypeForTypeOf(currentType, accessor.Name),
                _ => null
            };

            if (currentType == null)
                throw new TypeCheckException($"Cannot access '{accessor.Name}' on type in typeof.", tsCode: "TS2339");
        }

        return currentType;
    }

    /// <summary>
    /// Parses a typeof path into a list of accessors.
    /// </summary>
    private static List<TypeOfAccessor> ParseTypeOfPath(string path)
    {
        var result = new List<TypeOfAccessor>();
        int i = 0;

        while (i < path.Length)
        {
            // Defense-in-depth: a typeof path should only ever contain identifiers, '.', and '[...]'.
            // If a character matches none of the branches below (e.g. a stray '&'/'|' from an un-split
            // composite type), `i` would not advance and the loop would spin forever — a hard checker
            // hang. Record the start and break if an iteration consumes nothing, so malformed input
            // degrades to a partial parse instead of locking up.
            int iterStart = i;

            // Skip leading whitespace
            while (i < path.Length && char.IsWhiteSpace(path[i])) i++;
            if (i >= path.Length) break;

            // Parse identifier
            int start = i;
            while (i < path.Length && (char.IsLetterOrDigit(path[i]) || path[i] == '_'))
                i++;

            if (i > start)
                result.Add(new TypeOfAccessor(path[start..i], TypeOfAccessorKind.Property));

            // Skip whitespace
            while (i < path.Length && char.IsWhiteSpace(path[i])) i++;
            if (i >= path.Length) break;

            // Check what follows
            if (path[i] == '.')
            {
                i++; // skip dot
            }
            else if (path[i] == '[')
            {
                i++; // skip [

                // Skip whitespace
                while (i < path.Length && char.IsWhiteSpace(path[i])) i++;

                if (i < path.Length && path[i] == '"')
                {
                    // String index: ["key"]
                    i++; // skip opening quote
                    start = i;
                    while (i < path.Length && path[i] != '"') i++;
                    result.Add(new TypeOfAccessor(path[start..i], TypeOfAccessorKind.StringIndex));
                    i++; // skip closing quote
                }
                else
                {
                    // Numeric index: [0] or identifier index
                    start = i;
                    while (i < path.Length && (char.IsDigit(path[i]) || char.IsLetter(path[i]) || path[i] == '_'))
                        i++;
                    string indexValue = path[start..i];

                    // Determine if it's a numeric index or identifier
                    if (indexValue.All(char.IsDigit))
                        result.Add(new TypeOfAccessor(indexValue, TypeOfAccessorKind.NumericIndex));
                    else
                        result.Add(new TypeOfAccessor(indexValue, TypeOfAccessorKind.Property));
                }

                // Skip whitespace and closing bracket
                while (i < path.Length && char.IsWhiteSpace(path[i])) i++;
                if (i < path.Length && path[i] == ']') i++; // skip ]
            }

            // No branch advanced past this iteration's start — stop rather than spin.
            if (i == iterStart) break;
        }

        return result;
    }

    /// <summary>
    /// Resolves the type that typeof should return for a given value type.
    /// </summary>
    private TypeInfo ResolveTypeOfValue(TypeInfo type) => type switch
    {
        // For class types, typeof returns the class type itself (not an instance)
        // This represents the constructor/static side of the class
        TypeInfo.Class cls => cls,
        TypeInfo.GenericClass gc => gc,
        // For instances, typeof returns the instance type
        TypeInfo.Instance => type,
        // For function types, return as-is
        TypeInfo.Function => type,
        TypeInfo.GenericFunction => type,
        // For other types, return as-is
        _ => type
    };

    /// <summary>
    /// Gets the type of a property for typeof evaluation.
    /// </summary>
    private TypeInfo? GetPropertyTypeForTypeOf(TypeInfo type, string propName) => type switch
    {
        TypeInfo.Class cls => cls.StaticMethods.GetValueOrDefault(propName)
                           ?? cls.StaticProperties.GetValueOrDefault(propName)
                           ?? cls.Methods.GetValueOrDefault(propName)
                           ?? cls.FieldTypes.GetValueOrDefault(propName)
                           ?? cls.Getters.GetValueOrDefault(propName),
        TypeInfo.Instance inst => GetPropertyTypeForTypeOf(inst.ClassType, propName) switch
        {
            // For instance property access, return instance properties not static ones
            TypeInfo t when inst.ClassType is TypeInfo.Class c &&
                (c.Methods.ContainsKey(propName) || c.FieldTypes.ContainsKey(propName) || c.Getters.ContainsKey(propName)) => t,
            _ => null
        } ?? GetInstancePropertyType(inst, propName),
        TypeInfo.Record rec => rec.Fields.GetValueOrDefault(propName),
        TypeInfo.Interface itf => itf.Members.GetValueOrDefault(propName),
        TypeInfo.InstantiatedGeneric ig => GetPropertyTypeFromInstantiatedGeneric(ig, propName),
        TypeInfo.GenericClass gc => gc.StaticMethods.GetValueOrDefault(propName)
                                 ?? gc.StaticProperties.GetValueOrDefault(propName),
        TypeInfo.Namespace ns => ns.GetMember(propName),
        // Property access on `any` stays `any` (e.g. `(typeof globalThis)["x"]`).
        TypeInfo.Any => new TypeInfo.Any(),
        _ => null
    };

    /// <summary>
    /// Gets the type of an instance property (non-static).
    /// </summary>
    private static TypeInfo? GetInstancePropertyType(TypeInfo.Instance inst, string propName) => inst.ClassType switch
    {
        TypeInfo.Class cls => cls.Methods.GetValueOrDefault(propName)
                           ?? cls.FieldTypes.GetValueOrDefault(propName)
                           ?? cls.Getters.GetValueOrDefault(propName),
        TypeInfo.InstantiatedGeneric => null, // Handled separately
        _ => null
    };

    /// <summary>
    /// Gets the type at a numeric index for typeof evaluation (arrays, tuples).
    /// </summary>
    private static TypeInfo? GetIndexedTypeForTypeOf(TypeInfo type, int index) => type switch
    {
        TypeInfo.Array arr => arr.ElementType,
        TypeInfo.Tuple tup when index >= 0 && index < tup.ElementTypes.Count => tup.ElementTypes[index],
        _ => null
    };

    /// <summary>
    /// Checks if a string is a valid identifier (for type predicate parameter name validation).
    /// </summary>
    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        // Must start with letter or underscore
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        // Rest must be alphanumeric or underscore (no spaces)
        return s.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// Checks if a type is a direct circular reference to a type alias.
    /// Direct circular references (without structural indirection) are illegal in TypeScript.
    /// </summary>
    /// <param name="type">The expanded type to check.</param>
    /// <param name="aliasName">The name of the type alias being expanded.</param>
    /// <returns>True if the type is a direct circular reference.</returns>
    private static bool IsDirectCircularReference(TypeInfo type, string aliasName)
    {
        return type switch
        {
            // Direct reference to self
            TypeInfo.RecursiveTypeAlias rta when rta.AliasName == aliasName => true,
            // Union where ALL branches are circular references - this is illegal
            TypeInfo.Union u => u.FlattenedTypes.All(t => IsDirectCircularReference(t, aliasName)),
            // Intersection where ALL branches are circular references - this is illegal
            TypeInfo.Intersection i => i.FlattenedTypes.All(t => IsDirectCircularReference(t, aliasName)),
            // Structural types provide valid indirection - they break the cycle
            TypeInfo.Record or TypeInfo.Array or TypeInfo.Tuple or TypeInfo.Function
                or TypeInfo.Interface or TypeInfo.Instance or TypeInfo.Map
                or TypeInfo.Set or TypeInfo.Promise => false,
            _ => false
        };
    }
}
