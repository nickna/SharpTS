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
public sealed class SharpTSArrayGlobal : ISharpTSCallable, ISharpTSMutableBuiltIn
{
    public static readonly SharpTSArrayGlobal Instance = new();
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    internal SharpTSArrayGlobal() { }

    internal SharpTSArrayPrototype? RealmPrototype { get; set; }

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        if (name == "prototype") return;
        _deletedBuiltIns.Remove(name);
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedBuiltIns.Remove(name);
        return _extras.DefineProperty(name, descriptor);
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && GetBuiltInMember(name) != null);
    public bool DeleteProperty(string name)
    {
        if (name == "prototype") return false;
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (GetBuiltInMember(name) != null) _deletedBuiltIns.Add(name);
        return true;
    }
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

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
            long len = (long)d;
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
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        return GetBuiltInMember(name);
    }

    private object? GetBuiltInMember(string name)
        => name == "prototype"
            ? RealmPrototype ?? new SharpTSArrayPrototype { RealmConstructor = this }
            : BuiltInRegistry.Instance.GetStaticMethod("Array", name);

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
public sealed class SharpTSArrayPrototype : ISharpTSMutableBuiltIn
{
    internal SharpTSArrayGlobal? RealmConstructor { get; set; }
    // Array.prototype is an ordinary mutable object. Reuse SharpTSObject's
    // descriptor-aware storage so defineProperty can install accessors and
    // enforce writable/configurable flags instead of maintaining a parallel
    // value-only expando dictionary.
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ArrayPrototypeMethodWrapper>
        _methodCache = new(StringComparer.Ordinal);

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        _deletedBuiltIns.Remove(name);
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedBuiltIns.Remove(name);
        return _extras.DefineProperty(name, descriptor);
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);

    private object? GetBuiltInMember(string name)
    {
        if (name == "constructor") return RealmConstructor ?? SharpTSArrayGlobal.Instance;
        if (name == "length") return 0d;

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
        return method is null
            ? null
            : _methodCache.GetOrAdd(name, _ => new ArrayPrototypeMethodWrapper(name, method));
    }

    private bool IsBuiltIn(string name) => GetBuiltInMember(name) != null;

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    public bool DeleteProperty(string name)
    {
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

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
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        return GetBuiltInMember(name);
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
internal sealed class ArrayPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly string _name;
    private readonly BuiltInMethod _inner;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public ArrayPrototypeMethodWrapper(string name, BuiltInMethod inner)
    {
        _name = name;
        _inner = inner;
        _metadata = new BuiltInFunctionMetadata();
    }

    private ArrayPrototypeMethodWrapper(
        string name, BuiltInMethod inner, BuiltInFunctionMetadata metadata, object? receiver)
    {
        _name = name;
        _inner = inner;
        _metadata = metadata;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => _inner.SpecLength;

    // Bound copies share the metadata store, so a `delete Array.prototype.map.length`
    // stays observable through `Array.prototype.map.call(...)` and friends.
    public ArrayPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _inner, _metadata, receiver);

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

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

        // indexOf/lastIndexOf are deliberately not routed through the eager
        // materialization below. Their HasProperty/Get steps must observe
        // mutations caused by fromIndex coercion and by earlier indexed getters.
        if (_name is "indexOf" or "lastIndexOf")
        {
            return BuiltIns.ArrayBuiltIns.SearchArrayLike(
                interpreter, receiver!, arguments, fromEnd: _name == "lastIndexOf");
        }

        // Callback-based methods must not use the eager materialization below.
        // Length is captured once, but each indexed HasProperty/Get happens at
        // the point prescribed by the method, so callback/getter mutations of
        // the original array-like remain visible. Dispatching the original
        // callback also lets thisArg binding see its real callable type.
        if (BuiltIns.ArrayBuiltIns.IsGenericCallbackMethod(_name))
        {
            return BuiltIns.ArrayBuiltIns.InvokeArrayLikeCallbackMethod(
                interpreter, receiver!, _name, arguments);
        }

        if (_name == "with")
        {
            return BuiltIns.ArrayBuiltIns.CopyWithArrayLike(
                interpreter, receiver!, arguments);
        }

        // Fast path: receiver is a real array (ToObject is identity for objects).
        bool requiresObservableIndexedGet = _name is
            "toReversed" or "toSorted" or "toSpliced";
        if (receiver is SharpTSArray arr && !requiresObservableIndexedGet)
            return _inner.Bind(arr).Call(interpreter, arguments);

        // Slow path: receiver is array-like (a wrapper object with `length` +
        // indexed props, e.g. a boxed String, or any object exposing them).
        // Iterate via LengthOfArrayLike(O) / HasProperty(O, i) / Get(O, i)
        // by materializing into a temp SharpTSArray for dispatch, but wrap any
        // callable argument so the callback sees O as its final "array"
        // parameter — per spec, callbacks get O, not the internal materialization.
        if (TryMaterializeArrayLike(
                receiver, interpreter, out var tempArr,
                rejectInvalidArrayLength: _name is
                    "slice" or "toReversed" or "toSorted" or "with"))
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
    private static bool TryMaterializeArrayLike(
        object? receiver,
        Interp interpreter,
        out SharpTSArray tempArr,
        bool rejectInvalidArrayLength = false)
    {
        tempArr = null!;
        if (receiver is null or SharpTSUndefined)
            return false;

        // LengthOfArrayLike and indexed HasProperty/Get are deliberately
        // routed through the interpreter's generic property operations. Array
        // prototype methods are generic: Date, RegExp, JSON, Error, Function,
        // boxed primitives, and ordinary records all participate once user
        // code gives them a length and indexed properties.
        object? rawLen = interpreter.GetProperty(receiver, "length");
        long len = ToLength(rawLen, interpreter);
        if (rejectInvalidArrayLength && len > uint.MaxValue)
            throw new ThrowException(new SharpTSRangeError("Invalid array length."));
        len = Math.Min(len, 1 << 20);
        var list = new List<object?>((int)len);
        for (int i = 0; i < len; i++)
        {
            string key = i.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            list.Add(interpreter.HasProperty(receiver, key)
                ? interpreter.GetProperty(receiver, key)
                : ArrayHole.Instance);
        }
        tempArr = new SharpTSArray(list);
        return true;
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
    private static long ToLength(object? value, Interp interpreter)
    {
        double n = interpreter.ToNumberWithPrimitive(value);
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
public sealed class SharpTSArrayUnboundMethod : ISharpTSCallable, IBuiltInFunctionMetadata
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
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;
    private readonly int _jsLength;

    private SharpTSArrayUnboundMethod(string name, Func<SharpTSArray, List<object?>, object?> impl, int jsLength)
    {
        _name = name;
        _impl = impl;
        _jsLength = jsLength;
        _metadata = new BuiltInFunctionMetadata();
        _hasBoundThis = false;
    }

    private SharpTSArrayUnboundMethod(
        string name,
        Func<SharpTSArray, List<object?>, object?> impl,
        int jsLength,
        BuiltInFunctionMetadata metadata,
        object? boundThis)
    {
        _name = name;
        _impl = impl;
        _jsLength = jsLength;
        _metadata = metadata;
        _boundThis = boundThis;
        _hasBoundThis = true;
    }

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public int Arity() => _jsLength;

    /// <summary>
    /// True once <see cref="BindTo"/> has supplied a receiver. Callers use this to avoid
    /// re-binding an already-bound method.
    /// </summary>
    public bool HasBoundThis => _hasBoundThis;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        // Receiver: explicit bind (from .bind/.apply/.call) takes precedence,
        // otherwise treat the first argument as the receiver (pragmatic form).
        SharpTSArray? target = _boundThis as SharpTSArray;
        List<object?> rest;
        if (_hasBoundThis && target != null)
        {
            rest = arguments;
        }
        else if (_hasBoundThis)
        {
            if (_boundThis is null or SharpTSUndefined)
                throw new ThrowException(new SharpTSTypeError(
                    $"Array.prototype.{_name} called on null or undefined"));

            if (arguments.Count == 0 && _name is "push" or "unshift")
            {
                object receiver = BuiltIns.BuiltInConstructorFactory.ToObject(_boundThis)!;
                long length = ToLength(interpreter.GetProperty(receiver, "length"), interpreter);
                interpreter.SetProperty(receiver, "length", (double)length);
                return (double)length;
            }

            throw new ThrowException(new SharpTSTypeError(
                $"Array.prototype.{_name} requires an array receiver"));
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

    private static long ToLength(object? value, Interp interpreter)
    {
        double number = interpreter.ToNumberWithPrimitive(value);
        if (double.IsNaN(number) || number <= 0) return 0;
        if (double.IsPositiveInfinity(number)) return (1L << 53) - 1;
        return (long)Math.Min(Math.Truncate(number), (double)((1L << 53) - 1));
    }

    /// <summary>
    /// Produces a bound variant — used by <c>Function.prototype.apply/call</c>
    /// to pre-attach <c>thisArg</c> before invocation.
    /// </summary>
    public SharpTSArrayUnboundMethod BindTo(object? thisArg)
        => new(_name, _impl, _jsLength, _metadata, thisArg);

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
