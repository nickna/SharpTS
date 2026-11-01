using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for Function.prototype (bind, call, apply).
/// </summary>
public static class FunctionBuiltIns
{
    internal static BuiltInMethod CreateHasInstanceMethod()
        => BuiltInMethod.CreateV2("[Symbol.hasInstance]", 0, int.MaxValue,
            static (interpreter, receiver, args) => RuntimeValue.FromBoolean(
                interpreter.OrdinaryHasInstance(
                    args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance,
                    receiver.ToObject())))
            .WithSpecLength(1).AsNonConstructor().MarkRealmOwned();

    /// <summary>
    /// Creates an unbound Function.prototype method for one realm. The owning
    /// prototype retains it so mutable function metadata stays realm-local and
    /// repeated reads preserve identity.
    /// </summary>
    internal static BuiltInMethod? CreatePrototypeMethod(string name) => name switch
    {
        // Spec lengths per ECMA-262 §20.2.3: bind=1, call=1, apply=2.
        "bind" => new PrototypeMethod("bind", int.MaxValue, Bind, 1),
        "call" => new PrototypeMethod("call", int.MaxValue, Call, 1),
        "apply" => new PrototypeMethod("apply", 2, Apply, 2),
        _ => null,
    };

    /// <summary>Creates a fresh intrinsic; interpreter reads use their realm's cached copy.</summary>
    public static BuiltInMethod? GetPrototypeMethod(string name) => CreatePrototypeMethod(name);

    private sealed class PrototypeMethod : BuiltInMethod
    {
        internal PrototypeMethod(string name, int maxArity,
            Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation,
            int specLength)
            : base(name, 0, maxArity, null, implementation, null)
        {
            WithSpecLength(specLength);
            AsNonConstructor();
            MarkRealmOwned();
        }
    }

