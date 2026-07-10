namespace SharpTS.Runtime.Types;

using SharpTS.Runtime.BuiltIns;

/// <summary>
/// Singleton for the JavaScript process object.
/// Extends EventEmitter to support process.on('exit'), process.on('uncaughtException'), etc.
/// </summary>
public class SharpTSProcess : SharpTSEventEmitter
{
    public static readonly SharpTSProcess Instance = new();
    private SharpTSProcess() { }

    // Expando storage for user-assigned properties (process is an ordinary
    // mutable object in Node: `process.myFlag = true` must round-trip).
    private Dictionary<string, object?>? _expando;

    /// <summary>
    /// Resolves process members: process-specific surface first, then user
    /// expando properties, then the inherited EventEmitter methods.
    /// </summary>
    public override object? GetMember(string name)
    {
        var member = ProcessBuiltIns.GetOwnMember(name);
        if (member != null) return member;
        if (_expando != null && _expando.TryGetValue(name, out var expando)) return expando;
        return base.GetMember(name);
    }

    /// <summary>
    /// Handles property assignment: process-managed setters (exitCode, title,
    /// deprecation flags) first, everything else lands in expando storage.
    /// </summary>
    public void SetProcessMember(string name, object? value)
    {
        if (ProcessBuiltIns.SetMember(name, value)) return;
        (_expando ??= new Dictionary<string, object?>())[name] = value;
    }

    /// <summary>
    /// Lazily installs OS signal handlers when a signal-event listener is added
    /// (process.on('SIGINT', …)).
    /// </summary>
    protected override void OnListenerAdded(string eventName)
        => ProcessBuiltIns.OnProcessListenerAdded(eventName);

    /// <summary>Clears expando state (test isolation).</summary>
    internal void ClearExpando() => _expando = null;

    public override string ToString() => "[object process]";
}
