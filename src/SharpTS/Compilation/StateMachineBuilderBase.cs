using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Root of the state-machine builder hierarchy shared by all four compiled state machines: the async
/// function (<see cref="AsyncStateMachineBuilder"/>) and async arrow (<see cref="AsyncArrowStateMachineBuilder"/>)
/// value-type structs, and the sync generator (<see cref="GeneratorStateMachineBuilder"/>) and async
/// generator (<see cref="AsyncGeneratorStateMachineBuilder"/>) reference-type classes. All four expose a
/// type being built, a way to resolve a hoisted variable field, and a finalizer; declaring that surface
/// once lets callers hold a builder polymorphically (#1125).
/// </summary>
/// <remarks>
/// The awaiter plumbing that the two async *function* builders share lives one layer down in
/// <see cref="AsyncBuilderBase"/>; the iterator surface that the two generator builders share is exposed
/// through <see cref="IIteratorStateMachineBuilder"/>. The async generator composes both (it is an
/// iterator that also awaits, but via a ValueTask source rather than a TaskAwaiter, so it derives from
/// this root directly rather than from <see cref="AsyncBuilderBase"/>).
/// </remarks>
public abstract class StateMachineBuilderBase
{
    /// <summary>The value-type struct or reference-type class being built for this state machine.</summary>
    public abstract TypeBuilder StateMachineType { get; }

    /// <summary>The hoisted state-machine field backing the named parameter/local, or null if not hoisted.</summary>
    public abstract FieldBuilder? GetVariableField(string name);

    /// <summary>Finalizes and returns the concrete state-machine type.</summary>
    public abstract Type CreateType();
}