    /// <summary>
    /// Gets a member from a function (bind, call, apply, length, name).
    /// </summary>
    public static object? GetMember(ISharpTSCallable receiver, string name, Interpreter? interpreter = null)
    {
        if (receiver is ISharpTSFunctionObject functionObject)
        {
            var properties = interpreter is null ? functionObject.OwnProperties
                : FunctionObjectProperties.GetProperties(functionObject, interpreter);
            var descriptor = properties.GetOwnPropertyDescriptor(name);
            if (descriptor is not null)
            {
                if (descriptor.HasGet || descriptor.HasSet)
                    return descriptor.Get is null || interpreter is null
                        ? SharpTSUndefined.Instance
                        : CallWithThis(interpreter, descriptor.Get, receiver, []);
                return descriptor.Value;
            }
            // Absence in the canonical bag must not resurrect native metadata.
            if (name is "name" or "length") return null;
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
            case "bind":
            case "call":
            case "apply":
                return interpreter is not null
                    ? interpreter.GetFunctionPrototype().GetMember(name)
                    : CreatePrototypeMethod(name)!.Bind(receiver);
            // BuiltInMethod carries an explicit ECMA-262 spec length distinct
            // from MinArity (variadic methods like Array.prototype.slice have
            // MinArity 0 but spec length 2). Other callables fall back to
            // their Arity() — typically the parameter count of a user-defined
            // function, which already matches the spec.
            case "length":
                return receiver is BoundFunction bound ? bound.Length
                    : receiver is BuiltInMethod bim
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

    internal static string GetFunctionName(ISharpTSCallable callable)
    {
        return callable switch
        {
            // Built-in wrappers know their own spec name; without this they fell through
            // to "" and every `<method>/name.js` test failed.
            ISharpTSFunctionObject function =>
                function.OwnProperties.GetOwnPropertyDescriptor("name")?.Value as string ?? "",
            IBuiltInFunctionMetadata builtIn => builtIn.FunctionName,
            SharpTSBuiltInConstructor constructor => constructor.Name,
            SharpTSFunction fn => fn.TryGetProperty("name", out var name) ? name as string ?? "" : "",
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
            ?? throw new ThrowException(new SharpTSTypeError(
                "Function.prototype.bind called on incompatible receiver"));
        if (callable is SharpTSProxy { IsCallable: false })
            throw new ThrowException(new SharpTSTypeError(
                "Function.prototype.bind called on incompatible receiver"));

        var thisArg = args.Length > 0 ? args[0].ToObject() : DefaultThisArgument(callable);
        var boundArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            boundArgs.Add(args[i].ToObject());

        // BoundFunctionCreate snapshots the target's actual internal prototype,
        // including a derived class's base constructor and explicit null.
        object? prototype = ObjectBuiltIns.PrototypeOf(interp, callable);
        double targetLength = 0;
        if (SharpTSObjectUnboundMethod.HasOwn(interp, callable, "length")
            && interp.GetPropertyValue(callable, "length") is double length
            && length > 0)
        {
            targetLength = Math.Truncate(length);
        }
        // Bind snapshots the target's current guest-visible name, including
        // constructor aliases and descriptor overrides, independently of it.
        string targetName = interp.GetPropertyValue(callable, "name") as string ?? "";

        // Arrow functions ignore thisArg (they capture 'this' from lexical scope)
        if (callable is SharpTSArrowFunction arrow && !arrow.HasOwnThis)
        {
            // Still create a bound function for partial application, but 'this' won't change
            var boundArrow = new BoundFunction(callable, null, boundArgs,
                ignoreThisArg: true, targetName: targetName, targetLength: targetLength);
            boundArrow.SetPrototypeSnapshot(prototype);
            return RuntimeValue.FromObject(boundArrow);
        }

        var bound = new BoundFunction(callable, thisArg, boundArgs, targetName: targetName,
            targetLength: targetLength);
        bound.SetPrototypeSnapshot(prototype);
        return RuntimeValue.FromObject(bound);
    }

    /// <summary>
    /// Function.prototype.call(thisArg, ...args)
    /// Calls the function with the specified 'this' value and individual arguments.
    /// </summary>
    private static RuntimeValue Call(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callable = receiver.ToObject() as ISharpTSCallable
            ?? throw new ThrowException(new SharpTSTypeError(
                "Function.prototype.call called on incompatible receiver"));

        var thisArg = args.Length > 0 ? args[0].ToObject() : DefaultThisArgument(callable);
        var callArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            callArgs.Add(args[i].ToObject());

        return RuntimeValue.FromBoxed(InvokeWithThis(interp, callable, thisArg, callArgs));
    }

    /// <summary>
    /// Function.prototype.apply(thisArg, argsArray)
    /// Calls the function with the specified 'this' value and an array-like argument list.
    /// </summary>
    private static RuntimeValue Apply(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callable = receiver.ToObject() as ISharpTSCallable
            ?? throw new ThrowException(new SharpTSTypeError(
                "Function.prototype.apply called on incompatible receiver"));
        if (callable is SharpTSProxy { IsCallable: false })
            throw new ThrowException(new SharpTSTypeError(
                "Function.prototype.apply called on incompatible receiver"));

        var thisArg = args.Length > 0 ? args[0].ToObject() : DefaultThisArgument(callable);
        var argsArray = args.Length > 1 ? args[1].ToObject() : null;

        var callArgs = new List<object?>();
        if (argsArray is not (null or SharpTSUndefined))
        {
            if (!args[1].IsObject)
                throw new ThrowException(new SharpTSTypeError(
                    "Function.prototype.apply argument list must be an object"));

            long length = ArrayBuiltIns.ToLength(
                interp.GetPropertyValue(argsArray, "length"), interp);
            for (long index = 0; index < length; index++)
            {
                // Get every index, including holes and inherited/non-enumerable
                // properties. The length is captured before any indexed getter.
                callArgs.Add(interp.GetPropertyValue(argsArray,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
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

    // Canonical static methods are strict and distinguish an absent receiver
    // from explicit null. Other callables retain their existing normalization.
    private static object? DefaultThisArgument(ISharpTSCallable callable)
        => callable is IUndefinedThisDefault
            or SharpTSFunction { PrivateOwner: not null }
            or SharpTSAsyncFunction { PrivateOwner: not null }
            or SharpTSGeneratorFunction { PrivateOwner: not null }
            or SharpTSAsyncGeneratorFunction { PrivateOwner: not null }
            or SharpTSFunction { IsStaticMethod: true }
            or SharpTSAsyncFunction { IsStaticMethod: true }
            or SharpTSGeneratorFunction { IsStaticMethod: true }
            or SharpTSAsyncGeneratorFunction { IsStaticMethod: true }
                ? SharpTSUndefined.Instance : null;

    /// <summary>Preserves an exact receiver for an interpreted class static method.</summary>
    internal static ISharpTSCallable? BindStaticReceiver(ISharpTSCallable callable, object? receiver)
        => callable switch
        {
            SharpTSFunction fn when fn.IsStaticMethod => fn.BindThis(receiver),
            SharpTSAsyncFunction fn when fn.IsStaticMethod => fn.BindThisValue(receiver),
            SharpTSGeneratorFunction fn when fn.IsStaticMethod => fn.BindToReceiver(receiver!),
            SharpTSAsyncGeneratorFunction fn when fn.IsStaticMethod => fn.BindToReceiver(receiver!),
            _ => null,
        };

    /// <summary>Class method calls retain the exact guest receiver for every function kind.</summary>
    internal static ISharpTSCallable? BindClassReceiver(ISharpTSCallable callable, object? receiver)
        => callable switch
        {
            SharpTSFunction fn when fn.PrivateOwner != null && !fn.IsStaticMethod => fn.BindInstanceReceiver(receiver),
            SharpTSFunction fn when fn.PrivateOwner != null => fn.BindThis(receiver),
            SharpTSAsyncFunction fn when fn.PrivateOwner != null => fn.BindThisValue(receiver),
            SharpTSGeneratorFunction fn when fn.PrivateOwner != null => fn.BindToReceiver(receiver!),
            SharpTSAsyncGeneratorFunction fn when fn.PrivateOwner != null => fn.BindToReceiver(receiver!),
            _ => null,
        };

    private static object? InvokeWithThis(Interpreter interp, ISharpTSCallable callable, object? thisArg, List<object?> args)
    {
        if (callable is SharpTSClass klass)
            klass.ValidateCall();
        if (callable is SharpTSProxy proxy)
            return proxy.TrapApply(thisArg, args, interp);

        if (callable is CompiledCallableAdapter compiled)
            return compiled.CallWithThis(thisArg, args);

        if (callable is SharpTSTextDecoder.TextDecoderDecodeMethod decode)
            return decode.CallWithReceiver(thisArg, args);

        if (BindStaticReceiver(callable, thisArg) is { } staticMethod)
            return staticMethod.Call(interp, args);

        if (BindClassReceiver(callable, thisArg) is { } classMethod)
            return classMethod.Call(interp, args);

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
                && builtIn.Name is
                    "includes" or "flat" or "flatMap" or "copyWithin" or
                    "slice" or "sort" or "splice" or "toLocaleString" or
                    "toReversed" or "toSpliced" or "fill" or "filter" or
                    "indexOf" or "lastIndexOf")
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
    internal double Length { get; }
    private SharpTSObject? _ownProperties;
    internal SharpTSObject OwnPropertyStorage => _ownProperties ??= FunctionObjectProperties.Create(Name, Length);

    internal void SetPrototypeSnapshot(object? prototype)
        => FunctionObjectProperties.SetExplicitPrototype(OwnPropertyStorage, prototype);

    internal void FreezeOwnProperties()
    {
        OwnPropertyStorage.Freeze();
        PropertyDescriptorStore.Freeze(this);
    }

    internal void SealOwnProperties()
    {
        OwnPropertyStorage.Seal();
        PropertyDescriptorStore.Seal(this);
    }

    internal void PreventExtensions()
    {
        OwnPropertyStorage.PreventExtensions();
        PropertyDescriptorStore.PreventExtensions(this);
    }

    internal bool DeleteProperty(string name, bool strictMode)
        => GetOwnPropertyDescriptor(name) is null || OwnPropertyStorage.DeletePropertyStrict(name, strictMode);

    internal ISharpTSCallable Target => _target;

    internal List<object?> PrependArguments(IEnumerable<object?> arguments) => [.. _boundArgs, .. arguments];

    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => (_ownProperties ??= new SharpTSObject([])).DefineProperty(name, descriptor);

    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _ownProperties?.GetOwnPropertyDescriptor(name);

    public void SetProperty(string name, object? value, bool strictMode)
        => (_ownProperties ??= new SharpTSObject([]))
            .SetPropertyStrict(name, value, strictMode);

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

    public BoundFunction(ISharpTSCallable target, object? thisArg, List<object?> boundArgs,
        bool ignoreThisArg = false, string? targetName = null, double? targetLength = null)
    {
        _target = target;
        _thisArg = thisArg;
        _boundArgs = boundArgs;
        _ignoreThisArg = ignoreThisArg;
        Length = Math.Max(0, (targetLength ?? target.Arity()) - boundArgs.Count);

        // Get base function name and prefix with "bound "
        var baseName = targetName ?? GetBaseName(target);
        Name = string.IsNullOrEmpty(baseName) ? "bound " : $"bound {baseName}";
    }

    private static string GetBaseName(ISharpTSCallable target)
    {
        return FunctionBuiltIns.GetFunctionName(target);
    }

    public int Arity() => (int)Math.Min(int.MaxValue, Length);

    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        // Combine bound args + call args into a single array
        var combined = new RuntimeValue[_boundArgs.Count + arguments.Length];
        for (int i = 0; i < _boundArgs.Count; i++)
            combined[i] = RuntimeValue.FromBoxed(_boundArgs[i]);
        arguments.CopyTo(combined.AsSpan(_boundArgs.Count));

        if (_target is SharpTSClass klass)
            klass.ValidateCall();

        if (!_ignoreThisArg && _target is SharpTSProxy proxy)
            return RuntimeValue.FromBoxed(proxy.TrapApply(
                _thisArg, CallableInterop.ToBoxedList(combined), interpreter));

        // Static methods preserve the exact strict receiver, including null
        // and primitives, rather than using a synthetic instance wrapper.
        if (!_ignoreThisArg && FunctionBuiltIns.BindStaticReceiver(_target, _thisArg) is { } staticMethod)
            return staticMethod.CallV2(interpreter, combined);

        // Canonical class method values keep the supplied receiver itself,
        // including null/primitives, rather than a synthetic instance view.
        if (!_ignoreThisArg && FunctionBuiltIns.BindClassReceiver(_target, _thisArg) is { } classMethod)
            return classMethod.CallV2(interpreter, combined);
        if (!_ignoreThisArg && _target is BuiltInMethod intrinsic && _target is IUndefinedThisDefault)
            return intrinsic.Bind(_thisArg).CallV2(interpreter, combined);

        if (!_ignoreThisArg && _target is CompiledCallableAdapter compiled)
            return RuntimeValue.FromBoxed(compiled.CallWithThis(_thisArg, CallableInterop.ToBoxedList(combined)));

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

        if (_target is SharpTSClass klass)
            klass.ValidateCall();

        if (!_ignoreThisArg && _target is SharpTSProxy proxy)
            return proxy.TrapApply(_thisArg, combinedArgs, interpreter);

        if (!_ignoreThisArg && FunctionBuiltIns.BindStaticReceiver(_target, _thisArg) is { } staticMethod)
            return staticMethod.Call(interpreter, combinedArgs);

        if (!_ignoreThisArg && FunctionBuiltIns.BindClassReceiver(_target, _thisArg) is { } classMethod)
            return classMethod.Call(interpreter, combinedArgs);
        if (!_ignoreThisArg && _target is BuiltInMethod intrinsic && _target is IUndefinedThisDefault)
            return intrinsic.Bind(_thisArg).Call(interpreter, combinedArgs);

        if (!_ignoreThisArg && _target is CompiledCallableAdapter compiled)
            return compiled.CallWithThis(_thisArg, combinedArgs);

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
        return fn.BindThis(thisArg);
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
