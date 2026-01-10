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
            return ToTypeInfo(aliasExpansion);
        }

        // Handle keyof type operator: "keyof T"
        if (typeName.StartsWith("keyof "))
        {
            string innerTypeStr = typeName[6..].Trim();
            TypeInfo innerType = ToTypeInfo(innerTypeStr);
            return new TypeInfo.KeyOf(innerType);
        }

        // Handle generic type syntax: Box<number>, Map<string, number>
        if (typeName.Contains('<') && typeName.Contains('>'))
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

        if (typeName == "string") return new TypeInfo.Primitive(TokenType.TYPE_STRING);
        if (typeName == "number") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (typeName == "boolean") return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (typeName == "symbol") return new TypeInfo.Symbol();
        if (typeName == "bigint") return new TypeInfo.BigInt();
        if (typeName == "void") return new TypeInfo.Void();
        if (typeName == "null") return new TypeInfo.Null();
        if (typeName == "unknown") return new TypeInfo.Unknown();
        if (typeName == "never") return new TypeInfo.Never();

        TypeInfo? type = _environment.Get(typeName);
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
            if (c == '(') depth++;
            else if (c == ')') depth--;
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
            if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == '>' || c == ']' || c == '}') depth--;
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
        var primitives = types.OfType<TypeInfo.Primitive>().ToList();
        if (primitives.Count > 1)
        {
            var distinctPrimitives = primitives.Select(p => p.Type).Distinct().ToList();
            if (distinctPrimitives.Count > 1)
                return new TypeInfo.Never();  // Conflicting primitives
        }

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
        var arrowIdx = funcType.IndexOf("=>");
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

        var elements = SplitTupleElements(inner.AsSpan());
        List<TypeInfo> elementTypes = [];
        int requiredCount = 0;
        bool seenOptional = false;
        TypeInfo? restType = null;

        for (int i = 0; i < elements.Count; i++)
        {
            string elem = elements[i].Trim();

            // Rest element: ...type[]
            if (elem.StartsWith("..."))
            {
                if (i != elements.Count - 1)
                    throw new Exception("Type Error: Rest element must be last in tuple type.");
                string arrayType = elem[3..];
                if (!arrayType.EndsWith("[]"))
                    throw new Exception("Type Error: Rest element must be an array type.");
                restType = ToTypeInfo(arrayType[..^2]);
                break;
            }

            // Optional element: type?
            bool isOptional = elem.EndsWith("?");
            if (isOptional)
            {
                elem = elem[..^1];
                seenOptional = true;
            }
            else if (seenOptional)
            {
                throw new Exception("Type Error: Required element cannot follow optional element in tuple.");
            }

            elementTypes.Add(ToTypeInfo(elem));
            if (!isOptional) requiredCount++;
        }

        return new TypeInfo.Tuple(elementTypes, requiredCount, restType);
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

            // Check for optional marker (?) and remove it
            if (propName.EndsWith("?"))
            {
                propName = propName[..^1].Trim();
            }

            fields[propName] = ToTypeInfo(propType);
        }

        return new TypeInfo.Record(fields.ToFrozenDictionary(), stringIndexType, numberIndexType, symbolIndexType);
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
            throw new Exception("Type Error: Invalid mapped type syntax.");

        string bracketContent = inner[(openBracket + 1)..closeBracket];
        string afterBracket = inner[(closeBracket + 1)..].Trim();

        // Parse [K in Constraint as RemapType]
        int inIndex = bracketContent.IndexOf(" in ");
        if (inIndex < 0)
            throw new Exception("Type Error: Mapped type must contain 'in' keyword.");

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
            throw new Exception("Type Error: Expected ':' after mapped type parameter.");

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
}
