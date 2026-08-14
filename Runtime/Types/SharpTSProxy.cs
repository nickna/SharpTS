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

    private static bool IsUndefinedLike(object? value)
        => value is SharpTSUndefined
            || value?.GetType().Name == "$Undefined";

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
            throw new ThrowException(new SharpTSTypeError(
                $"Cannot create proxy with a non-object as {argName}."));
        if (value is string or bool or byte or sbyte or short or ushort or int
            or uint or long or ulong or float or double or decimal
            or System.Numerics.BigInteger or SharpTSSymbol or SharpTSBigInt)
            throw new ThrowException(new SharpTSTypeError(
                $"Cannot create proxy with a non-object as {argName}."));
    }

    private object? GetTrapCallable(string trapName, Interpreter? interpreter)
    {
        EnsureNotRevoked();

        object? value = null;

        if (interpreter != null)
        {
            // GetMethod performs an ordinary [[Get]] on the handler. Going
            // through the interpreter preserves accessor side effects and
            // abrupt completions instead of reading the handler's backing
            // property store directly.
            value = interpreter.GetPropertyValue(_handler, trapName);
        }
        else if (_handler is SharpTSProxy proxy)
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

        if (value == null || IsUndefinedLike(value))
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

    private object? GetTrapCallableCompiled(
        string trapName, Func<object, string, object?> ordinaryGet)
    {
        EnsureNotRevoked();
        object? value = _handler is SharpTSProxy proxy
            ? proxy.TrapGetCompiled(trapName, ordinaryGet)
            : ordinaryGet(_handler!, trapName);

        if (value == null || IsUndefinedLike(value)) return null;
        if (RuntimeCallableDispatcher.IsCallable(value)) return value;
        if (ManagedStructuralClrReflection.TryGetPublicMethodByName(
                value.GetType(), "Invoke") != null)
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
        => TrapGet(prop, interp, interp != null ? this : null);

    /// <summary>
    /// Compiled-runtime get dispatch. The emitted runtime owns ordinary
    /// property lookup for compiler representations such as Task-backed
    /// promises, so an absent proxy trap must delegate back to it instead of
    /// BuiltInRegistry's interpreter-oriented fallback.
    /// </summary>
    public object? TrapGetCompiled(
        string prop,
        Func<object, string, object?> ordinaryGet)
    {
        var trap = GetTrapCallable("get", null);
        if (trap == null)
        {
            return _target is SharpTSProxy targetProxy
                ? targetProxy.TrapGetCompiled(prop, ordinaryGet)
                : ordinaryGet(_target, prop);
        }

        object? result = InvokeTrap(trap, null, [_target, prop, this]);
        ValidateGetTrapResult(prop, result, null);
        return result;
    }

    internal object? TrapGet(
        string prop, Interpreter? interp, object? receiver)
    {
        var trap = GetTrapCallable("get", interp);
        if (trap == null)
        {
            if (interp == null) return ForwardGet(prop, interp);
            return _target is SharpTSProxy targetProxy
                ? targetProxy.TrapGet(prop, interp, receiver)
                : interp.GetPropertyValue(_target, prop, receiver);
        }

        object? result = InvokeTrap(trap, interp, [_target, prop, receiver]);
        ValidateGetTrapResult(prop, result, interp);
        return result;
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

        object? result = InvokeTrap(trap, interp, [_target, prop, this]);
        ValidateGetTrapResult(prop, result, interp);
        return result;
    }

    private void ValidateGetTrapResult(
        object propertyKey, object? result, Interpreter? interpreter)
    {
        object? targetDescriptor = GetTargetOwnPropertyDescriptor(
            propertyKey, interpreter);
        if (targetDescriptor is null or SharpTSUndefined) return;

        var descriptor = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor);
        if (descriptor.Configurable) return;

        if (descriptor.HasValue && !descriptor.Writable
            && !SharpTSObject.SameValue(result, descriptor.Value))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy get trap must return the value of a fixed data property"));
        }
        if ((descriptor.HasGet || descriptor.HasSet) && descriptor.Get == null
            && result is not SharpTSUndefined)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy get trap must return undefined for a fixed accessor without a getter"));
        }
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

    /// <summary>ECMA-262 §10.5.1 [[GetPrototypeOf]].</summary>
    internal object? TrapGetPrototypeOf(Interpreter? interpreter)
    {
        var trap = GetTrapCallable("getPrototypeOf", interpreter);
        if (trap == null)
            return _target is SharpTSProxy proxy
                ? proxy.TrapGetPrototypeOf(interpreter)
                : ObjectBuiltIns.PrototypeOf(interpreter, _target);

        object? result = InvokeTrap(trap, interpreter, [_target]);
        if (result is SharpTSUndefined or string or bool or double or int or long
            or float or decimal or SharpTSSymbol or SharpTSBigInt
            or System.Numerics.BigInteger)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getPrototypeOf trap must return an object or null"));
        }

        if (TargetIsExtensible(_target)) return result;

        object? targetPrototype = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapGetPrototypeOf(interpreter)
            : ObjectBuiltIns.PrototypeOf(interpreter, _target);
        if (!ReferenceEquals(result, targetPrototype))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getPrototypeOf trap result does not match the non-extensible target"));
        }
        return result;
    }

    /// <summary>
    /// Compiled-runtime [[GetPrototypeOf]] dispatch. Ordinary target operations
    /// are supplied by the emitted runtime so compiler-owned object shapes and
    /// integrity state participate in the Proxy invariant checks.
    /// </summary>
    public object? TrapGetPrototypeOfCompiled(
        Func<object, object?> ordinaryGetPrototypeOf,
        Func<object, bool> ordinaryIsExtensible)
    {
        var trap = GetTrapCallable("getPrototypeOf", null);
        if (trap == null)
        {
            return _target is SharpTSProxy proxy
                ? proxy.TrapGetPrototypeOfCompiled(
                    ordinaryGetPrototypeOf, ordinaryIsExtensible)
                : ordinaryGetPrototypeOf(_target);
        }

        object? result = InvokeTrap(trap, null, [_target]);
        if (IsUndefinedLike(result)
            || result is string or bool or byte or sbyte or short or ushort
                or int or uint or long or ulong or float or double or decimal
                or System.Numerics.BigInteger or SharpTSSymbol or SharpTSBigInt
            || result?.GetType().Name is "$TSSymbol" or "$TSBigInt")
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getPrototypeOf trap must return an object or null"));
        }

        if (ordinaryIsExtensible(_target)) return result;

        object? targetPrototype = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapGetPrototypeOfCompiled(
                ordinaryGetPrototypeOf, ordinaryIsExtensible)
            : ordinaryGetPrototypeOf(_target);
        if (!ReferenceEquals(result, targetPrototype))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getPrototypeOf trap result does not match the non-extensible target"));
        }
        return result;
    }

    /// <summary>ECMA-262 §10.5.2 [[SetPrototypeOf]].</summary>
    internal bool TrapSetPrototypeOf(Interpreter interpreter, object? prototype)
    {
        var trap = GetTrapCallable("setPrototypeOf", interpreter);
        if (trap == null)
            return ObjectBuiltIns.SetPrototypeOfTarget(interpreter, _target, prototype);

        bool result = ToBoolean(InvokeTrap(trap, interpreter, [_target, prototype]));
        if (!result) return false;

        bool targetIsExtensible = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapIsExtensible(interpreter)
            : TargetIsExtensible(_target);
        if (targetIsExtensible) return true;

        object? targetPrototype = _target is SharpTSProxy proxy
            ? proxy.TrapGetPrototypeOf(interpreter)
            : ObjectBuiltIns.PrototypeOf(interpreter, _target);
        if (!ReferenceEquals(prototype, targetPrototype))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy setPrototypeOf trap cannot change a non-extensible target"));
        }
        return true;
    }

    /// <summary>ECMA-262 §10.5.3 [[IsExtensible]].</summary>
    internal bool TrapIsExtensible(Interpreter? interpreter)
    {
        var trap = GetTrapCallable("isExtensible", interpreter);
        if (trap == null)
            return _target is SharpTSProxy proxy
                ? proxy.TrapIsExtensible(interpreter)
                : TargetIsExtensible(_target);

        bool result = ToBoolean(InvokeTrap(trap, interpreter, [_target]));
        bool targetResult = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapIsExtensible(interpreter)
            : TargetIsExtensible(_target);
        if (result != targetResult)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy isExtensible trap result does not match the target"));
        }
        return result;
    }

    /// <summary>ECMA-262 §10.5.4 [[PreventExtensions]].</summary>
    internal bool TrapPreventExtensions(Interpreter interpreter)
    {
        var trap = GetTrapCallable("preventExtensions", interpreter);
        if (trap == null)
            return ObjectBuiltIns.PreventExtensionsTarget(interpreter, _target);

        bool result = ToBoolean(InvokeTrap(trap, interpreter, [_target]));
        if (!result) return false;
        if ((_target is SharpTSProxy proxy
                ? proxy.TrapIsExtensible(interpreter)
                : TargetIsExtensible(_target)))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy preventExtensions trap returned true for an extensible target"));
        }
        return true;
    }

    /// <summary>
    /// Compiled-runtime [[PreventExtensions]] dispatch. Ordinary target
    /// operations are supplied by the emitted runtime so its descriptor-store
    /// state participates in the proxy invariant check.
    /// </summary>
    public bool TrapPreventExtensionsCompiled(
        Func<object, object?> ordinaryPreventExtensions,
        Func<object, bool> ordinaryIsExtensible)
    {
        var trap = GetTrapCallable("preventExtensions", null);
        if (trap == null)
        {
            if (_target is SharpTSProxy proxy)
                return proxy.TrapPreventExtensionsCompiled(
                    ordinaryPreventExtensions, ordinaryIsExtensible);
            _ = ordinaryPreventExtensions(_target);
            return true;
        }

        bool result = ToBoolean(InvokeTrap(trap, null, [_target]));
        if (!result) return false;
        if (ordinaryIsExtensible(_target))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy preventExtensions trap returned true for an extensible target"));
        }
        return true;
    }

    public object? TrapSet(string prop, object? value, Interpreter? interp)
    {
        TrapSetProperty(prop, value, interp, interp != null ? this : null);
        return value;
    }

    internal bool TrapSetProperty(
        string prop, object? value, Interpreter? interp, object? receiver)
    {
        var trap = GetTrapCallable("set", interp);
        if (trap == null)
        {
            if (_target is SharpTSProxy targetProxy)
                return targetProxy.TrapSetProperty(prop, value, interp, receiver);
            return ForwardOrdinarySet(prop, value, interp, receiver);
        }

        bool result = ToBoolean(InvokeTrap(
            trap, interp, [_target, prop, value, receiver]));
        if (!result) return false;

        object? targetDescriptor = GetTargetOwnPropertyDescriptor(prop, interp);
        if (targetDescriptor is null or SharpTSUndefined) return true;

        var descriptor = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor);
        if (descriptor.Configurable) return true;
        if (descriptor.HasValue && !descriptor.Writable
            && !SharpTSObject.SameValue(value, descriptor.Value))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy set trap cannot change a fixed data property"));
        }
        if ((descriptor.HasGet || descriptor.HasSet) && descriptor.Set == null)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy set trap cannot assign to a fixed accessor without a setter"));
        }
        return true;
    }

    /// <summary>
    /// OrdinarySetWithOwnDescriptor for the proxy's target.  The receiver must
    /// remain the original proxy: defining directly on the target would bypass
    /// the receiver's getOwnPropertyDescriptor and defineProperty internal
    /// methods, which are observable when those operations are trapped.
    /// </summary>
    private bool ForwardOrdinarySet(
        string prop, object? value, Interpreter? interpreter, object? receiver)
    {
        if (interpreter == null)
        {
            ForwardSet(prop, value, interpreter);
            return true;
        }

        return OrdinarySet(interpreter, _target, prop, value, receiver);
    }

    internal static bool OrdinarySet(
        Interpreter interpreter,
        object target,
        string prop,
        object? value,
        object? receiver)
    {
        if (target is SharpTSProxy targetProxy)
            return targetProxy.TrapSetProperty(prop, value, interpreter, receiver);

        SharpTSPropertyDescriptor? ownDescriptor =
            ObjectBuiltIns.OwnPropertyDescriptorOf(interpreter, target, prop);
        if (ownDescriptor == null)
        {
            object? parent = ObjectBuiltIns.PrototypeOf(interpreter, target);
            if (parent is not null and not SharpTSUndefined)
                return OrdinarySet(interpreter, parent, prop, value, receiver);
        }

        SharpTSPropertyDescriptor descriptor;
        if (ownDescriptor == null)
        {
            descriptor = new SharpTSPropertyDescriptor
            {
                Writable = true,
                Enumerable = true,
                Configurable = true,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true,
            };
        }
        else
        {
            descriptor = ownDescriptor;
        }

        if (descriptor.HasValue)
        {
            if (!descriptor.Writable || !IsObjectValue(receiver))
                return false;

            object? existing = receiver is SharpTSProxy receiverProxy
                ? receiverProxy.TrapGetOwnPropertyDescriptor(prop, interpreter)
                : ObjectBuiltIns.OwnPropertyDescriptorOf(interpreter, receiver!, prop)?.ToObject();
            bool receiverHasProperty = existing is not (null or SharpTSUndefined);
            if (receiverHasProperty)
            {
                var existingDescriptor = SharpTSPropertyDescriptor.FromAnyObject(existing!);
                if (existingDescriptor.HasGet || existingDescriptor.HasSet
                    || !existingDescriptor.Writable)
                    return false;
            }

            var valueDescriptor = new SharpTSObject([]);
            valueDescriptor.SetProperty("value", value);
            if (!receiverHasProperty)
            {
                valueDescriptor.SetProperty("writable", true);
                valueDescriptor.SetProperty("enumerable", true);
                valueDescriptor.SetProperty("configurable", true);
            }

            if (receiver is SharpTSProxy receiverProxyForDefine)
                return receiverProxyForDefine.TrapDefineProperty(
                    prop, valueDescriptor, interpreter);

            try
            {
                return ObjectBuiltIns.DefinePropertyOnProxyTarget(
                    interpreter, receiver!, prop, valueDescriptor);
            }
            catch (Exception ex) when (ex.Message.StartsWith(
                "TypeError: Cannot define property '",
                StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (descriptor.Set == null) return false;
        FunctionBuiltIns.CallWithThis(
            interpreter, descriptor.Set, receiver, [value]);
        return true;
    }

    private static bool IsObjectValue(object? value)
        => value is not (null or SharpTSUndefined or string or bool or byte
            or sbyte or short or ushort or int or uint or long or ulong or float
            or double or decimal or System.Numerics.BigInteger or SharpTSBigInt
            or SharpTSSymbol);

    public bool TrapHas(string prop, Interpreter? interp)
        => TrapHasCore(prop, interp);

    internal bool TrapHas(SharpTSSymbol prop, Interpreter interp)
        => TrapHasCore(prop, interp);

    private bool TrapHasCore(object prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("has", interp);
        if (trap == null)
        {
            if (interp != null)
                return prop is SharpTSSymbol symbol
                    ? interp.HasSymbolProperty(_target, symbol)
                    : interp.HasProperty(_target, (string)prop);
            return ForwardHas((string)prop, interp);
        }

        bool result = ToBoolean(InvokeTrap(trap, interp, [_target, prop]));
        if (result) return true;

        object? targetDescriptor = GetTargetOwnPropertyDescriptor(prop, interp);
        if (targetDescriptor is null or SharpTSUndefined) return false;

        var descriptor = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor);
        if (!descriptor.Configurable)
            throw new ThrowException(new SharpTSTypeError(
                "Proxy has trap cannot hide a non-configurable property"));

        bool targetIsExtensible = _target is SharpTSProxy proxy && interp != null
            ? proxy.TrapIsExtensible(interp)
            : TargetIsExtensible(_target);
        if (!targetIsExtensible)
            throw new ThrowException(new SharpTSTypeError(
                "Proxy has trap cannot hide a property on a non-extensible target"));

        return false;
    }

    public bool TrapDeleteProperty(string prop, Interpreter? interp)
    {
        var trap = GetTrapCallable("deleteProperty", interp);
        if (trap == null)
            return ForwardDeleteProperty(prop, interp);

        bool result = ToBoolean(InvokeTrap(trap, interp, [_target, prop]));
        if (!result) return false;

        object? targetDescriptor = GetTargetOwnPropertyDescriptor(prop, interp);
        if (targetDescriptor is null or SharpTSUndefined) return true;

        var descriptor = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor);
        if (!descriptor.Configurable)
            throw new ThrowException(new SharpTSTypeError(
                "Proxy deleteProperty trap cannot delete a non-configurable property"));

        bool targetIsExtensible = _target is SharpTSProxy proxy && interp != null
            ? proxy.TrapIsExtensible(interp)
            : TargetIsExtensible(_target);
        if (!targetIsExtensible)
            throw new ThrowException(new SharpTSTypeError(
                "Proxy deleteProperty trap cannot hide a property on a non-extensible target"));

        return true;
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

        object? result = InvokeTrap(trap, interp, [_target, prop]);
        // Compiler-emitted functions use CLR null for a returned JS undefined
        // on this reflection bridge. Normalize it before descriptor validation.
        if (interp == null && result == null)
            result = SharpTSUndefined.Instance;
        return ValidateDescriptorTrapResult(result, prop, interp);
    }

    /// <summary>
    /// Compiled-runtime [[GetOwnProperty]] dispatch. Property keys remain
    /// object-valued so emitted Symbols cross the SharpTS.dll reflection
    /// boundary without being stringified. Ordinary descriptor lookup and
    /// extensibility checks are delegated to the emitted runtime, whose
    /// descriptor/integrity stores own the compiled target representation.
    /// </summary>
    public object? TrapGetOwnPropertyDescriptorCompiled(
        object prop,
        Func<object, object, object?> ordinaryGetOwnPropertyDescriptor,
        Func<object, bool> ordinaryIsExtensible,
        Func<object, string, object?> ordinaryGet)
    {
        var trap = GetTrapCallableCompiled(
            "getOwnPropertyDescriptor", ordinaryGet);
        if (trap == null)
        {
            if (_target is SharpTSProxy proxy)
            {
                return proxy.TrapGetOwnPropertyDescriptorCompiled(
                    prop, ordinaryGetOwnPropertyDescriptor,
                    ordinaryIsExtensible, ordinaryGet);
            }

            return ordinaryGetOwnPropertyDescriptor(_target, prop)
                ?? SharpTSUndefined.Instance;
        }

        object? result = InvokeTrap(trap, null, [_target, prop]);
        if (result == null) result = SharpTSUndefined.Instance;

        object? targetDescriptor = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapGetOwnPropertyDescriptorCompiled(
                prop, ordinaryGetOwnPropertyDescriptor,
                ordinaryIsExtensible, ordinaryGet)
            : ordinaryGetOwnPropertyDescriptor(_target, prop);
        bool targetHasProperty = !IsUndefinedLike(targetDescriptor)
            && targetDescriptor != null;

        if (IsUndefinedLike(result))
        {
            if (!targetHasProperty) return SharpTSUndefined.Instance;
            var targetRecord = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor!);
            if (!targetRecord.Configurable || !ordinaryIsExtensible(_target))
                ThrowDescriptorInvariant();
            return SharpTSUndefined.Instance;
        }

        if (result is string or bool or byte or sbyte or short or ushort
            or int or uint or long or ulong or float or double or decimal
            or System.Numerics.BigInteger or SharpTSSymbol or SharpTSBigInt
            || result.GetType().Name is "$TSSymbol" or "$TSBigInt")
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getOwnPropertyDescriptor trap must return an object or undefined"));
        }

        var resultDescriptor = SharpTSPropertyDescriptor.FromAnyObject(result);
        if (!targetHasProperty)
        {
            if (!ordinaryIsExtensible(_target) || !resultDescriptor.Configurable)
                ThrowDescriptorInvariant();
        }
        else
        {
            var targetRecord = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor!);
            ValidateDefinePropertyInvariant(resultDescriptor, targetRecord);
            if (!resultDescriptor.Configurable && targetRecord.Configurable)
                ThrowDescriptorInvariant();
        }

        return resultDescriptor.ToObject();

        static void ThrowDescriptorInvariant() => throw new ThrowException(
            new SharpTSTypeError(
                "Proxy getOwnPropertyDescriptor trap result is incompatible with the target"));
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

        return ValidateDescriptorTrapResult(
            InvokeTrap(trap, interpreter, [_target, prop]), prop, interpreter);
    }

    private object? ValidateDescriptorTrapResult(
        object? result, object propertyKey, Interpreter? interpreter)
    {
        object? targetDescriptor = GetTargetOwnPropertyDescriptor(
            propertyKey, interpreter);
        bool targetHasProperty = targetDescriptor is not (null or SharpTSUndefined);

        if (IsUndefinedLike(result))
        {
            if (!targetHasProperty) return SharpTSUndefined.Instance;
            var targetRecord = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor!);
            if (!targetRecord.Configurable || !TargetIsExtensible(_target))
                ThrowDescriptorInvariant();
            return SharpTSUndefined.Instance;
        }
        if (result is null or string or bool or double or int or long or float
            or decimal or SharpTSSymbol or SharpTSBigInt
            or System.Numerics.BigInteger)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy getOwnPropertyDescriptor trap must return an object or undefined"));
        }

        var resultDescriptor = interpreter == null
            ? SharpTSPropertyDescriptor.FromAnyObject(result)
            : ObjectBuiltIns.ToPropertyDescriptor(interpreter, result);

        if (!targetHasProperty)
        {
            if (!TargetIsExtensible(_target) || !resultDescriptor.Configurable)
                ThrowDescriptorInvariant();
        }
        else
        {
            var targetRecord = SharpTSPropertyDescriptor.FromAnyObject(targetDescriptor!);
            ValidateDefinePropertyInvariant(resultDescriptor, targetRecord);
            if (!resultDescriptor.Configurable && targetRecord.Configurable)
                ThrowDescriptorInvariant();
        }

        // [[GetOwnProperty]] returns a complete descriptor record. Expose a fresh
        // descriptor object rather than the handler's potentially partial object.
        return resultDescriptor.ToObject();

        static void ThrowDescriptorInvariant() => throw new ThrowException(
            new SharpTSTypeError(
                "Proxy getOwnPropertyDescriptor trap result is incompatible with the target"));
    }

    private object? GetTargetOwnPropertyDescriptor(
        object propertyKey, Interpreter? interpreter)
    {
        if (_target is SharpTSProxy proxy)
        {
            return propertyKey switch
            {
                SharpTSSymbol symbol when interpreter != null
                    => proxy.TrapGetOwnPropertyDescriptor(symbol, interpreter),
                string name => proxy.TrapGetOwnPropertyDescriptor(name, interpreter),
                _ => SharpTSUndefined.Instance,
            };
        }

        if (propertyKey is SharpTSSymbol symbolKey)
            return ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, symbolKey)
                ?? SharpTSUndefined.Instance;
        if (propertyKey is string nameKey && interpreter != null)
            return (object?)ObjectBuiltIns.OwnPropertyDescriptorOf(
                    interpreter, _target, nameKey)?.ToObject()
                ?? SharpTSUndefined.Instance;
        return ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(_target, propertyKey)
            ?? SharpTSUndefined.Instance;
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
        {
            try
            {
                return ObjectBuiltIns.DefinePropertyOnProxyTarget(
                    interpreter, _target, prop, descriptor);
            }
            catch (Exception ex) when (ex.Message.StartsWith(
                "TypeError: Cannot define property '",
                StringComparison.Ordinal))
            {
                // [[DefineOwnProperty]] reports an incompatible descriptor as
                // false. Object.defineProperty turns that false into a thrown
                // TypeError, so translate only that wrapper error here while
                // preserving abrupt descriptor evaluation.
                return false;
            }
        }

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

    /// <summary>Compiled-runtime [[SetPrototypeOf]] dispatch.</summary>
    public bool TrapSetPrototypeOfCompiled(
        object? prototype,
        Func<object, object?, object?> ordinarySetPrototypeOf,
        Func<object, bool> ordinaryIsExtensible,
        Func<object, object?> ordinaryGetPrototypeOf)
    {
        var trap = GetTrapCallable("setPrototypeOf", null);
        if (trap == null)
        {
            if (_target is SharpTSProxy proxy)
            {
                return proxy.TrapSetPrototypeOfCompiled(
                    prototype, ordinarySetPrototypeOf,
                    ordinaryIsExtensible, ordinaryGetPrototypeOf);
            }
            _ = ordinarySetPrototypeOf(_target, prototype);
            return true;
        }

        bool result = ToBoolean(InvokeTrap(trap, null, [_target, prototype]));
        if (!result) return false;
        if (ordinaryIsExtensible(_target)) return true;

        object? targetPrototype = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapGetPrototypeOfCompiled(
                ordinaryGetPrototypeOf, ordinaryIsExtensible)
            : ordinaryGetPrototypeOf(_target);
        if (!ReferenceEquals(prototype, targetPrototype))
        {
            throw new ThrowException(new SharpTSTypeError(
                "Proxy setPrototypeOf trap cannot change a non-extensible target"));
        }
        return true;
    }

    /// <summary>
    /// Compiled-runtime [[DefineOwnProperty]] dispatch. When the handler has no
    /// trap, the emitted runtime callback applies the descriptor to the proxy
    /// target's compiled carrier rather than going through interpreter-only
    /// object representations.
    /// </summary>
    public bool TrapDefinePropertyCompiled(
        string prop,
        object descriptor,
        Func<object, object, object, object?> ordinaryDefine)
    {
        var trap = GetTrapCallable("defineProperty", null);
        if (trap == null)
        {
            if (_target is SharpTSProxy proxy)
                return proxy.TrapDefinePropertyCompiled(prop, descriptor, ordinaryDefine);
            ordinaryDefine(_target, prop, descriptor);
            return true;
        }

        bool trapResult = ToBoolean(InvokeTrap(
            trap, null, [_target, prop, descriptor]));
        if (!trapResult) return false;

        object? targetDescriptor = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapGetOwnPropertyDescriptor(prop, null)
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
    /// Compiled-runtime [[OwnPropertyKeys]] dispatch. Unlike the legacy
    /// string-only reflection bridge, this retains emitted Symbol keys through
    /// duplicate and target-invariant validation. Compiler-owned callbacks
    /// provide CreateListFromArrayLike, ordinary target keys/descriptors, and
    /// extensibility state.
    /// </summary>
    public List<object?> TrapOwnKeysCompiled(
        Func<object, List<object?>> ordinaryOwnPropertyKeys,
        Func<object, List<object?>> createListFromArrayLike,
        Func<object, object, object?> ordinaryGetOwnPropertyDescriptor,
        Func<object, bool> ordinaryIsExtensible,
        Func<object, bool> isSymbol,
        Func<object, string, object?> ordinaryGet)
    {
        var trap = GetTrapCallableCompiled("ownKeys", ordinaryGet);
        if (trap == null)
        {
            return _target is SharpTSProxy proxy
                ? proxy.TrapOwnKeysCompiled(
                    ordinaryOwnPropertyKeys, createListFromArrayLike,
                    ordinaryGetOwnPropertyDescriptor, ordinaryIsExtensible,
                    isSymbol, ordinaryGet)
                : ordinaryOwnPropertyKeys(_target);
        }

        object? trapResult = InvokeTrap(trap, null, [_target]);
        if (trapResult == null || IsUndefinedLike(trapResult)
            || trapResult is string or bool or byte or sbyte or short or ushort
                or int or uint or long or ulong or float or double or decimal
                or System.Numerics.BigInteger or SharpTSSymbol or SharpTSBigInt
            || trapResult.GetType().Name is "$TSSymbol" or "$TSBigInt")
        {
            throw InvalidOwnKeysResult();
        }

        List<object?> values = createListFromArrayLike(trapResult);
        var uniqueKeys = new HashSet<object?>();
        foreach (object? value in values)
        {
            if (value is not string && (value == null || !isSymbol(value)))
                throw InvalidOwnKeysResult();
            if (!uniqueKeys.Add(value))
            {
                throw new ThrowException(new SharpTSTypeError(
                    "Proxy ownKeys trap returned duplicate property keys"));
            }
        }

        List<object?> targetKeys = _target is SharpTSProxy targetProxy
            ? targetProxy.TrapOwnKeysCompiled(
                ordinaryOwnPropertyKeys, createListFromArrayLike,
                ordinaryGetOwnPropertyDescriptor, ordinaryIsExtensible,
                isSymbol, ordinaryGet)
            : ordinaryOwnPropertyKeys(_target);

        foreach (object? targetKey in targetKeys)
        {
            if (targetKey == null) continue;
            object? descriptor = _target is SharpTSProxy descriptorProxy
                ? descriptorProxy.TrapGetOwnPropertyDescriptorCompiled(
                    targetKey, ordinaryGetOwnPropertyDescriptor,
                    ordinaryIsExtensible, ordinaryGet)
                : ordinaryGetOwnPropertyDescriptor(_target, targetKey);
            if (descriptor == null || IsUndefinedLike(descriptor)) continue;
            if (!SharpTSPropertyDescriptor.FromAnyObject(descriptor).Configurable
                && !uniqueKeys.Contains(targetKey))
            {
                ThrowOwnKeysInvariant();
            }
        }

        if (!ordinaryIsExtensible(_target)
            && (values.Count != targetKeys.Count
                || targetKeys.Any(key => !uniqueKeys.Contains(key))))
        {
            ThrowOwnKeysInvariant();
        }

        return values;

        static ThrowException InvalidOwnKeysResult() => new(
            new SharpTSTypeError(
                "Proxy ownKeys trap result must contain only strings and symbols"));
        static void ThrowOwnKeysInvariant() => throw new ThrowException(
            new SharpTSTypeError(
                "Proxy ownKeys trap result is incompatible with the target"));
    }

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
            if (_target is SharpTSProxy targetProxy)
                return targetProxy.TrapApply(thisArg, args, interp);
            if (_target is ISharpTSCallable callable && interp != null)
                return FunctionBuiltIns.CallWithThis(
                    interp, callable, thisArg, args);

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

    public object? TrapConstruct(
        List<object?> args, Interpreter? interp, object? newTarget = null)
    {
        var trap = GetTrapCallable("construct", interp);
        if (trap == null)
        {
            if (_target is SharpTSProxy targetProxy)
                return targetProxy.TrapConstruct(
                    args, interp, newTarget ?? this);
            if (_target is SharpTSClass klass)
                return klass.Call(interp!, args);
            if (_target is ISharpTSCallable callable)
                return callable.Call(interp!, args);
            throw new Exception("Runtime Error: Proxy target is not constructable.");
        }

        var argsArray = new SharpTSArray(args);
        object? result = InvokeTrap(
            trap, interp, [_target, argsArray, newTarget ?? this]);
        if (!IsObjectValue(result))
            throw new ThrowException(new SharpTSTypeError(
                "Proxy construct trap must return an object"));
        return result;
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
        if (_target is SharpTSArray array)
        {
            if (long.TryParse(prop, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long index)
                && index >= 0)
                array.Set(index, value);
            else
                array.SetNamedProperty(prop, value);
            return value;
        }
        if (_target is SharpTSRegExp regex)
        {
            regex.SetPropertyStrict(prop, value, strictMode: false);
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
        if (_target is SharpTSRegExp regex)
            return regex.DeleteProperty(prop);
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
    public bool IsCallable => _target is SharpTSProxy proxy
        ? proxy.IsCallable
        : _target is ISharpTSCallable or Delegate
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
        null or SharpTSUndefined => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        float f => f != 0 && !float.IsNaN(f),
        int i => i != 0,
        long l => l != 0,
        decimal m => m != 0,
        System.Numerics.BigInteger bigInteger => !bigInteger.IsZero,
        SharpTSBigInt bigInt => !bigInt.Value.IsZero,
        string s => s.Length > 0,
        _ => true
    };
}
