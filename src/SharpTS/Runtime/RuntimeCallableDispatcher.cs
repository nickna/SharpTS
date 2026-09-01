using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime;

/// <summary>
/// Unified dispatcher for invoking any callable runtime value, regardless of
/// whether it originated from the interpreter or compiled mode.
/// </summary>
/// <remarks>
/// SharpTS has at least seven distinct callable shapes that all implement "a
/// function the user can call". Before this helper, every dispatch site
/// (Web Streams, EventEmitter, Proxy, AsyncLocalStorage, etc.) duplicated the
/// matrix and several silently dropped categories — most notably,
/// <see cref="SharpTSEventEmitter.InvokeListenerDirect"/> would silently skip
/// any <see cref="ISharpTSCallable"/> listener (e.g., a
/// <see cref="BuiltInMethod"/> registered as an event listener), so the
/// callback never fired.
///
/// Recognised shapes, in priority order:
/// <list type="number">
///   <item><see cref="ISharpTSCallable"/> — covers
///     <see cref="SharpTSFunction"/>, <see cref="SharpTSArrowFunction"/>,
///     <see cref="BuiltInMethod"/>, <see cref="BuiltInAsyncMethod"/>, and any
///     other interpreter-side callable. Receives a real
///     <see cref="Interp"/> when one is supplied; otherwise <c>null</c>.</item>
///   <item><see cref="SharpTS.Compilation.TSFunction"/> — the non-emitted
///     runtime <c>TSFunction</c> shipped in <c>SharpTS.dll</c>. Direct
///     <c>Invoke(args)</c> call, no reflection.</item>
///   <item>Emitted per-DLL <c>$TSFunction</c>/<c>$BoundTSFunction</c>. Detected
///     by type name; dispatched through a weakly cached closed delegate to
///     <c>InvokeWithThis(thisArg, args)</c>. Reflection is paid once when binding. Honours the
///     synthetic <c>__this</c> first parameter pattern that compiled object
///     literal methods use (see <c>RuntimeEmitter.TSFunction.cs</c>).</item>
///   <item><see cref="Func{TArray, TResult}"/> bound-method delegates created
///     by <c>RuntimeTypes.Methods.CreateBoundMethod</c>.</item>
///   <item><see cref="Action{TArray}"/> for internal listener-style
///     callbacks.</item>
///   <item>Generic <see cref="Delegate"/> fallback via
///     <see cref="Delegate.DynamicInvoke"/>.</item>
/// </list>
/// </remarks>
public static class RuntimeCallableDispatcher
{
    // Per-type method-info caches keyed by the runtime type of the callable.
    // We pay reflection on first encounter only.
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _invokeWithThisCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _invokeCache = new();
    private static readonly ConditionalWeakTable<object, Func<object?, object?[], object?>>
        _boundInvokeWithThisCache = new();

    /// <summary>
    /// Invokes <paramref name="callable"/> with the supplied arguments. Returns
    /// the call result, or <c>null</c> if <paramref name="callable"/> is null
    /// or no recognised shape matches.
    /// </summary>
    public static object? Invoke(Interp? interpreter, object? callable, params object?[] args)
        => InvokeWithThis(interpreter, callable, null, args);

    /// <summary>
    /// Invokes a callable with an explicit JavaScript receiver. This is the
    /// cross-runtime equivalent of Call(F, thisArgument, argumentsList).
    /// </summary>
    public static object? InvokeWithThis(
        Interp? interpreter,
        object? callable,
        object? thisArg,
        params object?[] args)
    {
        if (callable is null) return null;

        switch (callable)
        {
            case ISharpTSCallable tsCallable:
                return interpreter != null
                    ? FunctionBuiltIns.CallWithThis(
                        interpreter, tsCallable, thisArg, args.ToList())
                    : tsCallable.Call(null!, args.ToList());

            case SharpTS.Compilation.TSFunction tsFunc:
                return tsFunc.Invoke(args);

            case Func<object?[], object?> boundMethod:
                return boundMethod(args);

            case Action<object?[]> actionListener:
                actionListener(args);
                return null;
        }

        // Emitted $TSFunction / $BoundTSFunction live in compiled DLLs and are
        // not visible to SharpTS.dll at compile time. Detect by type name and
        // bind InvokeWithThis once (it understands the synthetic __this
        // first-parameter contract), then use a normal delegate call.
        var type = callable.GetType();
        if (ManagedEmittedShapeReflection.IsShape(type, ManagedEmittedShape.Function))
        {
            var invokeWithThis = _invokeWithThisCache.GetOrAdd(type, t =>
                ManagedEmittedShapeReflection.GetPublicMethod(
                    t, ManagedEmittedShape.Function, "InvokeWithThis",
                    [typeof(object), typeof(object[])]));
            if (invokeWithThis != null)
            {
                if (!_boundInvokeWithThisCache.TryGetValue(callable, out var boundInvoke))
                {
                    boundInvoke = _boundInvokeWithThisCache.GetValue(callable, target =>
                        (Func<object?, object?[], object?>)invokeWithThis.CreateDelegate(
                            typeof(Func<object?, object?[], object?>), target));
                }
                return boundInvoke(thisArg, args);
            }

            var invoke = _invokeCache.GetOrAdd(type, t =>
                ManagedEmittedShapeReflection.GetPublicMethod(
                    t, ManagedEmittedShape.Function, "Invoke", [typeof(object[])]));
            if (invoke != null)
            {
                return InvokeReflected(invoke, callable, [args]);
            }
        }

        if (callable is Delegate del)
        {
            try
            {
                return del.DynamicInvoke(new object[] { args });
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        return null;
    }

    private static object? InvokeReflected(
        MethodInfo method, object target, object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// Returns whether the given value matches one of the recognised callable
    /// shapes.
    /// </summary>
    public static bool IsCallable(object? value)
    {
        if (value is null) return false;
        if (value is ISharpTSCallable) return true;
        if (value is SharpTS.Compilation.TSFunction) return true;
        if (value is Func<object?[], object?>) return true;
        if (value is Action<object?[]>) return true;
        if (value is Delegate) return true;
        return ManagedEmittedShapeReflection.IsShape(
            value.GetType(), ManagedEmittedShape.Function);
    }

    /// <summary>
    /// Clears the per-Type method-info caches. Required when collectible
    /// AssemblyLoadContexts unload, since cached <see cref="MethodInfo"/>
    /// values strongly back-reference their declaring <see cref="Type"/>
    /// (the cache key), pinning emitted <c>$TSFunction</c>/<c>$BoundTSFunction</c>
    /// types — and through them, their entire assembly — indefinitely.
    /// See issue #109.
    /// </summary>
    public static void ClearCaches()
    {
        _invokeWithThisCache.Clear();
        _invokeCache.Clear();
        _boundInvokeWithThisCache.Clear();
    }
}
