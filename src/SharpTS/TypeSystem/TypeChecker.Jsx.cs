using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// JSX semantics. JSX elements reach the checker as ordinary <see cref="Expr.Call"/> nodes
/// lowered by the parser, marked with <see cref="Expr.Call.JsxOrigin"/>. Those calls bypass
/// normal call checking entirely (no factory-signature TS2554/TS2345 — users get tsc-shaped
/// JSX diagnostics, never both) and run this pipeline instead: intrinsic tags check against
/// <c>JSX.IntrinsicElements</c>, component tags against the component's props parameter, and
/// every JSX expression types as <c>JSX.Element</c>.
/// </summary>
public partial class TypeChecker
{
    private TypeInfo CheckJsxExpression(Expr.Call call, JsxCallInfo jsx)
    {
        var jsxNamespace = _environment.GetNamespace("JSX");

        // A missing factory (classic mode with no `React` in scope → TS2304 from
        // LookupVariable) must not mask attribute diagnostics: record and continue.
        try
        {
            CheckExpr(call.Callee);
        }
        catch (TypeCheckException ex)
        {
            ReportJsx(ex);
        }

        // Check every argument exactly once, structurally. The tag argument and the props
        // object get dedicated handling; the rest (classic-mode children, the automatic-mode
        // key, classic null props) are checked plainly so the type map stays fully populated.
        // Automatic-mode children live INSIDE the props object literal and are covered by the
        // props check.
        Expr? tagArgument = call.Arguments.Count > 0 ? call.Arguments[0] : null;
        TypeInfo componentType = TypeInfo.Any.Shared;
        if (tagArgument is not null)
        {
            if (jsx.Kind == JsxElementKind.Component)
            {
                try
                {
                    componentType = CheckExpr(tagArgument);
                }
                catch (TypeCheckException ex)
                {
                    // Unresolved component identifier (TS2304) — report, then skip
                    // props-vs-component checking (no type to check against).
                    ReportJsx(ex);
                    componentType = TypeInfo.Any.Shared;
                }
            }
            else
            {
                CheckExpr(tagArgument);
            }
        }

        TypeInfo propsType = jsx.PropsExpr is not null
            ? CheckExpr(jsx.PropsExpr)
            : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        foreach (var argument in call.Arguments)
        {
            if (ReferenceEquals(argument, tagArgument) || ReferenceEquals(argument, jsx.PropsExpr))
                continue;
            CheckExpr(argument);
        }

        propsType = ApplyJsxChildrenContract(jsx, jsxNamespace, propsType);

        switch (jsx.Kind)
        {
            case JsxElementKind.Intrinsic:
                CheckJsxIntrinsic(jsx, jsxNamespace, propsType);
                break;
            case JsxElementKind.Component:
                CheckJsxComponent(jsx, jsxNamespace, componentType, propsType);
                break;
            case JsxElementKind.Fragment:
                break;
        }

        return ResolveJsxElementType(jsxNamespace, jsx.Line, reportMissing: true);
    }

    private void CheckJsxIntrinsic(JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo propsType)
    {
        TypeInfo? intrinsics = jsxNamespace?.Types.GetValueOrDefault("IntrinsicElements");
        if (intrinsics is null)
        {
            // tsc: without a JSX.IntrinsicElements interface every intrinsic is implicit
            // any — an error only under noImplicitAny (TS7026), silent otherwise.
            if (_noImplicitAny)
                ReportJsx(new TypeCheckException(
                    $"JSX element implicitly has type 'any' because no interface 'JSX.IntrinsicElements' exists.",
                    jsx.Line, tsCode: "TS7026"));
            return;
        }

        TypeInfo? tagType = LookupJsxObjectMember(intrinsics, jsx.TagName!);
        if (tagType is null)
        {
            ReportJsx(new TypeCheckException(
                $"Property '{jsx.TagName}' does not exist on type 'JSX.IntrinsicElements'.",
                jsx.Line, tsCode: "TS2339"));
            return;
        }

        CheckJsxAttributes(tagType, propsType, jsx, jsxNamespace);
    }

