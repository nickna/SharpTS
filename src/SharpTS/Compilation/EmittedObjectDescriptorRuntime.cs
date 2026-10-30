using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required public Object descriptor operations for one compilation.</summary>
public sealed class EmittedObjectDescriptorRuntime
{
    internal EmittedObjectDescriptorRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _getOwnPropertyDescriptorBodyEmitted;
    private bool _getOwnPropertyDescriptorValueFormBodyEmitted;

    private MethodBuilder? _defineProperty;
    public MethodBuilder DefineProperty
    {
        get => Require(_defineProperty);
        internal set => SetHandle(ref _defineProperty, value);
    }

    private MethodBuilder? _getOwnPropertyDescriptor;
    public MethodBuilder GetOwnPropertyDescriptor
    {
        get => Require(_getOwnPropertyDescriptor);
        internal set => SetHandle(ref _getOwnPropertyDescriptor, value);
    }

    private MethodBuilder? _getOwnPropertyDescriptorValueForm;
    public MethodBuilder GetOwnPropertyDescriptorValueForm
    {
        get => Require(_getOwnPropertyDescriptorValueForm);
        internal set => SetHandle(ref _getOwnPropertyDescriptorValueForm, value);
    }

    private MethodBuilder? _defineProperties;
    public MethodBuilder DefineProperties
    {
        get => Require(_defineProperties);
        internal set => SetHandle(ref _defineProperties, value);
    }

    private MethodBuilder? _getOwnPropertyDescriptors;
    public MethodBuilder GetOwnPropertyDescriptors
    {
        get => Require(_getOwnPropertyDescriptors);
        internal set => SetHandle(ref _getOwnPropertyDescriptors, value);
    }

    internal void MarkGetOwnPropertyDescriptorBodyEmitted()
    {
        EnsureMutable();
        if (_getOwnPropertyDescriptorBodyEmitted)
            throw new InvalidOperationException("Object descriptor lookup body emission is already complete.");
        _getOwnPropertyDescriptorBodyEmitted = true;
    }

    internal void MarkGetOwnPropertyDescriptorValueFormBodyEmitted()
    {
        EnsureMutable();
        if (_getOwnPropertyDescriptorValueFormBodyEmitted)
            throw new InvalidOperationException("Object descriptor value-form body emission is already complete.");
        _getOwnPropertyDescriptorValueFormBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object descriptor metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object descriptor metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object descriptor metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = DefineProperty;
        _ = GetOwnPropertyDescriptor;
        _ = GetOwnPropertyDescriptorValueForm;
        _ = DefineProperties;
        _ = GetOwnPropertyDescriptors;
        if (!_getOwnPropertyDescriptorBodyEmitted)
            throw new InvalidOperationException("Object descriptor lookup body has not been emitted.");
        if (!_getOwnPropertyDescriptorValueFormBodyEmitted)
            throw new InvalidOperationException("Object descriptor value-form body has not been emitted.");
        IsComplete = true;
    }
}
