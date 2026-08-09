using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for JavaScript Proxy objects.
/// Supports handler traps: get, set, has, deleteProperty, apply, construct.
/// </summary>
public class SharpTSProxy : ISharpTSCallable
{
    private readonly object _target;
    private object? _handler;
    private bool _isRevoked;

    public object Target => _target;
    public bool IsRevoked => _isRevoked;

    public SharpTSProxy(object target, object handler)
    {
        ValidateObject(target, "target");
        ValidateObject(handler, "handler");
        _target = target;
        _handler = handler;
    }

    public void Revoke()
    {
        _isRevoked = true;
        _handler = null;
    }

    private void EnsureNotRevoked()
    {
        if (_isRevoked)
            throw new ThrowException(new SharpTSTypeError(
                "Cannot perform operation on a revoked proxy."));
    }

    private static void ValidateObject(object? value, string argName)
    {
        if (value == null || value is SharpTSUndefined)
            throw new Exception($"Runtime Error: Cannot create proxy with a non-object as {argName}.");
        if (value is string or double or bool or int or long or float or decimal or SharpTSSymbol or SharpTSBigInt)
            throw new Exception($"Runtime Error: Cannot create proxy with a non-object as {argName}.");
    }

    private object? GetTrapCallable(string trapName)
    {
        EnsureNotRevoked();

        object? value = null;

        if (_handler is SharpTSObject obj)
        {
            value = obj.GetProperty(trapName);
        }
        else if (_handler is SharpTSInstance inst)
        {
            var token = new Parsing.Token(Parsing.TokenType.IDENTIFIER, trapName, null, 0);
            try { value = inst.Get(token); }
            catch { return null; }
        }
        else if (_handler is Dictionary<string, object?> dict)
        {
            dict.TryGetValue(trapName, out value);
        }

        if (value == null || value is SharpTSUndefined)
            return null;

        if (RuntimeCallableDispatcher.IsCallable(value))
            return value;

        // Preserve the managed .NET interop fallback for arbitrary callable
        // objects. Known compiler-emitted functions are handled above.
        var invokeMethod = ManagedStructuralClrReflection.TryGetPublicMethodByName(
            value.GetType(), "Invoke");
        if (invokeMethod != null)
            return value;

        throw new ThrowException(new SharpTSTypeError(
            $"Proxy handler trap '{trapName}' is not callable"));
    }

    /// <summary>
    /// Invokes a trap function (either ISharpTSCallable, TSFunction, or Func delegate).
    /// </summary>
    private object? InvokeTrap(object trap, Interpreter? interp, List<object?> args)
    {
        if (trap is ISharpTSCallable callable && interp != null)
            return FunctionBuiltIns.CallWithThis(
                interp, callable, _handler, args);

        if (RuntimeCallableDispatcher.IsCallable(trap))
            return RuntimeCallableDispatcher.Invoke(interp, trap, args.ToArray());

        // Managed .NET interop fallback for arbitrary Invoke-shaped objects.
        // The emitted function path above no longer needs to inspect its
        // private _method field because InvokeWithThis owns that contract.
        var invokeMethod = ManagedStructuralClrReflection.TryGetPublicMethodByName(
            trap.GetType(), "Invoke");
        if (invokeMethod != null)
            return invokeMethod.Invoke(trap, [args.ToArray()]);

        throw new Exception("Runtime Error: Cannot invoke proxy trap.");
    }

    #region Trap Dispatch

    public object? TrapGet(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("get");
        if (trap == null)
            return ForwardGet(prop, interp);

        // Pass target, prop, receiver (null for compiled mode compatibility)
        object? receiver = interp != null ? (object)this : null;
        return InvokeTrap(trap, interp, [_target, prop, receiver]);
    }

