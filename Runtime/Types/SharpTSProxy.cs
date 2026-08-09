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

    private object? GetTrapCallable(string trapName, Interpreter? interpreter)
    {
        EnsureNotRevoked();

        object? value = null;

        if (_handler is SharpTSProxy proxy)
        {
            value = proxy.TrapGet(trapName, interpreter);
        }
        else if (_handler is SharpTSObject obj)
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
        var trap = GetTrapCallable("get", interp);
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
        var trap = GetTrapCallable("get", interp);
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
        var trap = GetTrapCallable("set", interp);
        if (trap == null)
            return ForwardSet(prop, value, interp);

        // Pass target, prop, value, receiver (null for compiled mode compatibility)
        object? receiver = interp != null ? (object)this : null;
        InvokeTrap(trap, interp, [_target, prop, value, receiver]);
        return value;
    }

    public bool TrapHas(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("has", interp);
        if (trap == null)
            return ForwardHas(prop, interp);

        var result = InvokeTrap(trap, interp, [_target, prop]);
        return ToBoolean(result);
    }

    public bool TrapDeleteProperty(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("deleteProperty", interp);
        if (trap == null)
            return ForwardDeleteProperty(prop, interp);

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
        var trap = GetTrapCallable("getOwnPropertyDescriptor", interp);
        if (trap == null)
        {
            if (_target is SharpTSProxy proxy)
                return proxy.TrapGetOwnPropertyDescriptor(prop, interp);

            object? descriptor = interp == null
                ? ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, prop)
                : ObjectBuiltIns.OwnPropertyDescriptorOf(interp, _target, prop)?.ToObject();
            return descriptor ?? SharpTSUndefined.Instance;
        }

        return InvokeTrap(trap, interp, [_target, prop]);
    }

    internal object? TrapGetOwnPropertyDescriptor(
        SharpTSSymbol prop, Interpreter interpreter)
    {
        var trap = GetTrapCallable("getOwnPropertyDescriptor", interpreter);
        if (trap == null)
        {
            return _target is SharpTSProxy proxy
                ? proxy.TrapGetOwnPropertyDescriptor(prop, interpreter)
                : ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, prop)
                    ?? SharpTSUndefined.Instance;
        }

        return InvokeTrap(trap, interpreter, [_target, prop]);
    }

    /// <summary>
    /// Enumerates the proxy's own enumerable string keys by combining
    /// [[OwnPropertyKeys]] with [[GetOwnProperty]] for each key.
    /// </summary>
    internal IEnumerable<string> TrapOwnEnumerableKeys(Interpreter interpreter)
    {
        foreach (object? key in TrapOwnPropertyKeys(interpreter))
        {
            if (key is not string name) continue;
            object? descriptor = TrapGetOwnPropertyDescriptor(name, interpreter);
            if (descriptor is null or SharpTSUndefined) continue;
            if (SharpTSPropertyDescriptor.FromAnyObject(descriptor).Enumerable)
                yield return name;
        }
    }

    /// <summary>
    /// ECMA-262 §10.5.6 [[DefineOwnProperty]]. A missing trap forwards the
    /// original descriptor object to the target; otherwise the trap receives
    /// (target, propertyKey, descriptor) and its result is boolean-coerced.
    /// </summary>
    public bool TrapDefineProperty(
        string prop, object descriptor, Interpreter interpreter)
    {
        var trap = GetTrapCallable("defineProperty", interpreter);
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
        var requested = SharpTSPropertyDescriptor.FromAnyObject(descriptor);
        if (!targetHasProperty)
        {
            if (!TargetIsExtensible(_target))
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy defineProperty trap cannot add a property to a non-extensible target"));

            if (requested.HasConfigurable && !requested.Configurable)
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy defineProperty trap cannot create a non-configurable target property"));
        }
        else
        {
            ValidateDefinePropertyInvariant(
                requested,
                SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor!));
        }

        return true;
    }

    private static void ValidateDefinePropertyInvariant(
        SharpTSPropertyDescriptor requested,
        SharpTSPropertyDescriptor target)
    {
        bool settingConfigFalse = requested.HasConfigurable
            && !requested.Configurable;
        if (settingConfigFalse && target.Configurable)
            ThrowInvariant();

        if (target.Configurable) return;
        if (requested.HasConfigurable && requested.Configurable)
            ThrowInvariant();
        if (requested.HasEnumerable
            && requested.Enumerable != target.Enumerable)
            ThrowInvariant();

        bool requestedAccessor = requested.HasGet || requested.HasSet;
        bool requestedData = requested.HasValue || requested.HasWritable;
        bool targetAccessor = target.HasGet || target.HasSet;
        if ((requestedAccessor && !targetAccessor)
            || (requestedData && targetAccessor))
            ThrowInvariant();

        if (targetAccessor)
        {
            if (requested.HasGet
                && !SharpTSObject.SameValue(requested.Get, target.Get))
                ThrowInvariant();
            if (requested.HasSet
                && !SharpTSObject.SameValue(requested.Set, target.Set))
                ThrowInvariant();
            return;
        }

        if (target.Writable && requested.HasWritable && !requested.Writable)
            ThrowInvariant();
        if (!target.Writable)
        {
            if (requested.HasWritable && requested.Writable)
                ThrowInvariant();
            if (requested.HasValue
                && !SharpTSObject.SameValue(requested.Value, target.Value))
                ThrowInvariant();
        }

        static void ThrowInvariant() => throw new ThrowException(
            new SharpTSTypeError(
                "Proxy defineProperty trap returned an incompatible descriptor"));
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
        => TrapOwnPropertyKeys(interp).OfType<string>().ToList();

    /// <summary>
    /// Full [[OwnPropertyKeys]] result, retaining both string and Symbol keys.
    /// String-only consumers such as Object.keys use <see cref="TrapOwnKeys"/>.
    /// </summary>
    internal List<object?> TrapOwnPropertyKeys(Interpreter? interp)
    {
        var trap = GetTrapCallable("ownKeys", interp);
        if (trap == null)
            return ForwardOwnPropertyKeys(interp);

        var result = InvokeTrap(trap, interp, [_target]);
        var values = new List<object?>();
        switch (result)
        {
            case SharpTSArray arr:
                foreach (var item in arr)
                    values.Add(item);
                break;
            case List<object?> list:
                values.AddRange(list);
                break;
            case IEnumerable<object?> seq:
                values.AddRange(seq);
                break;
            case null or SharpTSUndefined or string or bool or double or int
                or long or SharpTSBigInt or SharpTSSymbol:
                throw InvalidOwnKeysResult();
            case object when interp != null:
                long length = ArrayBuiltIns.ToLength(
                    interp.GetProperty(result, "length"), interp);
                for (long index = 0; index < length; index++)
                {
                    values.Add(interp.GetProperty(
                        result,
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
                break;
            default:
                throw InvalidOwnKeysResult();
        }

        var uniqueKeys = new HashSet<object?>();
        foreach (object? value in values)
        {
            if (value is not (string or SharpTSSymbol))
                throw InvalidOwnKeysResult();
            if (!uniqueKeys.Add(value))
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy ownKeys trap returned duplicate property keys"));
        }
        ValidateOwnKeysInvariant(values, uniqueKeys, interp);
        return values;

        static ThrowException InvalidOwnKeysResult() => new(
            new SharpTSTypeError(
                "Proxy ownKeys trap result must contain only strings and symbols"));
    }

    private void ValidateOwnKeysInvariant(
        List<object?> trapKeys,
        HashSet<object?> trapKeySet,
        Interpreter? interpreter)
    {
        List<object?> targetKeys = ForwardOwnPropertyKeys(interpreter);
        bool extensible = TargetIsExtensible(_target);

        foreach (object? targetKey in targetKeys)
        {
            object? descriptor = targetKey switch
            {
                string name when _target is SharpTSProxy proxy
                    => proxy.TrapGetOwnPropertyDescriptor(name, interpreter),
                string name
                    => ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, name),
                SharpTSSymbol symbol when _target is SharpTSProxy proxy
                    && interpreter != null
                    => proxy.TrapGetOwnPropertyDescriptor(symbol, interpreter),
                SharpTSSymbol symbol
                    => ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, symbol),
                _ => null,
            };
            if (descriptor is null or SharpTSUndefined) continue;
            var record = SharpTSPropertyDescriptor.FromAnyObject(descriptor);
            if (!record.Configurable && !trapKeySet.Contains(targetKey))
                ThrowOwnKeysInvariant();
        }

        if (extensible) return;
        if (trapKeys.Count != targetKeys.Count
            || targetKeys.Any(key => !trapKeySet.Contains(key)))
            ThrowOwnKeysInvariant();

        static void ThrowOwnKeysInvariant() => throw new ThrowException(
            new SharpTSTypeError(
                "Proxy ownKeys trap result is incompatible with the target"));
    }

    private List<object?> ForwardOwnPropertyKeys(Interpreter? interpreter)
    {
        var keys = new List<object?>();
        switch (_target)
        {
            case SharpTSProxy proxy:
                keys.AddRange(proxy.TrapOwnPropertyKeys(interpreter));
                break;
            case SharpTSObject obj:
                keys.AddRange(obj.OwnVisibleStringKeys());
                keys.AddRange(obj.GetSymbolPropertyNames());
                break;
            case SharpTSInstance inst:
                keys.AddRange(inst.GetFieldNames());
                keys.AddRange(inst.GetSymbolPropertyNames());
                break;
            case SharpTSArray arr:
                for (int i = 0; i < arr.Length; i++)
                    keys.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                keys.Add("length");
                keys.AddRange(arr.GetSymbolPropertyNames());
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
        var trap = GetTrapCallable("apply", interp);
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
        var trap = GetTrapCallable("construct", interp);
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
        if (_target is SharpTSFunction function)
        {
            if (function.TryGetProperty(prop, out var value)) return value;
        }
        if (_target is SharpTSArrowFunction arrow)
        {
            if (arrow.TryGetProperty(prop, out var value)) return value;
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
        if (_target is SharpTSProxy proxy)
            return proxy.TrapSet(prop, value, interp);
        if (_target is SharpTSObject obj)
        {
            obj.SetProperty(prop, value);
            return value;
        }
        if (_target is SharpTSFunction function)
        {
            function.SetProperty(prop, value);
            return value;
        }
        if (_target is SharpTSArrowFunction arrow)
        {
            arrow.SetProperty(prop, value);
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

    private bool ForwardDeleteProperty(string prop, Interpreter? interp)
    {
        if (_target is SharpTSProxy proxy)
            return proxy.TrapDeleteProperty(prop, interp);
        if (_target is SharpTSObject obj)
            return obj.DeletePropertyStrict(prop, false);
        if (_target is SharpTSInstance inst)
            return inst.DeleteFieldStrict(prop, false);
        if (_target is Dictionary<string, object?> dict)
            return dict.Remove(prop);
        if (_target is SharpTSFunction function)
            return function.DeleteProperty(prop);
        if (_target is SharpTSArrowFunction arrow)
            return arrow.DeleteProperty(prop);
        if (_target is SharpTSArray array)
            return array.DeletePropertyStrict(prop, false);
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
