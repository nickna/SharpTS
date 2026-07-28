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

            case TypeInfo.GenericFunction:
                // Best effort only: without full JSX generic inference, reporting attribute
                // mismatches here risks false positives. Deferred to a follow-up.
                return;

            case TypeInfo.OverloadedFunction overloaded:
            {
                foreach (var signature in overloaded.Signatures)
                {
                    TypeInfo expected = signature.ParamTypes.Count > 0
                        ? signature.ParamTypes[0]
                        : new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
                    if (IsCompatible(expected, propsType))
                        return;
                }
                ReportJsx(new TypeCheckException(
                    "No overload matches this call.", jsx.Line, tsCode: "TS2769"));
                return;
            }

            // Class components (basic scope): constructable, so the tag itself is valid.
            // ElementClass/ElementAttributesProperty-driven props checking is deferred.
            case TypeInfo.Class or TypeInfo.MutableClass or TypeInfo.GenericClass
                or TypeInfo.InstantiatedGeneric:
                return;

            // Callable object types ({ (props): Element }) are usable as components.
            case TypeInfo.Record { CallSignatures.Count: > 0 }:
            case TypeInfo.Interface { CallSignatures.Count: > 0 }:
                return;

            case TypeInfo.Union union:
                // Constituent-by-constituent fidelity is deferred; only flag the clearly
                // uncallable case where no constituent could ever render.
                if (union.FlattenedTypes.Any(IsJsxRenderableTagType))
                    return;
                break;
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
        // Async/lazy component escape hatches: leave promise-shaped returns alone.
        if (returnType is TypeInfo.Promise)
            return;
        if (!IsCompatible(elementType, returnType))
        {
            ReportJsx(new TypeCheckException(
                $"'{jsx.TagName}' cannot be used as a JSX component. " +
                $"Its return type '{returnType}' is not a valid JSX element.",
                jsx.Line, tsCode: "TS2786"));
        }
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

        // Missing required props. `children` is satisfied by child elements (arity fidelity
        // deferred); key/ref never participate.
        var missing = new List<string>();
        foreach (var required in RequiredMemberNames(expected))
        {
            if (required is "children" or "key" or "ref")
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
    /// Whether a written attribute participates in props checking: key/ref/children never do,
    /// and tsc exempts hyphenated/namespaced names — this is what keeps data-*/aria-* legal
    /// against props types with no index signature.
    /// </summary>
    private static bool IsCheckedJsxAttribute(string name) =>
        name is not ("key" or "ref" or "children") && !name.Contains('-') && !name.Contains(':');

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
