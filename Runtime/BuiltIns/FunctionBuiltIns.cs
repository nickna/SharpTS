using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for Function.prototype (bind, call, apply).
/// </summary>
public static class FunctionBuiltIns
{
    // Spec lengths per ECMA-262 §20.2.3: bind=1, call=1, apply=2.
    private static readonly BuiltInMethod _bind = BuiltInMethod.CreateV2("bind", 0, int.MaxValue, Bind).WithSpecLength(1);
    private static readonly BuiltInMethod _call = BuiltInMethod.CreateV2("call", 0, int.MaxValue, Call).WithSpecLength(1);
    private static readonly BuiltInMethod _apply = BuiltInMethod.CreateV2("apply", 0, 2, Apply).WithSpecLength(2);

    /// <summary>
    /// Returns the unbound singleton callable for a Function.prototype method
    /// (call/apply/bind), or null. Lets <c>Function.prototype.call</c> and
    /// <c>fn.call</c> share one BuiltInMethod instance, so reference equality
    /// holds across both access paths and bound variants compose correctly.
    /// </summary>
    public static BuiltInMethod? GetPrototypeMethod(string name) => name switch
    {
        "call" => _call,
        "apply" => _apply,
        "bind" => _bind,
        _ => null,
    };

    /// <summary>
    /// Gets a member from a function (bind, call, apply, length, name).
    /// </summary>
    public static object? GetMember(ISharpTSCallable receiver, string name)
    {
        // Own function-object properties win over the prototype surface —
        // Node models process.hrtime.bigint / process.memoryUsage.rss as
        // properties on the function itself (see BuiltInMethod.OwnProperties).
        if (receiver is BuiltInMethod { OwnProperties: { } own }
            && own.TryGetValue(name, out var ownValue))
        {
            return ownValue;
        }

        // A deleted `name`/`length` (ECMA-262 §17 makes both configurable) must read
        // back as undefined, not resurrect from the switch below.
        if (receiver is IBuiltInFunctionMetadata meta
            && name is "name" or "length"
            && !meta.HasMetadataProperty(name))
        {
            return null;
        }

        switch (name)
        {
            case "bind": return _bind.Bind(receiver);
            case "call": return _call.Bind(receiver);
            case "apply": return _apply.Bind(receiver);
            // BuiltInMethod carries an explicit ECMA-262 spec length distinct
            // from MinArity (variadic methods like Array.prototype.slice have
            // MinArity 0 but spec length 2). Other callables fall back to
            // their Arity() — typically the parameter count of a user-defined
            // function, which already matches the spec.
            case "length":
                return receiver is BuiltInMethod bim
                    ? (double)bim.SpecLength
                    : (double)receiver.Arity();
            case "name":
                return GetFunctionName(receiver);
        }
        // Functions inherit Object.prototype — propertyHelper.js's verifyXxx
        // helpers call `fn.hasOwnProperty('length')` directly. Resolve those
        // through the Object.prototype unbound methods, rebound to `receiver`.
        var protoMember = Runtime.Types.SharpTSObjectPrototype.Instance.GetMember(name);
        if (protoMember is Runtime.Types.SharpTSObjectUnboundMethod ub)
            return ub.BindTo(receiver);
        return null;
    }

    private static string GetFunctionName(ISharpTSCallable callable)
    {
        return callable switch
        {
            // Built-in wrappers know their own spec name; without this they fell through
            // to "" and every `<method>/name.js` test failed.
            IBuiltInFunctionMetadata builtIn => builtIn.FunctionName,
            SharpTSFunction fn => fn.ToString().Replace("<fn ", "").TrimEnd('>'),
            SharpTSArrowFunction arrow => arrow.ToString().Contains("<fn ")
                ? arrow.ToString().Replace("<fn ", "").TrimEnd('>')
                : "",
            BoundFunction bound => bound.Name,
            _ => ""
        };
    }

