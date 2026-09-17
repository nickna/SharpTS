using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required RegExp prototype declarations and optional implementation for one compilation.</summary>
public sealed class EmittedRegExpRuntime
{
    internal EmittedRegExpRuntime() { }
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

    public EmittedRegExpImplementation? Implementation { get; private set; }
    public EmittedRegExpImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("RegExp implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("RegExp implementation emission has already started.");
        Implementation = new EmittedRegExpImplementation();
    }

    internal void MarkPrototypeBodyEmitted()
    {
        EnsureMutable();
        if (_prototypeBodyEmitted)
            throw new InvalidOperationException("RegExp prototype body emission is already complete.");
        _prototypeBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("RegExp metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("RegExp metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("RegExp metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Prototype;
        _ = PopulatePrototype;
        if (!_prototypeBodyEmitted)
            throw new InvalidOperationException("RegExp prototype body has not been emitted.");
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
