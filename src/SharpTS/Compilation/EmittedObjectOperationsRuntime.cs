using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Object value, entry, assignment, comparison and grouping operations for one compilation.</summary>
public sealed class EmittedObjectOperationsRuntime
{
    internal EmittedObjectOperationsRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _values;
    public MethodBuilder Values
    {
        get => Require(_values);
        internal set => SetHandle(ref _values, value);
    }

    private MethodBuilder? _entries;
    public MethodBuilder Entries
    {
        get => Require(_entries);
        internal set => SetHandle(ref _entries, value);
    }

    private MethodBuilder? _fromEntries;
    public MethodBuilder FromEntries
    {
        get => Require(_fromEntries);
        internal set => SetHandle(ref _fromEntries, value);
    }

    private MethodBuilder? _is;
    public MethodBuilder Is
    {
        get => Require(_is);
        internal set => SetHandle(ref _is, value);
    }

    private MethodBuilder? _assign;
    public MethodBuilder Assign
    {
        get => Require(_assign);
        internal set => SetHandle(ref _assign, value);
    }

    private MethodBuilder? _groupBy;
    public MethodBuilder GroupBy
    {
        get => Require(_groupBy);
        internal set => SetHandle(ref _groupBy, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object operation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object operation metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object operation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Values;
        _ = Entries;
        _ = FromEntries;
        _ = Is;
        _ = Assign;
        _ = GroupBy;
        IsComplete = true;
    }
}