    /// <summary>
    /// Function.prototype.bind(thisArg, ...args)
    /// Returns a new function with 'this' bound and optional partial application.
    /// </summary>
    private static RuntimeValue Bind(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callable = receiver.ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: bind called on non-function.");

        var thisArg = args.Length > 0 ? args[0].ToObject() : null;
        var boundArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            boundArgs.Add(args[i].ToObject());

        // Arrow functions ignore thisArg (they capture 'this' from lexical scope)
        if (callable is SharpTSArrowFunction arrow && !arrow.HasOwnThis)
        {
            // Still create a bound function for partial application, but 'this' won't change
            return RuntimeValue.FromObject(new BoundFunction(callable, null, boundArgs, ignoreThisArg: true));
        }

        return RuntimeValue.FromObject(new BoundFunction(callable, thisArg, boundArgs));
    }

    /// <summary>
    /// Function.prototype.call(thisArg, ...args)
    /// Calls the function with the specified 'this' value and individual arguments.
    /// </summary>
    private static RuntimeValue Call(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callable = receiver.ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: call invoked on non-function.");

        var thisArg = args.Length > 0 ? args[0].ToObject() : null;
        var callArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            callArgs.Add(args[i].ToObject());

        return RuntimeValue.FromBoxed(InvokeWithThis(interp, callable, thisArg, callArgs));
    }

    /// <summary>
    /// Function.prototype.apply(thisArg, argsArray)
    /// Calls the function with the specified 'this' value and arguments as an array.
    /// </summary>
    private static RuntimeValue Apply(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callable = receiver.ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: apply invoked on non-function.");

        var thisArg = args.Length > 0 ? args[0].ToObject() : null;
        var argsArray = args.Length > 1 ? args[1].ToObject() : null;

        List<object?> callArgs;
        if (argsArray == null)
        {
            callArgs = new List<object?>();
        }
        else if (argsArray is SharpTSArray tsArray)
        {
            callArgs = new List<object?>(tsArray);
        }
        else if (argsArray is List<object?> list)
        {
            callArgs = new List<object?>(list);
        }
        else
        {
            throw new Exception("Runtime Error: apply second argument must be an array or null.");
        }

        return RuntimeValue.FromBoxed(InvokeWithThis(interp, callable, thisArg, callArgs));
    }

    /// <summary>
    /// Invokes a callable with a specific 'this' value.
    /// </summary>
    /// <summary>
    /// ECMA-262 §7.3.14 Call(F, V, [argumentsList]) abstract operation —
    /// invoke <paramref name="callable"/> with <paramref name="thisArg"/> as
    /// receiver. Used by built-in spec algorithms (e.g. RegExp Symbol.* —
    /// `Call(exec, R, « S »)`) that need real Call semantics across the
    /// various callable shapes (SharpTSFunction, BuiltInMethod, ArrayProto
    /// wrappers, etc.). Same dispatch as <c>Function.prototype.call</c>.
    /// </summary>
    public static object? CallWithThis(Interpreter interp, ISharpTSCallable callable, object? thisArg, List<object?> args)
        => InvokeWithThis(interp, callable, thisArg, args);

    private static object? InvokeWithThis(Interpreter interp, ISharpTSCallable callable, object? thisArg, List<object?> args)
    {
        // Arrow functions ignore thisArg
        if (callable is SharpTSArrowFunction arrow && !arrow.HasOwnThis)
        {
            return callable.Call(interp, args);
        }

        // For regular functions, we need to bind 'this'
        if (callable is SharpTSFunction fn)
        {
            object? effectiveThis = !fn.IsStrict && thisArg is null or SharpTSUndefined
                ? interp.GlobalThis
                : thisArg;
            return fn.BindThis(effectiveThis).Call(interp, args);
        }

        if (callable is SharpTSArrowFunction arrowWithThis)
        {
            // Function expression with its own 'this'
            object? effectiveThis = !arrowWithThis.IsStrict
                && thisArg is null or SharpTSUndefined
                    ? interp.GlobalThis
                    : thisArg;
            var bound = arrowWithThis.Bind(effectiveThis!);
            return bound.Call(interp, args);
        }

