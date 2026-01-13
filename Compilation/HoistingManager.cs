using System.Reflection;
using System.Reflection.Emit;

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
    /// Checks if a variable is hoisted.
    /// </summary>
    public bool IsHoisted(string name) =>
        HoistedParameters.ContainsKey(name) || HoistedLocals.ContainsKey(name);
}