    /// <summary>
    /// Symbol-keyed variant of <see cref="TrapGet(string, Interpreter?)"/>.
    /// Proxy traps observe the symbol itself as the property key; it must not be
    /// stringified before dispatch. When no trap exists, ordinary symbol lookup
    /// is forwarded to the target, including through nested proxies.
    /// </summary>
    internal object? TrapGet(SharpTSSymbol prop, Interpreter interp)
    {
        var trap = GetTrapCallable("get");
        if (trap == null)
            return interp.GetSymbolPropertyValue(_target, prop);

        return InvokeTrap(trap, interp, [_target, prop, this]);
    }

    /// <summary>
    /// Implements the proxy branch of ECMA-262 IsArray. Revoked proxies throw;
    /// otherwise classification recursively follows the final proxy target.
    /// </summary>
    internal bool HasArrayTarget()
    {
        EnsureNotRevoked();
        return _target is SharpTSArray
            || _target is SharpTSProxy proxy && proxy.HasArrayTarget();
    }

    public object? TrapSet(string prop, object? value, Interpreter? interp)
    {
        var trap = GetTrapCallable("set");
        if (trap == null)
            return ForwardSet(prop, value, interp);

        // Pass target, prop, value, receiver (null for compiled mode compatibility)
        object? receiver = interp != null ? (object)this : null;
        InvokeTrap(trap, interp, [_target, prop, value, receiver]);
        return value;
    }

    public bool TrapHas(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("has");
        if (trap == null)
            return ForwardHas(prop, interp);

        var result = InvokeTrap(trap, interp, [_target, prop]);
        return ToBoolean(result);
    }

    public bool TrapDeleteProperty(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("deleteProperty");
        if (trap == null)
            return ForwardDeleteProperty(prop);

        var result = InvokeTrap(trap, interp, [_target, prop]);
        return ToBoolean(result);
    }

    /// <summary>
    /// ECMA-262 §10.5.5 [[GetOwnProperty]]. A missing trap forwards to the target;
    /// an explicit undefined result means the proxy reports no own descriptor.
    /// The returned descriptor object is intentionally not read through [[Get]].
    /// </summary>
    public object? TrapGetOwnPropertyDescriptor(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("getOwnPropertyDescriptor");
        if (trap == null)
            return ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, prop)
                ?? SharpTSUndefined.Instance;