        // Async function expressions / async arrows with their own 'this' rebind
        // the receiver; true async arrows ignore thisArg (lexical this). A null
        // thisArg leaves the captured 'this' unchanged (mirrors the sync path).
        if (callable is SharpTSAsyncArrowFunction asyncArrow)
        {
            if (asyncArrow.HasOwnThis && thisArg != null)
                return asyncArrow.Bind(thisArg).Call(interp, args);
            return asyncArrow.Call(interp, args);
        }
        if (callable is SharpTSAsyncFunction asyncFn)
        {
            return thisArg != null
                ? asyncFn.BindThisValue(thisArg).Call(interp, args)
                : asyncFn.Call(interp, args);
        }

        // Generator / async-generator function values (declarations and `function*` expressions) have
        // their own dynamic `this`; .call/.apply rebind the receiver (#775). A null thisArg leaves the
        // body's `this` defaulting to undefined (the wrapper's own Call handles that).
        if (callable is SharpTSGeneratorFunction or SharpTSAsyncGeneratorFunction
            or SharpTSArrowGeneratorFunction or SharpTSAsyncArrowGeneratorFunction)
        {
            var target = thisArg != null ? ((IReceiverBindable)callable).BindToReceiver(thisArg) : callable;
            return target.Call(interp, args);
        }

        // Array.prototype methods rebind their receiver via BindTo so that
        // Array.prototype.push.apply(target, items) pushes onto `target`.
        if (callable is SharpTSArrayUnboundMethod unbound)
        {
            return unbound.BindTo(thisArg).Call(interp, args);
        }
        // Function.prototype.toString rebinds so that funcToString.call(fn) works.
        if (callable is SharpTSFunctionProtoToString fnToStr)
        {
            return fnToStr.BindTo(thisArg).Call(interp, args);
        }
        // Object.prototype methods rebind to support hasOwnProperty.call(obj, key).
        if (callable is SharpTSObjectUnboundMethod objUnbound)
        {
            return objUnbound.BindTo(thisArg).Call(interp, args);
        }
        if (callable is ErrorToStringCallable errorToString)
        {
            return errorToString.Bind(thisArg).Call(interp, args);
        }

        if (callable is BuiltInAsyncMethod asyncBuiltIn)
        {
            return asyncBuiltIn.Bind(thisArg).Call(interp, args);
        }

        // BuiltInMethod (e.g. Array.prototype.every exposed via
        // SharpTSArrayPrototype) must rebind the receiver on every .call/.apply
        // so that Array.prototype.every.call(arr, cb) targets `arr`. Without
        // this, the invocation inherits whatever receiver was bound earlier
        // (typically null), and the implementation sees a null receiver.
        if (callable is BuiltInMethod builtIn)
        {
            if (builtIn.ExpectedReceiverType == typeof(string))
            {
                return new Types.StringPrototypeMethodWrapper(
                    builtIn.Name, builtIn).Bind(thisArg).Call(interp, args);
            }
            if (builtIn.ExpectedReceiverType == typeof(SharpTSArray)
                && builtIn.Name is "includes" or "flat" or "flatMap" or "copyWithin" or "slice" or "sort" or "splice")
            {
                return new Types.ArrayPrototypeMethodWrapper(builtIn.Name, builtIn)
                    .Bind(thisArg)
                    .Call(interp, args);
            }
            return builtIn.Bind(thisArg).Call(interp, args);
        }

        // Array.prototype adapter — same rebind story. Without this,
        // `Array.prototype.map.call(null, cb)` would invoke the wrapper with
        // no receiver, silently skipping the spec-mandated ToObject / TypeError.
        if (callable is Types.ArrayPrototypeMethodWrapper arrayProto)
        {
            return arrayProto.Bind(thisArg).Call(interp, args);
        }

        // String.prototype adapter — same pattern. Rebind so
        // `String.prototype.trim.call(x)` sees `x` as the receiver for ToString
        // coercion + dispatch.
        if (callable is Types.StringPrototypeMethodWrapper stringProto)
        {
            return stringProto.Bind(thisArg).Call(interp, args);
        }

        // Number.prototype adapter — same pattern.
        if (callable is Types.NumberPrototypeMethodWrapper numberProto)
        {
            return numberProto.Bind(thisArg).Call(interp, args);
        }

