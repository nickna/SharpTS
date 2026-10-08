using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public sealed record EditorRenderLimits(int MaxDepth = 8, int MaxNodes = 256,
    int MaxCharacters = 4096, int MaxCandidates = 32);

/// <summary>Bounded editor presentation. It never calls recursive TypeInfo.ToString().</summary>
public static class EditorTypeRenderer
{
    public static EditorTypePresentation Render(TypeInfo? type,
        EditorTypeRenderContext context = EditorTypeRenderContext.Type, EditorRenderLimits? limits = null,
        IReadOnlyDictionary<string, TypeInfo>? substitutions = null,
        IReadOnlyDictionary<TypeInfo, EditorCallableSurface>? callableSurfaces = null)
    {
        var writer = new Writer(limits ?? new(), substitutions, callableSurfaces);
        writer.Type(type, 0, context);
        return writer.Presentation(0, 0, 0);
    }

    public static EditorSignaturePresentation RenderSignature(TypeInfo? signature,
        bool isConstructor = false, TypeInfo? constructedType = null,
        IReadOnlyList<TypeInfo.TypeParameter>? typeParameters = null, EditorRenderLimits? limits = null,
        IReadOnlyDictionary<string, TypeInfo>? substitutions = null,
        IReadOnlyDictionary<TypeInfo, EditorCallableSurface>? callableSurfaces = null,
        IReadOnlyList<string>? parameterNames = null)
    {
        var writer = new Writer(limits ?? new(), substitutions, callableSurfaces);
        return writer.Signature(signature, 0, isConstructor, constructedType, typeParameters, arrow: false,
            parameterNames: parameterNames);
    }

    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        private readonly HashSet<TypeInfo> _active = new(ReferenceEqualityComparer.Instance);
        private readonly EditorRenderLimits _limits;
        private readonly IReadOnlyDictionary<string, TypeInfo>? _substitutions;
        private readonly IReadOnlyDictionary<TypeInfo, EditorCallableSurface>? _callableSurfaces;
        private readonly HashSet<string> _shadowed = new(StringComparer.Ordinal);
        private int _suppressSubstitutions;
        private int _nodes;
        private int _unavailable;
        private int _truncated;
        private bool _exhausted;
        private bool Full => _exhausted || _text.Length >= _limits.MaxCharacters;

        public Writer(EditorRenderLimits limits, IReadOnlyDictionary<string, TypeInfo>? substitutions,
            IReadOnlyDictionary<TypeInfo, EditorCallableSurface>? callableSurfaces)
        {
            _substitutions = substitutions;
            _callableSurfaces = callableSurfaces;
            _limits = new(Math.Clamp(limits.MaxDepth, 1, 32), Math.Clamp(limits.MaxNodes, 1, 4096),
                Math.Clamp(limits.MaxCharacters, 16, 65536), Math.Clamp(limits.MaxCandidates, 1, 256));
        }

        public EditorTypePresentation Presentation(int start, int unavailable, int truncated) =>
            new(_text.ToString(start, _text.Length - start), _unavailable == unavailable,
                _truncated != truncated);

        private void Text(string value)
        {
            if (Full) { if (value.Length > 0) _truncated++; return; }
            int count = Math.Min(value.Length, _limits.MaxCharacters - _text.Length);
            bool clipped = count != value.Length;
            if (count > 0 && _text.Length + count == _limits.MaxCharacters && char.IsHighSurrogate(value[count - 1]))
            {
                count--;
                clipped = true;
            }
            if (clipped) { _truncated++; _exhausted = true; }
            _text.Append(value.AsSpan(0, count));
        }

