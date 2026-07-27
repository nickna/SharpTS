using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Declaration;

/// <summary>Renders checked SharpTS types as valid TypeScript type syntax.</summary>
internal static class TypeInfoDeclarationRenderer
{
    public static string Render(TypeInfo type) => Render(type, 0);

    private static string Render(TypeInfo type, int parentPrecedence)
    {
        const int conditionalPrecedence = 1;
        const int unionPrecedence = 2;
        const int intersectionPrecedence = 3;
        const int prefixPrecedence = 4;
        const int primaryPrecedence = 5;

        (string Text, int Precedence) rendered = type switch
        {
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => ("number", primaryPrecedence),
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => ("boolean", primaryPrecedence),
            TypeInfo.String => ("string", primaryPrecedence),
            TypeInfo.Void => ("void", primaryPrecedence),
            TypeInfo.Any or TypeInfo.Inferred => ("any", primaryPrecedence),
            TypeInfo.Null => ("null", primaryPrecedence),
            TypeInfo.Undefined => ("undefined", primaryPrecedence),
            TypeInfo.Unknown => ("unknown", primaryPrecedence),
            TypeInfo.Never => ("never", primaryPrecedence),
            TypeInfo.Symbol => ("symbol", primaryPrecedence),
            TypeInfo.UniqueSymbol => ("unique symbol", primaryPrecedence),
            TypeInfo.BigInt => ("bigint", primaryPrecedence),
            TypeInfo.Object => ("object", primaryPrecedence),
            TypeInfo.StringLiteral s => (JsonSerializer.Serialize(s.Value), primaryPrecedence),
            TypeInfo.NumberLiteral n => (n.Value.ToString("R", CultureInfo.InvariantCulture), primaryPrecedence),
            TypeInfo.BooleanLiteral b => (b.Value ? "true" : "false", primaryPrecedence),
            TypeInfo.BigIntLiteral b => ($"{b.Value.ToString(CultureInfo.InvariantCulture)}n", primaryPrecedence),

            TypeInfo.Class c => (c.Name, primaryPrecedence),
            TypeInfo.MutableClass c => (c.Name, primaryPrecedence),
            TypeInfo.GenericClass c => (c.Name, primaryPrecedence),
            TypeInfo.Interface i => (i.Name, primaryPrecedence),
            TypeInfo.GenericInterface i => (i.Name, primaryPrecedence),
            TypeInfo.Enum e => (e.Name, primaryPrecedence),
            TypeInfo.Instance i => (Render(i.ResolvedClassType, primaryPrecedence), primaryPrecedence),
            TypeInfo.RecursiveTypeAlias a => (
                a.TypeArguments is { Count: > 0 }
                    ? $"{a.AliasName}<{string.Join(", ", a.TypeArguments.Select(type => Render(type)))}>"
                    : a.AliasName,
                primaryPrecedence),
            TypeInfo.TypeParameter p => (p.Name, primaryPrecedence),
            TypeInfo.InferredTypeParameter p => (
                p.Constraint is null ? $"infer {p.Name}" : $"infer {p.Name} extends {Render(p.Constraint)}",
                primaryPrecedence),

            TypeInfo.Array a => (
                a.IsReadonly
                    ? $"ReadonlyArray<{Render(a.ElementType)}>"
                    : $"Array<{Render(a.ElementType)}>",
                primaryPrecedence),
            TypeInfo.Tuple t => (RenderTuple(t), primaryPrecedence),
            TypeInfo.Map m => ($"Map<{Render(m.KeyType)}, {Render(m.ValueType)}>", primaryPrecedence),
            TypeInfo.Set s => ($"Set<{Render(s.ElementType)}>", primaryPrecedence),
            TypeInfo.Iterator i => ($"IterableIterator<{Render(i.ElementType)}>", primaryPrecedence),
            TypeInfo.Iterable i => ($"Iterable<{Render(i.ElementType)}>", primaryPrecedence),
            TypeInfo.AsyncIterable i => ($"AsyncIterable<{Render(i.ElementType)}>", primaryPrecedence),
            TypeInfo.AsyncIterator i => ($"AsyncIterableIterator<{Render(i.ElementType)}>", primaryPrecedence),
            TypeInfo.WeakMap m => ($"WeakMap<{Render(m.KeyType)}, {Render(m.ValueType)}>", primaryPrecedence),
            TypeInfo.WeakSet s => ($"WeakSet<{Render(s.ElementType)}>", primaryPrecedence),
            TypeInfo.WeakRef w => ($"WeakRef<{Render(w.TargetType)}>", primaryPrecedence),
            TypeInfo.FinalizationRegistry f => ($"FinalizationRegistry<{Render(f.TargetType)}>", primaryPrecedence),
            TypeInfo.Promise p => ($"Promise<{Render(p.ValueType)}>", primaryPrecedence),
            TypeInfo.Generator g => ($"Generator<{Render(g.YieldType)}>", primaryPrecedence),
            TypeInfo.AsyncGenerator g => ($"AsyncGenerator<{Render(g.YieldType)}>", primaryPrecedence),

            TypeInfo.Function f => (RenderFunction(f.ParamTypes, f.ReturnType, f.MinArity,
                f.HasRestParam, f.ParamNames, null, f.ThisType), conditionalPrecedence),
            TypeInfo.GenericFunction f => (RenderFunction(f.ParamTypes, f.ReturnType, f.MinArity,
                f.HasRestParam, f.ParamNames, f.TypeParams, f.ThisType), conditionalPrecedence),
            TypeInfo.OverloadedFunction f => (
                RenderFunction(f.Implementation.ParamTypes, f.Implementation.ReturnType,
                    f.Implementation.MinArity, f.Implementation.HasRestParam,
                    f.Implementation.ParamNames, null, f.Implementation.ThisType),
                conditionalPrecedence),
            TypeInfo.GenericOverloadedFunction f => (
                RenderFunction(f.Implementation.ParamTypes, f.Implementation.ReturnType,
                    f.Implementation.MinArity, f.Implementation.HasRestParam,
                    f.Implementation.ParamNames, f.TypeParams, f.Implementation.ThisType),
                conditionalPrecedence),

            TypeInfo.Record r => (RenderRecord(r), primaryPrecedence),
            TypeInfo.Union u => (
                string.Join(" | ", u.FlattenedTypes.Select(t => Render(t, unionPrecedence))),
                unionPrecedence),
            TypeInfo.Intersection i => (
                string.Join(" & ", i.FlattenedTypes.Select(t => Render(t, intersectionPrecedence))),
                intersectionPrecedence),
            TypeInfo.SpreadType s => ($"...{Render(s.Inner, prefixPrecedence)}", prefixPrecedence),
            TypeInfo.KeyOf k => ($"keyof {Render(k.SourceType, prefixPrecedence)}", prefixPrecedence),
            TypeInfo.TypeOf t => ($"typeof {t.Path}", prefixPrecedence),
            TypeInfo.IndexedAccess i => (
                $"{Render(i.ObjectType, primaryPrecedence)}[{Render(i.IndexType)}]",
                primaryPrecedence),
            TypeInfo.ConditionalType c => (
                $"{Render(c.CheckType, conditionalPrecedence)} extends {Render(c.ExtendsType, conditionalPrecedence)} ? " +
                $"{Render(c.TrueType, conditionalPrecedence)} : {Render(c.FalseType, conditionalPrecedence)}",
                conditionalPrecedence),
            TypeInfo.MappedType m => (RenderMapped(m), primaryPrecedence),
            TypeInfo.InstantiatedGeneric i => (
                $"{GenericName(i.GenericDefinition)}<{string.Join(", ", i.TypeArguments.Select(type => Render(type)))}>",
                primaryPrecedence),
            TypeInfo.TemplateLiteralType t => (RenderTemplateLiteral(t), primaryPrecedence),
            TypeInfo.IntrinsicStringType i => (
                $"{i.Operation}<{Render(i.Inner)}>",
                primaryPrecedence),
            TypeInfo.TypePredicate p => (
                p.IsAssertion
                    ? $"asserts {p.ParameterName} is {Render(p.PredicateType)}"
                    : $"{p.ParameterName} is {Render(p.PredicateType)}",
                conditionalPrecedence),
            TypeInfo.AssertsNonNull a => ($"asserts {a.ParameterName}", conditionalPrecedence),

            TypeInfo.Date => ("Date", primaryPrecedence),
            TypeInfo.RegExp => ("RegExp", primaryPrecedence),
            TypeInfo.Error e => (e.Name, primaryPrecedence),
            TypeInfo.Timeout => ("NodeJS.Timeout", primaryPrecedence),
            TypeInfo.Buffer => ("Buffer", primaryPrecedence),
            TypeInfo.EventEmitter => ("EventEmitter", primaryPrecedence),
            TypeInfo.AbortController => ("AbortController", primaryPrecedence),
            TypeInfo.AbortSignal => ("AbortSignal", primaryPrecedence),
            TypeInfo.Worker => ("Worker", primaryPrecedence),
            TypeInfo.MessagePort => ("MessagePort", primaryPrecedence),
            TypeInfo.MessageChannel => ("MessageChannel", primaryPrecedence),
            TypeInfo.SharedArrayBuffer => ("SharedArrayBuffer", primaryPrecedence),
            TypeInfo.ArrayBuffer => ("ArrayBuffer", primaryPrecedence),
            TypeInfo.DataView => ("DataView", primaryPrecedence),
            TypeInfo.TypedArray t => ($"{t.ElementType}Array", primaryPrecedence),
            TypeInfo.AtomicsNamespace => ("typeof Atomics", prefixPrecedence),

            TypeInfo.ExternalDotNetType d => throw new DeclarationEmitException(
                $"Public declaration type '{d.TypeScriptName}' maps to CLR type '{d.ClrTypeName}' and is not portable to TypeScript consumers."),
            TypeInfo.Module m => throw new DeclarationEmitException(
                $"Module namespace type from '{m.ModulePath}' cannot currently be named in a portable declaration."),
            TypeInfo.Namespace n => ($"typeof {n.Name}", prefixPrecedence),
            TypeInfo.CallSignature s => (
                RenderFunction(s.ParamTypes, s.ReturnType, s.MinArity, s.HasRestParam,
                    s.ParamNames, s.TypeParams, null).Replace(" => ", ": ", StringComparison.Ordinal),
                conditionalPrecedence),
            TypeInfo.ConstructorSignature s => (
                $"new {RenderFunction(s.ParamTypes, s.ReturnType, s.MinArity, s.HasRestParam, s.ParamNames, s.TypeParams, null)}",
                conditionalPrecedence),
            _ => throw new DeclarationEmitException(
                $"Type '{type.GetType().Name}' cannot currently be represented in a TypeScript declaration."),
        };

        return rendered.Precedence < parentPrecedence ? $"({rendered.Text})" : rendered.Text;
    }

