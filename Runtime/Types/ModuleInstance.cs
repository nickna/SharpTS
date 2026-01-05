namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a module instance at runtime with its exported values.
/// </summary>
/// <remarks>
/// Used by the interpreter to track module execution state and exported values.
/// Each module is executed once and its exports are cached in this instance.
/// </remarks>
public class ModuleInstance
{
    /// <summary>
    /// Named exports from this module (name -> runtime value).
    /// </summary>
    public Dictionary<string, object?> Exports { get; } = [];

    /// <summary>
    /// Default export value, if any.
    /// </summary>
    public object? DefaultExport { get; set; }

    /// <summary>
    /// Whether the module has been executed.
    /// </summary>
    public bool IsExecuted { get; set; }

    /// <summary>
    /// Gets all exports as a SharpTSObject for namespace imports.
    /// </summary>
    public SharpTSObject ExportsAsObject()
    {
        return new SharpTSObject(new Dictionary<string, object?>(Exports));
    }

    /// <summary>
    /// Gets an exported value by name.
    /// </summary>
    public object? GetExport(string name)
    {
        return Exports.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// Sets an exported value.
    /// </summary>
    public void SetExport(string name, object? value)
    {
        Exports[name] = value;
    }
}