        // Boolean.prototype adapter — same pattern.
        if (callable is Types.BooleanPrototypeMethodWrapper boolProto)
        {
            return boolProto.Bind(thisArg).Call(interp, args);
        }

        if (callable is Types.SymbolPrototypeMethodWrapper symbolProto)
        {
            return symbolProto.Bind(thisArg).Call(interp, args);
        }

        if (callable is Types.BigIntPrototypeMethodWrapper bigIntProto)
        {
            return bigIntProto.Bind(thisArg).Call(interp, args);
        }

        // For other callables, just call directly
        return callable.Call(interp, args);
    }
}

/// <summary>
/// A function that has been bound to a specific 'this' value and/or has partial application.
/// </summary>
public class BoundFunction : ISharpTSCallable
{
    private readonly ISharpTSCallable _target;
    private readonly object? _thisArg;
    private readonly List<object?> _boundArgs;
    private readonly bool _ignoreThisArg;
    private SharpTSObject? _ownProperties;

    internal ISharpTSCallable Target => _target;

    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => (_ownProperties ??= new SharpTSObject([])).DefineProperty(name, descriptor);

    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _ownProperties?.GetOwnPropertyDescriptor(name);

    public bool TryGetProperty(string name, out object? value)
    {
        if (_ownProperties != null && _ownProperties.HasProperty(name))
        {
            value = _ownProperties.GetProperty(name);
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetAccessor(
        string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_ownProperties != null)
        {
            getter = _ownProperties.GetGetter(name);
            setter = _ownProperties.GetSetter(name);
            return getter != null || setter != null;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>
    /// The name of the bound function (for Function.prototype.name).
    /// </summary>
    public string Name { get; }

    public BoundFunction(ISharpTSCallable target, object? thisArg, List<object?> boundArgs, bool ignoreThisArg = false)
    {
        _target = target;
        _thisArg = thisArg;
        _boundArgs = boundArgs;
        _ignoreThisArg = ignoreThisArg;

        // Get base function name and prefix with "bound "
        var baseName = GetBaseName(target);
        Name = string.IsNullOrEmpty(baseName) ? "bound " : $"bound {baseName}";
    }

    private static string GetBaseName(ISharpTSCallable target)
    {
        return target switch
        {
            SharpTSFunction fn => fn.ToString().Replace("<fn ", "").TrimEnd('>'),
            SharpTSArrowFunction arrow => arrow.ToString().Contains("<fn ")
                ? arrow.ToString().Replace("<fn ", "").TrimEnd('>')
                : "",
            BoundFunction bound => bound.Name.StartsWith("bound ")
                ? bound.Name.Substring(6)
                : bound.Name,
            _ => ""
        };
    }

    public int Arity()
    {
        int baseArity = _target.Arity();
        return Math.Max(0, baseArity - _boundArgs.Count);
    }

    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        // Combine bound args + call args into a single array
        var combined = new RuntimeValue[_boundArgs.Count + arguments.Length];
        for (int i = 0; i < _boundArgs.Count; i++)
            combined[i] = RuntimeValue.FromBoxed(_boundArgs[i]);
        arguments.CopyTo(combined.AsSpan(_boundArgs.Count));

        // Delegate to target's V2 path if available
        if (!_ignoreThisArg && _thisArg != null)
        {
            if (_target is SharpTSFunction fn)
            {
                var boundFn = CreateBoundSharpTSFunction(fn, _thisArg);
                return boundFn.CallV2(interpreter, combined);
            }

            if (_target is SharpTSArrowFunction arrow && arrow.HasOwnThis)
            {
                var boundArrow = arrow.Bind(_thisArg);
                return boundArrow.CallV2(interpreter, combined);
            }

            // Async function expressions (HasOwnThis) and async function
            // declarations rebind 'this'; true async arrows capture lexically
            // and fall through to the unbound target call below.
            if (_target is SharpTSAsyncArrowFunction asyncArrow && asyncArrow.HasOwnThis)
            {
                return ((ISharpTSCallable)asyncArrow.Bind(_thisArg)).CallV2(interpreter, combined);
            }
            if (_target is SharpTSAsyncFunction asyncFn)
            {
                return ((ISharpTSCallable)asyncFn.BindThisValue(_thisArg)).CallV2(interpreter, combined);
            }

            // Generator / async-generator function values rebind their dynamic `this` (#775).
            if (_target is SharpTSGeneratorFunction or SharpTSAsyncGeneratorFunction
                or SharpTSArrowGeneratorFunction or SharpTSAsyncArrowGeneratorFunction)
            {
                return ((IReceiverBindable)_target).BindToReceiver(_thisArg).CallV2(interpreter, combined);
            }

            var rebound = BindNativeReceiver(_target, _thisArg);
            if (!ReferenceEquals(rebound, _target))
                return rebound.CallV2(interpreter, combined);
        }

        return _target.CallV2(interpreter, combined);
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Combine bound args with call args
        var combinedArgs = new List<object?>(_boundArgs);
        combinedArgs.AddRange(arguments);

        // Handle binding 'this' for the target function
        if (!_ignoreThisArg && _thisArg != null)
        {
            if (_target is SharpTSFunction fn)
            {
                // For regular functions, we need to invoke with proper 'this' binding
                // Since SharpTSFunction.Bind requires SharpTSInstance, we need a workaround
                // We'll create a special environment handling in the BoundFunction call
                var boundFn = CreateBoundSharpTSFunction(fn, _thisArg);
                return boundFn.Call(interpreter, combinedArgs);
            }

            if (_target is SharpTSArrowFunction arrow && arrow.HasOwnThis)
            {
                var boundArrow = arrow.Bind(_thisArg);
                return boundArrow.Call(interpreter, combinedArgs);
            }

            // Async function expressions (HasOwnThis) and async function
            // declarations rebind 'this'; true async arrows capture lexically
            // and fall through to the unbound target call below.
            if (_target is SharpTSAsyncArrowFunction asyncArrow && asyncArrow.HasOwnThis)
            {
                return asyncArrow.Bind(_thisArg).Call(interpreter, combinedArgs);
            }
            if (_target is SharpTSAsyncFunction asyncFn)
            {
                return asyncFn.BindThisValue(_thisArg).Call(interpreter, combinedArgs);
            }

            // Generator / async-generator function values rebind their dynamic `this` (#775).
            if (_target is SharpTSGeneratorFunction or SharpTSAsyncGeneratorFunction
                or SharpTSArrowGeneratorFunction or SharpTSAsyncArrowGeneratorFunction)
            {
                return ((IReceiverBindable)_target).BindToReceiver(_thisArg).Call(interpreter, combinedArgs);
            }

            var rebound = BindNativeReceiver(_target, _thisArg);
            if (!ReferenceEquals(rebound, _target))
                return rebound.Call(interpreter, combinedArgs);
        }

        // For arrow functions or when no 'this' binding needed
        return _target.Call(interpreter, combinedArgs);
    }

    /// <summary>
    /// Creates a SharpTSFunction-like callable that binds 'this' to any object.
    /// </summary>
    private static ISharpTSCallable CreateBoundSharpTSFunction(SharpTSFunction fn, object thisArg)
    {
        // We wrap the function in a BoundSharpTSFunctionWrapper
        return new BoundSharpTSFunctionWrapper(fn, thisArg);
    }

    private static ISharpTSCallable BindNativeReceiver(
        ISharpTSCallable target, object receiver) => target switch
    {
        BuiltInMethod method => method.Bind(receiver),
        BuiltInAsyncMethod method => method.Bind(receiver),
        StringPrototypeMethodWrapper method => method.Bind(receiver),
        NumberPrototypeMethodWrapper method => method.Bind(receiver),
        BooleanPrototypeMethodWrapper method => method.Bind(receiver),
        SymbolPrototypeMethodWrapper method => method.Bind(receiver),
        BigIntPrototypeMethodWrapper method => method.Bind(receiver),
        ArrayPrototypeMethodWrapper method => method.Bind(receiver),
        SharpTSObjectUnboundMethod method => method.BindTo(receiver),
        SharpTSArrayUnboundMethod method => method.BindTo(receiver),
        SharpTSFunctionProtoToString method => method.BindTo(receiver),
        ErrorToStringCallable method => method.Bind(receiver),
        _ => target,
    };

    public override string ToString() => $"<fn {Name}>";
}

/// <summary>
/// Internal wrapper to call a SharpTSFunction with an arbitrary 'this' value.
/// </summary>
internal class BoundSharpTSFunctionWrapper : ISharpTSCallable
{
    private readonly SharpTSFunction _fn;
    private readonly object _thisArg;

    public BoundSharpTSFunctionWrapper(SharpTSFunction fn, object thisArg)
    {
        _fn = fn;
        _thisArg = thisArg;
    }

    public int Arity() => _fn.Arity();

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Create a wrapper instance if needed
        if (_thisArg is SharpTSInstance instance)
        {
            var boundFn = _fn.Bind(instance);
            return boundFn.Call(interpreter, arguments);
        }

        // For non-instance 'this' values, we need to set up the environment manually
        // Create a synthetic instance that wraps the actual object
        var syntheticInstance = new SyntheticThisInstance(_thisArg);
        var boundFn2 = _fn.Bind(syntheticInstance);
        var result = boundFn2.Call(interpreter, arguments);
        FlushSyntheticBack(syntheticInstance, _thisArg);
        return result;
    }

    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        if (_thisArg is SharpTSInstance instance)
        {
            var boundFn = _fn.Bind(instance);
            return boundFn.CallV2(interpreter, arguments);
        }

        var syntheticInstance = new SyntheticThisInstance(_thisArg);
        var boundFn2 = _fn.Bind(syntheticInstance);
        var result = boundFn2.CallV2(interpreter, arguments);
        FlushSyntheticBack(syntheticInstance, _thisArg);
        return result;
    }

