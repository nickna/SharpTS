using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Symbols;

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
    // Managed expression evaluators find the kickoff method on the state machine's containing
    // type, using the method name encoded in <method>d__N. Keep the historical standalone shape
    // for builds without symbols; only debugger-facing builds need this relationship.
    private TypeBuilder? _debugContainingType;
    private string? _debugKickoffName;

    internal StateMachineDebugSymbols? DebugSymbols { get; set; }

    internal void AttachDebugSymbols(CompilationContext context)
    {
        if (DebugSymbols is null || context.DebugScope is null || context.CurrentMethod is null) return;
        context.HoistedDebugSymbols = DebugSymbols;
        context.Locals.SymbolSink = context.DebugScope.Collector.BeginMethodLocals(context.CurrentMethod, context.IL);
        context.DebugScope.Collector.RecordHoistedLocals(context.CurrentMethod, DebugSymbols);
    }

    internal string DebugScaffoldingName(string name) => DebugSymbols is null || name.StartsWith("<>7__")
        ? name : $"<>7__{(name.StartsWith("<>") ? name[2..] : name)}";

    protected string DebugDisplayClassName() => DebugSymbols is null
        ? "<>__functionDC" : $"<>8__{DebugSymbols.AddDisplayClassSlot() + 1}";

    internal void SetDebugMetadataOwner(TypeBuilder containingType, string kickoffName)
    {
        _debugContainingType = containingType;
        _debugKickoffName = kickoffName;
    }

    protected TypeBuilder DefineStateMachineType(
        ModuleBuilder module,
        string name,
        int ordinal,
        TypeAttributes attributes,
        Type parent,
        Type[] interfaces)
    {
        if (_debugContainingType is null)
            return EmitTypeDefinitions.DefineType(module, name, attributes, parent, interfaces);

        return EmitTypeDefinitions.DefineNestedType(
            _debugContainingType,
            $"<{_debugKickoffName!.Replace('.', '-')}>d__{ordinal}",
            (attributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.NestedPublic,
            parent,
            interfaces);
    }

    /// <summary>The value-type struct or reference-type class being built for this state machine.</summary>
    public abstract TypeBuilder StateMachineType { get; }

    /// <summary>The hoisted state-machine field backing the named parameter/local, or null if not hoisted.</summary>
    public abstract FieldBuilder? GetVariableField(string name);

    /// <summary>Finalizes and returns the concrete state-machine type.</summary>
    public abstract Type CreateType();
}