        return InvokeTrap(trap, interp, [_target, prop]);
    }

    /// <summary>
    /// ECMA-262 §10.5.6 [[DefineOwnProperty]]. A missing trap forwards the
    /// original descriptor object to the target; otherwise the trap receives
    /// (target, propertyKey, descriptor) and its result is boolean-coerced.
    /// </summary>
    public bool TrapDefineProperty(
        string prop, object descriptor, Interpreter interpreter)
    {
        var trap = GetTrapCallable("defineProperty");
        if (trap == null)
            return ObjectBuiltIns.DefinePropertyOnProxyTarget(
                interpreter, _target, prop, descriptor);

        bool trapResult = ToBoolean(InvokeTrap(
            trap, interpreter, [_target, prop, descriptor]));
        if (!trapResult) return false;

        object? targetDescriptor = _target is SharpTSProxy proxy
            ? proxy.TrapGetOwnPropertyDescriptor(prop, interpreter)
            : ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, prop);
        bool targetHasProperty = targetDescriptor is not (null or SharpTSUndefined);
        if (!targetHasProperty)
        {
            if (!TargetIsExtensible(_target))
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy defineProperty trap cannot add a property to a non-extensible target"));

            var requested = SharpTSPropertyDescriptor.FromAnyObject(descriptor);
            if (requested.HasConfigurable && !requested.Configurable)
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy defineProperty trap cannot create a non-configurable target property"));
        }

        return true;
    }

    private static bool TargetIsExtensible(object target) => target switch
    {
        SharpTSProxy proxy => TargetIsExtensible(proxy.Target),
        SharpTSObject obj => obj.IsExtensible,
        SharpTSInstance instance => instance.IsExtensible,
        SharpTSArray array => array.IsExtensible,
        Dictionary<string, object?> dictionary
            => PropertyDescriptorStore.IsExtensible(dictionary),
        System.Collections.IDictionary dictionary
            => PropertyDescriptorStore.IsExtensible(dictionary),
        _ => PropertyDescriptorStore.IsExtensible(target),
    };

    /// <summary>
    /// ECMA-262 10.5.11 [[OwnPropertyKeys]] trap. Returns the property names visible
    /// to enumeration (Object.keys / JSON.stringify / for-in). Falls back to forwarding
    /// to the target's own string keys when no ownKeys trap is defined. Throws if the
    /// proxy is revoked. The returned list is the union of the trap's keys and any
    /// non-configurable own keys on the target (per spec, those must always appear).
    /// </summary>
    public List<string> TrapOwnKeys(Interpreter? interp)
    {
        var trap = GetTrapCallable("ownKeys");
        if (trap == null)
            return ForwardOwnKeys();

        var result = InvokeTrap(trap, interp, [_target]);
        var keys = new List<string>();
        switch (result)
        {
            case SharpTSArray arr:
                foreach (var item in arr)
                    if (item is string s) keys.Add(s);
                break;
            case List<object?> list:
                foreach (var item in list)
                    if (item is string s) keys.Add(s);
                break;
            case IEnumerable<object?> seq:
                foreach (var item in seq)
                    if (item is string s) keys.Add(s);
                break;
        }
        return keys;
    }

    private List<string> ForwardOwnKeys()
    {
        var keys = new List<string>();
        switch (_target)
        {
            case SharpTSObject obj:
                keys.AddRange(obj.OwnStringKeys());
                break;
            case SharpTSInstance inst:
                keys.AddRange(inst.GetFieldNames());
                break;
            case SharpTSArray arr:
                for (int i = 0; i < arr.Length; i++)
                    keys.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                keys.Add("length");
                break;
            case Dictionary<string, object?> dict:
                keys.AddRange(dict.Keys);
                break;
            case List<object?> list:
                for (int i = 0; i < list.Count; i++)
                    keys.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                keys.Add("length");
                break;
        }
        return keys;
    }

    public object? TrapApply(object? thisArg, List<object?> args, Interpreter? interp)
    {
        var trap = GetTrapCallable("apply");
        if (trap == null)
        {
            if (_target is ISharpTSCallable callable)
                return callable.Call(interp!, args);

            // Compiled mode: target is a TSFunction, not ISharpTSCallable
            if (interp == null)
            {
                var invokeMethod = ManagedStructuralClrReflection.TryGetPublicMethodByName(
                    _target.GetType(), "Invoke");
                if (invokeMethod != null)
                    return invokeMethod.Invoke(_target, [args.ToArray()]);
            }

            throw new Exception("Runtime Error: Proxy target is not callable.");
        }

        // Compiled mode uses List<object?> for arrays; interpreter uses SharpTSArray
        object argsArg = interp != null ? new SharpTSArray(args) : (object)args;
        return InvokeTrap(trap, interp, [_target, thisArg, argsArg]);
    }

    public object? TrapConstruct(List<object?> args, Interpreter? interp)
    {
        var trap = GetTrapCallable("construct");
        if (trap == null)
        {
            if (_target is SharpTSClass klass)
                return klass.Call(interp!, args);
            if (_target is ISharpTSCallable callable)
                return callable.Call(interp!, args);
            throw new Exception("Runtime Error: Proxy target is not constructable.");
        }

        var argsArray = new SharpTSArray(args);
        return InvokeTrap(trap, interp, [_target, argsArray, this]);
    }

    #endregion

    #region Default Forwarding

    private object? ForwardGet(string prop, Interpreter? interp)
    {
        if (_target is SharpTSProxy proxy)
            return proxy.TrapGet(prop, interp);
        if (_target is SharpTSObject obj)
            return obj.GetProperty(prop);
        if (_target is SharpTSInstance inst)
        {
            if (interp != null) inst.SetInterpreter(interp);
            var token = new Parsing.Token(Parsing.TokenType.IDENTIFIER, prop, null, 0);
            return inst.Get(token);
        }
        if (_target is SharpTSArray arr)
        {
            // Handle array properties
            if (prop == "length") return (double)arr.Length;
            if (long.TryParse(prop, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long index)
                && index >= 0)
            {
                return arr.HasIndex(index)
                    ? arr.Get(index)
                    : SharpTSUndefined.Instance;
            }
            var member = BuiltInRegistry.Instance.GetInstanceMember(arr, prop);
            if (member is BuiltInMethod m) return m.Bind(arr);
            if (member is BuiltInAsyncMethod am) return am.Bind(arr);
            return member;
        }
        if (_target is Dictionary<string, object?> dict)
        {
            dict.TryGetValue(prop, out var val);
            return val;
        }
        // Fall through to built-in member lookup
        if (_target != null)
        {
            var member = BuiltInRegistry.Instance.GetInstanceMember(_target, prop);
            if (member is BuiltInMethod m) return m.Bind(_target);
            if (member is BuiltInAsyncMethod am) return am.Bind(_target);
            return member;
        }
        return null;
    }

    private object? ForwardSet(string prop, object? value, Interpreter? interp)
    {
        if (_target is SharpTSObject obj)
        {
            obj.SetProperty(prop, value);
            return value;
        }
        if (_target is SharpTSInstance inst)
        {
            if (interp != null) inst.SetInterpreter(interp);
            var token = new Parsing.Token(Parsing.TokenType.IDENTIFIER, prop, null, 0);
            inst.Set(token, value);
            return value;
        }
        if (_target is Dictionary<string, object?> dict)
        {
            dict[prop] = value;
            return value;
        }
        return value;
    }

    private bool ForwardHas(string prop, Interpreter? interp)
    {
        if (_target is SharpTSProxy proxy)
            return proxy.TrapHas(prop, interp);
        if (_target is SharpTSObject obj)
            return obj.HasProperty(prop);
        if (_target is SharpTSInstance inst)
            return inst.HasProperty(prop);
        if (_target is SharpTSArray arr)
        {
            if (double.TryParse(prop, out double index))
            {
                int i = (int)index;
                return i >= 0 && i < arr.Length;
            }
            return false;
        }
        if (_target is Dictionary<string, object?> dict)
            return dict.ContainsKey(prop);
        return false;
    }

    private bool ForwardDeleteProperty(string prop)
    {
        if (_target is SharpTSObject obj)
            return obj.DeletePropertyStrict(prop, false);
        if (_target is SharpTSInstance inst)
            return inst.DeleteFieldStrict(prop, false);
        if (_target is Dictionary<string, object?> dict)
            return dict.Remove(prop);
        return true;
    }

    #endregion

    #region ISharpTSCallable (for apply trap)

    public int Arity() => _target is ISharpTSCallable callable ? callable.Arity() : 0;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        return TrapApply(null, arguments, interpreter);
    }

    #endregion

    /// <summary>
    /// Returns whether the proxy target is callable (function-like).
    /// Checks ISharpTSCallable (interpreter mode), Delegate, and emitted compiled function types.
    /// </summary>
    public bool IsCallable => _target is ISharpTSCallable or Delegate
        || _target?.GetType().Name is "$TSFunction" or "$BoundTSFunction"
            or "$PromisifiedFunction" or "$DeprecatedFunction";

    #region RuntimeValue Overloads

    public RuntimeValue TrapGetRV(string property, Interpreter? interpreter)
        => RuntimeValue.FromBoxed(TrapGet(property, interpreter));

    public RuntimeValue TrapSetRV(string property, object? value, Interpreter? interpreter)
        => RuntimeValue.FromBoxed(TrapSet(property, value, interpreter));

    public RuntimeValue TrapConstructRV(List<object?> args, Interpreter? interpreter)
        => RuntimeValue.FromBoxed(TrapConstruct(args, interpreter));

    #endregion

    public override string ToString() => "Proxy {}";

    /// <summary>
    /// Converts a trap result to boolean using JavaScript truthiness rules.
    /// </summary>
    private static bool ToBoolean(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };
}
