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
    /// For CommonJS modules: the live <c>module</c> object whose <c>exports</c> property is the
    /// module's current exports value. Used so circular requires see the up-to-date value
    /// (including any reassignment via <c>module.exports = X</c>) at the moment they execute.
    /// Null for ES modules and built-ins.
    /// </summary>
    public SharpTSObject? CommonJsModuleObject { get; set; }

    /// <summary>
    /// When set, the namespace object returned by <see cref="ExportsAsObject"/> instead
    /// of a per-call snapshot copy. Built-in modules whose namespace members need live
    /// accessor semantics (cluster, #1167) install a stable accessor-backed object here.
    /// </summary>
    public SharpTSObject? NamespaceObject { get; set; }

    /// <summary>
    /// Gets all exports as a SharpTSObject for namespace imports.
    /// </summary>
    public SharpTSObject ExportsAsObject()
    {
        return NamespaceObject ?? new SharpTSObject(new Dictionary<string, object?>(Exports));
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
