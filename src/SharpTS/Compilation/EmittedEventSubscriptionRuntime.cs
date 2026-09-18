using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required .NET event-subscription declarations for one emitted assembly.</summary>
public sealed class EmittedEventSubscriptionRuntime
{
    internal EmittedEventSubscriptionRuntime() { }
    public bool IsComplete { get; private set; }

    private FieldBuilder? _entries;
    public FieldBuilder Entries
    {
        get => Require(_entries);
        internal set => SetHandle(ref _entries, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => SetHandle(ref _add, value);
    }

    private MethodBuilder? _remove;
    public MethodBuilder Remove
    {
        get => Require(_remove);
        internal set => SetHandle(ref _remove, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Event-subscription metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Event-subscription metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Event-subscription metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Entries;
        _ = Add;
        _ = Remove;
        IsComplete = true;
    }
}
