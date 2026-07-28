using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Global <c>Array</c> identifier. Behaves as both a namespace (<c>Array.from</c>,
/// <c>Array.isArray</c>) and a constructor (<c>new Array(...)</c>), and exposes
/// <c>Array.prototype</c> with the common mutating methods rebound via
/// <c>Function.prototype.apply/call</c>.
/// </summary>
/// <remarks>
/// Prior to this type, a bare <c>Array</c> reference threw "Undefined variable"
/// because <c>Array</c> was registered as a non-singleton namespace. Real-world
/// code (yaml, lodash internals) frequently uses patterns like
/// <c>Array.prototype.push.apply(target, items)</c>, which requires <c>Array</c>
/// to be reifiable as a value and <c>Array.prototype</c> to carry its classic
/// methods.
/// </remarks>
public sealed class SharpTSArrayGlobal : ISharpTSCallable
{
    public static readonly SharpTSArrayGlobal Instance = new();
    private readonly SharpTSArrayPrototype _prototype = new();
    private SharpTSArrayGlobal() { }

    public int Arity() => 0;

    /// <summary>
    /// <c>new Array(...)</c> / <c>Array(...)</c>.
    /// If called with a single numeric argument, creates an array of that length;
    /// otherwise treats all arguments as elements.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        if (arguments.Count == 1 && arguments[0] is double d)
        {
            if (d < 0 || d > uint.MaxValue || Math.Floor(d) != d)
                throw new ThrowException(new SharpTSRangeError("Invalid array length."));
            int len = (int)d;
            // new Array(N) gives an array of length N with N holes — not N
            // explicit undefined values. Use SetLength so large N is sparse
            // storage, not an eager allocation (see SharpTSArray #73 Stage B).
            var arr = new SharpTSArray();
            arr.SetLength(len);
            return arr;
        }
        return new SharpTSArray(new List<object?>(arguments));
    }

    public object? GetMember(string name)
    {
        if (name == "prototype") return _prototype;
        return BuiltInRegistry.Instance.GetStaticMethod("Array", name);
    }

    public override string ToString() => "function Array() { [native code] }";
}

/// <summary>
/// <c>Array.prototype</c>. Exposes every registered Array.prototype method as
/// an unbound <see cref="BuiltInMethod"/> sourced from
/// <see cref="BuiltIns.ArrayBuiltIns"/> — the same implementation used for
/// direct instance-method dispatch (<c>arr.map(fn)</c>). When user code does
/// <c>Array.prototype.map.call(arrayLike, fn)</c>, <c>Function.prototype.call</c>
/// rebinds the receiver before invoking, so both access paths share one
/// implementation.
/// </summary>
public sealed class SharpTSArrayPrototype
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArrayPrototypeMethodWrapper>
        _methodCache = new(StringComparer.Ordinal);

    // Mutating methods (push/pop/shift/unshift) keep the bespoke
    // SharpTSArrayUnboundMethod path because spec-compliant array-like
    // mutation would require writing indexed properties back onto the
    // original receiver — a larger refactor. Non-mutating methods
    // (slice/concat — pure reads returning new arrays, plus indexOf) are
    // routed through ArrayBuiltIns, so they share one implementation with
    // instance-method dispatch and inherit the array-like receiver
    // coercion in ArrayPrototypeMethodWrapper.
    public object? GetMember(string name)
    {
        if (name == "constructor") return SharpTSArrayGlobal.Instance;

        var legacy = name switch
        {
            "push" => (object?)SharpTSArrayUnboundMethod.Push,
            "pop" => SharpTSArrayUnboundMethod.Pop,
            "shift" => SharpTSArrayUnboundMethod.Shift,
            "unshift" => SharpTSArrayUnboundMethod.Unshift,
            _ => null,
        };
        if (legacy is not null) return legacy;

        var method = BuiltIns.ArrayBuiltIns.GetPrototypeMethod(name);
        if (method is null) return null;

        // ECMA-262: Array.prototype.* starts with ToObject(this), which throws
        // TypeError if this is null or undefined. Wrap with a receiver-check
        // so `Array.prototype.map.call(null)` throws a real TypeError in JS
        // instead of surfacing a C# InvalidCastException via the method body.
        return _methodCache.GetOrAdd(name, _ => new ArrayPrototypeMethodWrapper(name, method));
    }

    public override string ToString() => "[object Array]";
}

