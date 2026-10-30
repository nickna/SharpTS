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
    private Dictionary<string, Func<object?>>? _liveExports;

    /// <summary>
    /// Default export value, if any.
    /// </summary>
    private object? _defaultExport;
    public object? DefaultExport
    {
        get
        {
            if (_liveExports?.TryGetValue("default", out var read) == true) return read();
            return _defaultExport;
        }
        set => _defaultExport = value;
    }

    internal void SetDefaultExport(object? value)
    {
        _defaultExport = value;
        Exports["default"] = value;
    }

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
        return NamespaceObject ?? new SharpTSObject(Exports.Keys.ToDictionary(name => name, GetExport));
    }

    /// <summary>
    /// Gets an exported value by name.
    /// </summary>
    public object? GetExport(string name)
    {
        if (_liveExports?.TryGetValue(name, out var read) == true)
            return read();
        return Exports.TryGetValue(name, out var value) ? value : null;
    }

    internal void SetLiveExport(string name, Func<object?> read)
    {
        Exports.TryAdd(name, SharpTSUndefined.Instance);
        (_liveExports ??= new(StringComparer.Ordinal))[name] = read;
    }

    /// <summary>
    /// Sets an exported value.
    /// </summary>
    public void SetExport(string name, object? value)
    {
        Exports[name] = value;
    }
}
