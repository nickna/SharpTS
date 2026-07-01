using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// The slice of a reference-type iterator state-machine builder's surface that the shared stub emitter
/// needs, common to <see cref="GeneratorStateMachineBuilder"/> and
/// <see cref="AsyncGeneratorStateMachineBuilder"/>. An async generator is a generator that also awaits,
/// so the two build byte-identical creation stubs (newobj → optionally copy <c>this</c> → copy/box
/// parameters into object-typed fields → seed the function display class → return); this interface lets
/// one stub emitter serve both instead of the previous six near-identical copies (#1126). Both builders
/// already expose these members publicly, so implementing the interface adds no new surface.
/// </summary>
public interface IIteratorStateMachineBuilder
{
    /// <summary>The parameterless constructor of the reference-type state-machine class.</summary>
    ConstructorBuilder Constructor { get; }

    /// <summary>The <c>&lt;&gt;4__this</c> field, or null when the iterator captures no receiver.</summary>
    FieldBuilder? ThisField { get; }

    /// <summary>The <c>&lt;&gt;__functionDC</c> field, or null when the iterator has no function display class.</summary>
    FieldBuilder? FunctionDCField { get; }

    /// <summary>The hoisted state-machine field backing the named parameter/local, or null if not hoisted.</summary>
    FieldBuilder? GetVariableField(string name);
}