    private static string RenderFunction(
        IReadOnlyList<TypeInfo> parameterTypes,
        TypeInfo returnType,
        int minimumArity,
        bool hasRest,
        IReadOnlyList<string>? names,
        IReadOnlyList<TypeInfo.TypeParameter>? typeParameters,
        TypeInfo? thisType)
    {
        var parameters = new List<string>();
        if (thisType is not null)
            parameters.Add($"this: {Render(thisType)}");

        for (int index = 0; index < parameterTypes.Count; index++)
        {
            bool rest = hasRest && index == parameterTypes.Count - 1;
            bool optional = !rest && index >= minimumArity;
            string name = names is not null && index < names.Count && !string.IsNullOrWhiteSpace(names[index])
                ? names[index]
                : $"arg{index}";
            parameters.Add($"{(rest ? "..." : "")}{name}{(optional ? "?" : "")}: {Render(parameterTypes[index])}");
        }

        string typeParametersText = typeParameters is { Count: > 0 }
            ? $"<{string.Join(", ", typeParameters.Select(RenderTypeParameter))}>"
            : "";
        return $"{typeParametersText}({string.Join(", ", parameters)}) => {Render(returnType)}";
    }

    private static string RenderTuple(TypeInfo.Tuple tuple)
    {
        var elements = tuple.Elements.Select(element =>
        {
            string name = element.Name is null ? "" : $"{element.Name}{(element.IsOptional ? "?" : "")}: ";
            string marker = element.IsSpread ? "..." : "";
            string optional = element.Name is null && element.IsOptional ? "?" : "";
            return $"{marker}{name}{Render(element.Type)}{optional}";
        }).ToList();
        if (tuple.RestElementType is not null)
            elements.Add($"...{Render(tuple.RestElementType)}[]");
        return $"{(tuple.IsReadonly ? "readonly " : "")}[{string.Join(", ", elements)}]";
    }

