namespace SharpTS.Runtime.Types;

/// <summary>
/// Flags for property descriptor attributes (writable, enumerable, configurable).
/// Used by Object.defineProperty() and Object.getOwnPropertyDescriptor().
/// </summary>
public struct PropertyDescriptorFlags
{
    /// <summary>
    /// Whether the property value can be changed.
    /// Default: true for data properties created via assignment.
    /// </summary>
    public bool Writable { get; set; }

    /// <summary>
    /// Whether the property shows up in enumeration (Object.keys, for...in).
    /// Default: true for properties created via assignment.
    /// </summary>
    public bool Enumerable { get; set; }

    /// <summary>
    /// Whether the property can be deleted or its attributes changed.
    /// Default: true for properties created via assignment.
    /// </summary>
    public bool Configurable { get; set; }

    /// <summary>
    /// Whether this property has explicit descriptor flags set via defineProperty.
    /// If false, the property uses default flags (all true).
    /// </summary>
    public bool HasExplicitDescriptor { get; set; }

    /// <summary>
    /// Creates default descriptor flags (all true, as for normal assignment).
    /// </summary>
    public static PropertyDescriptorFlags Default => new()
    {
        Writable = true,
        Enumerable = true,
        Configurable = true,
        HasExplicitDescriptor = false
    };

    /// <summary>
    /// Creates descriptor flags for a property defined via Object.defineProperty().
    /// Per JS spec, missing attributes default to false when using defineProperty.
    /// </summary>
    public static PropertyDescriptorFlags ForDefineProperty(bool writable = false, bool enumerable = false, bool configurable = false)
    {
        return new PropertyDescriptorFlags
        {
            Writable = writable,
            Enumerable = enumerable,
            Configurable = configurable,
            HasExplicitDescriptor = true
        };
    }
}
