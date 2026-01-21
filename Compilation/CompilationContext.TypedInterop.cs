using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Typed Interop: Real .NET Property Support
    // ============================================

    // Property backing fields (class name -> property name -> FieldBuilder)
    // Used for typed properties with real .NET backing fields
    public Dictionary<string, Dictionary<string, FieldBuilder>>? PropertyBackingFields { get; set; }

    // Property builders (class name -> property name -> PropertyBuilder)
    // Tracks real .NET PropertyBuilder for each declared TypeScript property
    public Dictionary<string, Dictionary<string, PropertyBuilder>>? ClassProperties { get; set; }

    // Declared property names per class (class name -> set of property names)
    // Used to distinguish declared properties (have backing fields) from dynamic properties (_extras)
    public Dictionary<string, HashSet<string>>? DeclaredPropertyNames { get; set; }

    // Readonly property names per class (class name -> set of readonly property names)
    // Properties that can only be set in the constructor
    public Dictionary<string, HashSet<string>>? ReadonlyPropertyNames { get; set; }

    // Property types per class (class name -> property name -> .NET Type)
    // The actual .NET type for each typed property backing field
    public Dictionary<string, Dictionary<string, Type>>? PropertyTypes { get; set; }

    // Union type generator for creating discriminated union types
    public UnionTypeGenerator? UnionGenerator { get; set; }

    // Dynamic property dictionary field (class name -> FieldBuilder for _extras)
    // Used for runtime-added properties that weren't declared in TypeScript
    public Dictionary<string, FieldBuilder>? ExtrasFields { get; set; }

    /// <summary>
    /// Resolve a property backing field by walking up the inheritance chain.
    /// </summary>
    public FieldBuilder? ResolvePropertyBackingField(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (PropertyBackingFields?.TryGetValue(current, out var fields) == true &&
                fields.TryGetValue(propertyName, out var field))
                return field;
            current = ClassRegistry?.GetSuperclass(current);
        }
        return null;
    }

    /// <summary>
    /// Resolve a property type by walking up the inheritance chain.
    /// </summary>
    public Type? ResolvePropertyType(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (PropertyTypes?.TryGetValue(current, out var types) == true &&
                types.TryGetValue(propertyName, out var type))
                return type;
            current = ClassRegistry?.GetSuperclass(current);
        }
        return null;
    }

    /// <summary>
    /// Check if a property is declared (has a backing field) vs dynamic.
    /// </summary>
    public bool IsDeclaredProperty(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (DeclaredPropertyNames?.TryGetValue(current, out var names) == true &&
                names.Contains(propertyName))
                return true;
            current = ClassRegistry?.GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Check if a property is readonly (can only be set in constructor).
    /// </summary>
    public bool IsReadonlyProperty(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (ReadonlyPropertyNames?.TryGetValue(current, out var names) == true &&
                names.Contains(propertyName))
                return true;
            current = ClassRegistry?.GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Get the _extras field for dynamic property storage for a class.
    /// </summary>
    public FieldBuilder? ResolveExtrasField(string className)
    {
        string? current = className;
        while (current != null)
        {
            if (ExtrasFields?.TryGetValue(current, out var field) == true)
                return field;
            current = ClassRegistry?.GetSuperclass(current);
        }
        return null;
    }
}