/// <summary>
/// Adapter around a <see cref="BuiltInMethod"/> exposed on
/// <c>Array.prototype</c>. Before dispatching, throws a spec-shaped
/// <c>TypeError</c> if the receiver is null or undefined. Carries binding
/// semantics through <c>.call</c>/<c>.apply</c> by delegating Bind/Call to
/// the inner method.
/// </summary>
internal sealed class ArrayPrototypeMethodWrapper : ISharpTSCallable
{
    private readonly string _name;
    private readonly BuiltInMethod _inner;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public ArrayPrototypeMethodWrapper(string name, BuiltInMethod inner)
    {
        _name = name;
        _inner = inner;
    }

    private ArrayPrototypeMethodWrapper(string name, BuiltInMethod inner, object? receiver)
    {
        _name = name;
        _inner = inner;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => _inner.SpecLength;

    public ArrayPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _inner, receiver);

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        if (!_hasReceiver || _receiver is null or SharpTSUndefined)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Array.prototype.{_name} called on null or undefined"));
        }

        // ECMA-262 §23.1.3: every Array.prototype method begins with
        // `O = ToObject(this value)`. A primitive receiver (string/number/
        // boolean) therefore becomes its wrapper object, so the callback's
        // final "array" argument (O) is an object — e.g.
        // `Array.prototype.forEach.call("ab", cb)` passes a String wrapper
        // (`typeof obj === "object"`, `obj instanceof String === true`),
        // not the bare `"ab"`. Objects/arrays are returned unchanged. (#454)
        object? receiver = BuiltIns.BuiltInConstructorFactory.ToObject(_receiver);

        // Fast path: receiver is a real array (ToObject is identity for objects).
        if (receiver is SharpTSArray arr)
            return _inner.Bind(arr).Call(interpreter, arguments);

        // Slow path: receiver is array-like (a wrapper object with `length` +
        // indexed props, e.g. a boxed String, or any object exposing them).
        // Iterate via LengthOfArrayLike(O) / HasProperty(O, i) / Get(O, i)
        // by materializing into a temp SharpTSArray for dispatch, but wrap any
        // callable argument so the callback sees O as its final "array"
        // parameter — per spec, callbacks get O, not the internal materialization.
        if (TryMaterializeArrayLike(receiver, interpreter, out var tempArr))
        {
            var wrappedArgs = WrapCallbackArguments(arguments, tempArr, receiver);
            return _inner.Bind(tempArr).Call(interpreter, wrappedArgs);
        }

        // Fallback: receiver type we can't coerce — let the inner method try.
        // It will likely throw a meaningful error.
        return _inner.Bind(receiver).Call(interpreter, arguments);
    }

    /// <summary>
    /// Attempts to build a temp <see cref="SharpTSArray"/> matching the
    /// array-like's length and indexed values. Preserves ECMA-262 holes
    /// (<see cref="ArrayHole"/>.<c>Instance</c>) at absent indices so
    /// hole-aware methods (map/filter/forEach/...) behave correctly.
    /// Caps length at 1M to protect against accidental runaway allocation
    /// from a stray <c>length: 2**53-1</c> configuration.
    /// </summary>
    private static bool TryMaterializeArrayLike(object? receiver, Interp interpreter, out SharpTSArray tempArr)
    {
        tempArr = null!;
        switch (receiver)
        {
            case SharpTSObject obj:
            {
                // Read `length` — invoking an accessor getter if one is defined
                // (Object.defineProperty(obj, "length", { get: ... })). Per spec,
                // a throwing length getter aborts the whole method; we propagate
                // by letting the ThrowException escape to the caller.
                object? rawLen = ReadArrayLikeProperty(obj, "length", interpreter);
                // When the wrapper has no own `length` (absent = SharpTSUndefined),
                // walk the prototype chain. A boxed Number/Boolean primitive
                // (ToObject result) carries __primitiveType but no own indexed
                // elements — consult *.prototype extras.
                if (rawLen is null or SharpTSUndefined)
                    rawLen = GetBoxedPrimitiveProtoExtra(obj, "length", interpreter);
                long len = ToLength(rawLen);
                len = Math.Min(len, 1 << 20);
                var list = new List<object?>((int)len);
                for (int i = 0; i < len; i++)
                {
                    var key = i.ToString();
                    if (obj.HasGetter(key))
                        list.Add(ReadArrayLikeProperty(obj, key, interpreter));
                    else if (obj.HasProperty(key))
                        list.Add(obj.GetProperty(key));
                    else
                        list.Add(GetBoxedPrimitiveProtoExtra(obj, key, interpreter) ?? ArrayHole.Instance);
                }
                tempArr = new SharpTSArray(list);
                return true;
            }
            case SharpTSMath math:
            {
                // Math is an extensible namespace object — Test262 tests set
                // `Math.length` and `Math[i]` before invoking Array.prototype.*
                // on Math, expecting array-like semantics.
                long len = ToLength(math.HasExtra("length") ? math.TryGetExtra("length") : null);
                len = Math.Min(len, 1 << 20);
                var list = new List<object?>((int)len);
                for (int i = 0; i < len; i++)
                {
                    var key = i.ToString();
                    list.Add(math.HasExtra(key) ? math.TryGetExtra(key) : ArrayHole.Instance);
                }
                tempArr = new SharpTSArray(list);
                return true;
            }
            case SharpTSNumberPrototype numProto:
            {
                long len = ToLength(numProto.HasExtra("length") ? numProto.TryGetExtra("length") : null);
                len = Math.Min(len, 1 << 20);
                var list = new List<object?>((int)len);
                for (int i = 0; i < len; i++)
                {
                    var key = i.ToString();
                    list.Add(numProto.HasExtra(key) ? numProto.TryGetExtra(key) : ArrayHole.Instance);
                }
                tempArr = new SharpTSArray(list);
                return true;
            }
            case SharpTSBooleanPrototype boolProto:
            {
                long len = ToLength(boolProto.HasExtra("length") ? boolProto.TryGetExtra("length") : null);
                len = Math.Min(len, 1 << 20);
                var list = new List<object?>((int)len);
                for (int i = 0; i < len; i++)
                {
                    var key = i.ToString();
                    list.Add(boolProto.HasExtra(key) ? boolProto.TryGetExtra(key) : ArrayHole.Instance);
                }
                tempArr = new SharpTSArray(list);
                return true;
            }
            case SharpTSStringPrototype strProto:
            {
                long len = ToLength(strProto.HasExtra("length") ? strProto.TryGetExtra("length") : null);
                len = Math.Min(len, 1 << 20);
                var list = new List<object?>((int)len);
                for (int i = 0; i < len; i++)
                {
                    var key = i.ToString();
                    list.Add(strProto.HasExtra(key) ? strProto.TryGetExtra(key) : ArrayHole.Instance);
                }
                tempArr = new SharpTSArray(list);
                return true;
            }
            // A primitive string receiver never reaches here: Call() runs it
            // through ToObject first, so it arrives as a boxed String wrapper
            // (SharpTSObject with `length` + indexed char slots) handled by the
            // SharpTSObject case above.
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the extra property value from the matching primitive prototype when
    /// <paramref name="obj"/> is a boxed primitive wrapper produced by ToObject,
    /// or null if the property is absent or the object is not a boxed primitive.
    /// Used to implement ECMA-262 LengthOfArrayLike/Get prototype-chain walk for
    /// Number and Boolean wrappers whose own property bags carry no indexed state.
    /// </summary>
    private static object? GetBoxedPrimitiveProtoExtra(SharpTSObject obj, string name, Interp interpreter)
    {
        if (!obj.HasProperty("__primitiveType")) return null;
        // Per-realm prototypes: read the boxed primitive's prototype extras off
        // this realm's instance, not the shared singleton, so they match what
        // guest `Number.prototype.x = …` wrote in the same realm.
        return obj.GetProperty("__primitiveType") switch
        {
            "Number"  => interpreter.GetNumberPrototype().TryGetExtra(name),
            "Boolean" => interpreter.GetBooleanPrototype().TryGetExtra(name),
            "String"  => interpreter.GetStringPrototype().TryGetExtra(name),
            _ => null
        };
    }

    /// <summary>
    /// Reads a property from a SharpTSObject, invoking an accessor getter if
    /// present. Matches the `Get(O, P)` abstract operation used by
    /// Array.prototype.* methods. If the getter throws, the exception
    /// propagates — spec-compliant short-circuit for things like
    /// <c>Object.defineProperty(obj, "length", { get: () => { throw ... } })</c>.
    /// </summary>
    private static object? ReadArrayLikeProperty(SharpTSObject obj, string name, Interp interpreter)
    {
        var getter = obj.GetGetter(name);
        if (getter != null)
            return getter.Call(interpreter, new List<object?>());
        return obj.GetProperty(name);
    }

    /// <summary>
    /// Returns <paramref name="arguments"/> with any <see cref="ISharpTSCallable"/>
    /// argument wrapped so every reference to <paramref name="tempArr"/> in the
    /// callback's arg list is substituted with the original receiver before the
    /// user callback runs. Callers that don't take a callback (<c>join</c>,
    /// <c>slice</c>) return the list unchanged.
    /// </summary>
    private static List<object?> WrapCallbackArguments(
        List<object?> arguments, SharpTSArray tempArr, object? originalReceiver)
    {
        if (arguments.Count == 0) return arguments;
        if (arguments[0] is not ISharpTSCallable userCb) return arguments;

        var result = new List<object?>(arguments.Count)
        {
            new ReceiverSubstitutingCallback(userCb, tempArr, originalReceiver)
        };
        for (int i = 1; i < arguments.Count; i++) result.Add(arguments[i]);
        return result;
    }

    /// <summary>
    /// ECMA-262 7.1.20 ToLength: coerces <paramref name="value"/> to a
    /// non-negative integer length, clamped to <c>2^53 − 1</c>. NaN/negative
    /// input becomes 0; non-numeric strings parse to NaN → 0.
    /// </summary>
    private static long ToLength(object? value)
    {
        double n = value switch
        {
            double d => d,
            bool b => b ? 1.0 : 0.0,
            string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
            null => 0.0,
            SharpTSUndefined => double.NaN,
            _ => double.NaN,
        };
        if (double.IsNaN(n) || n <= 0) return 0;
        if (double.IsPositiveInfinity(n)) return (1L << 53) - 1;
        return (long)Math.Min(Math.Truncate(n), (double)((1L << 53) - 1));
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";

    /// <summary>
    /// Wraps a user callback so every position in its argument list that
    /// references the internal <see cref="SharpTSArray"/> materialization is
    /// substituted with the original array-like receiver. Mutates in place —
    /// the pooled arg list is reused across iterations, and only the element
    /// and index positions get reset each call, so the substitution sticks.
    /// </summary>
    private sealed class ReceiverSubstitutingCallback : ISharpTSCallable
    {
        private readonly ISharpTSCallable _inner;
        private readonly SharpTSArray _tempArr;
        private readonly object? _originalReceiver;

        public ReceiverSubstitutingCallback(
            ISharpTSCallable inner, SharpTSArray tempArr, object? originalReceiver)
        {
            _inner = inner;
            _tempArr = tempArr;
            _originalReceiver = originalReceiver;
        }

        public int Arity() => _inner.Arity();

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                if (ReferenceEquals(arguments[i], _tempArr))
                    arguments[i] = _originalReceiver;
            }
            return _inner.Call(interpreter, arguments);
        }
    }
}