    private static string RenderRecord(TypeInfo.Record record)
    {
        var members = new List<string>();
        if (record.CallSignatures is not null)
            members.AddRange(record.CallSignatures.Select(RenderCallSignature));
        if (record.ConstructorSignatures is not null)
            members.AddRange(record.ConstructorSignatures.Select(RenderConstructorSignature));

        foreach (var field in record.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string readOnly = record.IsReadonly || record.IsGetterOnly(field.Key) ? "readonly " : "";
            string optional = record.IsFieldOptional(field.Key) ? "?" : "";
            members.Add($"{readOnly}{QuoteProperty(field.Key)}{optional}: {Render(field.Value)};");
        }
        if (record.StringIndexType is not null)
            members.Add($"[key: string]: {Render(record.StringIndexType)};");
        if (record.NumberIndexType is not null)
            members.Add($"[key: number]: {Render(record.NumberIndexType)};");
        if (record.SymbolIndexType is not null)
            members.Add($"[key: symbol]: {Render(record.SymbolIndexType)};");
        return $"{{ {string.Join(" ", members)} }}";
    }

    private static string RenderCallSignature(TypeInfo.CallSignature signature) =>
        $"{RenderTypeParameters(signature.TypeParams)}{RenderParameters(signature.ParamTypes, signature.MinArity, signature.HasRestParam, signature.ParamNames)}: {Render(signature.ReturnType)};";

