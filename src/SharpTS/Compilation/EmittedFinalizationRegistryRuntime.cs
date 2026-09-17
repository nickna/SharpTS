using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required finalization poke-table metadata with independently optional registry implementation.</summary>
public sealed class EmittedFinalizationRegistryRuntime
{
    internal EmittedFinalizationRegistryRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _pokeTable;
    public FieldBuilder PokeTable
    {
        get => Require(_pokeTable);
        internal set => SetHandle(ref _pokeTable, value);
    }

    public EmittedFinalizationRegistryImplementation? Implementation { get; private set; }

    public EmittedFinalizationRegistryImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("FinalizationRegistry implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("FinalizationRegistry implementation emission has already started.");
        Implementation = new EmittedFinalizationRegistryImplementation();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("FinalizationRegistry metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("FinalizationRegistry metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = PokeTable;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
