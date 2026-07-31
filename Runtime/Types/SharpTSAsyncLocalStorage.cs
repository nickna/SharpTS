using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Node.js AsyncLocalStorage.
/// Uses .NET's AsyncLocal&lt;T&gt; to automatically propagate context across
/// await boundaries, Promise chains, and timer callbacks.
/// </summary>
public class SharpTSAsyncLocalStorage
{
    /// <summary>Sentinel value to distinguish "not set" from "explicitly set to null".</summary>
    private static readonly object NotSet = new();

    private readonly AsyncLocal<object?> _store = new();
    private bool _enabled = true;

    public SharpTSAsyncLocalStorage()
    {
        _store.Value = NotSet;
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "run" => BuiltInMethod.CreateV2("run", 2, int.MaxValue, Run),
            "getStore" => BuiltInMethod.CreateV2("getStore", 0, GetStore),
            "enterWith" => BuiltInMethod.CreateV2("enterWith", 1, EnterWith),
            "exit" => BuiltInMethod.CreateV2("exit", 1, int.MaxValue, Exit),
            "disable" => BuiltInMethod.CreateV2("disable", 0, Disable),
            _ => null
        };
    }

    /// <summary>
    /// Runs a callback with the given store value. The store is visible via getStore()
    /// inside the callback and any async operations it spawns. The previous store
    /// is restored after the callback completes.
    /// </summary>
    private RuntimeValue Run(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("run() requires store and callback arguments");

        var store = args[0].ToObject();
        var callback = args[1].ToObject();

        // Collect extra args for the callback
        var callbackArgs = new List<object?>(Math.Max(0, args.Length - 2));
        for (int i = 2; i < args.Length; i++)
            callbackArgs.Add(args[i].ToObject());

        var oldValue = _store.Value;
        _store.Value = store;
        try
        {
            return RuntimeValue.FromBoxed(InvokeCallback(interpreter, callback, callbackArgs));
        }
        finally
        {
            _store.Value = oldValue;
        }
    }

    /// <summary>
    /// Returns the current store value, or null (undefined) if not inside a run() call
    /// or if the storage has been disabled.
    /// </summary>
    private RuntimeValue GetStore(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_enabled) return RuntimeValue.Null;
        var val = _store.Value;
        return ReferenceEquals(val, NotSet) ? RuntimeValue.Null : RuntimeValue.FromBoxed(val);
    }

    /// <summary>
    /// Sets the store for the current async context without running a callback.
    /// The store will be visible in subsequent code in this async flow.
    /// </summary>
    private RuntimeValue EnterWith(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("enterWith() requires a store argument");

        _store.Value = args[0].ToObject();
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Runs a callback with the store cleared (set to null/undefined).
    /// The previous store is restored after the callback completes.
    /// </summary>
    private RuntimeValue Exit(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("exit() requires a callback argument");

        var callback = args[0].ToObject();
        var callbackArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            callbackArgs.Add(args[i].ToObject());

        var oldValue = _store.Value;
        _store.Value = NotSet;
        try
        {
            return RuntimeValue.FromBoxed(InvokeCallback(interpreter, callback, callbackArgs));
        }
        finally
        {
            _store.Value = oldValue;
        }
    }

    /// <summary>
    /// Disables the AsyncLocalStorage instance. After calling disable(),
    /// getStore() will always return undefined.
    /// </summary>
    private RuntimeValue Disable(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _enabled = false;
        _store.Value = null;
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Invokes a callback supporting multiple callable types.
    /// In interpreter mode, callbacks are ISharpTSCallable.
    /// In compiled mode, callbacks have an Invoke(object[]) method (e.g. $TSFunction).
    /// </summary>
    private static object? InvokeCallback(Interp? interpreter, object? callback, List<object?> args)
    {
        if (RuntimeCallableDispatcher.IsCallable(callback))
            return RuntimeCallableDispatcher.Invoke(interpreter, callback, args.ToArray());

        // Preserve the managed .NET interop fallback for arbitrary objects
        // exposing Invoke(object[]) through the managed-only structural seam.
        var invokeMethod = callback == null
            ? null
            : ManagedStructuralClrReflection.GetPublicMethodBySignature(
                callback.GetType(), "Invoke", [typeof(object?[])]);
        if (invokeMethod != null)
            return invokeMethod.Invoke(callback, [args.ToArray()]);

        throw new Exception("Callback must be a function");
    }

    public override string ToString() => "AsyncLocalStorage {}";
}
