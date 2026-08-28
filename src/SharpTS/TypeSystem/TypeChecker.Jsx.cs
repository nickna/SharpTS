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
        var jsxNamespace = ResolveJsxNamespace(jsx);

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

        foreach (Expr.Spread spreadChild in jsx.ChildExprs.OfType<Expr.Spread>())
        {
            // The ordinary child pass has already checked the expression. If that check failed
            // (for example `this` is undefined), keep reporting the independent JSX spread-child
            // diagnostic without evaluating the same expression a second time.
            TypeInfo spreadType = _typeMap.Get(spreadChild.Expression) ?? TypeInfo.Unknown.Shared;
            if (jsx.Mode is JsxMode.React or JsxMode.Preserve &&
                (!IsValidJsxSpreadChild(spreadType) || IsOptionalJsxSpreadAccess(spreadChild.Expression)))
            {
                ReportJsx(new TypeCheckException(
                    "JSX spread child must be an array type.",
                    spreadChild.Expression is Expr.Get spreadGet ? spreadGet.Name.Line : jsx.Line,
                    tsCode: "TS2609"));
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

    private static bool IsValidJsxSpreadChild(TypeInfo type) => type switch
    {
        TypeInfo.Any or TypeInfo.Array or TypeInfo.Tuple => true,
        TypeInfo.Union union => union.FlattenedTypes.All(IsValidJsxSpreadChild),
        _ => false,
    };

    private bool IsOptionalJsxSpreadAccess(Expr expression)
    {
        if (expression is not Expr.Get get)
            return false;

        // A failed/short-circuited receiver check may intentionally leave no type-map entry.
        // For a spread `children` access that uncertainty is enough to reject the iterable:
        // the ordinary expression check has already emitted the more specific diagnostic.
        if (_typeMap.Get(get.Object) is not { } receiver)
            return get.Name.Lexeme == "children";

        if (!TryGetJsxMemberView(receiver, out _, out var optional, out _))
            return false;
        return optional.Contains(get.Name.Lexeme);
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
            // any — an error only under noImplicitAny (TS7026), silent otherwise. The automatic
            // runtimes obtain their JSX contract from jsx-runtime itself; when that ambient
            // module is only partially modeled, don't manufacture a classic global-namespace
            // TS7026 on otherwise valid emit-only inputs.
            if (_noImplicitAny &&
                (jsx.Mode is JsxMode.React or JsxMode.Preserve || jsx.FactoryName is null))
            {
                ReportJsx(new TypeCheckException(
                    $"JSX element implicitly has type 'any' because no interface 'JSX.IntrinsicElements' exists.",
                    jsx.Line, tsCode: "TS7026"));
                if (jsx.ClosingLine is { } closingLine)
                {
                    ReportJsx(new TypeCheckException(
                        "JSX element implicitly has type 'any' because no interface 'JSX.IntrinsicElements' exists.",
                        closingLine, tsCode: "TS7026"));
                }
            }
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

        // Some older React declaration files express intrinsic props through a qualified generic
        // alias (`React.DetailedHTMLProps<...>`). When that alias is outside the namespace's
        // temporary declaration scope it can degrade to `any`; retain the important contextual
        // typing of intrinsic `ref` callbacks from the DOM element associated with the tag.
        if (JsxIntrinsicRefNeedsDomFallback(tagType) &&
            propsType is TypeInfo.Record intrinsicProps &&
            JsxDomElementTypeName(jsx.TagName) is { } domTypeName &&
            _environment.GetTypeBinding(domTypeName) is { } domElementType)
        {
            ContextualizeJsxAttributeCallbacks(
                jsx,
                new Dictionary<string, TypeInfo>(StringComparer.Ordinal)
                {
                    ["ref"] = new TypeInfo.Function([domElementType], TypeInfo.Any.Shared),
                },
                stringIndexType: null,
                intrinsicProps);
        }

        CheckJsxAttributes(tagType, propsType, jsx, jsxNamespace);
    }

    private static bool JsxIntrinsicRefNeedsDomFallback(TypeInfo tagType) => tagType switch
    {
        TypeInfo.Any or TypeInfo.Unknown => true,
        TypeInfo.InstantiatedGeneric instantiated =>
            instantiated.TypeArguments.Count > 0 &&
            instantiated.TypeArguments.All(argument => argument is TypeInfo.Any),
        _ => false,
    };

    private static string? JsxDomElementTypeName(string? tagName) => tagName switch
    {
        "a" => "HTMLAnchorElement",
        "button" => "HTMLButtonElement",
        "div" => "HTMLDivElement",
        "form" => "HTMLFormElement",
        "img" => "HTMLImageElement",
        "input" => "HTMLInputElement",
        "li" => "HTMLLIElement",
        "option" => "HTMLOptionElement",
        "p" => "HTMLParagraphElement",
        "select" => "HTMLSelectElement",
        "span" => "HTMLSpanElement",
        "textarea" => "HTMLTextAreaElement",
        "ul" => "HTMLUListElement",
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "HTMLHeadingElement",
        _ => null,
    };

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
                if (LookupJsxObjectMember(
                        jsxNamespace.Types["IntrinsicElements"], literal.Value) is null)
                {
                    ReportJsx(new TypeCheckException(
                        $"JSX element type '{jsx.TagName}' does not have any construct or call signatures.",
                        jsx.Line, tsCode: "TS2604"));
                }
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
                if (!TryInstantiateJsxGenericFunction(jsx, generic, propsType, out TypeInfo.Function instantiated))
                    return;
                if (HasUnconstrainedJsxSpread(jsx) && jsxNamespace?.Types.ContainsKey("IntrinsicAttributes") == true)
                {
                    ReportJsx(new TypeCheckException(
                        "A spread type parameter is not assignable to JSX.IntrinsicAttributes.",
                        jsx.Line, tsCode: "TS2322"));
                    return;
                }
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
                bool hasPropsOverload = overloaded.Signatures.Any(signature =>
                    JsxTypeHasMembers(JsxFirstParameter(signature.ParamTypes)));
                foreach (var signature in overloaded.Signatures)
                {
                    TypeInfo expected = signature.ParamTypes.Count > 0
                        ? signature.ParamTypes[0]
                        : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
                    if (hasPropsOverload && !JsxTypeHasMembers(expected) &&
                        propsType is TypeInfo.Record record && record.Fields.Keys.Any(name =>
                            IsCheckedJsxAttribute(name) && IsDirectOrChildJsxAttribute(jsx, name)))
                        continue;
                    if (AreJsxAttributesCompatible(expected, propsType, jsxNamespace, jsx))
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
                var instantiatedSignatures = new List<TypeInfo.Function>();
                foreach (TypeInfo.Function signature in genericOverloaded.Signatures)
                {
                    var generic = new TypeInfo.GenericFunction(
                        genericOverloaded.TypeParams, signature.ParamTypes, signature.ReturnType,
                        signature.RequiredParams, signature.HasRestParam, signature.ThisType, signature.ParamNames);
                    instantiatedSignatures.Add((TypeInfo.Function)InstantiateGenericFunction(
                        generic, InferTypeArguments(generic, [propsType], combineCandidates: true)));
                }
                bool hasPropsOverload = instantiatedSignatures.Any(signature =>
                    JsxTypeHasMembers(JsxFirstParameter(signature.ParamTypes)));
                foreach (TypeInfo.Function instantiated in instantiatedSignatures)
                {
                    TypeInfo expected = JsxFirstParameter(instantiated.ParamTypes);
                    if (hasPropsOverload && !JsxTypeHasMembers(expected) &&
                        propsType is TypeInfo.Record record && record.Fields.Keys.Any(name =>
                            IsCheckedJsxAttribute(name) && IsDirectOrChildJsxAttribute(jsx, name)))
                        continue;
                    if (!AreJsxAttributesCompatible(expected, propsType, jsxNamespace, jsx)) continue;
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

            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } instantiatedInterface
                when FlattenInstantiatedInterface(instantiatedInterface) is { } interfaceView:
                if (interfaceView.CallSignatures is { Count: > 0 } callSignatures)
                    CheckJsxCallSignatures(jsx, jsxNamespace, callSignatures, propsType);
                else if (interfaceView.ConstructorSignatures is { Count: > 0 } constructSignatures)
                    CheckJsxConstructSignatures(jsx, jsxNamespace, constructSignatures, propsType);
                else
                    ReportJsx(new TypeCheckException(
                        $"JSX element type '{jsx.TagName}' does not have any construct or call signatures.",
                        jsx.Line, tsCode: "TS2604"));
                return;

            case TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass:
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass }:
                CheckJsxClassComponent(jsx, jsxNamespace, componentType, propsType);
                return;

            // Callable object types ({ (props): Element }) use their call signatures just like
            // overloaded function declarations.
            case TypeInfo.Record { CallSignatures.Count: > 0 } record:
                CheckJsxCallSignatures(jsx, jsxNamespace, record.CallSignatures!, propsType);
                return;
            case TypeInfo.Interface iface when GetAllJsxCallSignatures(iface) is { Count: > 0 } calls:
                CheckJsxCallSignatures(jsx, jsxNamespace, calls, propsType);
                return;
            case TypeInfo.Record { ConstructorSignatures.Count: > 0 } record:
                CheckJsxConstructSignatures(jsx, jsxNamespace, record.ConstructorSignatures!, propsType);
                return;
            case TypeInfo.Interface iface when GetAllJsxConstructorSignatures(iface) is { Count: > 0 } constructors:
                CheckJsxConstructSignatures(jsx, jsxNamespace, constructors, propsType);
                return;

            case TypeInfo.Union union:
                CheckJsxUnionComponent(jsx, jsxNamespace, union, propsType);
                return;
        }

        ReportJsx(new TypeCheckException(
            $"JSX element type '{jsx.TagName}' does not have any construct or call signatures.",
            jsx.Line, tsCode: "TS2604"));
    }

    private bool IsJsxRenderableTagType(TypeInfo type) => type switch
    {
        TypeInfo.Any or TypeInfo.Unknown => true,
        TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or
            TypeInfo.GenericOverloadedFunction or TypeInfo.OverloadSet => true,
        TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass => true,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass } => true,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface genericInterface } =>
            genericInterface.HasCallSignature || genericInterface.HasConstructorSignature,
        TypeInfo.Record { CallSignatures.Count: > 0 } => true,
        TypeInfo.Interface iface when GetAllJsxCallSignatures(iface).Count > 0 => true,
        TypeInfo.Record { ConstructorSignatures.Count: > 0 } => true,
        TypeInfo.Interface iface when GetAllJsxConstructorSignatures(iface).Count > 0 => true,
        _ => false,
    };

    private static List<TypeInfo.CallSignature> GetAllJsxCallSignatures(TypeInfo.Interface type)
    {
        var signatures = new List<TypeInfo.CallSignature>();
        if (type.CallSignatures is { } own) signatures.AddRange(own);
        foreach (TypeInfo.Interface parent in type.Extends ?? [])
            signatures.AddRange(GetAllJsxCallSignatures(parent));
        return signatures.Distinct().ToList();
    }

    private static List<TypeInfo.ConstructorSignature> GetAllJsxConstructorSignatures(TypeInfo.Interface type)
    {
        var signatures = new List<TypeInfo.ConstructorSignature>();
        if (type.ConstructorSignatures is { } own) signatures.AddRange(own);
        foreach (TypeInfo.Interface parent in type.Extends ?? [])
            signatures.AddRange(GetAllJsxConstructorSignatures(parent));
        return signatures.Distinct().ToList();
    }

    private void CheckJsxComponentReturnType(JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, TypeInfo returnType)
    {
        // Without a declared JSX.Element contract there is nothing to validate against. A
        // declared contract can still resolve to the checker's best-effort `any` (for example
        // through a complex third-party .d.ts); explicit undefined/void returns remain invalid.
        if (jsxNamespace is null || returnType is TypeInfo.Any)
            return;
        TypeInfo elementType = ResolveJsxElementType(jsxNamespace);
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
                    generic, InferTypeArguments(generic, [propsType], combineCandidates: true));
                expected = JsxFirstParameter(instantiated.ParamTypes);
                returnType = instantiated.ReturnType;
            }
            else
            {
                expected = JsxFirstParameter(signature.ParamTypes);
                returnType = signature.ReturnType;
            }
            if (signatures.Count > 1 &&
                !AreJsxAttributesCompatible(expected, propsType, jsxNamespace, jsx))
                continue;
            CheckJsxAttributes(expected, propsType, jsx, jsxNamespace);
            CheckJsxComponentReturnType(jsx, jsxNamespace, returnType);
            return;
        }
        ReportJsx(new TypeCheckException(
            "No overload matches this call.", jsx.Line, tsCode: "TS2769"));
        TypeInfo combinedReturn = signatures.Count == 1
            ? signatures[0].ReturnType
            : new TypeInfo.Intersection(signatures.Select(signature => signature.ReturnType).ToList());
        CheckJsxComponentReturnType(jsx, jsxNamespace, combinedReturn);
    }

    private static TypeInfo JsxFirstParameter(IReadOnlyList<TypeInfo> parameters) =>
        parameters.Count > 0 ? parameters[0] : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

    private bool JsxTypeHasMembers(TypeInfo type) =>
        TryGetJsxMemberView(type, out var members, out _, out _) && members.Count > 0;

    private void CheckJsxConstructSignatures(
        JsxCallInfo jsx,
        TypeInfo.Namespace? jsxNamespace,
        IReadOnlyList<TypeInfo.ConstructorSignature> signatures,
        TypeInfo propsType)
    {
        TypeInfo? elementClass = jsxNamespace?.Types.GetValueOrDefault("ElementClass");
        TypeInfo? attributesMarker = jsxNamespace?.Types.GetValueOrDefault("ElementAttributesProperty");
        string? attributesProperty = null;
        bool hasAttributesMarker = attributesMarker is not null &&
            TryGetJsxMemberView(attributesMarker, out var allMarkerMembers, out _, out _) &&
            allMarkerMembers.Count <= 1;
        if (attributesMarker is not null &&
            TryGetJsxMemberView(attributesMarker, out var markerMembers, out _, out _) &&
            markerMembers.Count == 1)
        {
            attributesProperty = markerMembers.Keys.First();
        }

        var candidates = new List<TypeInfo>();
        foreach (TypeInfo.ConstructorSignature signature in signatures)
        {
            TypeInfo instanceType;
            IReadOnlyList<TypeInfo> parameterTypes;
            if (signature.IsGeneric)
            {
                var generic = new TypeInfo.GenericFunction(
                    signature.TypeParams!, signature.ParamTypes, signature.ReturnType,
                    signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames);
                var instantiated = (TypeInfo.Function)InstantiateGenericFunction(
                    generic, InferTypeArguments(generic, [propsType], fallbackToConstraints: true,
                        combineCandidates: true));
                instanceType = instantiated.ReturnType;
                parameterTypes = instantiated.ParamTypes;
            }
            else
            {
                instanceType = signature.ReturnType;
                parameterTypes = signature.ParamTypes;
            }

            if (signatures.Count > 1 && !hasAttributesMarker &&
                !AreJsxAttributesCompatible(
                    JsxFirstParameter(parameterTypes), propsType, jsxNamespace, jsx))
                continue;

            // An `any` instance makes both the element-class and props contracts dynamic.
            if (instanceType is TypeInfo.Any)
                return;

            if (elementClass is not null && !IsJsxElementClassCompatible(elementClass, instanceType))
            {
                ReportJsx(new TypeCheckException(
                    $"'{jsx.TagName}' cannot be used as a JSX component. Its instance type '{instanceType}' is not a valid JSX element class.",
                    jsx.Line, tsCode: "TS2786"));
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
                expected = JsxFirstParameter(parameterTypes);
            }
            candidates.Add(expected);
        }

        if (candidates.Count == 0)
        {
            ReportJsx(new TypeCheckException(
                "No overload matches this call.", jsx.Line, tsCode: "TS2769"));
            return;
        }

        TypeInfo expectedProps = candidates.FirstOrDefault(candidate =>
            AreJsxAttributesCompatible(candidate, propsType, jsxNamespace)) ?? candidates[0];
        CheckJsxAttributes(expectedProps, propsType, jsx, jsxNamespace, forceAssignmentDiagnostic: true);
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

        if (IsDirectJsxAttribute(jsx, childrenName))
        {
            ReportJsx(new TypeCheckException(
                $"'{childrenName}' are specified twice. The attribute named '{childrenName}' will be overwritten.",
                jsx.Line, tsCode: "TS2710"));
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
        // Without a JSX namespace there is no ElementClass/attributes contract for a
        // value-based element. Intrinsic names still report TS7026 in their dedicated path.
        if (jsxNamespace is null)
            return;

        TypeInfo instanceType = classType is TypeInfo.MutableClass mutable ? mutable.Freeze() : classType;
        if (classType is TypeInfo.GenericClass genericClass)
        {
            List<TypeInfo> typeArguments;
            if (jsx.TypeArgumentCount > 0)
            {
                if (!TryResolveExplicitJsxTypeArguments(jsx, genericClass.TypeParams, out typeArguments))
                    return;
            }
            else
            {
                string? inferenceProperty = GetJsxAttributesPropertyName(jsxNamespace);
                TypeInfo? openProps = inferenceProperty is null
                    ? null
                    : LookupJsxClassCoreMember(genericClass.Core, inferenceProperty);
                var inference = new TypeInfo.GenericFunction(
                    genericClass.TypeParams,
                    openProps is null ? [] : [openProps],
                    TypeInfo.Any.Shared);
                typeArguments = InferTypeArguments(
                    inference, [propsType], fallbackToConstraints: true, combineCandidates: true);
            }

            try
            {
                instanceType = InstantiateGenericClass(genericClass, typeArguments);
            }
            catch (TypeCheckException ex)
            {
                ReportJsx(WithJsxLine(ex, jsx.Line));
                return;
            }
        }
        else if (jsx.TypeArgumentCount > 0)
        {
            ReportJsx(new TypeCheckException(
                $"Expected 0 type arguments, but got {jsx.TypeArgumentCount}.",
                jsx.Line, tsCode: "TS2558"));
            return;
        }

        TypeInfo? elementClass = jsxNamespace?.Types.GetValueOrDefault("ElementClass");
        if (elementClass is not null && !IsJsxElementClassCompatible(elementClass, instanceType))
        {
            ReportJsx(new TypeCheckException(
                $"'{jsx.TagName}' cannot be used as a JSX component. Its instance type '{instanceType}' is not a valid JSX element class.",
                jsx.Line, tsCode: "TS2786"));
            return;
        }

        string? propertyName = GetJsxAttributesPropertyName(jsxNamespace);

        TypeInfo expected;
        if (propertyName is null)
        {
            expected = LookupJsxGenericBaseProps(instanceType) ??
                new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
        }
        else
        {
            TypeInfo? declaredProps = LookupJsxClassInstanceMember(instanceType, propertyName);
            if (declaredProps is null && propertyName == "props")
                declaredProps = LookupJsxGenericBaseProps(instanceType);
            if (declaredProps is null)
            {
                ReportJsx(new TypeCheckException(
                    $"JSX element class does not support attributes because it does not have a '{propertyName}' property.",
                    jsx.Line, tsCode: "TS2607"));
                return;
            }
            expected = declaredProps;
        }

        bool usesLibraryManagedAttributes =
            jsxNamespace?.Types.ContainsKey("LibraryManagedAttributes") == true;
        expected = ApplyJsxLibraryManagedAttributes(jsxNamespace, classType, expected);

        if (expected is not (TypeInfo.Any or TypeInfo.Unknown) &&
            jsxNamespace?.Types.GetValueOrDefault("IntrinsicClassAttributes") is
            TypeInfo.GenericInterface intrinsicClassAttributes)
        {
            TypeInfo attributeInstanceType = instanceType switch
            {
                TypeInfo.Class cls => new TypeInfo.Instance(cls),
                TypeInfo.InstantiatedGeneric
                    { GenericDefinition: TypeInfo.GenericClass } instantiated =>
                    new TypeInfo.Instance(instantiated),
                _ => instanceType,
            };
            expected = new TypeInfo.Intersection([
                expected,
                new TypeInfo.InstantiatedGeneric(intrinsicClassAttributes, [attributeInstanceType]),
            ]);
        }

        if (HasOverloadedJsxClassConstructor(instanceType) &&
            !AreJsxAttributesCompatible(expected, propsType, jsxNamespace, jsx))
        {
            ReportJsx(new TypeCheckException(
                "No overload matches this call.",
                jsx.Line,
                tsCode: "TS2769"));
            return;
        }

        CheckJsxAttributes(
            expected, propsType, jsx, jsxNamespace,
            forceAssignmentDiagnostic: usesLibraryManagedAttributes);
    }

    private TypeInfo? LookupJsxGenericBaseProps(TypeInfo type)
    {
        TypeInfo? current = type;
        for (int guard = 0; current is not null && guard < 128; guard++)
        {
            switch (current)
            {
                case TypeInfo.Class cls:
                    current = cls.Superclass;
                    break;
                case TypeInfo.MutableClass mutable:
                    if (mutable.FieldTypes.TryGetValue("props", out TypeInfo? mutableProps))
                        return mutableProps;
                    current = mutable.Superclass;
                    break;
                case TypeInfo.GenericClass generic:
                    current = generic.Superclass;
                    break;
                case TypeInfo.InstantiatedGeneric
                    { GenericDefinition: TypeInfo.GenericClass generic } instantiated:
                    if ((generic.Name.EndsWith("Component", StringComparison.Ordinal) ||
                         generic.Core.FieldTypes.ContainsKey("props")) &&
                        instantiated.TypeArguments.Count > 0)
                        return instantiated.TypeArguments[0];
                    current = generic.Superclass is null
                        ? null
                        : Substitute(generic.Superclass, GenericClassSubs(generic, instantiated.TypeArguments));
                    break;
                case TypeInfo.Instance instance:
                    current = instance.ResolvedClassType;
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    private TypeInfo ApplyJsxLibraryManagedAttributes(
        TypeInfo.Namespace? jsxNamespace, TypeInfo componentType, TypeInfo declaredProps)
    {
        if (jsxNamespace is null || !jsxNamespace.Types.ContainsKey("LibraryManagedAttributes"))
            return declaredProps;

        ClassMetadataCore? core = componentType switch
        {
            TypeInfo.Class cls => cls.Core,
            TypeInfo.MutableClass mutable => mutable.Frozen?.Core,
            TypeInfo.GenericClass generic => generic.Core,
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } => generic.Core,
            _ => null,
        };
        if (core is null)
            return declaredProps;

        Dictionary<string, TypeInfo> propTypeMembers = [];
        Dictionary<string, TypeInfo> defaultMembers = [];
        bool hasPropTypes = core.StaticProperties.TryGetValue("propTypes", out TypeInfo? propTypes) &&
            TryGetJsxMemberView(propTypes, out propTypeMembers, out _, out _);
        bool hasDefaults = core.StaticProperties.TryGetValue("defaultProps", out TypeInfo? defaults) &&
            TryGetJsxMemberView(defaults, out defaultMembers, out _, out _);
        if (!hasPropTypes && !hasDefaults)
            return declaredProps;

        var fields = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        var optional = new HashSet<string>(StringComparer.Ordinal);
        if (TryGetJsxMemberView(declaredProps, out var declaredMembers, out var declaredOptional, out _))
        {
            foreach (var (name, type) in declaredMembers)
                fields[name] = type;
            optional.UnionWith(declaredOptional);
        }

        if (hasPropTypes)
        {
            foreach (var (name, checker) in propTypeMembers)
            {
                if (!fields.ContainsKey(name) && TryGetJsxPropType(checker, out TypeInfo? checkedType))
                    fields[name] = checkedType;
                // PropTypes keys are present members even when their value type includes
                // null/undefined; only defaultProps changes presence to optional.
                optional.Remove(name);
            }
        }

        if (hasDefaults)
        {
            foreach (var (name, defaultType) in defaultMembers)
            {
                fields.TryAdd(name, defaultType);
                optional.Add(name);
            }
        }

        return new TypeInfo.Record(
            fields.ToFrozenDictionary(StringComparer.Ordinal),
            OptionalFields: optional.ToFrozenSet(StringComparer.Ordinal));
    }

    private static bool TryGetJsxPropType(TypeInfo checker, out TypeInfo checkedType)
    {
        if (checker is TypeInfo.InstantiatedGeneric
            {
                GenericDefinition: TypeInfo.GenericInterface { Name: "PropTypeChecker" },
                TypeArguments: { Count: > 0 } arguments
            })
        {
            checkedType = arguments[0];
            bool required = arguments.Count > 1 && arguments[1] is TypeInfo.BooleanLiteral { Value: true };
            if (required && checkedType is TypeInfo.Any)
            {
                // A forward ambient alias may remain structurally unresolved while its checker
                // wrapper is known. Required checkers still exclude null and undefined.
                checkedType = new TypeInfo.Union([
                    TypeInfo.String.Shared,
                    TypeInfo.Primitive.Number,
                    TypeInfo.Primitive.Boolean,
                    new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty),
                ]);
            }
            if (!required)
                checkedType = new TypeInfo.Union([checkedType, TypeInfo.Null.Shared, TypeInfo.Undefined.Shared]);
            return true;
        }
        checkedType = TypeInfo.Any.Shared;
        return false;
    }

    private bool HasOverloadedJsxClassConstructor(TypeInfo type)
    {
        TypeInfo? constructor = type switch
        {
            TypeInfo.Class cls => LookupJsxClassCoreMember(cls.Core, "constructor"),
            TypeInfo.GenericClass generic => LookupJsxClassCoreMember(generic.Core, "constructor"),
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
                LookupJsxClassCoreMember(generic.Core, "constructor") is { } open
                    ? Substitute(open, GenericClassSubs(generic, instantiated.TypeArguments))
                    : null,
            _ => null,
        };
        return constructor is TypeInfo.OverloadedFunction { Signatures.Count: > 1 } or
            TypeInfo.OverloadSet { Signatures.Count: > 1 };
    }

    private TypeInfo.Namespace? ResolveJsxNamespace(JsxCallInfo jsx)
    {
        if (jsx.FactoryName is { Length: > 0 })
        {
            string root = jsx.FactoryName.Split('.')[0];
            if (_environment.GetNamespace(root) is { } factoryNamespace)
            {
                if (FindNestedJsxNamespace(factoryNamespace) is { } local)
                    return local;
            }
        }
        return _environment.GetNamespace("JSX");
    }

    private static TypeInfo.Namespace? FindNestedJsxNamespace(TypeInfo.Namespace root)
    {
        HashSet<TypeInfo.Namespace> visited = new(ReferenceEqualityComparer.Instance);
        return Visit(root);

        TypeInfo.Namespace? Visit(TypeInfo.Namespace current)
        {
            if (!visited.Add(current))
                return null;
            if (current.Types.GetValueOrDefault("JSX") is TypeInfo.Namespace jsx)
                return jsx;

            foreach (TypeInfo.Namespace nested in current.Types.Values.OfType<TypeInfo.Namespace>())
            {
                if (Visit(nested) is { } found)
                    return found;
            }
            return null;
        }
    }

    private void CheckJsxSpreadOverwrites(JsxCallInfo jsx)
    {
        if (jsx.PropsExpr is not Expr.ObjectLiteral literal)
            return;

        for (int index = 0; index < literal.Properties.Count; index++)
        {
            Expr.Property property = literal.Properties[index];
            string? name = property.Key switch
            {
                Expr.IdentifierKey identifier => identifier.Name.Lexeme,
                Expr.LiteralKey { Literal.Literal: string text } => text,
                _ => null,
            };
            if (property.IsSpread || name is null)
                continue;

            for (int later = index + 1; later < literal.Properties.Count; later++)
            {
                Expr.Property spread = literal.Properties[later];
                if (!spread.IsSpread || (_typeMap.Get(spread.Value) ?? CheckExpr(spread.Value)) is not { } spreadType ||
                    !JsxSpreadDefinitelyContains(spreadType, name))
                    continue;

                ReportJsx(new TypeCheckException(
                    $"'{name}' is specified more than once, so this usage will be overwritten.",
                    JsxAttributeLine(jsx, name), tsCode: "TS2783"));
                break;
            }
        }
    }

    private bool HasUnconstrainedJsxSpread(JsxCallInfo jsx) =>
        jsx.PropsExpr is Expr.ObjectLiteral literal && literal.Properties.Any(property =>
            property.IsSpread &&
            (_typeMap.Get(property.Value) ?? CheckExpr(property.Value)) is TypeInfo.TypeParameter
                { Constraint: null or TypeInfo.Any });

    private bool HasJsxTypeParameterSpread(JsxCallInfo jsx) =>
        jsx.PropsExpr is Expr.ObjectLiteral literal && literal.Properties.Any(property =>
            property.IsSpread &&
            ContainsJsxTypeParameter(_typeMap.Get(property.Value) ?? CheckExpr(property.Value)));

    private static bool ContainsJsxTypeParameter(TypeInfo type) => type switch
    {
        TypeInfo.TypeParameter => true,
        TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(ContainsJsxTypeParameter),
        TypeInfo.SpreadType spread => ContainsJsxTypeParameter(spread.Inner),
        _ => false,
    };

    private bool HasAnyJsxSpread(JsxCallInfo jsx) =>
        jsx.PropsExpr is Expr.ObjectLiteral literal && literal.Properties.Any(property =>
            property.IsSpread &&
            (_typeMap.Get(property.Value) ?? CheckExpr(property.Value)) is TypeInfo.Any);

    private static bool IsDirectJsxAttribute(JsxCallInfo jsx, string name) =>
        jsx.PropsExpr is Expr.ObjectLiteral literal && literal.Properties.Any(property =>
            !property.IsSpread && (property.Key switch
            {
                Expr.IdentifierKey identifier => identifier.Name.Start >= 0 && identifier.Name.Lexeme == name,
                Expr.LiteralKey { Literal.Literal: string text } => text == name,
                _ => false,
            }));

    private static bool IsDirectOrChildJsxAttribute(JsxCallInfo jsx, string name) =>
        name == "children" && jsx.ChildExprs.Count > 0 || IsDirectJsxAttribute(jsx, name);

    private bool JsxSpreadDefinitelyContains(TypeInfo type, string name) => type switch
    {
        TypeInfo.Record record => record.Fields.ContainsKey(name) && !record.IsFieldOptional(name),
        TypeInfo.Interface iface => iface.GetAllMembers().Any(member => member.Key == name) &&
            !iface.GetAllOptionalMembers().Contains(name),
        TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(member =>
            JsxSpreadDefinitelyContains(member, name)),
        TypeInfo.TypeParameter { Constraint: { } constraint } =>
            JsxSpreadDefinitelyContains(constraint, name),
        TypeInfo.SpreadType spread => JsxSpreadDefinitelyContains(spread.Inner, name),
        _ => false,
    };

    private string? GetJsxAttributesPropertyName(TypeInfo.Namespace? jsxNamespace)
    {
        if (jsxNamespace?.Types.GetValueOrDefault("ElementAttributesProperty") is { } marker &&
            TryGetJsxMemberView(marker, out var markerMembers, out _, out _) && markerMembers.Count == 1)
            return markerMembers.Keys.First();
        return null;
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
            if (!IsJsxElementClassMemberCompatible(expected, actual))
            {
                return false;
            }
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
        TypeInfo.MutableClass mutable =>
            CollectPublicInstanceMembers(mutable.Freeze()).GetValueOrDefault(name),
        TypeInfo.GenericClass generic => CollectGenericClassMembers(
            generic, generic.TypeParams.Cast<TypeInfo>().ToList()).GetValueOrDefault(name),
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
            CollectGenericClassMembers(generic, instantiated.TypeArguments).GetValueOrDefault(name),
        TypeInfo.Instance instance => LookupJsxClassInstanceMember(instance.ResolvedClassType, name),
        _ => null,
    };

    private TypeInfo? LookupJsxClassCoreMember(ClassMetadataCore core, string name)
    {
        if (core.FieldTypes.TryGetValue(name, out TypeInfo? field)) return field;
        if (core.Getters.TryGetValue(name, out TypeInfo? getter)) return getter;
        if (core.Methods.TryGetValue(name, out TypeInfo? method)) return method;
        return LookupJsxClassTypeMember(core.Superclass, name);
    }

    private TypeInfo? LookupJsxClassTypeMember(TypeInfo? type, string name)
    {
        return type switch
        {
            TypeInfo.Class cls => LookupJsxClassCoreMember(cls.Core, name),
            TypeInfo.MutableClass mutable => LookupJsxClassCoreMember(mutable.Freeze().Core, name),
            TypeInfo.GenericClass generic => LookupJsxClassCoreMember(generic.Core, name),
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
                LookupJsxClassCoreMember(generic.Core, name) is { } member
                    ? Substitute(member, generic.TypeParams.Select((parameter, index) =>
                        (parameter.Name, Type: instantiated.TypeArguments[index]))
                        .ToDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal))
                    : null,
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
            CollectJsxCandidates(constituent, propsType, jsxNamespace, candidates);
        if (candidates.Count == 0)
        {
            ReportJsx(new TypeCheckException("No union component signatures are available.", jsx.Line, tsCode: "TS2604"));
            return;
        }

        // TypeScript's synthesized JSX signature for a union is anchored to its first
        // constituent. This preserves the first parameter's member diagnostics while allowing a
        // later zero-argument signature to coexist (the tsxUnionElementType* suite pins this).
        CheckJsxAttributes(candidates[0].Props, propsType, jsx, jsxNamespace);
        foreach ((_, TypeInfo? returnType) in candidates)
            if (returnType is not null) CheckJsxComponentReturnType(jsx, jsxNamespace, returnType);
    }

    private void CollectJsxCandidates(
        TypeInfo type, TypeInfo propsType, TypeInfo.Namespace? jsxNamespace,
        List<(TypeInfo Props, TypeInfo? Return)> candidates)
    {
        switch (type)
        {
            case TypeInfo.Function function:
                candidates.Add((JsxFirstParameter(function.ParamTypes), function.ReturnType));
                break;
            case TypeInfo.GenericFunction generic:
                var instantiated = (TypeInfo.Function)InstantiateGenericFunction(
                    generic, InferTypeArguments(generic, [propsType], fallbackToConstraints: true,
                        combineCandidates: true));
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
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } instantiatedInterface
                when FlattenInstantiatedInterface(instantiatedInterface) is { } interfaceView:
                if (interfaceView.CallSignatures is { Count: > 0 } interfaceCalls)
                    CollectCallSignatureCandidates(interfaceCalls, propsType, candidates);
                if (interfaceView.ConstructorSignatures is { Count: > 0 } interfaceConstructors)
                    candidates.AddRange(interfaceConstructors.Select(signature =>
                        (JsxFirstParameter(signature.ParamTypes), (TypeInfo?)null)));
                break;
            case TypeInfo.Class or TypeInfo.GenericClass:
            case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass }:
            {
                string? propertyName = GetJsxAttributesPropertyName(jsxNamespace);
                TypeInfo expected = propertyName is null
                    ? new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty)
                    : LookupJsxClassInstanceMember(type, propertyName) ?? TypeInfo.Any.Shared;
                candidates.Add((expected, null));
                break;
            }
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
            var instantiated = (TypeInfo.Function)InstantiateGenericFunction(
                generic, InferTypeArguments(generic, [propsType], fallbackToConstraints: true,
                    combineCandidates: true));
            candidates.Add((JsxFirstParameter(instantiated.ParamTypes), instantiated.ReturnType));
        }
    }

    private void CheckJsxMixedSignatures(
        JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace, IEnumerable<TypeInfo> signatures, TypeInfo propsType)
    {
        var candidates = new List<(TypeInfo Props, TypeInfo? Return)>();
        foreach (TypeInfo signature in signatures)
            CollectJsxCandidates(signature, propsType, jsxNamespace, candidates);
        var match = candidates.FirstOrDefault(candidate => AreJsxAttributesCompatible(candidate.Props, propsType, jsxNamespace, jsx));
        if (match == default)
        {
            ReportJsx(new TypeCheckException("No overload matches this call.", jsx.Line, tsCode: "TS2769"));
            return;
        }
        CheckJsxAttributes(match.Props, propsType, jsx, jsxNamespace);
        if (match.Return is not null) CheckJsxComponentReturnType(jsx, jsxNamespace, match.Return);
    }

    private bool AreJsxAttributesCompatible(
        TypeInfo expected, TypeInfo actual, TypeInfo.Namespace? jsxNamespace, JsxCallInfo? jsx = null)
    {
        if (jsx is not null && HasAnyJsxSpread(jsx)) return true;
        if (expected is TypeInfo.Union union)
            return union.FlattenedTypes.Any(member =>
                AreJsxAttributesCompatible(member, actual, jsxNamespace, jsx));
        if (expected is TypeInfo.Any or TypeInfo.Unknown ||
            expected is TypeInfo.Intersection anyIntersection &&
                anyIntersection.FlattenedTypes.Any(member => member is TypeInfo.Any) ||
            actual is not TypeInfo.Record actualRecord)
            return true;
        if (!TryGetJsxMemberView(expected, out var members, out var optional, out TypeInfo? stringIndex))
            return false;
        TypeInfo? intrinsic = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");
        var consideredFields = actualRecord.Fields
            .Where(field => IsCheckedJsxAttribute(field.Key))
            .ToList();
        if (members.Count == 0 && intrinsic is not null && consideredFields.Count > 0 &&
            jsx is not null && consideredFields.All(field => !IsDirectOrChildJsxAttribute(jsx, field.Key)))
            return false;
        foreach (string required in members.Keys.Where(name => !optional.Contains(name)))
            if (required is not ("key" or "toString") && !actualRecord.Fields.ContainsKey(required) &&
                actualRecord.StringIndexType is not TypeInfo.Any) return false;
        foreach ((string name, TypeInfo value) in actualRecord.Fields)
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
            if (member is null)
            {
                if (jsx is not null && !IsDirectOrChildJsxAttribute(jsx, name)) continue;
                return false;
            }
            if (!IsJsxAttributeValueCompatible(member, value)) return false;
        }
        return true;
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
            if (member is null || !IsJsxAttributeValueCompatible(member, value))
                return false;
        }
        return true;
    }

    private bool IsJsxComponentResult(TypeInfo elementType, TypeInfo returnType)
    {
        if (returnType is TypeInfo.Undefined or TypeInfo.Void)
            return false;
        if (IsCompatible(elementType, returnType) ||
            IsJsxElementClassMemberCompatible(elementType, returnType))
            return true;
        return returnType switch
        {
            TypeInfo.Null or TypeInfo.String or TypeInfo.StringLiteral or
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
        TypeInfo expected, TypeInfo actual, JsxCallInfo jsx, TypeInfo.Namespace? jsxNamespace,
        bool forceAssignmentDiagnostic = false)
    {
        if (HasAnyJsxSpread(jsx))
            return;
        if (expected is TypeInfo.Any or TypeInfo.Unknown ||
            expected is TypeInfo.Intersection anyIntersection &&
                anyIntersection.FlattenedTypes.Any(member => member is TypeInfo.Any))
            return;
        if (actual is not TypeInfo.Record actualRecord)
            return;

        TypeInfo? intrinsicAttributes = jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes");
        if (jsx.Kind == JsxElementKind.Component && intrinsicAttributes is not null &&
            TryGetJsxMemberView(intrinsicAttributes, out var intrinsicMembers, out var intrinsicOptional, out _))
        {
            List<string> missingIntrinsic = intrinsicMembers.Keys
                .Where(name => !intrinsicOptional.Contains(name) && !actualRecord.Fields.ContainsKey(name))
                .ToList();
            if (missingIntrinsic.Count > 0)
            {
                ReportJsx(new TypeCheckException(
                    missingIntrinsic.Count == 1
                        ? $"Property '{missingIntrinsic[0]}' is missing in type '{actual}' but required in type '{intrinsicAttributes}'."
                        : $"Type '{actual}' is missing the following properties from type '{intrinsicAttributes}': {string.Join(", ", missingIntrinsic)}",
                    jsx.Line, tsCode: missingIntrinsic.Count == 1 ? "TS2741" : "TS2739"));
                return;
            }
        }

        // Intersections distribute over union props for JSX assignment just as they do for
        // ordinary object assignment: `(Canadian | American) & Children` accepts either
        // complete address shape. Select the compatible branch before flattening members.
        IReadOnlyList<TypeInfo> alternatives = JsxAttributeAlternatives(expected);
        bool selectedAlternative = false;
        if (alternatives.Count > 1)
        {
            TypeInfo? matching = alternatives.FirstOrDefault(candidate =>
                AreJsxAttributesCompatible(candidate, actual, jsxNamespace, jsx));
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
            else if (expected is TypeInfo.Union)
            {
                ReportJsx(new TypeCheckException(
                    $"Type '{actual}' is not assignable to type '{expected}'.",
                    jsx.Line, tsCode: "TS2322"));
                return;
            }
        }
        if (!TryGetJsxMemberView(expected, out var expectedMembers, out var expectedOptional,
                out TypeInfo? stringIndexType))
        {
            ReportJsx(new TypeCheckException(
                $"Type '{actual}' is not assignable to type '{expected}'.",
                jsx.Line, tsCode: "TS2322"));
            return;
        }

        actualRecord = ContextualizeJsxAttributeCallbacks(
            jsx, expectedMembers, stringIndexType, actualRecord);

        string childrenAttributeName = "children";
        Dictionary<string, TypeInfo> childrenMembers = [];
        bool hasChildrenMarker = jsx.ChildExprs.Count > 0 &&
            jsxNamespace?.Types.GetValueOrDefault("ElementChildrenAttribute") is { } childrenMarker &&
            TryGetJsxMemberView(childrenMarker, out childrenMembers, out _, out _) &&
            childrenMembers.Count > 0;
        if (hasChildrenMarker)
        {
            childrenAttributeName = childrenMembers.Keys.First();
        }
        if (expectedMembers.TryGetValue(childrenAttributeName, out TypeInfo? expectedChildren))
        {
            actualRecord = ContextualizeJsxChildCallbacks(
                jsx, childrenAttributeName, expectedChildren, actualRecord);
        }

        HashSet<string> directAttributes = jsx.PropsExpr is Expr.ObjectLiteral literal
            ? literal.Properties
                .Where(property => !property.IsSpread && property.Key is Expr.IdentifierKey)
                .Select(property => ((Expr.IdentifierKey)property.Key!).Name.Lexeme)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        if (jsx.ChildExprs.Count > 0)
        {
            // Without ElementChildrenAttribute, children are not an excess attribute on
            // an empty props bag. Still validate them when the props type itself explicitly
            // declares the conventional `children` member.
            if ((hasChildrenMarker && jsx.Kind == JsxElementKind.Component) ||
                expectedMembers.ContainsKey(childrenAttributeName))
                directAttributes.Add(childrenAttributeName);
        }

        // Weak-type failure is an assignment diagnostic for a spread source. Direct JSX
        // attributes instead receive the JSX TS2322 excess-property diagnostic.
        bool checksChildren = hasChildrenMarker || expectedMembers.ContainsKey(childrenAttributeName) ||
            IsDirectJsxAttribute(jsx, childrenAttributeName);
        var consideredFields = actualRecord.Fields
            .Where(field =>
                (field.Key != childrenAttributeName || checksChildren) &&
                (IsCheckedJsxAttribute(field.Key) || expectedMembers.ContainsKey(field.Key)))
            .ToFrozenDictionary(StringComparer.Ordinal);
        if (consideredFields.Count > 0 &&
            !consideredFields.ContainsKey(childrenAttributeName) &&
            consideredFields.Keys.All(name => !directAttributes.Contains(name)) &&
            consideredFields.Keys.All(name => !expectedMembers.ContainsKey(name)) &&
            jsxNamespace?.Types.GetValueOrDefault("IntrinsicAttributes") is not null)
        {
            bool childrenMismatch = consideredFields.ContainsKey("children");
            ReportJsx(new TypeCheckException(
                childrenMismatch
                    ? $"Type '{actual}' is not assignable to type '{expected}'."
                    : $"Type '{actual}' has no properties in common with type 'JSX.IntrinsicAttributes'.",
                jsx.Line, tsCode: childrenMismatch ? "TS2322" : "TS2559"));
            return;
        }
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
        bool hasKnownTypeMismatch = actualRecord.Fields.Any(field =>
            expectedMembers.TryGetValue(field.Key, out TypeInfo? expectedMember)
                ? !IsJsxAttributeValueCompatible(expectedMember, field.Value)
                : IsCheckedJsxAttribute(field.Key) && IsDirectJsxAttribute(jsx, field.Key));
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
            if (!actualRecord.Fields.ContainsKey(required) && actualRecord.StringIndexType is not TypeInfo.Any)
                missing.Add(required);
        }
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
        bool useAssignmentDiagnostic = genericSpreadSource || selectedAlternative || forceAssignmentDiagnostic;
        bool hasDirectTypeMismatch = hasKnownTypeMismatch &&
            directAttributes.Any(name =>
                expectedMembers.TryGetValue(name, out TypeInfo? member) &&
                actualRecord.Fields.TryGetValue(name, out TypeInfo? value) &&
                !IsJsxAttributeValueCompatible(member, value));
        if (!hasDirectExcess && !hasDirectTypeMismatch && missing.Count == 1)
        {
            ReportJsx(new TypeCheckException(
                useAssignmentDiagnostic
                    ? $"Type '{actual}' is not assignable to type '{expected}'. Property '{missing[0]}' is missing."
                    : $"Property '{missing[0]}' is missing in type '{actual}' but required in type '{expected}'.",
                jsx.Line, tsCode: useAssignmentDiagnostic ? "TS2322" : "TS2741"));
            return;
        }
        else if (!hasDirectExcess && !hasDirectTypeMismatch && missing.Count > 1)
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
            {
                ReportImplicitAnyJsxAttributeCallback(jsx, name);
                continue;
            }

            TypeInfo? memberType = expectedMembers.TryGetValue(name, out var direct)
                ? direct
                : stringIndexType;
            if (memberType is null && intrinsicAttributes is not null)
                memberType = LookupJsxObjectMember(intrinsicAttributes, name);

            if (memberType is null)
            {
                // Excess properties that originate only in a spread are allowed. A directly
                // written JSX attribute remains subject to the ordinary excess check.
                if (!directAttributes.Contains(name) || HasJsxTypeParameterSpread(jsx))
                    continue;
                ReportImplicitAnyJsxAttributeCallback(jsx, name);
                ReportJsx(new TypeCheckException(
                    $"Type '{actual}' is not assignable to type '{expected}'. " +
                    $"Property '{name}' does not exist on type '{expected}'.",
                    JsxAttributeLine(jsx, name), tsCode: "TS2322"));
                continue;
            }

            if (name == childrenAttributeName &&
                TryReportJsxChildMismatches(jsx, memberType, valueType))
                continue;

            bool compatible = name == childrenAttributeName
                ? IsJsxChildValueCompatible(memberType, valueType)
                : IsJsxAttributeValueCompatible(memberType, valueType);
            if (!compatible)
            {
                if (name == childrenAttributeName && jsx.ChildExprs.Count > 1)
                {
                    TypeInfo? repeatedChildType = JsxRepeatedChildElementType(memberType);
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
                    JsxAttributeLine(jsx, name),
                    tsCode: name == childrenAttributeName
                        ? JsxChildMismatchDiagnosticCode(memberType, valueType)
                        : "TS2322"));
            }
        }

        // Silence the "declared but unused" analysis shape: optional members are only
        // consulted through RequiredMemberNames today; the set is kept for parity with
        // future exact-optional handling.
        _ = expectedOptional;
    }

    private bool IsJsxAttributeValueCompatible(TypeInfo expected, TypeInfo actual)
    {
        if (IsCompatible(expected, actual) || IsJsxElementClassMemberCompatible(expected, actual))
            return true;
        return expected is TypeInfo.Union union && union.FlattenedTypes.Any(member =>
            IsCompatible(member, actual) ||
            IsJsxElementClassMemberCompatible(member, actual) ||
            IsJsxAnyReturningCallbackCompatible(member, actual));
    }

    private bool IsJsxAnyReturningCallbackCompatible(TypeInfo expected, TypeInfo actual)
    {
        if (expected is not TypeInfo.Function
            {
                ReturnType: TypeInfo.Any or TypeInfo.Void,
                ParamTypes: { } expectedParameters
            } ||
            actual is not TypeInfo.Function { ParamTypes: { } actualParameters } ||
            expectedParameters.Count != actualParameters.Count)
            return false;

        return expectedParameters.Zip(actualParameters).All(pair =>
            IsCompatible(pair.First, pair.Second) ||
            IsCompatible(pair.Second, pair.First) ||
            IsJsxElementClassMemberCompatible(pair.First, pair.Second) ||
            pair is { First: TypeInfo.Interface expectedInterface, Second: TypeInfo.Interface actualInterface } &&
            expectedInterface.Name == actualInterface.Name);
    }

    private bool TryReportJsxChildMismatches(
        JsxCallInfo jsx, TypeInfo expectedChildren, TypeInfo actualChildren)
    {
        // When several JSX children are checked against an array-valued children prop, tsc
        // reports a diagnostic at each incompatible child. The lowered props record collapses
        // those children into one array/union, so recover the individual types from the type map
        // instead of emitting a single aggregate error and losing duplicate locations.
        TypeInfo? expectedElement = JsxRepeatedChildElementType(expectedChildren);
        if (jsx.ChildExprs.Count <= 1 || expectedElement is null ||
            actualChildren is not (TypeInfo.Array or TypeInfo.Tuple) ||
            jsx.ChildExprs.Any(child => child is Expr.Spread))
            return false;

        var mismatches = jsx.ChildExprs
            .Select((child, index) => (Child: child, Index: index, Type: _typeMap.Get(child)))
            .Where(pair => pair.Type is not null &&
                !IsCompatible(expectedElement, pair.Type))
            .ToList();
        if (mismatches.Count == 0)
            return false;

        foreach ((Expr child, int index, TypeInfo? childType) in mismatches)
        {
            int line = jsx.ChildLines is { } lines && index < lines.Count
                ? lines[index]
                : TryGetExprLine(child) ?? jsx.Line;
            if (childType is TypeInfo.StringLiteral)
            {
                ReportJsx(new TypeCheckException(
                    $"'{jsx.TagName}' components don't accept text as child elements. Text in JSX has the type 'string', but the expected type of 'children' is '{expectedChildren}'.",
                    line,
                    tsCode: "TS2747"));
                continue;
            }

            ReportJsx(new TypeCheckException(
                $"Type '{childType}' is not assignable to type '{expectedElement}'.",
                line,
                tsCode: JsxChildMismatchDiagnosticCode(expectedElement, childType!)));
        }
        return true;
    }

    private TypeInfo? JsxRepeatedChildElementType(TypeInfo type) => type switch
    {
        TypeInfo.Array array => array.ElementType,
        TypeInfo.Interface { NumberIndexType: { } element } => element,
        TypeInfo.Record { NumberIndexType: { } element } => element,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } instantiated
            when FlattenInstantiatedInterface(instantiated) is { } flattened =>
                JsxRepeatedChildElementType(flattened),
        TypeInfo.Union union => union.FlattenedTypes
            .Select(JsxRepeatedChildElementType)
            .FirstOrDefault(element => element is not null),
        TypeInfo.Intersection intersection => intersection.FlattenedTypes
            .Where(member => member is not (TypeInfo.Function or TypeInfo.GenericFunction))
            .Select(JsxRepeatedChildElementType)
            .FirstOrDefault(element => element is not null),
        _ => null,
    };

    private bool JsxMissingRequiredMember(TypeInfo expected, TypeInfo actual) =>
        (expected, actual) switch
        {
            (TypeInfo.Array expectedArray, TypeInfo.Array actualArray) =>
                JsxMissingRequiredMember(expectedArray.ElementType, actualArray.ElementType),
            (TypeInfo.Tuple expectedTuple, TypeInfo.Tuple actualTuple)
                when expectedTuple.Elements.Count == actualTuple.Elements.Count =>
                expectedTuple.Elements.Zip(actualTuple.Elements)
                    .Any(pair => JsxMissingRequiredMember(pair.First.Type, pair.Second.Type)),
            (TypeInfo.Union expectedUnion, _) => expectedUnion.FlattenedTypes
                .All(member => JsxMissingRequiredMember(member, actual)),
            (_, TypeInfo.Union actualUnion) => actualUnion.FlattenedTypes
                .Any(member => JsxMissingRequiredMember(expected, member)),
            _ => MissingRequiredMember(expected, actual),
        };

    private bool IsJsxChildValueCompatible(TypeInfo expected, TypeInfo actual)
    {
        bool expectedIsClassInstance = expected is TypeInfo.Instance or TypeInfo.Class or
            TypeInfo.MutableClass or TypeInfo.GenericClass or
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass };
        if (expectedIsClassInstance && actual is TypeInfo.Class or TypeInfo.MutableClass or
            TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass })
            return false;
        if (expected is TypeInfo.Class or TypeInfo.InstantiatedGeneric
            { GenericDefinition: TypeInfo.GenericClass })
            expected = new TypeInfo.Instance(expected);
        return IsJsxAttributeValueCompatible(expected, actual);
    }

    private string JsxChildMismatchDiagnosticCode(TypeInfo expected, TypeInfo actual)
    {
        Dictionary<string, TypeInfo>? expectedMembers = expected switch
        {
            TypeInfo.Class cls => CollectPublicInstanceMembers(cls),
            TypeInfo.InstantiatedGeneric
                { GenericDefinition: TypeInfo.GenericClass generic } instantiated =>
                CollectGenericClassMembers(generic, instantiated.TypeArguments),
            TypeInfo.Instance { ResolvedClassType: TypeInfo.Class cls } => CollectPublicInstanceMembers(cls),
            TypeInfo.Instance
                { ResolvedClassType: TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } instantiated } =>
                CollectGenericClassMembers(generic, instantiated.TypeArguments),
            _ => null,
        };
        if (expectedMembers is not null)
        {
            IReadOnlyDictionary<string, TypeInfo> actualMembers = actual switch
            {
                TypeInfo.Instance { ResolvedClassType: TypeInfo.Class cls } => CollectPublicInstanceMembers(cls),
                TypeInfo.Interface iface => iface.GetAllMembers().ToDictionary(pair => pair.Key, pair => pair.Value),
                TypeInfo.Record record => record.Fields,
                _ => FrozenDictionary<string, TypeInfo>.Empty,
            };
            int missing = expectedMembers.Keys.Count(name => !actualMembers.ContainsKey(name));
            if (missing >= 4) return "TS2740";
            if (missing >= 2) return "TS2739";
            if (missing == 1) return "TS2741";
        }
        return JsxMissingRequiredMember(expected, actual) ? "TS2741" : "TS2322";
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

                        if (!partOptional.Contains(name) &&
                            !(name == "children" && IsImplicitReactChildrenType(value)))
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
            case TypeInfo.MappedType mapped:
                return TryGetJsxMemberView(
                    ExpandMappedType(mapped), out members, out optionalMembers, out stringIndexType);
            default:
                return false;
        }
    }

    /// <summary>Member lookup on an object-like type by name, falling back to its string index.</summary>
    private TypeInfo? LookupJsxObjectMember(TypeInfo container, string name)
    {
        if (!TryGetJsxMemberView(container, out var members, out _, out TypeInfo? stringIndex))
            return null;
        return members.TryGetValue(name, out TypeInfo? member) ? member : stringIndex;
    }

    private void ReportImplicitAnyJsxAttributeCallback(JsxCallInfo jsx, string name)
    {
        if (!_noImplicitAny || jsx.PropsExpr is not Expr.ObjectLiteral literal)
            return;
        Expr.ArrowFunction? callback = literal.Properties.FirstOrDefault(property =>
            !property.IsSpread && IsDirectJsxAttribute(
                jsx with { PropsExpr = new Expr.ObjectLiteral([property]) }, name))?.Value
            as Expr.ArrowFunction;
        if (callback is null) return;
        foreach (Stmt.Parameter parameter in callback.Parameters.Where(parameter => parameter.Type is null))
            ReportJsx(new TypeCheckException(
                $"Parameter '{parameter.Name.Lexeme}' implicitly has an 'any' type.",
                parameter.Name.Line, tsCode: "TS7006"));
    }

    private static bool IsImplicitReactChildrenType(TypeInfo type) =>
        type is TypeInfo.Union union &&
        union.FlattenedTypes.Any(member =>
            member is TypeInfo.InstantiatedGeneric
                { GenericDefinition: TypeInfo.GenericInterface { Name: "ReactElement" } }) &&
        union.FlattenedTypes.Any(member =>
            member is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN });

    private TypeInfo.Record ContextualizeJsxAttributeCallbacks(
        JsxCallInfo jsx,
        IReadOnlyDictionary<string, TypeInfo> expectedMembers,
        TypeInfo? stringIndexType,
        TypeInfo.Record actual)
    {
        if (jsx.PropsExpr is not Expr.ObjectLiteral literal)
            return actual;

        Dictionary<string, TypeInfo>? fields = null;
        foreach ((string name, Expr.ArrowFunction arrow, int line) in
                 EnumerateJsxAttributeCallbacks(literal))
        {
            TypeInfo? expected = expectedMembers.GetValueOrDefault(name) ?? stringIndexType;
            TypeInfo? contextualFunction = FindJsxContextualFunction(expected);
            if (contextualFunction is null) continue;

            try
            {
                TypeInfo callback = CheckArrowFunction(arrow, contextualFunction);
                (fields ??= actual.Fields.ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))[name] = callback;
                _typeMap.Set(arrow, callback);
            }
            catch (TypeCheckException ex)
            {
                ReportJsx(WithJsxLine(ex, line));
            }
        }

        return fields is null
            ? actual
            : new TypeInfo.Record(
                fields.ToFrozenDictionary(StringComparer.Ordinal),
                actual.StringIndexType,
                actual.NumberIndexType,
                actual.SymbolIndexType,
                actual.OptionalFields,
                actual.IsReadonly,
                actual.GetterOnlyFields,
                actual.CallSignatures,
                actual.ConstructorSignatures,
                actual.MethodMembers);
    }

    private static IEnumerable<(string Name, Expr.ArrowFunction Arrow, int Line)>
        EnumerateJsxAttributeCallbacks(Expr.ObjectLiteral literal)
    {
        foreach (Expr.Property property in literal.Properties)
        {
            if (property.IsSpread && property.Value is Expr.ObjectLiteral spreadLiteral)
            {
                foreach (var callback in EnumerateJsxAttributeCallbacks(spreadLiteral))
                    yield return callback;
                continue;
            }

            if (!property.IsSpread && property.Key is Expr.IdentifierKey key &&
                property.Value is Expr.ArrowFunction arrow)
            {
                yield return (key.Name.Lexeme, arrow, key.Name.Line);
            }
        }
    }

    private static TypeInfo? FindJsxContextualFunction(TypeInfo? type) => type switch
    {
        TypeInfo.Function or TypeInfo.GenericFunction => type,
        TypeInfo.Union union => union.FlattenedTypes
            .Select(FindJsxContextualFunction)
            .FirstOrDefault(candidate => candidate is not null),
        TypeInfo.Intersection intersection => intersection.FlattenedTypes
            .Select(FindJsxContextualFunction)
            .FirstOrDefault(candidate => candidate is not null),
        _ => null,
    };

    private TypeInfo.Record ContextualizeJsxChildCallbacks(
        JsxCallInfo jsx,
        string childrenName,
        TypeInfo expectedChildren,
        TypeInfo.Record actual)
    {
        TypeInfo? contextualFunction = FindJsxContextualFunction(expectedChildren);
        if (contextualFunction is null || jsx.ChildExprs.Count == 0)
            return actual;

        var childTypes = new List<TypeInfo>(jsx.ChildExprs.Count);
        bool changed = false;
        foreach (Expr child in jsx.ChildExprs)
        {
            TypeInfo childType = _typeMap.Get(child) ?? TypeInfo.Any.Shared;
            if (child is Expr.ArrowFunction arrow)
            {
                try
                {
                    childType = CheckArrowFunction(
                        arrow, contextualFunction, useContextualReturnType: false);
                    _typeMap.Set(arrow, childType);
                    changed = true;
                }
                catch (TypeCheckException ex)
                {
                    ReportJsx(WithJsxLine(ex, TryGetExprLine(child) ?? jsx.Line));
                }
            }
            childTypes.Add(childType);
        }

        if (!changed) return actual;
        TypeInfo value = childTypes.Count == 1
            ? childTypes[0]
            : new TypeInfo.Tuple(
                jsx.ChildExprs.Select((child, index) => new TypeInfo.TupleElement(
                    childTypes[index],
                    child is Expr.Spread ? TupleElementKind.Spread : TupleElementKind.Required)).ToList(),
                jsx.ChildExprs.Count(child => child is not Expr.Spread));
        var fields = actual.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        fields[childrenName] = value;
        return actual with { Fields = fields.ToFrozenDictionary(StringComparer.Ordinal) };
    }

    private bool TryInstantiateJsxGenericFunction(
        JsxCallInfo jsx,
        TypeInfo.GenericFunction generic,
        TypeInfo propsType,
        out TypeInfo.Function instantiated)
    {
        instantiated = null!;
        List<TypeInfo> typeArguments;
        if (jsx.TypeArgumentCount > 0)
        {
            if (!TryResolveExplicitJsxTypeArguments(jsx, generic.TypeParams, out typeArguments))
                return false;
        }
        else
        {
            List<TypeInfo> inferenceArguments =
                propsType is TypeInfo.Record { Fields.Count: 0 } ? [] : [propsType];
            typeArguments = InferTypeArguments(
                generic, inferenceArguments, fallbackToConstraints: true, combineCandidates: true);
        }

        try
        {
            instantiated = (TypeInfo.Function)InstantiateGenericFunction(generic, typeArguments);
            return true;
        }
        catch (TypeCheckException ex)
        {
            ReportJsx(WithJsxLine(ex, jsx.Line));
            return false;
        }
    }

    private bool TryResolveExplicitJsxTypeArguments(
        JsxCallInfo jsx,
        IReadOnlyList<TypeInfo.TypeParameter> typeParameters,
        out List<TypeInfo> typeArguments)
    {
        typeArguments = [];
        int required = typeParameters.TakeWhile(parameter => parameter.Default is null).Count();
        if (jsx.TypeArgumentCount < required || jsx.TypeArgumentCount > typeParameters.Count)
        {
            string expected = required == typeParameters.Count
                ? required.ToString()
                : $"{required}-{typeParameters.Count}";
            ReportJsx(new TypeCheckException(
                $"Expected {expected} type arguments, but got {jsx.TypeArgumentCount}.",
                jsx.Line, tsCode: "TS2558"));
            return false;
        }

        if (jsx.TypeArguments is null)
        {
            typeArguments.AddRange(Enumerable.Repeat(TypeInfo.Any.Shared, jsx.TypeArgumentCount));
            return true;
        }

        for (int i = 0; i < jsx.TypeArguments.Count; i++)
            typeArguments.Add(ResolveTypeArg(jsx.TypeArguments, jsx.TypeArgumentNodes, i));
        return true;
    }

    private bool IsJsxElementClassMemberCompatible(TypeInfo expected, TypeInfo actual)
    {
        if (IsCompatible(expected, actual) ||
            string.Equals(expected.CacheKey(), actual.CacheKey(), StringComparison.Ordinal))
            return true;

        if (!TryGetJsxMemberView(expected, out var expectedMembers, out var expectedOptional, out _) ||
            !TryGetJsxMemberView(actual, out var actualMembers, out _, out _))
            return false;

        foreach ((string name, TypeInfo expectedMember) in expectedMembers)
        {
            if (!actualMembers.TryGetValue(name, out TypeInfo? actualMember))
            {
                if (expectedOptional.Contains(name)) continue;
                return false;
            }
            if (!IsJsxElementClassMemberCompatible(expectedMember, actualMember))
                return false;
        }
        return true;
    }

    private static TypeCheckException WithJsxLine(TypeCheckException exception, int line) =>
        exception.Diagnostic.Location is null
            ? new TypeCheckException(
                exception.Diagnostic.Message,
                line,
                tsCode: exception.Diagnostic.TsCode)
            : exception;

    /// <summary>Line of the written attribute (via the props object literal), else the element line.</summary>
    private static int JsxAttributeLine(JsxCallInfo jsx, string attributeName)
    {
        if (attributeName == "children" && jsx.ChildLines is { Count: > 0 } childLines)
            return childLines[0];
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
