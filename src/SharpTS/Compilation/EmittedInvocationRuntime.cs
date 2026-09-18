using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required dynamic value, receiver-aware and zero-argument invocation metadata.</summary>
public sealed class EmittedInvocationRuntime
{
    internal EmittedInvocationRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _methodBodyEmitted;

    private MethodBuilder? _value;
    public MethodBuilder Value
    {
        get => Require(_value);
        internal set => SetHandle(ref _value, value);
    }

    private MethodBuilder? _method;
    public MethodBuilder Method
    {
        get => Require(_method);
        internal set => SetHandle(ref _method, value);
    }

    private MethodBuilder? _method0;
    public MethodBuilder Method0
    {
        get => Require(_method0);
        internal set => SetHandle(ref _method0, value);
    }

    internal void MarkMethodBodyEmitted()
    {
        EnsureMutable();
        _ = Method;
        if (_methodBodyEmitted)
            throw new InvalidOperationException("Invocation method body is already emitted.");
        _methodBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Invocation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Invocation metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Invocation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Value;
        _ = Method;
        _ = Method0;
        if (!_methodBodyEmitted)
            throw new InvalidOperationException("Invocation method body has not been emitted.");
        IsComplete = true;
    }
}
