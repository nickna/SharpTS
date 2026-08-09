using System.Text;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static methods on the JSON namespace (JSON.parse, JSON.stringify)
/// </summary>
public static class JSONBuiltIns
{
    // ECMA-262 25.5: JSON.parse / JSON.stringify are single built-in function
    // objects, so repeated access must return the SAME callable (identity
    // stability: `JSON.stringify === JSON.stringify`). Build the methods once
    // into a cached lookup — mirroring MathBuiltIns — rather than synthesizing
    // a fresh BuiltInMethod per access.
    private static readonly BuiltInStaticMemberLookup _lookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("parse", 0, 2, 2, ParseJson)
            .MethodV2("stringify", 1, 3, 3, StringifyJson)
            .MethodV2("rawJSON", 0, int.MaxValue, 1, RawJson)
            .MethodV2("isRawJSON", 0, int.MaxValue, 1, IsRawJson)
            .Build();

    public static object? GetStaticMethod(string name) => _lookup.GetMember(name);

    /// <summary>Member names for REPL autocomplete.</summary>
    public static IEnumerable<string> MemberNames => _lookup.MemberNames;

    private static RuntimeValue RawJson(
        Interpreter interpreter,
        RuntimeValue _,
        ReadOnlySpan<RuntimeValue> args)
    {
        object? input = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        string text = interpreter.ToStringForBuiltInArgument(input);
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
            throw new ThrowException(new SharpTSSyntaxError("Invalid raw JSON text"));

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                throw new ThrowException(new SharpTSSyntaxError(
                    "Raw JSON text must be a primitive value"));
        }
        catch (JsonException ex)
        {
            throw new ThrowException(new SharpTSSyntaxError(
                $"Invalid raw JSON text: {ex.Message}"));
        }

