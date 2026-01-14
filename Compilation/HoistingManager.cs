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
    /// Variables captured from outer scopes (closures) that need to be copied into
    /// the state machine.
    /// </summary>
    public Dictionary<string, FieldBuilder> CapturedVariables { get; } = [];

    /// <summary>
    /// Enumerators for for...of loops that contain yield statements.
    /// These must be hoisted because the enumerator state persists across yield boundaries.
    /// </summary>
    public Dictionary<Stmt.ForOf, FieldBuilder> HoistedEnumerators { get; } = [];

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
    /// Defines fields for captured outer scope variables.
    /// Field names use a special prefix to distinguish from local hoisting.
    /// </summary>
    public void DefineHoistedCapturedVariables(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            // Use <>5__ prefix following C# compiler convention for captured variables
            var field = _typeBuilder.DefineField($"<>5__{name}", _objectType, FieldAttributes.Public);
            CapturedVariables[name] = field;
        }
    }

    /// <summary>
    /// Gets the field for a hoisted variable, or null if not hoisted.
    /// Checks parameters, locals, and captured variables.
    /// </summary>
    public FieldBuilder? GetVariableField(string name)
    {
        if (HoistedParameters.TryGetValue(name, out var paramField))
            return paramField;
        if (HoistedLocals.TryGetValue(name, out var localField))
            return localField;
        if (CapturedVariables.TryGetValue(name, out var capturedField))
            return capturedField;
        return null;
    }

    /// <summary>
    /// Gets the field for a captured variable specifically, or null if not captured.
    /// </summary>
    public FieldBuilder? GetCapturedVariableField(string name) =>
        CapturedVariables.TryGetValue(name, out var field) ? field : null;

    /// <summary>
    /// Checks if a variable is hoisted.
    /// </summary>
    public bool IsHoisted(string name) =>
        HoistedParameters.ContainsKey(name) || HoistedLocals.ContainsKey(name) || CapturedVariables.ContainsKey(name);

    /// <summary>
    /// Checks if a variable is captured from an outer scope.
    /// </summary>
    public bool IsCaptured(string name) => CapturedVariables.ContainsKey(name);

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
}
