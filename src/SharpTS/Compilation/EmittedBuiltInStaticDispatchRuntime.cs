using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required built-in static-member lookup declaration for one emitted assembly.</summary>
public sealed class EmittedBuiltInStaticDispatchRuntime
{
    internal EmittedBuiltInStaticDispatchRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _lookup;
    private bool _bodyEmitted;
    /// <summary>Declared before property consumers; its body is emitted after backing methods exist.</summary>
    public MethodBuilder Lookup
    {
        get => _lookup ?? throw new InvalidOperationException("Built-in static dispatch has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_lookup is not null)
                throw new InvalidOperationException("Built-in static dispatch has already been declared.");
            _lookup = value;
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Built-in static-dispatch metadata emission is already complete.");
    }

    internal void MarkLookupBodyEmitted()
    {
        EnsureMutable();
        _ = Lookup;
        if (_bodyEmitted)
            throw new InvalidOperationException("Built-in static dispatch body has already been emitted.");
        _bodyEmitted = true;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Lookup;
        if (!_bodyEmitted)
            throw new InvalidOperationException("Built-in static dispatch body has not been emitted.");
        IsComplete = true;
    }
}