        return RuntimeValue.FromObject(new SharpTSRawJson(text));
    }

    private static RuntimeValue IsRawJson(
        Interpreter _,
        RuntimeValue __,
        ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoolean(
            args.Length > 0 && args[0].ToObject() is SharpTSRawJson);

    private static RuntimeValue ParseJson(Interpreter interp, RuntimeValue _, ReadOnlySpan<RuntimeValue> args)
    {
        object? input = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        var text = interp.ToStringForBuiltInArgument(input);
        var reviver = args.Length > 1 ? args[1].ToObject() as ISharpTSCallable : null;

        object? parsed;
        try
        {
            parsed = RuntimeJson.Parse(text);
        }
        catch (JsonException ex)
        {
            // ECMA-262 §25.5.1: a malformed JSON text is a SyntaxError — a real guest
            // Error object, so `catch (e) { e instanceof SyntaxError }` holds. A host
            // Exception here would surface to guest code as a bare string.
            throw new ThrowException(new SharpTSSyntaxError(
                $"Unexpected token in JSON: {ex.Message}"));
        }

        if (reviver != null)
        {
            // ECMA-262 25.5.1.1: synthesize a root holder { "": parsed } and
            // recurse via InternalizeJSONProperty so the reviver receives the
            // root through `this` and any in-place mutations the reviver makes
            // on `this` are visible to the surrounding walk.
            var root = new SharpTSObject(new Dictionary<string, object?> { [""] = parsed });
            return RuntimeValue.FromBoxed(InternalizeJSONProperty(interp, root, "", reviver));
        }

        return RuntimeValue.FromBoxed(parsed);
    }

    /// <summary>
    /// ECMA-262 25.5.1.1.1 InternalizeJSONProperty.
    /// Walks the holder's named property top-down: child mutation happens
    /// IN-PLACE on the value via Set / Delete, then the reviver is invoked
    /// with <c>this</c> = holder. When the value being walked is a Proxy,
    /// Get / OwnKeys / Set / Delete dispatch to the corresponding traps.
    /// </summary>
    private static object? InternalizeJSONProperty(Interpreter interp, object? holder, string key, ISharpTSCallable reviver)
    {
        var val = HolderGet(holder, key, interp);

        if (val is SharpTSArray arr)
        {
            // ECMA-262 step 2.b: iterate by length, key = ToString(index).
            long len = arr.LongLength;
            for (long i = 0; i < len; i++)
            {
                var prop = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var newElement = InternalizeJSONProperty(interp, val, prop, reviver);
                if (IsUndefinedRevive(newElement))
                    arr.DeletePropertyStrict(prop, strictMode: false);
                else
                    arr.DefineProperty(prop, new SharpTSPropertyDescriptor
                    {
                        Value = newElement,
                        HasValue = true,
                        Writable = true,
                        HasWritable = true,
                        Enumerable = true,
                        HasEnumerable = true,
                        Configurable = true,
                        HasConfigurable = true,
                    });
            }
        }
        else if (val is SharpTSProxy proxy)
        {
            // Snapshot keys before the loop — spec captures
            // EnumerableOwnProperties before iteration (the trap may
            // legitimately return varying lists across calls).
            var keys = proxy.TrapOwnKeys(interp).ToList();
            foreach (var prop in keys)
            {
                var newElement = InternalizeJSONProperty(interp, val, prop, reviver);
                if (IsUndefinedRevive(newElement))
                    proxy.TrapDeleteProperty(prop, interp);
                else
                    proxy.TrapSet(prop, newElement, interp);
            }
        }
        else if (val is SharpTSObject obj)
        {
            // Snapshot keys — the reviver can defineProperty on `this`,
            // adding sibling keys; the spec freezes the iteration list at
            // the start of step 2.c.
            var keys = obj.Fields.Keys.ToList();
            foreach (var prop in keys)
            {
                var newElement = InternalizeJSONProperty(interp, val, prop, reviver);
                if (IsUndefinedRevive(newElement))
                    obj.DeleteProperty(prop);
                else
                    obj.SetProperty(prop, newElement);
            }
        }
        else if (val is SharpTSInstance inst)
        {
            var keys = inst.GetFieldNames().ToList();
            foreach (var prop in keys)
            {
                var newElement = InternalizeJSONProperty(interp, val, prop, reviver);
                var token = new Parsing.Token(Parsing.TokenType.IDENTIFIER, prop, null, 0);
                if (IsUndefinedRevive(newElement))
                    inst.DeleteFieldStrict(prop, false);
                else
                    inst.Set(token, newElement);
            }
        }

        // Step 3: Call reviver with `this` = holder, args = (key, val).
        return InvokeReviverWithHolder(interp, reviver, holder, key, val);
    }

    private static object? HolderGet(object? holder, string key, Interpreter interp)
    {
        switch (holder)
        {
            case SharpTSProxy proxy:
                return proxy.TrapGet(key, interp);
            case SharpTSObject obj:
                {
                    var v = obj.GetProperty(key);
                    return v is SharpTSUndefined ? null : v;
                }
            case SharpTSArray arr:
                if (long.TryParse(key, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var idx)
                    && idx >= 0 && idx < arr.LongLength)
                {
                    var v = arr[idx];
                    return v is SharpTSUndefined ? null : v;
                }
                return null;
            case SharpTSInstance inst:
                {
                    var token = new Parsing.Token(Parsing.TokenType.IDENTIFIER, key, null, 0);
                    var v = inst.Get(token);
                    return v is SharpTSUndefined ? null : v;
                }
            default:
                return null;
        }
    }

    /// <summary>
    /// JS undefined removes the property; JS null is preserved. In our
    /// representation a function with no explicit return yields C# null,
    /// which we conservatively also treat as "remove" to match the
    /// pre-existing behavior of this path (an explicit <c>return null</c>
    /// is indistinguishable from no return here — out of scope to refine).
    /// </summary>
    private static bool IsUndefinedRevive(object? v) => v is null or SharpTSUndefined;

    private static object? InvokeReviverWithHolder(Interpreter interp, ISharpTSCallable reviver, object? holder, string key, object? val)
    {
        // Bind `this` = holder for the reviver call. The interpreter models
        // both classic <c>function</c> declarations and function expressions
        // through two related types:
        //   - SharpTSFunction: <c>function name() {}</c> declarations.
        //   - SharpTSArrowFunction: arrow functions AND function expressions
        //     (the latter has <c>HasOwnThis=true</c>, the former false).
        // True arrow functions capture <c>this</c> lexically and ignore any
        // bind attempt; we forward to their plain Call. Both other shapes
        // honor an explicit binding via their respective Bind helpers.
        if (reviver is SharpTSFunction fn)
            return fn.BindThis(holder).Call(interp, [key, val]);
        if (reviver is SharpTSArrowFunction arrow && arrow.HasOwnThis && holder != null)
            return arrow.Bind(holder).Call(interp, [key, val]);
        return reviver.Call(interp, [key, val]);
    }

    private static RuntimeValue StringifyJson(Interpreter interp, RuntimeValue _, ReadOnlySpan<RuntimeValue> args)
    {
        var value = args[0].ToObject();
        var replacer = args.Length > 1 ? args[1].ToObject() : null;
        var space = args.Length > 2 ? args[2].ToObject() : null;

        // ECMA-262 25.5.2.1 step 5: a boxed Number/String wrapper passed as `space`
        // contributes its primitive value before the numeric/string indent rules below.
        // (Compiled mode does the same — RuntimeEmitter.Json.StringifyFull.cs.)
        if (TryUnwrapBoxedPrimitive(interp, space, out var unwrappedSpace))
            space = unwrappedSpace;

        // Handle space parameter: number = spaces, string = literal indent string
        string indentStr = "";
        switch (space)
        {
            case double d:
                var count = (int)Math.Min(Math.Max(d, 0), 10);
                indentStr = new string(' ', count);
                break;
            case string s:
                indentStr = s.Length > 10 ? s[..10] : s;
                break;
        }

        var replacerFunc = replacer is SharpTSProxy replacerProxy && !replacerProxy.IsCallable
            ? null
            : replacer as ISharpTSCallable;
        var replacerArray = replacer as SharpTSArray;
        IReadOnlyList<string>? allowedKeys = null;

        if (replacerArray != null)
        {
            // ECMA-262 25.5.2.1 step 4.b: build PropertyList from the replacer
            // array. A String element is used as-is; a Number or a boxed
            // String/Number wrapper is coerced via ToString (honoring an own
            // toString/valueOf — #574); any other element is ignored.
            var propertyList = new List<string>();
            var propertySet = new HashSet<string>();
            foreach (var element in replacerArray)
            {
                if (interp.TryCoerceReplacerArrayKey(element, out var coercedKey)
                    && propertySet.Add(coercedKey))
                {
                    propertyList.Add(coercedKey);
                }
            }
            allowedKeys = propertyList;
        }

        var sb = new StringBuilder();
        // ECMA-262 25.5.2.3: SerializeJSONProperty maintains a stack of currently-
        // serializing objects/arrays. A cycle throws TypeError. Reference equality
        // (not .Equals) is the spec's notion of identity.
        var seen = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var wrapper = new SharpTSObject(new Dictionary<string, object?> { [""] = value });
        if (StringifyValue(interp, wrapper, value, "", replacerFunc, allowedKeys, indentStr, 0, sb, seen))
        {
            return RuntimeValue.FromString(sb.ToString());
        }

        // ECMA-262 25.5.2.1 step 12 returns whatever SerializeJSONProperty
        // yields. A top-level value that serializes to nothing — undefined, a
        // function, or a symbol — makes SerializeJSONProperty return the JS
        // value `undefined` (steps 3, 9, 11), NOT null. `StringifyValue`
        // signals that case by returning false, so surface `undefined` here.
        // (Compiled mode does the same; see RuntimeEmitter.Json.Stringify.cs.)
        return RuntimeValue.Undefined;
    }

    private static bool StringifyValue(Interpreter interp, object holder, object? value, string key,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen)
    {
        // SerializeJSONProperty calls toJSON before the replacer, with the
        // original value as `this` and the property key as its sole argument.
        value = CallToJsonIfExists(interp, value, key);

        if (replacer != null)
        {
            value = FunctionBuiltIns.CallWithThis(
                interp, replacer, holder, [key, value]);
        }

        // ECMA-262 25.5.2.3 step 4: a boxed primitive wrapper (new Number/String/Boolean)
        // serializes as its underlying primitive — not as an object exposing the internal
        // __primitiveType/__primitiveValue marker slots. Applied after toJSON/replacer,
        // before the type switch. (Compiled mode does the same — RuntimeEmitter.Json.Stringify.cs.)
        if (TryUnwrapBoxedPrimitive(interp, value, out var unwrappedPrimitive))
            value = unwrappedPrimitive;

        switch (value)
        {
            case SharpTSRawJson rawJson:
                sb.Append(rawJson.RawText);
                return true;
            case null:
                sb.Append("null");
                return true;
            case bool b:
                sb.Append(b ? "true" : "false");
                return true;
            case double d:
                var numStr = FormatJsonNumber(d);
                if (numStr == "null") sb.Append("null");
                else sb.Append(numStr);
                return true;
            case string s:
                JsonStringEscaper.AppendQuoted(sb, s);
                return true;
            case SharpTSBigInt:
                throw new ThrowException(new SharpTSTypeError(
                    "BigInt value can't be serialized in JSON"));
            case SharpTSArray arr:
                StringifyArray(interp, arr, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            case SharpTSProxy proxy when !proxy.IsCallable:
                StringifyProxy(interp, proxy, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            case SharpTSRegExp regex:
                StringifyRegExp(interp, regex, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            case SharpTSObject obj:
                StringifyObject(interp, obj, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            case SharpTSInstance inst:
                StringifyInstance(interp, inst, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            // Plain Dictionary<string, object?> — used by runtime helpers like
            // Web Streams iterator results that produce JS-object-shaped data.
            // Compiled mode already serializes dicts as JS objects in its
            // emitted JSON.stringify path; this branch keeps the interpreter
            // at parity. SharpTSMap uses object keys (Dictionary<object, object?>)
            // and is handled separately by SharpTSMap-specific paths, so this
            // branch is unambiguous.
            case IReadOnlyDictionary<string, object?> dict:
                StringifyDictionary(interp, dict, replacer, allowedKeys, indentStr, depth, sb, seen);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if the value has a toJSON() method and calls it if present.
    /// </summary>
    private static object? CallToJsonIfExists(
        Interpreter interp,
        object? value,
        string key)
    {
        if (value is null or string or bool or double
            or SharpTSUndefined or SharpTSSymbol)
            return value;

        var toJson = interp.GetPropertyValue(value, "toJSON");
        if (toJson is ISharpTSCallable callable)
            return FunctionBuiltIns.CallWithThis(
                interp, callable, value, [key]);

        return value;
    }

    /// <summary>
    /// ECMA-262 25.5.2.3 SerializeJSONProperty step 4: a boxed primitive wrapper —
    /// <c>new Number()</c>/<c>new String()</c>/<c>new Boolean()</c>, modeled as a
    /// <see cref="SharpTSObject"/> carrying <c>__primitiveType</c>/<c>__primitiveValue</c>
    /// marker slots (see <see cref="BuiltInConstructorFactory"/>) — serializes as its
    /// underlying primitive value, not as an object exposing those internal slots.
    /// Returns <c>true</c> with the primitive in <paramref name="primitive"/> when
    /// <paramref name="value"/> is such a wrapper. Gating on <c>__primitiveType</c> (which
    /// only the boxed-primitive constructors set) keeps an ordinary user object that merely
    /// happens to have a <c>__primitiveValue</c> field from being unwrapped — i.e. only the
    /// objects with a genuine [[NumberData]]/[[StringData]]/[[BooleanData]] slot are unwrapped.
    /// Compiled mode performs the equivalent unwrap (RuntimeEmitter.Json.Stringify*.cs).
    /// </summary>
    private static bool TryUnwrapBoxedPrimitive(Interpreter interp, object? value, out object? primitive)
        => interp.TryCoerceBoxedPrimitiveForJson(value, out primitive);

    private static string FormatJsonNumber(double d)
    {
        // ECMA-262 JSON.stringify(number): NaN/Infinity serialize as null; every other
        // value uses the same Number::toString as the rest of the runtime.
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        return Compilation.RuntimeTypes.FormatNumber(d);
    }

    private static void StringifyArray(Interpreter interp, SharpTSArray arr,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen)
    {
        // ECMA-262 25.5.2.5 SerializeJSONArray — throw if we're re-entering
        // the same array mid-serialization (cycle).
        if (!seen.Add(arr))
            throw new ThrowException(new SharpTSTypeError(
                "Converting circular structure to JSON"));
        try
        {
            if (arr.Length == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append('[');

            bool pretty = indentStr.Length > 0;
            string stepIndent = pretty ? "\n" + GetIndent(indentStr, depth + 1) : "";
            string separator = pretty ? "," + stepIndent : ",";

            if (pretty) sb.Append(stepIndent);

            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(separator);

                if (!StringifyValue(interp, arr, arr[i],
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    replacer, allowedKeys, indentStr, depth + 1, sb, seen))
                {
                    sb.Append("null");
                }
            }

            if (pretty)
            {
                sb.Append('\n');
                sb.Append(GetIndent(indentStr, depth));
            }
            sb.Append(']');
        }
        finally
        {
            seen.Remove(arr);
        }
    }

    private static void StringifyDictionary(Interpreter interp, IReadOnlyDictionary<string, object?> dict,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen) =>
        StringifyJsonObject(interp, dict, dict.Keys,
            k => dict.TryGetValue(k, out var value) ? value : SharpTSUndefined.Instance,
            replacer, allowedKeys, indentStr, depth, sb, seen);

    private static void StringifyProxy(Interpreter interp, SharpTSProxy proxy,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen) =>
        StringifyJsonObject(interp, proxy, proxy.TrapOwnKeys(interp),
            key => proxy.TrapGet(key, interp),
            replacer, allowedKeys, indentStr, depth, sb, seen);

    private static void StringifyRegExp(Interpreter interp, SharpTSRegExp regex,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen) =>
        StringifyJsonObject(interp, regex, regex.OwnEnumerableKeys(),
            key => interp.GetPropertyValue(regex, key),
            replacer, allowedKeys, indentStr, depth, sb, seen);

    private static void StringifyObject(Interpreter interp, SharpTSObject obj,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen) =>
        StringifyJsonObject(interp, obj, obj.OwnEnumerableKeys(),
            k => interp.GetPropertyValue(obj, k),
            replacer, allowedKeys, indentStr, depth, sb, seen);

    private static void StringifyInstance(Interpreter interp, SharpTSInstance inst,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth, StringBuilder sb, HashSet<object> seen) =>
        StringifyJsonObject(interp, inst, inst.GetFieldNames(),
            k => interp.GetPropertyValue(inst, k),
            replacer, allowedKeys, indentStr, depth, sb, seen);

    /// <summary>
    /// Shared JSON-object serializer for the three object shapes (plain dictionary,
    /// SharpTSObject, class instance), which previously carried three copy-pasted bodies:
    /// circular guard keyed on the node's identity, empty-<c>{}</c> shortcut, pretty-print step
    /// indent, and the per-entry mark/serialize/rewind-on-undefined dance. Keys are snapshot up
    /// front and values read lazily per entry via <paramref name="read"/> — matching the spec's
    /// OwnPropertyKeys-then-Get order — so the allowedKeys filter never allocates a filtered
    /// dictionary.
    /// </summary>
    private static void StringifyJsonObject(Interpreter interp, object node,
        IEnumerable<string> keys, Func<string, object?> read,
        ISharpTSCallable? replacer, IReadOnlyList<string>? allowedKeys, string indentStr, int depth,
        StringBuilder sb, HashSet<object> seen)
    {
        if (!seen.Add(node))
            throw new ThrowException(new SharpTSTypeError(
                "Converting circular structure to JSON"));
        try
        {
            var keyList = allowedKeys?.ToList() ?? keys.ToList();
            if (keyList.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');

            bool pretty = indentStr.Length > 0;
            string stepIndent = pretty ? "\n" + GetIndent(indentStr, depth + 1) : "";

            if (pretty) sb.Append(stepIndent);

            bool first = true;
            foreach (var key in keyList)
            {
                int mark = sb.Length;

                if (!first)
                {
                    sb.Append(',');
                    if (pretty) sb.Append(stepIndent);
                }

                JsonStringEscaper.AppendQuoted(sb, key);
                sb.Append(':');
                if (pretty) sb.Append(' ');

                if (StringifyValue(interp, node, read(key), key,
                    replacer, allowedKeys, indentStr, depth + 1, sb, seen))
                {
                    first = false;
                }
                else
                {
                    // Value is undefined — rewind this entry (including the comma
                    // added above, since `mark` was captured before it).
                    sb.Length = mark;
                }
            }

            if (pretty)
            {
                sb.Append('\n');
                sb.Append(GetIndent(indentStr, depth));
            }
            sb.Append('}');
        }
        finally
        {
            seen.Remove(node);
        }
    }

    // Per-thread memo of "indentStr repeated depth times". One pretty-print walk calls
    // GetIndent at every nested node with the same step string and small depths, so the
    // previous fresh Concat per node dominated pretty-print allocations. Reset when the
    // step string changes; bounded by the deepest nesting seen on the thread.
    [ThreadStatic] private static List<string>? _indentCache;
    [ThreadStatic] private static string? _indentCacheStep;

    private static string GetIndent(string indentStr, int depth)
    {
        if (depth == 0 || indentStr.Length == 0) return string.Empty;

        if (_indentCache == null || _indentCacheStep != indentStr)
        {
            _indentCacheStep = indentStr;
            _indentCache = [string.Empty];
        }

        while (_indentCache.Count <= depth)
            _indentCache.Add(_indentCache[^1] + indentStr);
        return _indentCache[depth];
    }
}
