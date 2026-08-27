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
    // Automatic JSX lowering stores spread children inside a synthetic array literal. They
    // are JSX list splices, not JavaScript array spreads, so CheckArray must not require the
    // operand to be iterable while that synthetic props object is being checked.
    private readonly HashSet<Expr.Spread> _jsxSpreadChildren = [];

    private TypeInfo CheckJsxExpression(Expr.Call call, JsxCallInfo jsx)
    {
        var jsxNamespace = _environment.GetNamespace("JSX");

        // A missing factory (classic mode with no `React` in scope) reports the JSX-specific
        // TS2874 rather than exposing raw TS2304 from the lowered emitter expression.
        // tsc does not otherwise type-check the synthesized classic factory access:
        // an ambient `react` module may intentionally be empty while still making
        // `React.createElement(...)` a valid emit target.
        bool factoryRootMissing = false;
        try
        {
            CheckJsxFactoryReference(call.Callee, jsx);
        }
        catch (TypeCheckException ex)
        {
            factoryRootMissing = ex.Diagnostic.TsCode == "TS2304" && jsx.Mode == JsxMode.React;
            TypeCheckException diagnostic = ex.Diagnostic.TsCode == "TS2304" && jsx.Mode == JsxMode.React
                ? MissingJsxRuntimeName(call.Callee, jsx, "TS2874", "factory")
                : ex;
            ReportJsx(diagnostic);
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
            else if (jsx.Kind == JsxElementKind.Fragment && jsx.Mode == JsxMode.React)
            {
                if (factoryRootMissing && SameJsxReferenceRoot(call.Callee, tagArgument))
                {
                    ReportJsx(MissingJsxRuntimeName(
                        tagArgument, jsx, "TS2879", "fragment factory"));
                }
                else
                {
                    try
                    {
                        CheckJsxReferenceRoot(tagArgument);
                    }
                    catch (TypeCheckException ex)
                    {
                        ReportJsx(ex.Diagnostic.TsCode == "TS2304"
                            ? MissingJsxRuntimeName(tagArgument, jsx, "TS2879", "fragment factory")
                            : ex);
                    }
                }
            }
            else if (jsx.Kind == JsxElementKind.Intrinsic)
            {
                CheckExpr(tagArgument);
            }
        }

        TypeInfo propsType;
        foreach (Expr.Spread spreadChild in jsx.ChildExprs.OfType<Expr.Spread>())
            _jsxSpreadChildren.Add(spreadChild);
        try
        {
            propsType = jsx.PropsExpr is not null
                ? CheckExpr(jsx.PropsExpr)
                : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
        }
        catch (TypeCheckException ex)
        {
            ReportJsx(ex);
            propsType = TypeInfo.Any.Shared;
        }
        finally
        {
            foreach (Expr.Spread spreadChild in jsx.ChildExprs.OfType<Expr.Spread>())
                _jsxSpreadChildren.Remove(spreadChild);
        }

        if (jsx.PropsExpr is Expr.ObjectLiteral propsLiteral &&
            propsLiteral.Properties.Any(property =>
                property.IsSpread && _typeMap.Get(property.Value) is TypeInfo.Any))
        {
            // Object spread from `any` makes the complete JSX attributes object dynamic.
            // Required, excess, and value checks are therefore all suppressed by tsc.
            propsType = TypeInfo.Any.Shared;
        }

        CheckJsxSpreadOverwrites(jsx);

        foreach (var argument in call.Arguments)
        {
            if (ReferenceEquals(argument, tagArgument) || ReferenceEquals(argument, jsx.PropsExpr))
                continue;
            try
            {
                // JSX spread children are an emit-time list splice, not an ordinary
                // expression spread. TypeScript accepts even a non-iterable static type here;
                // check the operand for its own diagnostics without imposing TS2488.
                if (argument is Expr.Spread spreadChild && jsx.ChildExprs.Contains(argument))
                    CheckExpr(spreadChild.Expression);
                else
                    CheckExpr(argument);
            }
            catch (TypeCheckException ex)
            {
                // One malformed child must not prevent sibling JSX nodes or the
                // enclosing intrinsic from contributing their own diagnostics.
                ReportJsx(ex);
            }
        }

        propsType = ApplyJsxKeyContract(jsx, propsType);
        propsType = ApplyJsxChildrenContract(jsx, jsxNamespace, propsType);

        if (jsx.Mode == JsxMode.React &&
            jsx.TagName is { Length: > 0 } namespacedTag &&
            namespacedTag.Contains(':') &&
            char.IsUpper(namespacedTag[0]))
        {
            ReportJsx(new TypeCheckException(
                "React components cannot include JSX namespace names.",
                jsx.Line,
                tsCode: "TS2639"));
        }

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

        return ResolveJsxElementType(jsxNamespace);
    }

    private void CheckJsxSpreadOverwrites(JsxCallInfo jsx)
    {
        if (jsx.PropsExpr is not Expr.ObjectLiteral literal)
            return;

        for (int i = 0; i < literal.Properties.Count; i++)
        {
            if (literal.Properties[i] is not { IsSpread: false, Key: Expr.IdentifierKey key })
                continue;

            for (int j = i + 1; j < literal.Properties.Count; j++)
            {
                Expr.Property later = literal.Properties[j];
                if (!later.IsSpread || _typeMap.Get(later.Value) is not { } spreadType ||
                    !TryGetJsxMemberView(spreadType, out var members, out var optional, out _))
                {
                    continue;
                }
                if (!members.ContainsKey(key.Name.Lexeme) || optional.Contains(key.Name.Lexeme))
                    continue;

                ReportJsx(new TypeCheckException(
                    $"'{key.Name.Lexeme}' is specified more than once, so this usage will be overwritten.",
                    key.Name.Line,
                    tsCode: "TS2783"));
                break;
            }
        }
    }

    private TypeInfo CheckJsxFactoryReference(Expr callee, JsxCallInfo jsx)
    {
        // Automatic-runtime imports and preserve-mode calls are lowering details. Their
        // synthesized identifiers must never leak TS2304/TS2305/TS2307 into diagnostics.
        if (jsx.Mode != JsxMode.React)
            return TypeInfo.Any.Shared;

        return CheckJsxReferenceRoot(callee);
    }

    private TypeInfo CheckJsxReferenceRoot(Expr expression)
    {
        Expr root = expression;
        while (root is Expr.Get get)
            root = get.Object;
        if (root is Expr.Variable { Name.Start: < 0 } synthesized &&
            CurrentSourceDocument is { } document)
        {
            _synthesizedJsxUses.Add((document, synthesized.Name.Lexeme));
        }
        return CheckExpr(root);
    }

    private static TypeCheckException MissingJsxRuntimeName(
        Expr expression, JsxCallInfo jsx, string code, string role)
    {
        Expr root = expression;
        while (root is Expr.Get get)
            root = get.Object;
        string name = root is Expr.Variable variable ? variable.Name.Lexeme : "the configured runtime";
        return new TypeCheckException(
            $"This JSX tag requires '{name}' to be in scope as the {role}.",
            jsx.Line,
            tsCode: code);
    }

    private static bool SameJsxReferenceRoot(Expr left, Expr right) =>
        string.Equals(JsxReferenceRootName(left), JsxReferenceRootName(right), StringComparison.Ordinal);

    private static string? JsxReferenceRootName(Expr expression)
    {
        Expr root = expression;
        while (root is Expr.Get get)
            root = get.Object;
        return root is Expr.Variable variable ? variable.Name.Lexeme : null;
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
        if (jsx.TypeArgumentCount > 0 && componentType is TypeInfo.Function)
        {
            ReportJsx(new TypeCheckException(
                $"Expected 0 type arguments, but got {jsx.TypeArgumentCount}.",
                jsx.Line,
                tsCode: "TS2558"));
            // Once the explicit arity is invalid, tsc does not continue through
            // the component signature and report a secondary return-type error.
            return;
        }

        switch (componentType)
        {
            case TypeInfo.Any or TypeInfo.Unknown:
                return;

            // A member expression can select an intrinsic name dynamically
            // (`const t = { tag: 'h1' }; <t.tag />`). String-valued tag
            // expressions are valid JSX element constructors; a literal can
            // still use the matching IntrinsicElements entry for prop checks.
            case TypeInfo.StringLiteral literal
                when jsxNamespace?.Types.ContainsKey("IntrinsicElements") == true:
                CheckJsxIntrinsic(jsx with { TagName = literal.Value }, jsxNamespace, propsType);
                return;

            case TypeInfo.StringLiteral:
                // With no JSX.IntrinsicElements contract, a dynamic member whose value is a
                // string literal is still a valid element constructor; there is no intrinsic
                // declaration against which to check it and tsc does not report TS7026.
                return;

            case TypeInfo.String when jsx.TagName?.Contains('.') == true ||
                                      jsxNamespace?.Types.ContainsKey("IntrinsicElements") != true:
                // An unconstrained JSX namespace has no finite intrinsic-name set to
                // reject against. This also covers widened, initialized aliases such as
                // `var CustomTag = "h1"; <CustomTag />`; when IntrinsicElements exists,
                // plain string declarations remain invalid unless the tag is a dynamic
                // member expression handled above.
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

            // Constructor-only object types are class components too. Their construct
            // signature return type is the JSX instance type used by ElementClass and
            // ElementAttributesProperty.
            case TypeInfo.Record { ConstructorSignatures.Count: > 0 } constructRecord:
                CheckJsxConstructSignatures(jsx, jsxNamespace, constructRecord.ConstructorSignatures!, propsType);
                return;
            case TypeInfo.Interface { ConstructorSignatures.Count: > 0 } constructInterface:
                CheckJsxConstructSignatures(jsx, jsxNamespace, constructInterface.ConstructorSignatures!, propsType);
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
        TypeInfo.Record { ConstructorSignatures.Count: > 0 } => true,
        TypeInfo.Interface { ConstructorSignatures.Count: > 0 } => true,
        _ => false,
    };

    private void CheckJsxComponentReturnType(JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo returnType)
    {
        TypeInfo elementType = ResolveJsxElementType(jsxNamespace);
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

    private void CheckJsxConstructSignatures(
        JsxCallInfo jsx,
        TypeInfo.Namespace? jsxNamespace,
        IReadOnlyList<TypeInfo.ConstructorSignature> signatures,
        TypeInfo propsType)
    {
        TypeInfo? elementClass = jsxNamespace?.Types.GetValueOrDefault("ElementClass");
        TypeInfo? attributesMarker = jsxNamespace?.Types.GetValueOrDefault("ElementAttributesProperty");
        string? attributesProperty = null;
        bool hasAttributesMarker = attributesMarker is not null;
        if (attributesMarker is not null &&
            TryGetJsxMemberView(attributesMarker, out var markerMembers, out _, out _) &&
            markerMembers.Count > 0)
        {
            attributesProperty = markerMembers.Keys.First();
        }

        var candidates = new List<TypeInfo>();
        foreach (TypeInfo.ConstructorSignature signature in signatures)
        {
            TypeInfo instanceType = signature.IsGeneric
                ? Substitute(signature.ReturnType, signature.TypeParams!.ToDictionary(
                    parameter => parameter.Name,
                    parameter => parameter.Constraint ?? parameter.Default ?? TypeInfo.Any.Shared,
                    StringComparer.Ordinal))
                : signature.ReturnType;

            // An `any` instance makes both the element-class and props contracts dynamic.
            if (instanceType is TypeInfo.Any)
                return;

            if (elementClass is not null && !IsJsxElementClassCompatible(elementClass, instanceType))
            {
                ReportJsx(new TypeCheckException(
                    $"'{jsx.TagName}' cannot be used as a JSX component. Its instance type '{instanceType}' is not a valid JSX element class.",
                    jsx.Line, tsCode: "TS2786"));
                return;
            }

            TypeInfo expected;
            if (attributesProperty is not null)
            {
                TypeInfo? declaredProps = LookupJsxInstanceMember(instanceType, attributesProperty);
                if (declaredProps is null)
                {
                    ReportJsx(new TypeCheckException(
                        $"JSX element class does not support attributes because it does not have a '{attributesProperty}' property.",
                        jsx.Line, tsCode: "TS2607"));
                    return;
                }
                expected = declaredProps;
            }
            else if (hasAttributesMarker)
            {
                // An explicitly empty ElementAttributesProperty selects the instance shape.
                expected = instanceType;
            }
            else
            {
                // Without a marker, constructor-only values follow the historical JSX
                // fallback to the first construct parameter.
                expected = JsxFirstParameter(signature.ParamTypes);
            }
            candidates.Add(expected);
        }

        TypeInfo expectedProps = candidates.FirstOrDefault(candidate =>
            AreJsxAttributesCompatible(candidate, propsType, jsxNamespace)) ?? candidates[0];
        CheckJsxAttributes(expectedProps, propsType, jsx, jsxNamespace);
    }

    private TypeInfo ApplyJsxKeyContract(JsxCallInfo jsx, TypeInfo propsType)
    {
        // The automatic transform extracts `key` into a separate runtime argument, but JSX
        // attribute checking still sees it as a written attribute. Put it back into the
        // synthetic props view so an explicitly-declared required `key` prop is satisfied.
        if (jsx.KeyExpr is null || propsType is not TypeInfo.Record record)
            return propsType;

        var fields = record.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        fields["key"] = _typeMap.Get(jsx.KeyExpr) ?? TypeInfo.Any.Shared;
        FrozenSet<string>? optional = record.OptionalFields is null
            ? null
            : record.OptionalFields.Where(name => name != "key").ToFrozenSet(StringComparer.Ordinal);
        return record with
        {
            Fields = fields.ToFrozenDictionary(StringComparer.Ordinal),
            OptionalFields = optional,
        };
    }

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
        var childTypes = jsx.ChildExprs.Select(child => _typeMap.Get(child) ?? TypeInfo.Any.Shared).ToList();
        TypeInfo childType = childTypes.Count == 1
            ? childTypes[0]
            : new TypeInfo.Tuple(
                jsx.ChildExprs.Select((child, index) => new TypeInfo.TupleElement(
                    childTypes[index],
                    child is Expr.Spread ? TupleElementKind.Spread : TupleElementKind.Required)).ToList(),
                jsx.ChildExprs.Count(child => child is not Expr.Spread));
        if (childrenName != "children") fields.Remove("children");
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

        TypeInfo expected;
        if (propertyName is null)
        {
            expected = new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
        }
        else
        {
            TypeInfo? declaredProps = LookupJsxClassInstanceMember(instanceType, propertyName);
            if (declaredProps is null)
            {
                ReportJsx(new TypeCheckException(
                    $"JSX element class does not support attributes because it does not have a '{propertyName}' property.",
                    jsx.Line, tsCode: "TS2607"));
                return;
            }
            expected = declaredProps;
        }

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
            TypeInfo? actual = LookupJsxInstanceMember(instanceType, name);
            if (actual is null)
            {
                if (optional.Contains(name)) continue;
                return false;
            }
            if (!IsCompatible(expected, actual)) return false;
        }
        return true;
    }

    private TypeInfo? LookupJsxInstanceMember(TypeInfo type, string name)
    {
        TypeInfo? classMember = LookupJsxClassInstanceMember(type, name);
        if (classMember is not null)
            return classMember;
        return TryGetJsxMemberView(type, out var members, out _, out TypeInfo? stringIndex)
            ? members.GetValueOrDefault(name) ?? stringIndex
            : null;
    }

    private TypeInfo? LookupJsxClassInstanceMember(TypeInfo type, string name) => type switch
    {
        TypeInfo.Class cls => CollectPublicInstanceMembers(cls).GetValueOrDefault(name),
        TypeInfo.GenericClass generic => CollectGenericClassMembers(
            generic, generic.TypeParams.Cast<TypeInfo>().ToList()).GetValueOrDefault(name),
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
            CollectGenericClassMembers(generic, instantiated.TypeArguments).GetValueOrDefault(name),
        TypeInfo.Instance instance => LookupJsxClassInstanceMember(instance.ResolvedClassType, name),
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
        if (!TryGetJsxMemberView(expected, out var members, out var optional, out _))
            return false;
        foreach (string required in members.Keys)
        {
            if (required is "key" or "toString" || optional.Contains(required))
                continue;
            if (!actualRecord.Fields.ContainsKey(required))
                return false;
        }
        return AreJsxSuppliedAttributesCompatible(expected, actualRecord, jsxNamespace);
    }

    private bool AreJsxSuppliedAttributesCompatible(
        TypeInfo expected, TypeInfo.Record actual, TypeInfo.Namespace? jsxNamespace)
    {
        if (!TryGetJsxMemberView(expected, out var members, out _, out TypeInfo? stringIndex))
            return false;
        TypeInfo? intrinsic = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");
        foreach ((string name, TypeInfo value) in actual.Fields)
        {
            if (name == "key")
            {
                TypeInfo? key = intrinsic is null ? null : LookupJsxObjectMember(intrinsic, "key");
                if (key is null || !IsCompatible(key, value)) return false;
                continue;
            }
            if (!IsCheckedJsxAttribute(name) && !members.ContainsKey(name)) continue;
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

        // Intersections distribute over union props for JSX assignment just as they do for
        // ordinary object assignment: `(Canadian | American) & Children` accepts either
        // complete address shape. Select the compatible branch before flattening members.
        IReadOnlyList<TypeInfo> alternatives = JsxAttributeAlternatives(expected);
        bool selectedAlternative = false;
        if (alternatives.Count > 1)
        {
            TypeInfo? matching = alternatives.FirstOrDefault(candidate =>
                AreJsxAttributesCompatible(candidate, actual, jsxNamespace));
            // A discriminant may identify a branch even when another required member of that
            // branch is absent. Conversely, a spread whose static type still contains the whole
            // union must retain the undistributed view rather than being forced into branch 0.
            matching ??= alternatives.FirstOrDefault(candidate =>
                AreJsxSuppliedAttributesCompatible(candidate, actualRecord, jsxNamespace));
            if (matching is not null)
            {
                expected = matching;
                selectedAlternative = true;
            }
        }
        if (!TryGetJsxMemberView(expected, out var expectedMembers, out var expectedOptional,
                out TypeInfo? stringIndexType))
            return;

        HashSet<string> directAttributes = jsx.PropsExpr is Expr.ObjectLiteral literal
            ? literal.Properties
                .Where(property => !property.IsSpread && property.Key is Expr.IdentifierKey)
                .Select(property => ((Expr.IdentifierKey)property.Key!).Name.Lexeme)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        string childrenAttributeName = "children";
        bool hasChildrenAttributeContract = false;
        if (jsx.ChildExprs.Count > 0)
        {
            if (jsxNamespace?.Types.GetValueOrDefault("ElementChildrenAttribute") is { } childrenMarker &&
                TryGetJsxMemberView(childrenMarker, out var childrenMembers, out _, out _) &&
                childrenMembers.Count > 0)
            {
                childrenAttributeName = childrenMembers.Keys.First();
                hasChildrenAttributeContract = true;
            }
            // Without ElementChildrenAttribute, children are not an excess attribute on
            // an empty props bag. Still validate them when the props type itself explicitly
            // declares the conventional `children` member.
            if (hasChildrenAttributeContract || expectedMembers.ContainsKey(childrenAttributeName))
                directAttributes.Add(childrenAttributeName);
        }

        // Weak-type failure is an assignment diagnostic for a spread source. Direct JSX
        // attributes instead receive the JSX TS2322 excess-property diagnostic.
        var consideredFields = actualRecord.Fields
            .Where(field => IsCheckedJsxAttribute(field.Key) || expectedMembers.ContainsKey(field.Key))
            .ToFrozenDictionary(StringComparer.Ordinal);
        if (consideredFields.Count > 0 &&
            !directAttributes.Any(IsCheckedJsxAttribute) &&
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
        foreach (string required in expectedMembers.Keys)
        {
            // Every non-null object has Object.prototype.toString even though the structural
            // record view does not materialize prototype members.
            if (required == "toString")
                continue;
            if (required is "key" && !expectedMembers.ContainsKey("key"))
                continue;
            if (expectedOptional.Contains(required))
                continue;
            if (!actualRecord.Fields.ContainsKey(required))
                missing.Add(required);
        }
        TypeInfo? intrinsicAttributes = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");
        bool hasDirectExcess = directAttributes.Any(name =>
            IsCheckedJsxAttribute(name) &&
            !expectedMembers.ContainsKey(name) &&
            stringIndexType is null &&
            (intrinsicAttributes is null || LookupJsxObjectMember(intrinsicAttributes, name) is null));
        bool genericSpreadSource = jsx.PropsExpr is Expr.ObjectLiteral missingPropsLiteral &&
            missingPropsLiteral.Properties.Any(property =>
                property.IsSpread && _typeMap.Get(property.Value) is TypeInfo.TypeParameter);

        // A fresh direct excess property is the primary assignment failure, so don't also
        // report every required property it displaced. For a pure generic spread, tsc keeps
        // the source type in the diagnostic and uses TS2322 rather than literal TS2741/TS2739.
        bool useAssignmentDiagnostic = genericSpreadSource || selectedAlternative;
        if (!hasDirectExcess && missing.Count == 1)
        {
            ReportJsx(new TypeCheckException(
                useAssignmentDiagnostic
                    ? $"Type '{actual}' is not assignable to type '{expected}'. Property '{missing[0]}' is missing."
                    : $"Property '{missing[0]}' is missing in type '{actual}' but required in type '{expected}'.",
                jsx.Line, tsCode: useAssignmentDiagnostic ? "TS2322" : "TS2741"));
            return;
        }
        else if (!hasDirectExcess && missing.Count > 1)
        {
            ReportJsx(new TypeCheckException(
                $"Type '{actual}' is missing the following properties from type '{expected}': " +
                string.Join(", ", missing),
                jsx.Line, tsCode: useAssignmentDiagnostic ? "TS2322" : "TS2739"));
            return;
        }

        if (actualRecord.Fields.TryGetValue("key", out TypeInfo? keyType))
        {
            TypeInfo? expectedKey = expectedMembers.GetValueOrDefault("key") ??
                (intrinsicAttributes is null
                    ? null
                    : LookupJsxObjectMember(intrinsicAttributes, "key"));
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
            if (!IsCheckedJsxAttribute(name) && !expectedMembers.ContainsKey(name))
                continue;

            TypeInfo? memberType = expectedMembers.TryGetValue(name, out var direct)
                ? direct
                : stringIndexType;
            if (memberType is null && intrinsicAttributes is not null)
                memberType = LookupJsxObjectMember(intrinsicAttributes, name);

            if (memberType is null)
            {
                // Excess properties that originate only in a spread are allowed. A directly
                // written JSX attribute remains subject to the ordinary excess check.
                if (!directAttributes.Contains(name))
                    continue;
                ReportJsx(new TypeCheckException(
                    $"Type '{actual}' is not assignable to type '{expected}'. " +
                    $"Property '{name}' does not exist on type '{expected}'.",
                    JsxAttributeLine(jsx, name), tsCode: "TS2322"));
                continue;
            }

            if (!IsCompatible(memberType, valueType))
            {
                if (name == childrenAttributeName && jsx.ChildExprs.Count > 1)
                {
                    TypeInfo? repeatedChildType = memberType switch
                    {
                        TypeInfo.Array array => array.ElementType,
                        TypeInfo.Union union => union.FlattenedTypes
                            .OfType<TypeInfo.Array>()
                            .Select(array => array.ElementType)
                            .FirstOrDefault(),
                        _ => null,
                    };
                    int invalidTextIndex = repeatedChildType is null
                        ? -1
                        : jsx.ChildExprs
                            .Select((child, index) => (Type: _typeMap.Get(child) ?? TypeInfo.Any.Shared, index))
                            .Where(item => item.Type is TypeInfo.StringLiteral &&
                                           !IsCompatible(repeatedChildType, item.Type))
                            .Select(item => item.index)
                            .DefaultIfEmpty(-1)
                            .First();
                    if (invalidTextIndex >= 0)
                    {
                        ReportJsx(new TypeCheckException(
                            $"Components don't accept text as child elements. Text in JSX has the type 'string', but the expected type of '{childrenAttributeName}' is '{memberType}'.",
                            jsx.ChildLines is { } lines && invalidTextIndex < lines.Count
                                ? lines[invalidTextIndex]
                                : jsx.Line,
                            tsCode: "TS2747"));
                        continue;
                    }

                    if (repeatedChildType is null && memberType is not TypeInfo.Tuple)
                    {
                        ReportJsx(new TypeCheckException(
                            $"This JSX tag's '{childrenAttributeName}' prop expects a single child of type '{memberType}', but multiple children were provided.",
                            jsx.Line, tsCode: "TS2746"));
                        continue;
                    }
                }
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

    private static IReadOnlyList<TypeInfo> JsxAttributeAlternatives(TypeInfo type)
    {
        if (type is TypeInfo.Union union)
            return union.FlattenedTypes;
        if (type is not TypeInfo.Intersection intersection ||
            !intersection.FlattenedTypes.Any(part => part is TypeInfo.Union))
        {
            return [type];
        }

        List<List<TypeInfo>> products = [[]];
        foreach (TypeInfo part in intersection.FlattenedTypes)
        {
            IReadOnlyList<TypeInfo> choices = part is TypeInfo.Union partUnion
                ? partUnion.FlattenedTypes
                : [part];
            products = products
                .SelectMany(product => choices.Select(choice => product.Append(choice).ToList()))
                .ToList();
        }
        return products
            .Select(parts => parts.Count == 1 ? parts[0] : new TypeInfo.Intersection(parts))
            .ToList();
    }

    /// <summary>
    /// Whether an otherwise-unknown written attribute participates in props checking: key is
    /// checked separately through IntrinsicAttributes, and tsc exempts unknown hyphenated/
    /// namespaced names. Callers still check such a name when the props type explicitly declares it.
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
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } instantiated
                when FlattenInstantiatedInterface(instantiated) is { } flattened:
                return TryGetJsxMemberView(flattened, out members, out optionalMembers, out stringIndexType);
            case TypeInfo.TypeParameter { Constraint: { } constraint }:
                return TryGetJsxMemberView(constraint, out members, out optionalMembers, out stringIndexType);
            case TypeInfo.Intersection intersection:
            {
                bool found = false;
                foreach (TypeInfo part in intersection.FlattenedTypes)
                {
                    if (!TryGetJsxMemberView(part, out var partMembers, out var partOptional, out var partIndex))
                        continue;
                    found = true;
                    foreach ((string name, TypeInfo value) in partMembers)
                    {
                        bool existed = members.TryGetValue(name, out TypeInfo? previous);
                        if (existed)
                            members[name] = new TypeInfo.Intersection([previous!, value]);
                        else
                            members[name] = value;

                        if (!partOptional.Contains(name))
                            optionalMembers.Remove(name);
                        else if (!existed || optionalMembers.Contains(name))
                            optionalMembers.Add(name);
                    }
                    if (partIndex is not null)
                        stringIndexType = stringIndexType is null
                            ? partIndex
                            : new TypeInfo.Intersection([stringIndexType, partIndex]);
                }
                return found;
            }
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
    /// <c>any</c>. TypeScript 6's JSX lookup returns its internal error type when the
    /// member is absent, so its old TS2602 precondition is no longer emitted.
    /// </summary>
    private static TypeInfo ResolveJsxElementType(TypeInfo.Namespace? jsxNamespace)
    {
        TypeInfo? element = jsxNamespace?.Types.GetValueOrDefault("Element");
        return element ?? TypeInfo.Any.Shared;
    }

    /// <summary>
    /// Reports a JSX diagnostic: recorded (multiple errors per element) in recovery mode,
    /// thrown otherwise — mirroring the established multi-error-construct pattern.
    /// </summary>
    private void ReportJsx(TypeCheckException ex)
    {
        // CheckModules performs per-statement recovery without toggling the standalone
        // _recoveryMode flag. Treat its active module as recovery too; otherwise the first
        // JSX diagnostic aborts a comma-recovered adjacent root and suppresses diagnostics
        // for the retained sibling.
        if (_recoveryMode || _currentModule is not null)
            RecordTypeError(ex);
        else
            throw ex;
    }
}
