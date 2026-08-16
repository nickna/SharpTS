using System.Reflection;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Callable bound to a <see cref="DotNetInstance"/> or <see cref="DotNetClass"/> that
/// implements <c>addEventListener(name, handler)</c> / <c>removeEventListener(name, handler)</c>.
/// </summary>
/// <remarks>
/// <para>
/// TypeScript has no syntax for <c>+=</c> on .NET events, so we expose a DOM-style API.
/// The binder looks up the <see cref="EventInfo"/> by JS-facing name (PascalCase fallback),
/// builds a <see cref="DotNetDelegateShim"/> with the correct event-handler type, and
/// hooks/unhooks via reflection.
/// </para>
/// <para>
/// The <i>same</i> <see cref="ISharpTSCallable"/> reference must be passed to
/// <c>removeEventListener</c> as was passed to <c>addEventListener</c> — we key the
/// subscription dictionary by <c>(eventName, callable)</c> so we can look up the
/// compiled shim to pass back to <see cref="EventInfo.RemoveEventHandler"/>.
/// </para>
/// <para>
/// Threading: see <see cref="DotNetDelegateShim"/> — events that fire from a background
/// thread hit the interpreter on that thread, which is undefined behavior.
/// </para>
/// </remarks>
internal sealed class DotNetEventBinder : ISharpTSCallable
{
    private readonly Type _targetType;
    private readonly object? _receiver; // null for static events
    private readonly Dictionary<(string, ISharpTSCallable), Delegate> _subscriptions;
    private readonly bool _isAdd;

    public DotNetEventBinder(
        Type targetType,
        object? receiver,
        Dictionary<(string, ISharpTSCallable), Delegate> subscriptions,
        bool isAdd)
    {
        _targetType = targetType;
        _receiver = receiver;
        _subscriptions = subscriptions;
        _isAdd = isAdd;
    }

    public int Arity() => 2;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        string opName = _isAdd ? "addEventListener" : "removeEventListener";

        if (arguments.Count < 2)
        {
            throw new ThrowException(new SharpTSError(
                $"{opName} requires (eventName: string, handler: function).") { Name = "TypeError" });
        }

        if (arguments[0] is not string eventName)
        {
            throw new ThrowException(new SharpTSError(
                $"{opName}: first argument must be a string event name.") { Name = "TypeError" });
        }

        if (arguments[1] is not ISharpTSCallable handler)
        {
            throw new ThrowException(new SharpTSError(
                $"{opName}: second argument must be a function.") { Name = "TypeError" });
        }

        var evt = DotNetTypeRegistry.GetEvent(_targetType, eventName, isStatic: _receiver == null)
            ?? throw new ThrowException(new SharpTSError(
                $"Event '{eventName}' not found on '{_targetType.FullName}'.") { Name = "TypeError" });

        var key = (eventName, handler);

        if (_isAdd)
        {
            // Idempotent: subscribing the same (name, callable) twice is a no-op.
            if (_subscriptions.ContainsKey(key)) return SharpTSUndefined.Instance;
            var shim = DotNetDelegateShim.Create(evt.EventHandlerType!, handler, interpreter);
            _subscriptions[key] = shim;
            DotNetInstance.InvokeWithMapping(() => evt.AddEventHandler(_receiver, shim));
        }
        else
        {
            if (_subscriptions.TryGetValue(key, out var shim))
            {
                DotNetInstance.InvokeWithMapping(() => evt.RemoveEventHandler(_receiver, shim));
                _subscriptions.Remove(key);
            }
            // If not subscribed, silently no-op (matches DOM behavior).
        }

        return SharpTSUndefined.Instance;
    }

    // ----------------------------------------------------------------------
    // Compile-mode entry points
    // ----------------------------------------------------------------------

    /// <summary>
    /// Compile-mode subscription registry. Maps <c>(instance-or-type, eventName, tsFunction)</c>
    /// to the compiled delegate shim so <see cref="CompiledRemoveEventListener"/> can match
    /// the subscription set up by <see cref="CompiledAddEventListener"/>.
    /// </summary>
    /// <remarks>
    /// Keyed by <see cref="object"/> identity — static-event subscriptions use the
    /// <see cref="Type"/> itself as the identity (safe since <c>Type</c> instances are canonical).
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        CompiledSubscriptionKey, Delegate> _compiledSubscriptions = new();

    private readonly record struct CompiledSubscriptionKey(object Owner, string EventName, object TsFunction);

    /// <summary>
    /// Compile-mode helper called by emitted IL for
    /// <c>instance.addEventListener(name, handler)</c>. The caller threads the target
    /// <see cref="Type"/> through so this helper doesn't need to re-resolve it.
    /// Pass <paramref name="receiver"/> = null and a non-null <paramref name="targetType"/>
    /// for static events.
    /// </summary>
    public static void CompiledAddEventListener(object? receiver, Type targetType, string eventName, object tsFunction)
    {
        if (targetType == null) throw new ArgumentNullException(nameof(targetType));
        if (eventName == null) throw new ArgumentNullException(nameof(eventName));
        if (tsFunction == null) throw new ArgumentNullException(nameof(tsFunction));

        var evt = DotNetTypeRegistry.GetEvent(targetType, eventName, isStatic: receiver == null)
            ?? throw new Runtime.Exceptions.ThrowException(new SharpTSError(
                $"Event '{eventName}' not found on '{targetType.FullName}'.") { Name = "TypeError" });

        var key = new CompiledSubscriptionKey(
            Owner: receiver ?? (object)targetType,
            EventName: eventName,
            TsFunction: tsFunction);

        // Idempotent: subscribing the same (owner, name, handler) twice is a no-op.
        if (_compiledSubscriptions.ContainsKey(key)) return;

        var shim = DotNetDelegateShim.CreateForTSFunction(evt.EventHandlerType!, tsFunction);
        if (!_compiledSubscriptions.TryAdd(key, shim)) return;

        DotNetInstance.InvokeWithMapping(() => evt.AddEventHandler(receiver, shim));
    }

    /// <summary>
    /// Compile-mode counterpart of <c>removeEventListener</c>. Silently no-ops if the
    /// subscription isn't found (matches DOM semantics).
    /// </summary>
    public static void CompiledRemoveEventListener(object? receiver, Type targetType, string eventName, object tsFunction)
    {
        if (targetType == null) return;
        if (eventName == null) return;
        if (tsFunction == null) return;

        var key = new CompiledSubscriptionKey(
            Owner: receiver ?? (object)targetType,
            EventName: eventName,
            TsFunction: tsFunction);

        if (!_compiledSubscriptions.TryRemove(key, out var shim)) return;

        var evt = DotNetTypeRegistry.GetEvent(targetType, eventName, isStatic: receiver == null);
        if (evt == null) return; // event vanished? nothing to unhook

        DotNetInstance.InvokeWithMapping(() => evt.RemoveEventHandler(receiver, shim));
    }
}