/// <summary>
/// An unbound method living on <c>Array.prototype</c>. When called directly as
/// <c>fn(target, ...args)</c>, the first argument is treated as the receiver.
/// When used via <c>Function.prototype.apply</c>/<c>call</c>, the bound
/// <c>this</c> becomes the receiver.
/// </summary>
public sealed class SharpTSArrayUnboundMethod : ISharpTSCallable
{
    // ECMA-262 spec lengths (the "length" property visible to user code, NOT
    // the C# function's parameter count). Variadic methods like push/concat/
    // unshift have spec length 1; pop/shift/reverse are 0; slice is 2. These
    // appear on `Array.prototype.X.length` and are probed by Test262's
    // `function-property-length` cluster (#105).
    public static readonly SharpTSArrayUnboundMethod Push = new("push", PushImpl, jsLength: 1);
    public static readonly SharpTSArrayUnboundMethod Pop = new("pop", PopImpl, jsLength: 0);
    public static readonly SharpTSArrayUnboundMethod Shift = new("shift", ShiftImpl, jsLength: 0);
    public static readonly SharpTSArrayUnboundMethod Unshift = new("unshift", UnshiftImpl, jsLength: 1);
    public static readonly SharpTSArrayUnboundMethod Slice = new("slice", SliceImpl, jsLength: 2);
    public static readonly SharpTSArrayUnboundMethod Concat = new("concat", ConcatImpl, jsLength: 1);
    public static readonly SharpTSArrayUnboundMethod IndexOf = new("indexOf", IndexOfImpl, jsLength: 1);

