using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Manages hoisted variable fields for state machine types.
/// Single source of truth for variable hoisting logic across all state machine builders
/// (async, async arrow, generator, async generator).
/// </summary>
public class HoistingManager
{
    private readonly TypeBuilder _typeBuilder;
    private readonly Type _objectType;

    /// <summary>
    /// Parameters that are hoisted to state machine fields because they're accessed
    /// across yield or await boundaries.
    /// </summary>
    public Dictionary<string, FieldBuilder> HoistedParameters { get; } = [];

    /// <summary>
    /// Local variables that are hoisted to state machine fields because they're accessed
    /// across yield or await boundaries.
    /// </summary>
    public Dictionary<string, FieldBuilder> HoistedLocals { get; } = [];

    /// <summary>
    /// Enumerators for for...of loops that contain yield statements.
    /// These must be hoisted because the enumerator state persists across yield boundaries.
    /// </summary>
    public Dictionary<Stmt.ForOf, FieldBuilder> HoistedEnumerators { get; } = [];

    /// <summary>
    /// Key-list and index fields for for...in loops that contain yield/await. The base for...in
    /// emitter keeps the enumerated key list and the current index in IL locals, which a state-machine
    /// MoveNext re-entry wipes — so a yield in the loop body would restart from the first key (#547).
    /// Hoisting both to fields lets the iteration position survive the suspension.
    /// </summary>
    public Dictionary<Stmt.ForIn, FieldBuilder> HoistedForInKeys { get; } = [];
    public Dictionary<Stmt.ForIn, FieldBuilder> HoistedForInIndex { get; } = [];

    public HoistingManager(TypeBuilder typeBuilder, Type objectType)
    {
        _typeBuilder = typeBuilder;
        _objectType = objectType;
    }

    /// <summary>
    /// Defines fields for all hoisted parameters.
    /// </summary>
    public void DefineHoistedParameters(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var field = _typeBuilder.DefineField(name, _objectType, FieldAttributes.Public);
            HoistedParameters[name] = field;
        }
    }

    /// <summary>
    /// Defines fields for all hoisted locals.
    /// </summary>
    public void DefineHoistedLocals(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var field = _typeBuilder.DefineField(name, _objectType, FieldAttributes.Public);
            HoistedLocals[name] = field;
        }
    }

    /// <summary>
    /// Gets the field for a hoisted variable, or null if not hoisted.
    /// Checks parameters and locals. Captured outer-scope variables are deliberately not
    /// hoisted — state machines read them live from their enclosing storage (#541).
    /// </summary>
    public FieldBuilder? GetVariableField(string name)
    {
        if (HoistedParameters.TryGetValue(name, out var paramField))
            return paramField;
        if (HoistedLocals.TryGetValue(name, out var localField))
            return localField;
        return null;
    }

    /// <summary>
    /// Defines fields for hoisted enumerators from for...of loops containing yields.
    /// </summary>
    public void DefineHoistedEnumerators(IEnumerable<Stmt.ForOf> forOfLoops, Type enumeratorType)
    {
        int index = 0;
        foreach (var loop in forOfLoops)
        {
            // Use <>7__enum prefix following C# compiler convention for wrap fields
            var field = _typeBuilder.DefineField(
                $"<>7__enum{index++}",
                enumeratorType,
                FieldAttributes.Private
            );
            HoistedEnumerators[loop] = field;
        }
    }

    /// <summary>
    /// Gets the hoisted enumerator field for a for...of loop, or null if not hoisted.
    /// </summary>
    public FieldBuilder? GetEnumeratorField(Stmt.ForOf loop) =>
        HoistedEnumerators.TryGetValue(loop, out var field) ? field : null;

    /// <summary>
    /// Defines the key-list and index fields for for...in loops that contain yields/awaits (#547).
    /// <paramref name="keysListType"/> is <c>List&lt;object&gt;</c>; <paramref name="indexType"/> is <c>int</c>.
    /// </summary>
    public void DefineHoistedForInState(IEnumerable<Stmt.ForIn> forInLoops, Type keysListType, Type indexType)
    {
        int index = 0;
        foreach (var loop in forInLoops)
        {
            HoistedForInKeys[loop] = _typeBuilder.DefineField($"<>7__inKeys{index}", keysListType, FieldAttributes.Private);
            HoistedForInIndex[loop] = _typeBuilder.DefineField($"<>7__inIdx{index}", indexType, FieldAttributes.Private);
            index++;
        }
    }

    /// <summary>Gets the hoisted key-list field for a for...in loop, or null if not hoisted.</summary>
    public FieldBuilder? GetForInKeysField(Stmt.ForIn loop) =>
        HoistedForInKeys.TryGetValue(loop, out var field) ? field : null;

    /// <summary>Gets the hoisted index field for a for...in loop, or null if not hoisted.</summary>
    public FieldBuilder? GetForInIndexField(Stmt.ForIn loop) =>
        HoistedForInIndex.TryGetValue(loop, out var field) ? field : null;
}
