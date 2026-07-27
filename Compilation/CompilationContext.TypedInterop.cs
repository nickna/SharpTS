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
}
