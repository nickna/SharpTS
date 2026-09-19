using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

// GlobalThis declares the cache field even when the optional Web API implementation is absent.
public sealed class EmittedFetchRuntime
{
    internal EmittedFetchRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _cachedFunction;
    public FieldBuilder CachedFunction
    {
        get => Require(_cachedFunction);
        internal set => Set(ref _cachedFunction, value);
    }

    public EmittedFetchImplementation? Implementation { get; private set; }

    public EmittedFetchImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("Fetch implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("Fetch implementation emission has already started.");
        Implementation = new EmittedFetchImplementation();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Fetch metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"Fetch metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Fetch metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CachedFunction;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
