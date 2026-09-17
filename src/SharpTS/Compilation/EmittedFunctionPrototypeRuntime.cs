using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Function prototype singleton and staged population declarations.</summary>
public sealed class EmittedFunctionPrototypeRuntime
{
    internal EmittedFunctionPrototypeRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _populateBodyEmitted;

    private FieldBuilder? _prototype;
    /// <summary>Function.prototype singleton dict, populated lazily with $TSFunction wrappers for call/apply/bind/toString/constructor (ECMA-262 §20.2.3).</summary>
    public FieldBuilder Prototype
    {
        get => Require(_prototype);
        internal set => SetHandle(ref _prototype, value);
    }

    private MethodBuilder? _populate;
    /// <summary>Idempotent populate for <see cref="Prototype"/>.</summary>
    public MethodBuilder Populate
    {
        get => Require(_populate);
        internal set => SetHandle(ref _populate, value);
    }

    internal void MarkPopulateBodyEmitted()
    {
        EnsureMutable();
        _ = Populate;
        if (_populateBodyEmitted)
            throw new InvalidOperationException("Function prototype populate body is already emitted.");
        _populateBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function prototype metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function prototype metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function prototype metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Prototype;
        _ = Populate;
        if (!_populateBodyEmitted)
            throw new InvalidOperationException("Function prototype populate body has not been emitted.");
        IsComplete = true;
    }
}