    /// <summary>
    /// Copy fields written into the synthetic-this wrapper back to the original
    /// target object. <c>Fn.call(target, ...)</c> is supposed to mutate
    /// <c>target</c>; without this flush, writes inside the body land on the
    /// synthetic and disappear when the call returns. Covers <see cref="SharpTSObject"/>
    /// and plain <see cref="Dictionary{TKey,TValue}"/> targets — the only shapes
    /// the constructor accepts as actualThis.
    /// </summary>
    private static void FlushSyntheticBack(SyntheticThisInstance synthetic, object actualThis)
    {
        if (actualThis is SharpTSObject obj)
        {
            foreach (var name in synthetic.GetFieldNames())
            {
                obj.SetProperty(name, synthetic.GetRawField(name));
            }
        }
        else if (actualThis is Dictionary<string, object?> dict)
        {
            foreach (var name in synthetic.GetFieldNames())
            {
                dict[name] = synthetic.GetRawField(name);
            }
        }
    }
}

/// <summary>
/// Synthetic instance wrapper for binding 'this' to non-instance values.
/// This allows binding functions to plain objects like { name: "foo" }.
/// </summary>
internal class SyntheticThisInstance : SharpTSInstance
{
    private static readonly SharpTSClass _dummyClass = CreateDummyClass();

    public SyntheticThisInstance(object actualThis)
        : base(_dummyClass)
    {
        // Copy fields from the actual 'this' object into this instance
        if (actualThis is SharpTSObject obj)
        {
            foreach (var kvp in obj.Fields)
            {
                SetRawField(kvp.Key, kvp.Value);
            }
        }
        else if (actualThis is Dictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                SetRawField(kvp.Key, kvp.Value);
            }
        }
    }

    private static SharpTSClass CreateDummyClass()
    {
        // Create a minimal dummy class for the base constructor
        return new SharpTSClass(
            name: "SyntheticThis",
            superclass: null,
            methods: new Dictionary<string, ISharpTSCallable>(),
            staticMethods: new Dictionary<string, ISharpTSCallable>(),
            staticProperties: new Dictionary<string, object?>(),
            getters: null,
            setters: null,
            isAbstract: false);
    }
}