    private void CheckJsxComponent(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo componentType, TypeInfo propsType)
    {
        switch (componentType)
        {
            case TypeInfo.Any or TypeInfo.Unknown:
                return;

            case TypeInfo.Function fn:
                CheckJsxAttributes(
                    fn.ParamTypes.Count > 0
                        ? fn.ParamTypes[0]
                        : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty),
                    propsType, jsx, jsxNamespace);
                CheckJsxComponentReturnType(jsx, jsxNamespace, fn.ReturnType);
                return;

            case TypeInfo.GenericFunction generic:
            {
                List<TypeInfo> typeArguments = InferTypeArguments(generic, [propsType]);
                var instantiated = (TypeInfo.Function)InstantiateGenericFunction(generic, typeArguments);
                CheckJsxAttributes(
                    instantiated.ParamTypes.Count > 0
                        ? instantiated.ParamTypes[0]
                        : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty),
                    propsType, jsx, jsxNamespace);
                CheckJsxComponentReturnType(jsx, jsxNamespace, instantiated.ReturnType);
                return;
            }

            case TypeInfo.OverloadedFunction overloaded:
            {
                foreach (var signature in overloaded.Signatures)
                {
                    TypeInfo expected = signature.ParamTypes.Count > 0
                        ? signature.ParamTypes[0]
                        : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
                    if (IsCompatible(expected, propsType))
                    {
                        CheckJsxAttributes(expected, propsType, jsx, jsxNamespace);
                        CheckJsxComponentReturnType(jsx, jsxNamespace, signature.ReturnType);
                        return;
                    }
                }
                ReportJsx(new TypeCheckException(
                    "No overload matches this call.", jsx.Line, tsCode: "TS2769"));
                return;
            }

            case TypeInfo.GenericOverloadedFunction genericOverloaded:
            {
                foreach (TypeInfo.Function signature in genericOverloaded.Signatures)
                {
                    var generic = new TypeInfo.GenericFunction(
                        genericOverloaded.TypeParams, signature.ParamTypes, signature.ReturnType,
                        signature.RequiredParams, signature.HasRestParam, signature.ThisType, signature.ParamNames);
                    TypeInfo.Function instantiated = (TypeInfo.Function)InstantiateGenericFunction(
                        generic, InferTypeArguments(generic, [propsType]));
                    TypeInfo expected = JsxFirstParameter(instantiated.ParamTypes);
                    if (!AreJsxAttributesCompatible(expected, propsType, jsxNamespace)) continue;
                    CheckJsxAttributes(expected, propsType, jsx, jsxNamespace);
                    CheckJsxComponentReturnType(jsx, jsxNamespace, instantiated.ReturnType);
                    return;
                }
                ReportJsx(new TypeCheckException("No overload matches this call.", jsx.Line, tsCode: "TS2769"));
                return;
            }

            case TypeInfo.OverloadSet overloadSet:
                CheckJsxMixedSignatures(jsx, jsxNamespace, overloadSet.Signatures, propsType);
                return;

            case TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass
                or TypeInfo.InstantiatedGeneric:
                CheckJsxClassComponent(jsx, jsxNamespace, componentType, propsType);
                return;

            // Callable object types ({ (props): Element }) use their call signatures just like
            // overloaded function declarations.
            case TypeInfo.Record { CallSignatures.Count: > 0 } record:
                CheckJsxCallSignatures(jsx, jsxNamespace, record.CallSignatures!, propsType);
                return;
            case TypeInfo.Interface { CallSignatures.Count: > 0 } iface:
                CheckJsxCallSignatures(jsx, jsxNamespace, iface.CallSignatures!, propsType);
                return;

