using System.Linq.Expressions;
using System.Reflection;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Builds a .NET <see cref="Delegate"/> that forwards to an <see cref="ISharpTSCallable"/>.
/// Used when a <c>@DotNetType</c> method or event takes a delegate parameter
/// (<c>Action</c>, <c>Func&lt;T&gt;</c>, <c>Predicate&lt;T&gt;</c>, <c>EventHandler</c>, etc.).
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading contract:</b> the returned delegate MUST be invoked on the SharpTS event-loop
/// thread. Off-thread invocation is <i>undefined behavior</i> — the interpreter is not
/// thread-safe, so races, corrupted state, or crashes are possible. A future enhancement
/// may add opt-in marshalling back to the event loop (e.g. via a <c>@DotNetCallback</c> hint).
/// </para>
/// <para>
/// Exceptions thrown from the TS callable (including <c>ThrowException</c> from a TS
/// <c>throw</c>) propagate synchronously out of the delegate invocation, so .NET callers
/// see them as their own exception.
/// </para>
/// </remarks>
internal static class DotNetDelegateShim
{
    /// <summary>
    /// Compiles a delegate of type <paramref name="delegateType"/> whose body invokes
    /// <paramref name="callable"/> with marshalled arguments and the supplied interpreter.
    /// </summary>
    public static Delegate Create(Type delegateType, ISharpTSCallable callable, Interpreter interpreter)
    {
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
        {
            throw new ArgumentException(
                $"Type '{delegateType.FullName}' is not a delegate type.", nameof(delegateType));
        }

        // Native AOT cannot synthesize an arbitrary delegate shape. The official
        // catalog carries direct adapters for the callback shapes exposed by its
        // BCL profile; generated custom hosts add equivalent strongly typed thunks.
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            if (TryCreateCatalogedDelegate(delegateType, callable, interpreter, out Delegate? cataloged))
                return cataloged;
            throw new PlatformNotSupportedException(
                $"Delegate shape '{delegateType.FullName}' is not present in this native SharpTS catalog.");
        }

        var invoke = ManagedDotNetInterop.GetMethod(delegateType, "Invoke")
            ?? throw new InvalidOperationException(
                $"Delegate type '{delegateType.FullName}' has no Invoke method.");

        var parameters = invoke.GetParameters();
        var paramExprs = parameters
            .Select((p, idx) => Expression.Parameter(p.ParameterType, p.Name ?? $"arg{idx}"))
            .ToArray();

        // Build object?[] argsArray — value-type params must be boxed via Expression.Convert.
        Expression argsArray;
        if (paramExprs.Length == 0)
        {
            argsArray = Expression.Constant(Array.Empty<object?>(), typeof(object?[]));
        }
        else
        {
            var boxed = paramExprs
                .Select(pe => pe.Type.IsValueType
                    ? (Expression)Expression.Convert(pe, typeof(object))
                    : pe)
                .ToArray();
            argsArray = ManagedDotNetInterop.NewArrayInit(typeof(object), boxed);
        }

        var dispatchMethod = typeof(DotNetDelegateShim).GetMethod(
            nameof(Dispatch), BindingFlags.Static | BindingFlags.NonPublic)!;

        var dispatchCall = Expression.Call(dispatchMethod,
            Expression.Constant(callable, typeof(ISharpTSCallable)),
            Expression.Constant(interpreter, typeof(Interpreter)),
            Expression.Constant(invoke.ReturnType, typeof(Type)),
            argsArray);

        Expression body;
        if (invoke.ReturnType == typeof(void))
        {
            // Discard Dispatch's return value by wrapping in a void block.
            body = Expression.Block(typeof(void), dispatchCall);
        }
        else
        {
            // Expression.Convert handles both reference casts and value-type unboxing.
            body = Expression.Convert(dispatchCall, invoke.ReturnType);
        }

