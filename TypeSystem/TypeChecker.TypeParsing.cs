using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type string parsing - converting type annotations to TypeInfo.
/// </summary>
/// <remarks>
/// Contains type parsing methods:
/// ToTypeInfo(string), SplitUnionParts, SplitIntersectionParts, SimplifyIntersection,
/// ParseParenthesizedType, ParseFunctionTypeInfo, SplitFunctionParams,
/// ParseTupleTypeInfo, SplitTupleElements, ParseInlineObjectTypeInfo, SplitObjectMembers.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo ToTypeInfo(string typeName)
    {
        // Check for type parameter in current scope first
        var typeParam = _environment.GetTypeParameter(typeName);
        if (typeParam != null)
        {
            return typeParam;
        }

        // Check for type alias
        var aliasExpansion = _environment.GetTypeAlias(typeName);
        if (aliasExpansion != null)
        {
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
                        $"Type alias '{typeName}' circularly references itself.");
                }

                var expanded = ToTypeInfo(aliasExpansion);

                // Validate: direct self-reference without indirection is illegal
                if (IsDirectCircularReference(expanded, typeName))
                {
                    throw new TypeCheckException(
                        $"Type alias '{typeName}' circularly references itself.");
                }

                return expanded;
            }
            finally
            {
                _typeAliasExpansionStack.Remove(typeName);
                _typeAliasExpansionDepth--;
            }
        }

        // Handle type predicate return types: "asserts x is T", "asserts x", "x is T"
        if (typeName.StartsWith("asserts "))
        {
            string rest = typeName[8..]; // Remove "asserts "
            int isIndex = rest.IndexOf(" is ");
            if (isIndex > 0)
            {
                // "asserts x is T" form
                string paramName = rest[..isIndex];
                string predicateTypeStr = rest[(isIndex + 4)..];
                TypeInfo predicateType = ToTypeInfo(predicateTypeStr);
                return new TypeInfo.TypePredicate(paramName, predicateType, IsAssertion: true);
            }
            else
            {
                // "asserts x" form (non-null assertion)
                return new TypeInfo.AssertsNonNull(rest);
            }
        }

        // Check for "x is T" pattern (regular type predicate)
        // Must match: identifier followed by " is " and then a type
        // Avoid matching things like "unknown is not a type"
        {
            int isIndex = typeName.IndexOf(" is ");
            if (isIndex > 0)
            {
                string potentialParamName = typeName[..isIndex];
                // Validate it looks like an identifier (no spaces, etc.)
                if (IsValidIdentifier(potentialParamName))
                {
                    string predicateTypeStr = typeName[(isIndex + 4)..];
                    TypeInfo predicateType = ToTypeInfo(predicateTypeStr);
                    return new TypeInfo.TypePredicate(potentialParamName, predicateType, IsAssertion: false);
                }
            }
        }

        // Handle keyof type operator: "keyof T"
        if (typeName.StartsWith("keyof "))
        {
            string innerTypeStr = typeName[6..].Trim();
            TypeInfo innerType = ToTypeInfo(innerTypeStr);
            return new TypeInfo.KeyOf(innerType);
        }

        // Handle typeof type operator: "typeof variableName"
        if (typeName.StartsWith("typeof "))
        {
            string path = typeName[7..].Trim();
            return EvaluateTypeOf(path);
        }

        // Handle infer keyword for conditional types: "infer U"
        if (typeName.StartsWith("infer "))
        {
            string paramName = typeName[6..].Trim();
            return new TypeInfo.InferredTypeParameter(paramName);
        }

        // Handle conditional types: "T extends U ? X : Y"
        // Must check BEFORE union types since conditional has lowest precedence
        var conditionalMatch = TryParseConditionalType(typeName);
        if (conditionalMatch != null)
        {
            return conditionalMatch;
        }

        // Handle generic type syntax: Box<number>, Map<string, number>
        // Must NOT match inline object types that contain generic types like { x: Box<T> }
        // or tuple types that contain generics like [Box<T>, string]
        if (typeName.Contains('<') && typeName.Contains('>') &&
            !typeName.StartsWith("{ ") && !typeName.StartsWith("["))
        {
            return ParseGenericTypeReference(typeName);
        }

        // Handle union types: "string | number"
        // Union has lower precedence than intersection, check it first at top level
        if (typeName.Contains(" | "))
        {
            var parts = SplitUnionParts(typeName.AsSpan());
            if (parts.Count > 1)  // Only create union if we actually split at top level
            {
                var types = parts.Select(ToTypeInfo).ToList();
                return new TypeInfo.Union(types);
            }
        }

        // Handle intersection types: "A & B"
        // Intersection has higher precedence than union
        if (typeName.Contains(" & "))
        {
            var parts = SplitIntersectionParts(typeName.AsSpan());
            if (parts.Count > 1)  // Only create intersection if we actually split at top level
            {
                var types = parts.Select(ToTypeInfo).ToList();
                return SimplifyIntersection(types);
            }
        }

        // Handle inline object types: "{ x: number; y?: string }"
        // Must check BEFORE function types since objects can contain function-typed properties
        if (typeName.StartsWith("{ ") && typeName.EndsWith(" }"))
        {
            return ParseInlineObjectTypeInfo(typeName);
        }

        // Check for function type syntax: "(params) => returnType"
        // Must check BEFORE parenthesized types since both start with "("
        if (typeName.Contains("=>"))
        {
            return ParseFunctionTypeInfo(typeName);
        }

        // Handle parenthesized types: "(string | number)[]"
        if (typeName.StartsWith("("))
        {
            return ParseParenthesizedType(typeName);
        }

        // Handle tuple types: "[string, number, boolean?]"
        if (typeName.StartsWith("[") && typeName.EndsWith("]"))
        {
            return ParseTupleTypeInfo(typeName);
        }

        // Handle indexed access types: T[K] or T["key"] (but not array T[])
        // Check for brackets that have content (not just [])
        if (typeName.Contains('[') && !typeName.EndsWith("[]"))
        {
            var indexedAccess = TryParseIndexedAccessType(typeName);
            if (indexedAccess != null)
            {
                return indexedAccess;
            }
        }

        if (typeName.EndsWith("[]"))
        {
            string elementTypeString = typeName.Substring(0, typeName.Length - 2);
            TypeInfo elementType = ToTypeInfo(elementTypeString);
            return new TypeInfo.Array(elementType);
        }

        // Handle template literal types: `prefix${Type}suffix`
        if (typeName.StartsWith('`') && typeName.EndsWith('`'))
        {
            return ParseTemplateLiteralTypeInfo(typeName);
        }

        // Handle string literal types: "value"
        if (typeName.StartsWith("\"") && typeName.EndsWith("\""))
        {
            return new TypeInfo.StringLiteral(typeName[1..^1]);
        }

        // Handle boolean literal types
        if (typeName == "true") return new TypeInfo.BooleanLiteral(true);
        if (typeName == "false") return new TypeInfo.BooleanLiteral(false);

        // Handle number literal types (check before primitives)
        if (double.TryParse(typeName, out double numValue))
        {
            return new TypeInfo.NumberLiteral(numValue);
        }

        if (typeName == "string") return new TypeInfo.String();
        if (typeName == "number") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (typeName == "boolean") return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (typeName == "symbol") return new TypeInfo.Symbol();

        // Reject standalone "unique symbol" - it's only valid on const declarations with Symbol() initializer
        if (typeName == "unique symbol")
        {
            throw new TypeCheckException(
                "'unique symbol' type is only valid on const declarations initialized with Symbol().");
        }
        if (typeName == "bigint") return new TypeInfo.BigInt();
        if (typeName == "void") return new TypeInfo.Void();
        if (typeName == "null") return new TypeInfo.Null();
        if (typeName == "undefined") return new TypeInfo.Undefined();
        if (typeName == "unknown") return new TypeInfo.Unknown();
        if (typeName == "never") return new TypeInfo.Never();
        if (typeName == "object") return new TypeInfo.Object();
        if (typeName == "Buffer") return new TypeInfo.Buffer();

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

        return new TypeInfo.Any();
    }

    private List<string> SplitUnionParts(ReadOnlySpan<char> typeName)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            // Track depth for all bracket types to avoid splitting inside nested structures
            // But skip '>' when it's part of '=>' (arrow function syntax)
            if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == '>' && (i == 0 || typeName[i - 1] != '=')) depth--;  // Skip > in =>
            else if (c == '|' && depth == 0 && i > 0 && typeName[i - 1] == ' ')
            {
                ReadOnlySpan<char> segment = typeName[start..(i - 1)].Trim();
                parts.Add(segment.ToString());
                start = i + 2;
            }
        }
        ReadOnlySpan<char> lastSegment = typeName[start..].Trim();
        parts.Add(lastSegment.ToString());
        return parts;
    }

    /// <summary>
    /// Splits an intersection type string into its component parts, respecting nesting.
    /// E.g., "A &amp; B &amp; C" becomes ["A", "B", "C"]
    /// </summary>
    private List<string> SplitIntersectionParts(ReadOnlySpan<char> typeName)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            // Track depth for all bracket types to avoid splitting inside nested structures
            // But skip '>' when it's part of '=>' (arrow function syntax)
            if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == '>' && (i == 0 || typeName[i - 1] != '=')) depth--;  // Skip > in =>
            else if (c == '&' && depth == 0 && i > 0 && typeName[i - 1] == ' ')
            {
                ReadOnlySpan<char> segment = typeName[start..(i - 1)].Trim();
                parts.Add(segment.ToString());
                start = i + 2;
            }
        }
        ReadOnlySpan<char> lastSegment = typeName[start..].Trim();
        parts.Add(lastSegment.ToString());
        return parts;
    }

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
        bool hasBigInt = types.Any(t => t is TypeInfo.BigInt);

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

    private TypeInfo ParseParenthesizedType(string typeName)
    {
        int depth = 0;
        int closeIndex = -1;
        for (int i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] == '(') depth++;
            else if (typeName[i] == ')') { depth--; if (depth == 0) { closeIndex = i; break; } }
        }

        string inner = typeName[1..closeIndex];
        string suffix = typeName[(closeIndex + 1)..];

        TypeInfo result = ToTypeInfo(inner);
        while (suffix.StartsWith("[]")) { result = new TypeInfo.Array(result); suffix = suffix[2..]; }
        return result;
    }

    private TypeInfo ParseFunctionTypeInfo(string funcType)
    {
        // Parse "(param1, param2) => returnType" or "(this: Type, param1) => returnType"
        // Find the OUTERMOST => (not one inside nested function types)
        var arrowIdx = FindOutermostArrow(funcType);
        if (arrowIdx < 0)
        {
            // Malformed - no arrow found at top level
            return new TypeInfo.Any();
        }
        var paramsSection = funcType.Substring(0, arrowIdx).Trim();
        var returnTypeStr = funcType.Substring(arrowIdx + 2).Trim();

        // Remove surrounding parentheses
        if (paramsSection.StartsWith("(") && paramsSection.EndsWith(")"))
        {
            paramsSection = paramsSection.Substring(1, paramsSection.Length - 2);
        }

        TypeInfo? thisType = null;
        List<TypeInfo> paramTypes = [];

        if (!string.IsNullOrWhiteSpace(paramsSection))
        {
            var parts = SplitFunctionParams(paramsSection.AsSpan());
            foreach (var part in parts)
            {
                var param = part.Trim();

                // Check for 'this' parameter: "this: Type"
                if (param.StartsWith("this:"))
                {
                    var thisTypeStr = param.Substring(5).Trim(); // Skip "this:"
                    thisType = ToTypeInfo(thisTypeStr);
                }
                else
                {
                    paramTypes.Add(ToTypeInfo(param));
                }
            }
        }

        TypeInfo returnType = ToTypeInfo(returnTypeStr);
        return new TypeInfo.Function(paramTypes, returnType, -1, false, thisType);
    }

    /// <summary>
    /// Finds the outermost => in a function type string, respecting nested brackets.
    /// Returns -1 if no top-level arrow is found.
    /// </summary>
    private static int FindOutermostArrow(string funcType)
    {
        int depth = 0;
        for (int i = 0; i < funcType.Length - 1; i++)
        {
            char c = funcType[i];
            if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == '>' && (i == 0 || funcType[i - 1] != '=')) depth--;  // Skip > in =>
            else if (c == '=' && funcType[i + 1] == '>' && depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Split function parameters respecting nested brackets and generics.
    /// </summary>
    private List<string> SplitFunctionParams(ReadOnlySpan<char> paramsStr)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < paramsStr.Length; i++)
        {
            char c = paramsStr[i];
            if (c == '(' || c == '<' || c == '[' || c == '{')
                depth++;
            else if (c == ')' || c == '>' || c == ']' || c == '}')
                depth--;
            else if (c == ',' && depth == 0)
            {
                ReadOnlySpan<char> segment = paramsStr[start..i];
                parts.Add(segment.ToString());
                start = i + 1;
            }
        }

        if (start < paramsStr.Length)
        {
            ReadOnlySpan<char> lastSegment = paramsStr[start..];
            parts.Add(lastSegment.ToString());
        }

        return parts;
    }

    private TypeInfo ParseTupleTypeInfo(string tupleStr)
    {
        string inner = tupleStr[1..^1].Trim(); // Remove [ and ]
        if (string.IsNullOrEmpty(inner))
            return new TypeInfo.Tuple([], 0, null);

        var elemStrings = SplitTupleElements(inner.AsSpan());
        List<TypeInfo.TupleElement> tupleElements = [];
        int requiredCount = 0;
        bool seenOptional = false;
        bool seenSpread = false;
        TypeInfo? restType = null;

        for (int i = 0; i < elemStrings.Count; i++)
        {
            string elem = elemStrings[i].Trim();
            string? name = null;

            // Spread element: ...T (variadic) or ...T[] (rest)
            if (elem.StartsWith("..."))
            {
                string spreadTypeStr = elem[3..];

                // Trailing rest element: ...T[] (must be last)
                if (spreadTypeStr.EndsWith("[]"))
                {
                    if (i != elemStrings.Count - 1)
                    {
                        // Not last - treat as variadic spread of array type
                        var spreadInner = ToTypeInfo(spreadTypeStr);
                        tupleElements.Add(new TypeInfo.TupleElement(spreadInner, TupleElementKind.Spread, null));
                        seenSpread = true;
                        continue;
                    }
                    // Trailing rest element
                    restType = ToTypeInfo(spreadTypeStr[..^2]);
                    break;
                }
                else
                {
                    // Variadic spread: ...T (not ending with [])
                    TypeInfo spreadInner = ToTypeInfo(spreadTypeStr);
                    tupleElements.Add(new TypeInfo.TupleElement(spreadInner, TupleElementKind.Spread, null));
                    seenSpread = true;
                    continue;
                }
            }

            // Check for named element: name: type or name?: type
            int colonIdx = elem.IndexOf(':');
            bool isOptional = false;
            if (colonIdx > 0)
            {
                string potentialName = elem[..colonIdx].Trim();
                // Handle optional named element: name?: type
                if (potentialName.EndsWith('?'))
                {
                    potentialName = potentialName[..^1];
                    isOptional = true;
                }
                // Validate it's an identifier (not a built-in type name)
                if (IsValidTupleElementName(potentialName))
                {
                    name = potentialName;
                    elem = elem[(colonIdx + 1)..].Trim();
                }
                else
                {
                    isOptional = false; // Reset if name wasn't valid
                }
            }

            // Optional element: type? (for unnamed elements or if not already set)
            if (!isOptional && elem.EndsWith("?"))
            {
                isOptional = true;
                elem = elem[..^1];
            }

            // Validation: required elements cannot follow optional/spread elements
            if (isOptional)
            {
                seenOptional = true;
            }
            else if (seenOptional && !seenSpread)
            {
                throw new TypeCheckException(" Required element cannot follow optional element in tuple.");
            }

            TupleElementKind kind = isOptional ? TupleElementKind.Optional : TupleElementKind.Required;
            tupleElements.Add(new TypeInfo.TupleElement(ToTypeInfo(elem), kind, name));
            if (!isOptional) requiredCount++;
        }

        return new TypeInfo.Tuple(tupleElements, requiredCount, restType);
    }

    /// <summary>
    /// Checks if a string is a valid tuple element name (identifier that's not a type keyword).
    /// </summary>
    private static bool IsValidTupleElementName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;

        // Must start with letter or underscore
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;

        // Rest must be alphanumeric or underscore
        if (!s.All(c => char.IsLetterOrDigit(c) || c == '_')) return false;

        // Must not be a type keyword
        return s is not ("string" or "number" or "boolean" or "void" or "null" or "undefined"
                      or "unknown" or "never" or "any" or "symbol" or "bigint" or "object");
    }

    private List<string> SplitTupleElements(ReadOnlySpan<char> inner)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(' || c == '[' || c == '<') depth++;
            else if (c == ')' || c == ']' || c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                ReadOnlySpan<char> segment = inner[start..i];
                parts.Add(segment.ToString());
                start = i + 1;
            }
        }
        ReadOnlySpan<char> lastSegment = inner[start..];
        parts.Add(lastSegment.ToString());
        return parts;
    }

    /// <summary>
    /// Parses inline object type strings like "{ x: number; y?: string }".
    /// Also handles mapped types like "{ [K in keyof T]: T[K] }".
    /// </summary>
    private TypeInfo ParseInlineObjectTypeInfo(string objStr)
    {
        // Remove "{ " and " }" from the string
        string inner = objStr[2..^2].Trim();
        if (string.IsNullOrEmpty(inner))
            return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        // Check if this is a mapped type
        if (IsMappedTypeString(inner))
        {
            return ParseMappedTypeInfo(inner);
        }

        Dictionary<string, TypeInfo> fields = [];
        HashSet<string> optionalFields = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        // Split by semicolon (the separator used in ParseInlineObjectType)
        var members = SplitObjectMembers(inner.AsSpan());

        foreach (var member in members)
        {
            string m = member.Trim();
            if (string.IsNullOrEmpty(m)) continue;

            // Check for index signature: [string]: type, [number]: type, [symbol]: type
            if (m.StartsWith("["))
            {
                int bracketEnd = m.IndexOf(']');
                if (bracketEnd > 0)
                {
                    string keyType = m[1..bracketEnd].Trim();
                    int colonIdx = m.IndexOf(':', bracketEnd);
                    if (colonIdx > 0)
                    {
                        string valueType = m[(colonIdx + 1)..].Trim();
                        TypeInfo valueTypeInfo = ToTypeInfo(valueType);

                        switch (keyType)
                        {
                            case "string":
                                stringIndexType = valueTypeInfo;
                                break;
                            case "number":
                                numberIndexType = valueTypeInfo;
                                break;
                            case "symbol":
                                symbolIndexType = valueTypeInfo;
                                break;
                        }
                        continue;
                    }
                }
            }

            // Find the colon separator (property name: type)
            int regularColonIdx = m.IndexOf(':');
            if (regularColonIdx < 0) continue;

            string propName = m[..regularColonIdx].Trim();
            string propType = m[(regularColonIdx + 1)..].Trim();

            // Check for optional marker (?) and track it
            bool isOptional = propName.EndsWith("?");
            if (isOptional)
            {
                propName = propName[..^1].Trim();
                optionalFields.Add(propName);
            }

            fields[propName] = ToTypeInfo(propType);
        }

        return new TypeInfo.Record(
            fields.ToFrozenDictionary(),
            stringIndexType,
            numberIndexType,
            symbolIndexType,
            optionalFields.Count > 0 ? optionalFields.ToFrozenSet() : null);
    }

    private List<string> SplitObjectMembers(ReadOnlySpan<char> inner)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(' || c == '[' || c == '<' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '>' || c == '}') depth--;
            else if (c == ';' && depth == 0)
            {
                ReadOnlySpan<char> segment = inner[start..i];
                parts.Add(segment.ToString());
                start = i + 1;
            }
        }
        if (start < inner.Length)
        {
            ReadOnlySpan<char> lastSegment = inner[start..];
            parts.Add(lastSegment.ToString());
        }
        return parts;
    }

    /// <summary>
    /// Tries to parse an indexed access type like T[K] or T["key"].
    /// Returns null if not a valid indexed access pattern.
    /// </summary>
    private TypeInfo? TryParseIndexedAccessType(string typeName)
    {
        // Find the outermost [ that's followed by content and then ]
        int depth = 0;
        int bracketStart = -1;

        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '<' || c == '(' || c == '{') depth++;
            else if (c == '>' || c == ')' || c == '}') depth--;
            else if (c == '[' && depth == 0)
            {
                bracketStart = i;
                break;
            }
        }

        if (bracketStart < 0) return null;

        // Find the matching ]
        depth = 0;
        int bracketEnd = -1;
        for (int i = bracketStart; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    bracketEnd = i;
                    break;
                }
            }
        }

        if (bracketEnd < 0 || bracketEnd == bracketStart + 1) return null; // Empty brackets []

        string baseType = typeName[..bracketStart];
        string indexType = typeName[(bracketStart + 1)..bracketEnd];
        string remaining = typeName[(bracketEnd + 1)..];

        // Parse the base and index types
        TypeInfo baseTypeInfo = ToTypeInfo(baseType);
        TypeInfo indexTypeInfo = ToTypeInfo(indexType);
        TypeInfo result = new TypeInfo.IndexedAccess(baseTypeInfo, indexTypeInfo);

        // Handle chained indexed access: T[K][J]
        if (!string.IsNullOrEmpty(remaining) && remaining.StartsWith("["))
        {
            // Recurse with the remaining part
            return TryParseIndexedAccessType($"{result}{remaining}");
        }

        // Handle array suffix after indexed access: T[K][]
        while (remaining.StartsWith("[]"))
        {
            result = new TypeInfo.Array(result);
            remaining = remaining[2..];
        }

        return result;
    }

    /// <summary>
    /// Checks if an inline object type string represents a mapped type.
    /// Mapped types have the pattern: { [K in ...]: ... }
    /// </summary>
    private bool IsMappedTypeString(string inner)
    {
        // Must contain " in " to be a mapped type
        // Index signatures use ": " after the bracket, mapped types use " in "
        return inner.Contains(" in ") && inner.TrimStart().StartsWith("[") ||
               inner.TrimStart().StartsWith("+readonly ") ||
               inner.TrimStart().StartsWith("-readonly ") ||
               inner.TrimStart().StartsWith("readonly ");
    }

    /// <summary>
    /// Parses a mapped type string into a MappedType TypeInfo.
    /// Format: { [+/-readonly] [K in Constraint [as RemapType]][+/-?]: ValueType }
    /// </summary>
    private TypeInfo ParseMappedTypeInfo(string inner)
    {
        MappedTypeModifiers modifiers = MappedTypeModifiers.None;

        // Parse leading modifiers
        if (inner.StartsWith("+readonly "))
        {
            modifiers |= MappedTypeModifiers.AddReadonly;
            inner = inner[10..].Trim();
        }
        else if (inner.StartsWith("-readonly "))
        {
            modifiers |= MappedTypeModifiers.RemoveReadonly;
            inner = inner[10..].Trim();
        }
        else if (inner.StartsWith("readonly "))
        {
            modifiers |= MappedTypeModifiers.AddReadonly;
            inner = inner[9..].Trim();
        }

        // Find the [ and ] brackets for the parameter
        int openBracket = inner.IndexOf('[');
        int closeBracket = FindMatchingBracket(inner, openBracket);

        if (openBracket < 0 || closeBracket < 0)
            throw new TypeCheckException(" Invalid mapped type syntax.");

        string bracketContent = inner[(openBracket + 1)..closeBracket];
        string afterBracket = inner[(closeBracket + 1)..].Trim();

        // Parse [K in Constraint as RemapType]
        int inIndex = bracketContent.IndexOf(" in ");
        if (inIndex < 0)
            throw new TypeCheckException(" Mapped type must contain 'in' keyword.");

        string paramName = bracketContent[..inIndex].Trim();
        string afterIn = bracketContent[(inIndex + 4)..].Trim();

        // Check for 'as' clause
        TypeInfo? asClause = null;
        string constraintStr;
        int asIndex = FindTopLevelAs(afterIn);
        if (asIndex >= 0)
        {
            constraintStr = afterIn[..asIndex].Trim();
            string asTypeStr = afterIn[(asIndex + 3)..].Trim();
            asClause = ToTypeInfo(asTypeStr);
        }
        else
        {
            constraintStr = afterIn;
        }
        TypeInfo constraint = ToTypeInfo(constraintStr);

        // Parse trailing modifiers: +?, -?, ?
        if (afterBracket.StartsWith("+?"))
        {
            modifiers |= MappedTypeModifiers.AddOptional;
            afterBracket = afterBracket[2..].Trim();
        }
        else if (afterBracket.StartsWith("-?"))
        {
            modifiers |= MappedTypeModifiers.RemoveOptional;
            afterBracket = afterBracket[2..].Trim();
        }
        else if (afterBracket.StartsWith("?"))
        {
            modifiers |= MappedTypeModifiers.AddOptional;
            afterBracket = afterBracket[1..].Trim();
        }

        // Parse : ValueType
        if (!afterBracket.StartsWith(":"))
            throw new TypeCheckException(" Expected ':' after mapped type parameter.");

        string valueTypeStr = afterBracket[1..].Trim();
        TypeInfo valueType = ToTypeInfo(valueTypeStr);

        return new TypeInfo.MappedType(paramName, constraint, valueType, modifiers, asClause);
    }

    /// <summary>
    /// Finds the matching closing bracket for an opening bracket at the given index.
    /// </summary>
    private static int FindMatchingBracket(string str, int openIndex)
    {
        if (openIndex < 0 || openIndex >= str.Length) return -1;
        char openChar = str[openIndex];
        char closeChar = openChar switch
        {
            '[' => ']',
            '(' => ')',
            '{' => '}',
            '<' => '>',
            _ => '\0'
        };
        if (closeChar == '\0') return -1;

        int depth = 1;
        for (int i = openIndex + 1; i < str.Length; i++)
        {
            if (str[i] == openChar) depth++;
            else if (str[i] == closeChar)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Finds the index of ' as ' at the top level (not inside any brackets).
    /// Returns -1 if not found.
    /// </summary>
    private static int FindTopLevelAs(string str)
    {
        int depth = 0;
        for (int i = 0; i < str.Length - 3; i++)
        {
            char c = str[i];
            if (c == '<' || c == '(' || c == '[' || c == '{') depth++;
            else if (c == '>' || c == ')' || c == ']' || c == '}') depth--;
            else if (depth == 0 && str.Substring(i, 4) == " as " &&
                     (i + 4 >= str.Length || !char.IsLetterOrDigit(str[i + 4])))
            {
                return i + 1; // Return index after the space, at 'as'
            }
        }
        return -1;
    }

    /// <summary>
    /// Tries to parse a conditional type string. Returns null if not a conditional type.
    /// Format: "CheckType extends ExtendsType ? TrueType : FalseType"
    /// </summary>
    private TypeInfo? TryParseConditionalType(string typeName)
    {
        // Find " extends " at the top level (not inside brackets)
        int extendsIndex = FindTopLevelKeyword(typeName, " extends ");
        if (extendsIndex < 0) return null;

        string checkTypeStr = typeName[..extendsIndex].Trim();
        string remainder = typeName[(extendsIndex + 9)..]; // Skip " extends "

        // Find the '?' at the top level
        int questionIndex = FindTopLevelChar(remainder, '?');
        if (questionIndex < 0) return null;

        string extendsTypeStr = remainder[..questionIndex].Trim();
        string afterQuestion = remainder[(questionIndex + 1)..].Trim();

        // Find the ':' at the top level
        int colonIndex = FindTopLevelChar(afterQuestion, ':');
        if (colonIndex < 0) return null;

        string trueTypeStr = afterQuestion[..colonIndex].Trim();
        string falseTypeStr = afterQuestion[(colonIndex + 1)..].Trim();

        // Parse all four type components
        TypeInfo checkType = ToTypeInfo(checkTypeStr);
        TypeInfo extendsType = ToTypeInfo(extendsTypeStr);
        TypeInfo trueType = ToTypeInfo(trueTypeStr);
        TypeInfo falseType = ToTypeInfo(falseTypeStr);

        return new TypeInfo.ConditionalType(checkType, extendsType, trueType, falseType);
    }

    /// <summary>
    /// Finds a keyword at the top level (not inside brackets/parens/braces/angle brackets).
    /// Returns the index of the first character of the keyword, or -1 if not found.
    /// </summary>
    private static int FindTopLevelKeyword(string str, string keyword)
    {
        int depth = 0;
        for (int i = 0; i <= str.Length - keyword.Length; i++)
        {
            char c = str[i];
            if (c == '<' || c == '(' || c == '[' || c == '{') depth++;
            else if (c == '>' || c == ')' || c == ']' || c == '}') depth--;
            else if (depth == 0 && str.Substring(i, keyword.Length) == keyword)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Finds a character at the top level (not inside brackets/parens/braces/angle brackets).
    /// Returns the index of the character, or -1 if not found.
    /// </summary>
    private static int FindTopLevelChar(string str, char target)
    {
        int depth = 0;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (c == '<' || c == '(' || c == '[' || c == '{') depth++;
            else if (c == '>' || c == ')' || c == ']' || c == '}') depth--;
            else if (depth == 0 && c == target)
            {
                return i;
            }
        }
        return -1;
    }

    // ============== TEMPLATE LITERAL TYPE PARSING ==============

    /// <summary>
    /// Parses a template literal type string into TypeInfo.
    /// May return Union of StringLiterals if fully expandable, or TemplateLiteralType if contains 'string'.
    /// </summary>
    private TypeInfo ParseTemplateLiteralTypeInfo(string typeStr)
    {
        // Remove backticks
        string inner = typeStr[1..^1];

        List<string> strings = [];
        List<TypeInfo> interpolatedTypes = [];

        int pos = 0;
        var currentString = new System.Text.StringBuilder();

        while (pos < inner.Length)
        {
            if (pos < inner.Length - 1 && inner[pos] == '$' && inner[pos + 1] == '{')
            {
                // Found interpolation start
                strings.Add(currentString.ToString());
                currentString.Clear();
                pos += 2; // skip ${

                // Find matching } with brace depth tracking
                int braceDepth = 1;
                int start = pos;
                while (pos < inner.Length && braceDepth > 0)
                {
                    if (inner[pos] == '{') braceDepth++;
                    else if (inner[pos] == '}') braceDepth--;
                    if (braceDepth > 0) pos++;
                }

                string typeContent = inner[start..pos];
                interpolatedTypes.Add(ToTypeInfo(typeContent));
                pos++; // skip }
            }
            else
            {
                currentString.Append(inner[pos]);
                pos++;
            }
        }
        strings.Add(currentString.ToString());

        return NormalizeTemplateLiteralType(strings, interpolatedTypes);
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
            throw new TypeCheckException(" Template literal type produces too many combinations (limit: 10000).");

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
            throw new TypeCheckException($"Invalid typeof expression: '{path}'");

        // Look up first identifier in environment
        string firstName = accessors[0].Name;
        TypeInfo? currentType = _environment.Get(firstName);

        if (currentType == null)
            throw new TypeCheckException($"Cannot find name '{firstName}' for typeof.");

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
                throw new TypeCheckException($"Cannot access '{accessor.Name}' on type in typeof.");
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