        private void Ellipsis() { _truncated++; Text("…"); }
        private void Unavailable() { _unavailable++; Text("unavailable"); }
        private void Name(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("$ClassExpr_", StringComparison.Ordinal) ||
                name.StartsWith("$AnySuperclass", StringComparison.Ordinal)) Unavailable();
            else Text(name);
        }

        private void Quote(string value, bool template = false)
        {
            Text(template ? "`" : "\"");
            foreach (char character in value)
            {
                if (Full) { _truncated++; break; }
                Text(character switch
                {
                    '\\' => "\\\\", '\"' when !template => "\\\"", '`' when template => "\\`",
                    '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\0' => "\\0",
                    _ when char.IsControl(character) => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                    _ => character.ToString(),
                });
            }
            Text(template ? "`" : "\"");
        }

        public void Type(TypeInfo? type, int depth, EditorTypeRenderContext context = EditorTypeRenderContext.Type)
        {
            if (Full) { _truncated++; return; }
            if (type is null or TypeInfo.Inferred) { Unavailable(); return; }
            if (++_nodes > _limits.MaxNodes || depth >= _limits.MaxDepth || !_active.Add(type)) { Ellipsis(); return; }
            try
            {
                // The family is attached to this exact checker object, never inferred from
                // a matching name/shape or attached to an instantiated selected signature.
                if (_callableSurfaces?.TryGetValue(type, out var surface) == true)
                {
                    Overloads(surface.Signatures, surface.TypeParameters, depth + 1);
                    return;
                }
                switch (type)
                {
                    case TypeInfo.Primitive primitive:
                        if (primitive.Type == TokenType.TYPE_NUMBER) Text("number");
                        else if (primitive.Type == TokenType.TYPE_BOOLEAN) Text("boolean"); else Unavailable();
                        break;
                    case TypeInfo.String: Text("string"); break;
                    case TypeInfo.Any: Text("any"); break;
                    case TypeInfo.Unknown: Text("unknown"); break;
                    case TypeInfo.Never: Text("never"); break;
                    case TypeInfo.Void: Text("void"); break;
                    case TypeInfo.Null: Text("null"); break;
                    case TypeInfo.Undefined: Text("undefined"); break;
                    case TypeInfo.Symbol: Text("symbol"); break;
                    case TypeInfo.UniqueSymbol: Text("unique symbol"); break;
                    case TypeInfo.BigInt: Text("bigint"); break;
                    case TypeInfo.Object: Text("object"); break;
                    case TypeInfo.StringLiteral literal: Quote(literal.Value); break;
                    case TypeInfo.NumberLiteral literal: Text(literal.Value.ToString("R", CultureInfo.InvariantCulture)); break;
                    case TypeInfo.BooleanLiteral literal: Text(literal.Value ? "true" : "false"); break;
                    case TypeInfo.BigIntLiteral literal:
                        if (literal.Value.GetByteCount() > _limits.MaxCharacters) Ellipsis();
                        else { Text(literal.Value.ToString(CultureInfo.InvariantCulture)); Text("n"); }
                        break;
                    case TypeInfo.Class @class: if (context == EditorTypeRenderContext.Value) Text("typeof "); Name(@class.Name); break;
                    case TypeInfo.MutableClass @class: if (context == EditorTypeRenderContext.Value) Text("typeof "); Name(@class.Name); break;
                    case TypeInfo.GenericClass @class:
                        if (context == EditorTypeRenderContext.Value) Text("typeof ");
                        Name(@class.Name);
                        if (context == EditorTypeRenderContext.Type) TypeParameters(@class.TypeParams, depth + 1, namesOnly: true);
                        break;
                    case TypeInfo.Instance instance: Type(instance.ResolvedClassType, depth + 1); break;
                    case TypeInfo.Interface @interface: Name(@interface.Name); break;
                    case TypeInfo.GenericInterface @interface: Name(@interface.Name); TypeParameters(@interface.TypeParams, depth + 1, namesOnly: true); break;
                    // Enum member values and the enum namespace share this checker type. Only a
                    // source/query role can prove namespace ownership, so generic presentation
                    // must not describe every enum-typed value as typeof Enum.
                    case TypeInfo.Enum @enum: Name(@enum.Name); break;
                    case TypeInfo.Namespace @namespace: Text("typeof "); Name(@namespace.Name); break;
                    case TypeInfo.ExternalDotNetType external: if (context == EditorTypeRenderContext.Value) Text("typeof "); Name(external.TypeScriptName); break;
                    case TypeInfo.TypeParameter parameter:
                        if (_suppressSubstitutions == 0 && !_shadowed.Contains(parameter.Name) && _substitutions?.TryGetValue(parameter.Name, out var replacement) == true)
                            Type(replacement, depth + 1, context);
                        else Name(parameter.Name);
                        break;
                    case TypeInfo.InferredTypeParameter parameter: Text("infer "); Name(parameter.Name); if (parameter.Constraint is not null) { Text(" extends "); Type(parameter.Constraint, depth + 1); } break;
                    case TypeInfo.RecursiveTypeAlias alias: Name(alias.AliasName); Arguments(alias.TypeArguments, depth + 1); break;
                    case TypeInfo.InstantiatedGeneric generic:
                        switch (generic.GenericDefinition)
                        {
                            case TypeInfo.GenericClass definition: if (context == EditorTypeRenderContext.Value) Text("typeof "); Name(definition.Name); break;
                            case TypeInfo.GenericInterface definition: Name(definition.Name); break;
                            default: Unavailable(); break;
                        }
                        Arguments(generic.TypeArguments, depth + 1); break;
                    case TypeInfo.Array array: Text(array.IsReadonly ? "ReadonlyArray<" : "Array<"); Type(array.ElementType, depth + 1); Text(">"); break;
                    case TypeInfo.Tuple tuple:
                        if (tuple.IsReadonly) Text("readonly "); Text("[");
                        for (int index = 0; index < tuple.Elements.Count && !Full; index++)
                        {
                            if (index >= _limits.MaxNodes || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                            if (index != 0) Text(", ");
                            var element = tuple.Elements[index];
                            if (element.IsSpread) Text("...");
                            if (element.Name is not null) { Name(element.Name); if (element.IsOptional) Text("?"); Text(": "); }
                            Type(element.Type, depth + 1);
                            if (element.IsOptional && element.Name is null) Text("?");
                        }
                        if (tuple.RestElementType is not null) { if (tuple.Elements.Count > 0) Text(", "); Text("..."); Type(tuple.RestElementType, depth + 1); Text("[]"); }
                        Text("]"); break;
                    case TypeInfo.Union union: Joined(union.Types, " | ", depth + 1); break;
                    case TypeInfo.Intersection intersection: Joined(intersection.Types, " & ", depth + 1); break;
                    case TypeInfo.Record record: Record(record, depth + 1); break;
                    case TypeInfo.FunctionSupertype: Text("Function"); break;
                    case TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.CallSignature or TypeInfo.ConstructorSignature:
                        Signature(type, depth + 1, type is TypeInfo.ConstructorSignature, null, null, arrow: true); break;
                    case TypeInfo.OverloadedFunction overloaded: Overloads(overloaded.Signatures, null, depth + 1); break;
                    case TypeInfo.GenericOverloadedFunction overloaded: Overloads(overloaded.Signatures, overloaded.TypeParams, depth + 1); break;
                    case TypeInfo.OverloadSet overloaded: Overloads(overloaded.Signatures, null, depth + 1); break;
                    case TypeInfo.SpreadType spread: Text("..."); Type(spread.Inner, depth + 1); break;
                    case TypeInfo.KeyOf key: Text("keyof ("); Type(key.SourceType, depth + 1); Text(")"); break;
                    case TypeInfo.TypeOf query: Text("typeof "); Name(query.Path); break;
                    case TypeInfo.IndexedAccess access: Text("("); Type(access.ObjectType, depth + 1); Text(")["); Type(access.IndexType, depth + 1); Text("]"); break;
                    case TypeInfo.ConditionalType conditional:
                        Text("("); Type(conditional.CheckType, depth + 1); Text(" extends "); Type(conditional.ExtendsType, depth + 1);
                        Text(" ? "); Type(conditional.TrueType, depth + 1); Text(" : "); Type(conditional.FalseType, depth + 1); Text(")"); break;
                    case TypeInfo.MappedType mapped:
                        Text("{ "); if (mapped.Modifiers.HasFlag(MappedTypeModifiers.AddReadonly)) Text("+readonly ");
                        else if (mapped.Modifiers.HasFlag(MappedTypeModifiers.RemoveReadonly)) Text("-readonly ");
                        Text("["); Name(mapped.ParameterName); Text(" in "); Type(mapped.Constraint, depth + 1);
                        bool shadowMappedParameter = _shadowed.Add(mapped.ParameterName);
                        try
                        {
                        if (mapped.AsClause is not null) { Text(" as "); Type(mapped.AsClause, depth + 1); }
                        Text("]"); if (mapped.Modifiers.HasFlag(MappedTypeModifiers.AddOptional)) Text("+?");
                        else if (mapped.Modifiers.HasFlag(MappedTypeModifiers.RemoveOptional)) Text("-?");
                        Text(": "); Type(mapped.ValueType, depth + 1); Text(" }");
                        }
                        finally { if (shadowMappedParameter) _shadowed.Remove(mapped.ParameterName); }
                        break;
                    case TypeInfo.TemplateLiteralType template:
                        Text("`");
                        for (int index = 0; index < template.Strings.Count && !Full; index++)
                        {
                            if (index >= _limits.MaxNodes) { Ellipsis(); break; }
                            // Quote each segment without allocating the complete literal.
                            string segment = template.Strings[index];
                            foreach (char character in segment) { if (Full) break; Text(character is '`' or '\\' or '$' ? "\\" + character : character.ToString()); }
                            if (index < template.InterpolatedTypes.Count) { Text("${"); Type(template.InterpolatedTypes[index], depth + 1); Text("}"); }
                        }
                        Text("`"); break;
                    case TypeInfo.IntrinsicStringType intrinsic: Text(intrinsic.Operation.ToString()); Text("<"); Type(intrinsic.Inner, depth + 1); Text(">"); break;
                    case TypeInfo.TypePredicate predicate: if (predicate.IsAssertion) Text("asserts "); Name(predicate.ParameterName); Text(" is "); Type(predicate.PredicateType, depth + 1); break;
                    case TypeInfo.AssertsNonNull assertion: Text("asserts "); Name(assertion.ParameterName); break;
                    case TypeInfo.Promise promise: Generic("Promise", [promise.ValueType], depth + 1); break;
                    case TypeInfo.Map map: Generic("Map", [map.KeyType, map.ValueType], depth + 1); break;
                    case TypeInfo.Set set: Generic("Set", [set.ElementType], depth + 1); break;
                    case TypeInfo.Iterator iterator: Generic("IterableIterator", [iterator.ElementType], depth + 1); break;
                    case TypeInfo.Iterable iterable: Generic("Iterable", [iterable.ElementType], depth + 1); break;
                    case TypeInfo.Date: Text("Date"); break;
                    case TypeInfo.RegExp: Text("RegExp"); break;
                    case TypeInfo.Error error: Name(error.Name); break;
                    case TypeInfo.Buffer: Text("Buffer"); break;
                    case TypeInfo.Timeout: Text("NodeJS.Timeout"); break;
                    default: Unavailable(); break;
                }
            }
            finally { _active.Remove(type); }
        }

        private void Generic(string name, IReadOnlyList<TypeInfo> arguments, int depth) { Text(name); Arguments(arguments, depth); }
        private void Arguments(IReadOnlyList<TypeInfo>? arguments, int depth)
        {
            if (arguments is not { Count: > 0 }) return;
            Text("<"); Joined(arguments, ", ", depth); Text(">");
        }

        private void Joined(IReadOnlyList<TypeInfo> types, string separator, int depth)
        {
            for (int index = 0; index < types.Count && !Full; index++)
            {
                if (index >= _limits.MaxNodes || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                if (index != 0) Text(separator);
                bool parentheses = types[index] is TypeInfo.Union or TypeInfo.Intersection or TypeInfo.Function or TypeInfo.GenericFunction;
                if (parentheses) Text("("); Type(types[index], depth); if (parentheses) Text(")");
            }
        }

        private void TypeParameters(IReadOnlyList<TypeInfo.TypeParameter>? parameters, int depth, bool namesOnly = false)
        {
            if (parameters is not { Count: > 0 }) return;
            Text("<");
            for (int index = 0; index < parameters.Count && !Full; index++)
            {
                if (index >= _limits.MaxCandidates) { Ellipsis(); break; }
                if (index != 0) Text(", ");
                var parameter = parameters[index];
                if (!namesOnly && parameter.IsConst) Text("const ");
                Name(parameter.Name);
                if (!namesOnly && parameter.Constraint is not null) { Text(" extends "); Type(parameter.Constraint, depth); }
                if (!namesOnly && parameter.Default is not null) { Text(" = "); Type(parameter.Default, depth); }
            }
            Text(">");
        }

        private void Overloads(IEnumerable<TypeInfo> signatures, IReadOnlyList<TypeInfo.TypeParameter>? parameters, int depth)
        {
            Text("{ "); int count = 0;
            foreach (TypeInfo signature in signatures)
            {
                if (Full) break;
                if (count++ >= _limits.MaxCandidates || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                Signature(signature, depth, false, null, parameters, arrow: false); Text("; ");
            }
            if (count == 0) Unavailable();
            Text("}");
        }

        private void Record(TypeInfo.Record record, int depth)
        {
            Text("{ "); int count = 0;
            foreach (var field in record.Fields)
            {
                if (Full) break;
                if (count++ >= _limits.MaxNodes || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                if (record.IsReadonly || record.IsGetterOnly(field.Key)) Text("readonly ");
                Quote(field.Key); if (record.IsFieldOptional(field.Key)) Text("?"); Text(": "); Type(field.Value, depth); Text("; ");
            }
            foreach (var (key, type) in new[] { ("string", record.StringIndexType), ("number", record.NumberIndexType), ("symbol", record.SymbolIndexType) })
                if (type is not null) { Text("[key: "); Text(key); Text("]: "); Type(type, depth); Text("; "); }
            if (record.CallSignatures is not null)
                RecordSignatures(record.CallSignatures, depth, constructor: false);
            if (record.ConstructorSignatures is not null)
                RecordSignatures(record.ConstructorSignatures, depth, constructor: true);
            Text("}");
        }

        private void RecordSignatures(IEnumerable<TypeInfo> signatures, int depth, bool constructor)
        {
            int count = 0;
            foreach (var signature in signatures)
            {
                if (Full) break;
                if (count++ >= _limits.MaxCandidates || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                Signature(signature, depth, constructor, null, null, arrow: false);
                Text("; ");
            }
        }

        public EditorSignaturePresentation Signature(TypeInfo? signature, int depth, bool constructor,
            TypeInfo? constructed, IReadOnlyList<TypeInfo.TypeParameter>? sharedParameters, bool arrow,
            IReadOnlyList<string>? parameterNames = null)
        {
            int start = _text.Length, unavailable = _unavailable, truncated = _truncated;
            var shape = signature switch
            {
                TypeInfo.Function function => new SignatureShape(function.ParamTypes, function.ReturnType, function.MinArity, function.HasRestParam, function.ParamNames, sharedParameters, function.ThisType),
                TypeInfo.GenericFunction function => new SignatureShape(function.ParamTypes, function.ReturnType, function.MinArity, function.HasRestParam, function.ParamNames, sharedParameters ?? function.TypeParams, function.ThisType),
                TypeInfo.CallSignature function => new SignatureShape(function.ParamTypes, function.ReturnType, function.MinArity, function.HasRestParam, function.ParamNames, sharedParameters ?? function.TypeParams, null),
                TypeInfo.ConstructorSignature function => new SignatureShape(function.ParamTypes, function.ReturnType, function.MinArity, function.HasRestParam, function.ParamNames, sharedParameters ?? function.TypeParams, null),
                _ => null,
            };
            if (shape is null || depth >= _limits.MaxDepth || ++_nodes > _limits.MaxNodes)
            {
                if (shape is null) Unavailable(); else Ellipsis();
                return new(_text.ToString(start, _text.Length - start), [], new("unavailable", false, false), 0, false, false, _truncated != truncated);
            }
            // Instantiation may omit names while preserving the exact original parameter slots.
            // Presentation can borrow those names only when every slot still aligns; existing
            // names (including partially named signatures) remain authoritative.
            IReadOnlyList<string>? names = shape.Names is { Count: > 0 } ? shape.Names :
                parameterNames?.Count == shape.Parameters.Count ? parameterNames : null;
            var addedShadows = new List<string>();
            bool cappedTypeParameters = shape.TypeParameters?.Count > _limits.MaxCandidates;
            if (cappedTypeParameters) { _truncated++; _suppressSubstitutions++; }
            foreach (var parameter in (shape.TypeParameters ?? []).Take(_limits.MaxCandidates))
                if (_shadowed.Add(parameter.Name)) addedShadows.Add(parameter.Name);
            try
            {
            if (constructor) Text("new ");
            TypeParameters(shape.TypeParameters, depth + 1); Text("(");
            if (shape.ThisType is not null) { Text("this: "); Type(shape.ThisType, depth + 1); if (shape.Parameters.Count > 0) Text(", "); }
            var parameters = new List<EditorParameterPresentation>();
            for (int index = 0; index < shape.Parameters.Count && !Full; index++)
            {
                if (index >= _limits.MaxNodes || _nodes >= _limits.MaxNodes) { Ellipsis(); break; }
                if (index != 0) Text(", ");
                int parameterStart = _text.Length;
                bool rest = shape.Rest && index == shape.Parameters.Count - 1, optional = !rest && index >= shape.Minimum;
                string name = names is not null && index < names.Count && !string.IsNullOrWhiteSpace(names[index]) ? names[index] : $"arg{index}";
                if (rest) Text("..."); Name(name); if (optional) Text("?"); Text(": ");
                int typeStart = _text.Length, typeUnavailable = _unavailable, typeTruncated = _truncated;
                Type(shape.Parameters[index], depth + 1);
                parameters.Add(new(name, new(parameterStart - start, _text.Length - start), Presentation(typeStart, typeUnavailable, typeTruncated), optional, rest));
            }
            Text(arrow ? ") => " : "): ");
            int returnStart = _text.Length, returnUnavailable = _unavailable, returnTruncated = _truncated;
            Type(constructed ?? shape.ReturnType, depth + 1);
            var result = Presentation(returnStart, returnUnavailable, returnTruncated);
            return new(_text.ToString(start, _text.Length - start), parameters.ToImmutableArray(), result,
                shape.Minimum, shape.Rest, _unavailable == unavailable, _truncated != truncated);
            }
            finally
            {
                foreach (string name in addedShadows) _shadowed.Remove(name);
                if (cappedTypeParameters) _suppressSubstitutions--;
            }
        }

        private sealed record SignatureShape(IReadOnlyList<TypeInfo> Parameters, TypeInfo ReturnType,
            int Minimum, bool Rest, IReadOnlyList<string>? Names,
            IReadOnlyList<TypeInfo.TypeParameter>? TypeParameters, TypeInfo? ThisType);
    }
}
