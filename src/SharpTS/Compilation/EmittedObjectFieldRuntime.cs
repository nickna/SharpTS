using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required object-field protocol declarations for one emitted assembly.</summary>
public sealed class EmittedObjectFieldRuntime
{
    internal EmittedObjectFieldRuntime() { }
    public bool IsComplete { get; private set; }

    private Type? _interface;
    public Type Interface
    {
        get => Require(_interface);
        internal set => SetHandle(ref _interface, value);
    }

    private MethodInfo? _getProperty;
    public MethodInfo GetProperty
    {
        get => Require(_getProperty);
        internal set => SetHandle(ref _getProperty, value);
    }

    private MethodInfo? _setProperty;
    public MethodInfo SetProperty
    {
        get => Require(_setProperty);
        internal set => SetHandle(ref _setProperty, value);
    }

    private MethodInfo? _hasProperty;
    public MethodInfo HasProperty
    {
        get => Require(_hasProperty);
        internal set => SetHandle(ref _hasProperty, value);
    }

    private MethodInfo? _fieldsGetter;
    public MethodInfo FieldsGetter
    {
        get => Require(_fieldsGetter);
        internal set => SetHandle(ref _fieldsGetter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object-field metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object-field metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object-field metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Interface;
        _ = GetProperty;
        _ = SetProperty;
        _ = HasProperty;
        _ = FieldsGetter;
        IsComplete = true;
    }
}