    private readonly string _name;
    private readonly Func<SharpTSArray, List<object?>, object?> _impl;
    private readonly object? _boundThis;
    private readonly int _jsLength;

    private SharpTSArrayUnboundMethod(string name, Func<SharpTSArray, List<object?>, object?> impl, int jsLength, object? boundThis = null)
    {
        _name = name;
        _impl = impl;
        _jsLength = jsLength;
        _boundThis = boundThis;
    }

    public int Arity() => _jsLength;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        // Receiver: explicit bind (from .bind/.apply/.call) takes precedence,
        // otherwise treat the first argument as the receiver (pragmatic form).
        SharpTSArray? target = _boundThis as SharpTSArray;
        List<object?> rest;
        if (target != null)
        {
            rest = arguments;
        }
        else
        {
            if (arguments.Count == 0 || arguments[0] is not SharpTSArray first)
                throw new Exception($"Runtime Error: Array.prototype.{_name} requires an array receiver.");
            target = first;
            rest = arguments.Count > 1 ? arguments.GetRange(1, arguments.Count - 1) : new List<object?>();
        }
        return _impl(target, rest);
    }

    /// <summary>
    /// Produces a bound variant — used by <c>Function.prototype.apply/call</c>
    /// to pre-attach <c>thisArg</c> before invocation.
    /// </summary>
    public SharpTSArrayUnboundMethod BindTo(object? thisArg) => new(_name, _impl, _jsLength, thisArg);

    public override string ToString() => $"function {_name}() {{ [native code] }}";

    private static object? PushImpl(SharpTSArray arr, List<object?> args)
    {
        foreach (var item in args) arr.Add(item);
        return (double)arr.Length;
    }

    private static object? PopImpl(SharpTSArray arr, List<object?> args)
    {
        if (arr.Length == 0) return SharpTSUndefined.Instance;
        return arr.RemoveLast();
    }

    private static object? ShiftImpl(SharpTSArray arr, List<object?> args)
    {
        if (arr.Length == 0) return SharpTSUndefined.Instance;
        return arr.RemoveFirst();
    }

    private static object? UnshiftImpl(SharpTSArray arr, List<object?> args)
    {
        for (int i = 0; i < args.Count; i++) arr.Insert(i, args[i]);
        return (double)arr.Length;
    }

    private static object? SliceImpl(SharpTSArray arr, List<object?> args)
    {
        int start = args.Count > 0 && args[0] is double s ? (int)s : 0;
        int end = args.Count > 1 && args[1] is double e ? (int)e : arr.Length;
        if (start < 0) start = Math.Max(0, arr.Length + start);
        if (end < 0) end = Math.Max(0, arr.Length + end);
        start = Math.Min(start, arr.Length);
        end = Math.Min(end, arr.Length);
        if (end <= start) return new SharpTSArray(new List<object?>());
        var result = new List<object?>(end - start);
        for (int i = start; i < end; i++) result.Add(arr[i]);
        return new SharpTSArray(result);
    }

    private static object? ConcatImpl(SharpTSArray arr, List<object?> args)
    {
        var result = new List<object?>(arr);
        foreach (var a in args)
        {
            if (a is SharpTSArray inner) result.AddRange(inner);
            else result.Add(a);
        }
        return new SharpTSArray(result);
    }

    private static object? IndexOfImpl(SharpTSArray arr, List<object?> args)
    {
        if (args.Count == 0) return -1.0;
        var target = args[0];
        for (int i = 0; i < arr.Length; i++)
        {
            if (Equals(arr[i], target)) return (double)i;
        }
        return -1.0;
    }
}