        return ManagedDotNetInterop.CompileLambda(delegateType, body, paramExprs);
    }

    private static bool TryCreateCatalogedDelegate(
        Type delegateType,
        ISharpTSCallable callable,
        Interpreter interpreter,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Delegate? result)
    {
        if (delegateType == typeof(Action))
            result = (Action)(() => Dispatch(callable, interpreter, typeof(void), []));
        else if (delegateType == typeof(Action<string>))
            result = (Action<string>)(value => Dispatch(callable, interpreter, typeof(void), [value]));
        else if (delegateType == typeof(Action<double>))
            result = (Action<double>)(value => Dispatch(callable, interpreter, typeof(void), [value]));
        else if (delegateType == typeof(Func<string>))
            result = (Func<string>)(() => (string)Dispatch(callable, interpreter, typeof(string), [])!);
        else if (delegateType == typeof(Predicate<double>))
            result = (Predicate<double>)(value =>
                (bool)Dispatch(callable, interpreter, typeof(bool), [value])!);
        else if (delegateType == typeof(Comparison<double>))
            result = (Comparison<double>)((left, right) =>
                (int)Dispatch(callable, interpreter, typeof(int), [left, right])!);
        else if (delegateType == typeof(EventHandler))
            result = (EventHandler)((sender, eventArgs) =>
                Dispatch(callable, interpreter, typeof(void), [sender, eventArgs]));
        else if (delegateType == typeof(UnhandledExceptionEventHandler))
            result = (UnhandledExceptionEventHandler)((sender, eventArgs) =>
                Dispatch(callable, interpreter, typeof(void), [sender, eventArgs]));
        else
            result = null;
        return result != null;
    }

    /// <summary>
    /// Runtime dispatcher called by the compiled shim. Wraps each incoming .NET value so
    /// TypeScript sees usable primitives / <see cref="DotNetInstance"/>, invokes the callable,
    /// and converts the result back to the delegate's return type.
    /// </summary>
    private static object? Dispatch(
        ISharpTSCallable callable,
        Interpreter interpreter,
        Type returnType,
        object?[] rawArgs)
    {
        var argsList = new List<object?>(rawArgs.Length);
        for (int i = 0; i < rawArgs.Length; i++)
        {
            argsList.Add(WrapIncoming(rawArgs[i]));
        }

        var result = callable.Call(interpreter, argsList);

        if (returnType == typeof(void)) return null;
        return DotNetMarshaller.Convert(result, returnType, interpreter);
    }

    /// <summary>
    /// Wraps a .NET value flowing into a TS callback so the TS side sees a usable value.
    /// Primitives are normalized (int/long/short/byte/etc. → double, char → string);
    /// complex reference types are wrapped in <see cref="DotNetInstance"/>.
    /// </summary>
    private static object? WrapIncoming(object? value)
    {
        if (value == null) return null;
        return DotNetMarshaller.WrapReturn(value, value.GetType());
    }

    /// <summary>
    /// Compile-mode overload: builds a delegate that dispatches to a compiled
    /// <c>$TSFunction</c> via its <c>object Invoke(object[] args)</c> method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compile-mode path doesn't have an <see cref="Interpreter"/> — compiled code
    /// runs directly as .NET IL. Instead of <see cref="ISharpTSCallable"/> we accept any
    /// object whose type exposes <c>Invoke(object[])</c> (the shape of the emitted
    /// <c>$TSFunction</c> class in compiled DLLs).
    /// </para>
    /// <para>
    /// Same threading contract as <see cref="Create"/>: main-thread only.
    /// </para>
    /// </remarks>
    public static Delegate CreateForTSFunction(Type delegateType, object tsFunction)
    {
        if (tsFunction == null) throw new ArgumentNullException(nameof(tsFunction));
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
        {
            throw new ArgumentException(
                $"Type '{delegateType.FullName}' is not a delegate type.", nameof(delegateType));
        }

        var tsInvoke = ManagedDotNetInterop.GetMethod(
            tsFunction.GetType(), "Invoke", [typeof(object[])])
            ?? throw new InvalidOperationException(
                $"Value of type '{tsFunction.GetType().FullName}' has no 'Invoke(object[])' method — " +
                "expected the emitted $TSFunction shape.");

        var delegateInvoke = ManagedDotNetInterop.GetMethod(delegateType, "Invoke")
            ?? throw new InvalidOperationException(
                $"Delegate type '{delegateType.FullName}' has no Invoke method.");

        var parameters = delegateInvoke.GetParameters();
        var paramExprs = parameters
            .Select((p, idx) => Expression.Parameter(p.ParameterType, p.Name ?? $"arg{idx}"))
            .ToArray();

        // Normalize incoming .NET values for the TS side (int → double, wrap complex objects, etc.)
        // by routing each arg through DotNetMarshaller.WrapReturn.
        var wrapReturn = typeof(DotNetMarshaller).GetMethod(
            nameof(DotNetMarshaller.WrapReturn),
            BindingFlags.Public | BindingFlags.Static)!;

        Expression argsArray;
        if (paramExprs.Length == 0)
        {
            argsArray = Expression.Constant(Array.Empty<object?>(), typeof(object[]));
        }
        else
        {
            var wrapped = paramExprs.Select(pe =>
            {
                Expression boxed = pe.Type.IsValueType
                    ? (Expression)Expression.Convert(pe, typeof(object))
                    : pe;
                return (Expression)Expression.Call(wrapReturn, boxed,
                    Expression.Constant(pe.Type, typeof(Type)));
            }).ToArray();
            argsArray = ManagedDotNetInterop.NewArrayInit(typeof(object), wrapped);
        }

        // Call: tsFunction.Invoke(argsArray) — returns object.
        var targetConst = Expression.Constant(tsFunction, tsFunction.GetType());
        var invokeCall = Expression.Call(targetConst, tsInvoke, argsArray);

        Expression body;
        if (delegateInvoke.ReturnType == typeof(void))
        {
            body = Expression.Block(typeof(void), invokeCall);
        }
        else if (delegateInvoke.ReturnType == typeof(object))
        {
            body = invokeCall;
        }
        else
        {
            // Convert the TS return value back to the delegate's declared return type.
            // No interpreter context is needed for non-delegate return types; a delegate-
            // returning callback in compile mode would require further marshalling that
            // the null interpreter can't perform (Convert will throw).
            var convertMethod = typeof(DotNetMarshaller).GetMethod(
                nameof(DotNetMarshaller.Convert),
                BindingFlags.Public | BindingFlags.Static)!;
            var converted = Expression.Call(convertMethod,
                invokeCall,
                Expression.Constant(delegateInvoke.ReturnType, typeof(Type)),
                Expression.Constant(null, typeof(Execution.Interpreter)));
            body = Expression.Convert(converted, delegateInvoke.ReturnType);
        }

        return ManagedDotNetInterop.CompileLambda(delegateType, body, paramExprs);
    }
}