            case TypeInfo.Union union:
                CheckJsxUnionComponent(jsx, jsxNamespace, union, propsType);
                return;
        }

        ReportJsx(new TypeCheckException(
            $"JSX element type '{jsx.TagName}' does not have any construct or call signatures.",
            jsx.Line, tsCode: "TS2604"));
    }

    private static bool IsJsxRenderableTagType(TypeInfo type) => type switch
    {
        TypeInfo.Any or TypeInfo.Unknown => true,
        TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction => true,
        TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric => true,
        TypeInfo.Record { CallSignatures.Count: > 0 } => true,
        TypeInfo.Interface { CallSignatures.Count: > 0 } => true,
        _ => false,
    };

    private void CheckJsxComponentReturnType(JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo returnType)
    {
        TypeInfo elementType = ResolveJsxElementType(jsxNamespace, jsx.Line, reportMissing: false);
        if (elementType is TypeInfo.Any || returnType is TypeInfo.Any)
            return;
        if (returnType is TypeInfo.Promise)
        {
            ReportJsx(new TypeCheckException(
                $"'{jsx.TagName}' cannot be used as a JSX component. Its return type '{returnType}' is not a valid JSX element; async components are not supported.",
                jsx.Line, tsCode: "TS2786"));
            return;
        }
        if (!IsJsxComponentResult(elementType, returnType))
        {
            ReportJsx(new TypeCheckException(
                $"'{jsx.TagName}' cannot be used as a JSX component. " +
                $"Its return type '{returnType}' is not a valid JSX element.",
                jsx.Line, tsCode: "TS2786"));
        }
    }

    private void CheckJsxCallSignatures(
        JsxCallInfo jsx,
        TypeInfo.Namespace? jsxNamespace,
        IReadOnlyList<TypeInfo.CallSignature> signatures,
        TypeInfo propsType)
    {
        foreach (TypeInfo.CallSignature signature in signatures)
        {
            TypeInfo expected;
            TypeInfo returnType;
            if (signature.IsGeneric)
            {
                var generic = new TypeInfo.GenericFunction(
                    signature.TypeParams!, signature.ParamTypes, signature.ReturnType,
                    signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames);
                var instantiated = (TypeInfo.Function)InstantiateGenericFunction(
                    generic, InferTypeArguments(generic, [propsType]));
                expected = JsxFirstParameter(instantiated.ParamTypes);
                returnType = instantiated.ReturnType;
            }
            else
            {
                expected = JsxFirstParameter(signature.ParamTypes);
                returnType = signature.ReturnType;
            }
            if (!AreJsxAttributesCompatible(expected, propsType, jsxNamespace))
                continue;
            CheckJsxAttributes(expected, propsType, jsx, jsxNamespace);
            CheckJsxComponentReturnType(jsx, jsxNamespace, returnType);
            return;
        }
        ReportJsx(new TypeCheckException(
            "No overload matches this call.", jsx.Line, tsCode: "TS2769"));
    }

    private static TypeInfo JsxFirstParameter(IReadOnlyList<TypeInfo> parameters) =>
        parameters.Count > 0 ? parameters[0] : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

    private TypeInfo ApplyJsxChildrenContract(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo propsType)
    {
        if (jsx.ChildExprs.Count == 0 || propsType is not TypeInfo.Record record)
            return propsType;

        string childrenName = "children";
        if (jsxNamespace?.Types.GetValueOrDefault("ElementChildrenAttribute") is { } marker &&
            TryGetJsxMemberView(marker, out var members, out _, out _) && members.Count > 0)
        {
            childrenName = members.Keys.First();
        }

        var fields = record.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        TypeInfo childType;
        if (jsx.Mode is JsxMode.ReactJsx or JsxMode.ReactJsxDev &&
            fields.TryGetValue("children", out TypeInfo? loweredChildren))
        {
            childType = loweredChildren;
            if (childrenName != "children") fields.Remove("children");
        }
        else
        {
            var childTypes = jsx.ChildExprs.Select(child => _typeMap.Get(child) ?? TypeInfo.Any.Shared).ToList();
            childType = childTypes.Count == 1
                ? childTypes[0]
                : new TypeInfo.Array(CollapseOrCreateUnion(childTypes));
        }
        fields[childrenName] = childType;
        return new TypeInfo.Record(
            fields.ToFrozenDictionary(StringComparer.Ordinal),
            StringIndexType: record.StringIndexType,
            NumberIndexType: record.NumberIndexType,
            SymbolIndexType: record.SymbolIndexType,
            OptionalFields: record.OptionalFields,
            IsReadonly: record.IsReadonly,
            GetterOnlyFields: record.GetterOnlyFields,
            CallSignatures: record.CallSignatures,
            ConstructorSignatures: record.ConstructorSignatures,
            MethodMembers: record.MethodMembers);
    }

    private void CheckJsxClassComponent(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo classType, TypeInfo propsType)
    {
        TypeInfo instanceType = classType is TypeInfo.MutableClass mutable ? mutable.Freeze() : classType;
        TypeInfo? elementClass = jsxNamespace?.Types.GetValueOrDefault("ElementClass");
        if (elementClass is not null && !IsJsxElementClassCompatible(elementClass, instanceType))
        {
            ReportJsx(new TypeCheckException(
                $"'{jsx.TagName}' cannot be used as a JSX component. Its instance type '{instanceType}' is not a valid JSX element class.",
                jsx.Line, tsCode: "TS2786"));
            return;
        }

        string? propertyName = null;
        if (jsxNamespace?.Types.GetValueOrDefault("ElementAttributesProperty") is { } marker &&
            TryGetJsxMemberView(marker, out var markerMembers, out _, out _) && markerMembers.Count > 0)
            propertyName = markerMembers.Keys.First();

        TypeInfo expected = propertyName is null
            ? new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty)
            : LookupJsxClassInstanceMember(instanceType, propertyName) ?? TypeInfo.Any.Shared;

        if (classType is TypeInfo.GenericClass genericClass && propertyName is not null)
        {
            TypeInfo? genericExpected = LookupJsxClassCoreMember(genericClass.Core, propertyName);
            if (genericExpected is not null)
            {
                var inference = new TypeInfo.GenericFunction(genericClass.TypeParams, [genericExpected], TypeInfo.Any.Shared);
                expected = Substitute(genericExpected, genericClass.TypeParams
                    .Select((parameter, index) => (parameter.Name, Type: InferTypeArguments(inference, [propsType])[index]))
                    .ToDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal));
            }
        }
        CheckJsxAttributes(expected, propsType, jsx, jsxNamespace);
    }

    private bool IsJsxElementClassCompatible(TypeInfo contract, TypeInfo instanceType)
    {
        if (!TryGetJsxMemberView(contract, out var members, out var optional, out _))
            return IsCompatible(contract, instanceType);
        foreach ((string name, TypeInfo expected) in members)
        {
            TypeInfo? actual = LookupJsxClassInstanceMember(instanceType, name);
            if (actual is null)
            {
                if (optional.Contains(name)) continue;
                return false;
            }
            if (!IsCompatible(expected, actual)) return false;
        }
        return true;
    }

    private TypeInfo? LookupJsxClassInstanceMember(TypeInfo type, string name) => type switch
    {
        TypeInfo.Class cls => LookupJsxClassCoreMember(cls.Core, name),
        TypeInfo.GenericClass generic => LookupJsxClassCoreMember(generic.Core, name),
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
            LookupJsxClassCoreMember(generic.Core, name) is { } member
                ? Substitute(member, generic.TypeParams.Select((parameter, index) =>
                    (parameter.Name, Type: instantiated.TypeArguments[index]))
                    .ToDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal))
                : null,
        _ => null,
    };

    private static TypeInfo? LookupJsxClassCoreMember(ClassMetadataCore core, string name)
    {
        if (core.FieldTypes.TryGetValue(name, out TypeInfo? field)) return field;
        if (core.Getters.TryGetValue(name, out TypeInfo? getter)) return getter;
        if (core.Methods.TryGetValue(name, out TypeInfo? method)) return method;
        return core.Superclass switch
        {
            TypeInfo.Class cls => LookupJsxClassCoreMember(cls.Core, name),
            TypeInfo.GenericClass generic => LookupJsxClassCoreMember(generic.Core, name),
            _ => null,
        };
    }

    private void CheckJsxUnionComponent(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo.Union union, TypeInfo propsType)
    {
        foreach (TypeInfo constituent in union.FlattenedTypes)
        {
            if (!IsJsxRenderableTagType(constituent))
            {
                ReportJsx(new TypeCheckException(
                    $"JSX element type '{jsx.TagName}' has a union constituent '{constituent}' with no construct or call signatures.",
                    jsx.Line, tsCode: "TS2604"));
                return;
            }
        }

        var candidates = new List<(TypeInfo Props, TypeInfo? Return)>();
        foreach (TypeInfo constituent in union.FlattenedTypes)
            CollectJsxCandidates(constituent, propsType, candidates);
        (TypeInfo Props, TypeInfo? Return)? match = candidates.FirstOrDefault(candidate =>
            AreJsxAttributesCompatible(candidate.Props, propsType, jsxNamespace));
        if (match is null)
        {
            ReportJsx(new TypeCheckException("No union component signature accepts these JSX attributes.", jsx.Line, tsCode: "TS2769"));
            return;
        }
        CheckJsxAttributes(match.Value.Props, propsType, jsx, jsxNamespace);
        foreach ((_, TypeInfo? returnType) in candidates)
            if (returnType is not null) CheckJsxComponentReturnType(jsx, jsxNamespace, returnType);
    }

    private void CollectJsxCandidates(
        TypeInfo type, TypeInfo propsType, List<(TypeInfo Props, TypeInfo? Return)> candidates)
    {
        switch (type)
        {
            case TypeInfo.Function function:
                candidates.Add((JsxFirstParameter(function.ParamTypes), function.ReturnType));
                break;
            case TypeInfo.GenericFunction generic:
                var instantiated = (TypeInfo.Function)InstantiateGenericFunction(generic, InferTypeArguments(generic, [propsType]));
                candidates.Add((JsxFirstParameter(instantiated.ParamTypes), instantiated.ReturnType));
                break;
            case TypeInfo.OverloadedFunction overloaded:
                candidates.AddRange(overloaded.Signatures.Select(signature =>
                    (JsxFirstParameter(signature.ParamTypes), (TypeInfo?)signature.ReturnType)));
                break;
            case TypeInfo.Record { CallSignatures: { } signatures }:
                CollectCallSignatureCandidates(signatures, propsType, candidates);
                break;
            case TypeInfo.Interface { CallSignatures: { } signatures }:
                CollectCallSignatureCandidates(signatures, propsType, candidates);
                break;
            case TypeInfo.Any or TypeInfo.Unknown:
                candidates.Add((TypeInfo.Any.Shared, null));
                break;
            default:
                candidates.Add((TypeInfo.Any.Shared, null));
                break;
        }
    }

    private void CollectCallSignatureCandidates(
        IEnumerable<TypeInfo.CallSignature> signatures, TypeInfo propsType,
        List<(TypeInfo Props, TypeInfo? Return)> candidates)
    {
        foreach (TypeInfo.CallSignature signature in signatures)
        {
            if (!signature.IsGeneric)
            {
                candidates.Add((JsxFirstParameter(signature.ParamTypes), signature.ReturnType));
                continue;
            }
            var generic = new TypeInfo.GenericFunction(signature.TypeParams!, signature.ParamTypes, signature.ReturnType,
                signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames);
            var instantiated = (TypeInfo.Function)InstantiateGenericFunction(generic, InferTypeArguments(generic, [propsType]));
            candidates.Add((JsxFirstParameter(instantiated.ParamTypes), instantiated.ReturnType));
        }
    }

    private void CheckJsxMixedSignatures(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, IEnumerable<TypeInfo> signatures, TypeInfo propsType)
    {
        var candidates = new List<(TypeInfo Props, TypeInfo? Return)>();
        foreach (TypeInfo signature in signatures) CollectJsxCandidates(signature, propsType, candidates);
        var match = candidates.FirstOrDefault(candidate => AreJsxAttributesCompatible(candidate.Props, propsType, jsxNamespace));
        if (match == default)
        {
            ReportJsx(new TypeCheckException("No overload matches this call.", jsx.Line, tsCode: "TS2769"));
            return;
        }
        CheckJsxAttributes(match.Props, propsType, jsx, jsxNamespace);
        if (match.Return is not null) CheckJsxComponentReturnType(jsx, jsxNamespace, match.Return);
    }

    private bool AreJsxAttributesCompatible(
        TypeInfo expected, TypeInfo actual, TypeInfo.Namespace? jsxNamespace)
    {
        if (expected is TypeInfo.Any or TypeInfo.Unknown || actual is not TypeInfo.Record actualRecord)
            return true;
        if (!TryGetJsxMemberView(expected, out var members, out _, out TypeInfo? stringIndex))
            return false;
        TypeInfo? intrinsic = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");
        foreach (string required in RequiredMemberNames(expected))
            if (required != "key" && !actualRecord.Fields.ContainsKey(required)) return false;
        foreach ((string name, TypeInfo value) in actualRecord.Fields)
        {
            if (name == "key")
            {
                TypeInfo? key = intrinsic is null ? null : LookupJsxObjectMember(intrinsic, "key");
                if (key is null || !IsCompatible(key, value)) return false;
                continue;
            }
            if (!IsCheckedJsxAttribute(name)) continue;
            TypeInfo? member = members.GetValueOrDefault(name) ?? stringIndex ??
                (intrinsic is null ? null : LookupJsxObjectMember(intrinsic, name));
            if (member is null || !IsCompatible(member, value)) return false;
        }
        return true;
    }

    private bool IsJsxComponentResult(TypeInfo elementType, TypeInfo returnType)
    {
        if (IsCompatible(elementType, returnType))
            return true;
        return returnType switch
        {
            TypeInfo.Null or TypeInfo.Undefined or TypeInfo.String or TypeInfo.StringLiteral or
                TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral => true,
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN } => true,
            TypeInfo.Array array => IsJsxComponentResult(elementType, array.ElementType),
            TypeInfo.Tuple tuple => tuple.Elements.All(item => IsJsxComponentResult(elementType, item.Type)),
            TypeInfo.Union union => union.FlattenedTypes.All(item => IsJsxComponentResult(elementType, item)),
            _ => false,
        };
    }

    /// <summary>
    /// Relates the written attributes to the expected props type with tsc's JSX-flavored
    /// diagnostics. Bails out silently when either side is not object-shaped (a spread of
    /// <c>any</c> degrades the props literal — tsc's behavior).
    /// </summary>
    private void CheckJsxAttributes(
        TypeInfo expected, TypeInfo actual, JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace)
    {
        if (expected is TypeInfo.Any or TypeInfo.Unknown)
            return;
        if (actual is not TypeInfo.Record actualRecord)
            return;
        if (!TryGetJsxMemberView(expected, out var expectedMembers, out var expectedOptional,
                out TypeInfo? stringIndexType))
            return;

        // Weak-type failure first (tsc's dedicated code) — skip per-attribute noise. Judged
        // on the attributes that actually participate in checking: key/ref/children and
        // hyphenated/namespaced names are exempt and must not trigger it.
        var consideredFields = actualRecord.Fields
            .Where(field => IsCheckedJsxAttribute(field.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
        if (consideredFields.Count > 0 &&
            FailsWeakTypeCheck(expected, new TypeInfo.Record(consideredFields)))
        {
            ReportJsx(new TypeCheckException(
                $"Type '{actual}' has no properties in common with type '{expected}'.",
                jsx.Line, tsCode: "TS2559"));
            return;
        }

        // Missing required props. `key` is supplied through IntrinsicAttributes rather than the
        // component props object; children and ref retain their declared prop types.
        var missing = new List<string>();
        foreach (var required in RequiredMemberNames(expected))
        {
            if (required is "key")
                continue;
            if (!actualRecord.Fields.ContainsKey(required))
                missing.Add(required);
        }
        if (missing.Count == 1)
        {
            ReportJsx(new TypeCheckException(
                $"Property '{missing[0]}' is missing in type '{actual}' but required in type '{expected}'.",
                jsx.Line, tsCode: "TS2741"));
        }
        else if (missing.Count > 1)
        {
            ReportJsx(new TypeCheckException(
                $"Type '{actual}' is missing the following properties from type '{expected}': " +
                string.Join(", ", missing),
                jsx.Line, tsCode: "TS2739"));
        }

        TypeInfo? intrinsicAttributes = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");

        if (actualRecord.Fields.TryGetValue("key", out TypeInfo? keyType))
        {
            TypeInfo? expectedKey = intrinsicAttributes is null
                ? null
                : LookupJsxObjectMember(intrinsicAttributes, "key");
            if (expectedKey is null || !IsCompatible(expectedKey, keyType))
            {
                ReportJsx(new TypeCheckException(
                    expectedKey is null
                        ? $"Property 'key' does not exist on type 'JSX.IntrinsicAttributes'."
                        : $"Type '{keyType}' is not assignable to type '{expectedKey}'.",
                    JsxAttributeLine(jsx, "key"), tsCode: "TS2322"));
            }
        }

        foreach (var (name, valueType) in actualRecord.Fields)
        {
            if (!IsCheckedJsxAttribute(name))
                continue;

            TypeInfo? memberType = expectedMembers.TryGetValue(name, out var direct)
                ? direct
                : stringIndexType;
            if (memberType is null && intrinsicAttributes is not null)
                memberType = LookupJsxObjectMember(intrinsicAttributes, name);

            if (memberType is null)
            {
                ReportJsx(new TypeCheckException(
                    $"Type '{actual}' is not assignable to type '{expected}'. " +
                    $"Property '{name}' does not exist on type '{expected}'.",
                    JsxAttributeLine(jsx, name), tsCode: "TS2322"));
                continue;
            }

            if (!IsCompatible(memberType, valueType))
            {
                ReportJsx(new TypeCheckException(
                    $"Type '{valueType}' is not assignable to type '{memberType}'.",
                    JsxAttributeLine(jsx, name), tsCode: "TS2322"));
            }
        }

        // Silence the "declared but unused" analysis shape: optional members are only
        // consulted through RequiredMemberNames today; the set is kept for parity with
        // future exact-optional handling.
        _ = expectedOptional;
    }

    /// <summary>
    /// Whether a written attribute participates in props checking: key is checked separately
    /// through IntrinsicAttributes, and tsc exempts hyphenated/namespaced names — this keeps data-*/aria-* legal
    /// against props types with no index signature.
    /// </summary>
    private static bool IsCheckedJsxAttribute(string name) =>
        name != "key" && !name.Contains('-') && !name.Contains(':');

    /// <summary>
    /// Flattens an object-like props type into (members, optionals, string index). False for
    /// shapes attribute checking cannot see into (unions, generics) — callers bail silently.
    /// </summary>
    private bool TryGetJsxMemberView(
        TypeInfo type,
        out Dictionary<string, TypeInfo> members,
        out HashSet<string> optionalMembers,
        out TypeInfo? stringIndexType)
    {
        members = [];
        optionalMembers = [];
        stringIndexType = null;

        switch (type)
        {
            case TypeInfo.Interface iface:
                foreach (var member in iface.GetAllMembers())
                    members.TryAdd(member.Key, member.Value);
                foreach (var optional in iface.GetAllOptionalMembers())
                    optionalMembers.Add(optional);
                stringIndexType = iface.StringIndexType;
                return true;
            case TypeInfo.Record record:
                foreach (var (key, value) in record.Fields)
                    members.TryAdd(key, value);
                if (record.OptionalFields is not null)
                    foreach (var optional in record.OptionalFields)
                        optionalMembers.Add(optional);
                stringIndexType = record.StringIndexType;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Member lookup on an object-like type by name, falling back to its string index.</summary>
    private TypeInfo? LookupJsxObjectMember(TypeInfo container, string name)
    {
        switch (container)
        {
            case TypeInfo.Interface iface:
                foreach (var member in iface.GetAllMembers())
                    if (string.Equals(member.Key, name, StringComparison.Ordinal))
                        return member.Value;
                return iface.StringIndexType;
            case TypeInfo.Record record:
                return record.Fields.TryGetValue(name, out var field) ? field : record.StringIndexType;
            default:
                return null;
        }
    }

    /// <summary>Line of the written attribute (via the props object literal), else the element line.</summary>
    private static int JsxAttributeLine(JsxCallInfo jsx, string attributeName)
    {
        if (jsx.PropsExpr is Expr.ObjectLiteral literal)
        {
            foreach (var property in literal.Properties)
            {
                if (property.Key is Expr.IdentifierKey key &&
                    string.Equals(key.Name.Lexeme, attributeName, StringComparison.Ordinal))
                    return key.Name.Line;
            }
        }
        return jsx.Line;
    }

    /// <summary>
    /// The result type of every JSX expression: <c>JSX.Element</c> when declared, else
    /// <c>any</c> (with TS2602 under noImplicitAny, matching tsc).
    /// </summary>
    private TypeInfo ResolveJsxElementType(TypeInfo.Namespace? jsxNamespace, int line, bool reportMissing)
    {
        TypeInfo? element = jsxNamespace?.Types.GetValueOrDefault("Element");
        if (element is not null)
            return element;
        if (reportMissing && _noImplicitAny)
        {
            ReportJsx(new TypeCheckException(
                "JSX element implicitly has type 'any' because the global type 'JSX.Element' does not exist.",
                line, tsCode: "TS2602"));
        }
        return TypeInfo.Any.Shared;
    }

    /// <summary>
    /// Reports a JSX diagnostic: recorded (multiple errors per element) in recovery mode,
    /// thrown otherwise — mirroring the established multi-error-construct pattern.
    /// </summary>
    private void ReportJsx(TypeCheckException ex)
    {
        if (_recoveryMode)
            RecordTypeError(ex);
        else
            throw ex;
    }
}
