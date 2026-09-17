using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Date prototype declarations and optional Date implementation for one compilation.</summary>
public sealed class EmittedDateRuntime
{
    internal EmittedDateRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _prototypeBodyEmitted;

    private FieldBuilder? _prototype;
    public FieldBuilder Prototype
    {
        get => Require(_prototype);
        internal set => SetHandle(ref _prototype, value);
    }

    private MethodBuilder? _populatePrototype;
    public MethodBuilder PopulatePrototype
    {
        get => Require(_populatePrototype);
        internal set => SetHandle(ref _populatePrototype, value);
    }

    public EmittedDateImplementation? Implementation { get; private set; }
    public EmittedDateImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("Date implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("Date implementation emission has already started.");
        Implementation = new EmittedDateImplementation();
    }

    internal void MarkPrototypeBodyEmitted()
    {
        EnsureMutable();
        if (_prototypeBodyEmitted)
            throw new InvalidOperationException("Date prototype body emission is already complete.");
        _prototypeBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Date metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Date metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Prototype;
        _ = PopulatePrototype;
        if (!_prototypeBodyEmitted)
            throw new InvalidOperationException("Date prototype body has not been emitted.");
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
