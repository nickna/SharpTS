using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required dynamic function-construction and general constructor-value dispatch metadata.</summary>
public sealed class EmittedDynamicConstructionRuntime
{
    internal EmittedDynamicConstructionRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _functionBodyEmitted;

    private MethodBuilder? _function;
    public MethodBuilder Function
    {
        get => Require(_function);
        internal set => SetHandle(ref _function, value);
    }

    private MethodBuilder? _value;
    public MethodBuilder Value
    {
        get => Require(_value);
        internal set => SetHandle(ref _value, value);
    }

    internal void MarkFunctionBodyEmitted()
    {
        EnsureMutable();
        _ = Function;
        if (_functionBodyEmitted)
            throw new InvalidOperationException("Dynamic construction function body is already emitted.");
        _functionBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Dynamic construction metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Dynamic construction metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Dynamic construction metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Function;
        _ = Value;
        if (!_functionBodyEmitted)
            throw new InvalidOperationException("Dynamic construction function body has not been emitted.");
        IsComplete = true;
    }
}