    private static string RenderConstructorSignature(TypeInfo.ConstructorSignature signature) =>
        $"new {RenderTypeParameters(signature.TypeParams)}{RenderParameters(signature.ParamTypes, signature.MinArity, signature.HasRestParam, signature.ParamNames)}: {Render(signature.ReturnType)};";

    private static string RenderParameters(
        IReadOnlyList<TypeInfo> types,
        int minimumArity,
        bool hasRest,
        IReadOnlyList<string>? names)
    {
        var parameters = types.Select((type, index) =>
        {
            bool rest = hasRest && index == types.Count - 1;
            bool optional = !rest && index >= minimumArity;
            string name = names is not null && index < names.Count ? names[index] : $"arg{index}";
            return $"{(rest ? "..." : "")}{name}{(optional ? "?" : "")}: {Render(type)}";
        });
        return $"({string.Join(", ", parameters)})";
    }

    private static string RenderMapped(TypeInfo.MappedType mapped)
    {
        string readOnly = mapped.Modifiers.HasFlag(MappedTypeModifiers.AddReadonly) ? "+readonly "
            : mapped.Modifiers.HasFlag(MappedTypeModifiers.RemoveReadonly) ? "-readonly "
            : "";
        string optional = mapped.Modifiers.HasFlag(MappedTypeModifiers.AddOptional) ? "+?"
            : mapped.Modifiers.HasFlag(MappedTypeModifiers.RemoveOptional) ? "-?"
            : "";
        string remap = mapped.AsClause is null ? "" : $" as {Render(mapped.AsClause)}";
        return $"{{ {readOnly}[{mapped.ParameterName} in {Render(mapped.Constraint)}{remap}]{optional}: {Render(mapped.ValueType)} }}";
    }

    private static string RenderTemplateLiteral(TypeInfo.TemplateLiteralType template)
    {
        var builder = new StringBuilder("`");
        for (int index = 0; index < template.InterpolatedTypes.Count; index++)
        {
            builder.Append(template.Strings[index].Replace("`", "\\`", StringComparison.Ordinal));
            builder.Append("${").Append(Render(template.InterpolatedTypes[index])).Append('}');
        }
        builder.Append(template.Strings[^1].Replace("`", "\\`", StringComparison.Ordinal)).Append('`');
        return builder.ToString();
    }

    private static string RenderTypeParameters(IReadOnlyList<TypeInfo.TypeParameter>? typeParameters) =>
        typeParameters is { Count: > 0 }
            ? $"<{string.Join(", ", typeParameters.Select(RenderTypeParameter))}>"
            : "";

    private static string RenderTypeParameter(TypeInfo.TypeParameter parameter)
    {
        string variance = parameter.Variance switch
        {
            TypeParameterVariance.In => "in ",
            TypeParameterVariance.Out => "out ",
            TypeParameterVariance.InOut => "in out ",
            _ => "",
        };
        string text = $"{variance}{(parameter.IsConst ? "const " : "")}{parameter.Name}";
        if (parameter.Constraint is not null)
            text += $" extends {Render(parameter.Constraint)}";
        if (parameter.Default is not null)
            text += $" = {Render(parameter.Default)}";
        return text;
    }

    internal static string RenderTypeParameterDeclaration(TypeInfo.TypeParameter parameter) =>
        RenderTypeParameter(parameter);

    private static string GenericName(TypeInfo type) => type switch
    {
        TypeInfo.GenericClass c => c.Name,
        TypeInfo.GenericInterface i => i.Name,
        TypeInfo.RecursiveTypeAlias a => a.AliasName,
        _ => Render(type),
    };

    private static string QuoteProperty(string name) =>
        IsIdentifier(name) ? name : JsonSerializer.Serialize(name);

    private static bool IsIdentifier(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] is '_' or '$') &&
        name.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
}

public sealed class DeclarationEmitException(string message) : Exception(message);
