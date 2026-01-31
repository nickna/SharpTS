using System.Reflection.Emit;
using static SharpTS.Parsing.Expr;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Closure Support
    // ============================================

    // Closure analyzer for detecting captured variables
    public ClosureAnalyzer? ClosureAnalyzer { get; set; }

    // Arrow function methods (arrow node -> method info)
    public Dictionary<ArrowFunction, MethodBuilder> ArrowMethods { get; set; } = [];

    // Display classes for capturing closures (arrow node -> type builder)
    public Dictionary<ArrowFunction, TypeBuilder> DisplayClasses { get; set; } = [];

    // Display class fields (arrow node -> field mapping)
    public Dictionary<ArrowFunction, Dictionary<string, FieldBuilder>> DisplayClassFields { get; set; } = [];

    // Display class constructors (arrow node -> constructor)
    public Dictionary<ArrowFunction, ConstructorBuilder> DisplayClassConstructors { get; set; } = [];

    // For capturing closures: current display class instance local
    public LocalBuilder? DisplayClassLocal { get; set; }

    // For capturing closures: field mapping (variable name -> field)
    public Dictionary<string, FieldBuilder>? CapturedFields { get; set; }

    // ============================================
    // Entry-Point Display Class (for captured top-level variables)
    // ============================================

    /// <summary>
    /// Display class type for entry-point captured variables.
    /// When top-level variables are captured by closures, they're stored here
    /// instead of static fields, so modifications in closures are visible to outer code.
    /// </summary>
    public TypeBuilder? EntryPointDisplayClass { get; set; }

    /// <summary>
    /// Constructor for the entry-point display class.
    /// </summary>
    public ConstructorBuilder? EntryPointDisplayClassCtor { get; set; }

    /// <summary>
    /// Fields in the entry-point display class (variable name -> field).
    /// </summary>
    public Dictionary<string, FieldBuilder>? EntryPointDisplayClassFields { get; set; }

    /// <summary>
    /// Local variable holding the entry-point display class instance.
    /// Used in entry point methods for direct access.
    /// </summary>
    public LocalBuilder? EntryPointDisplayClassLocal { get; set; }

    /// <summary>
    /// Static field on $Program that holds the entry-point display class instance.
    /// Used by module init methods to access captured top-level variables.
    /// </summary>
    public FieldBuilder? EntryPointDisplayClassStaticField { get; set; }

    /// <summary>
    /// Set of top-level variable names that are captured by closures.
    /// These use the entry-point display class instead of static fields.
    /// </summary>
    public HashSet<string>? CapturedTopLevelVars { get; set; }

    /// <summary>
    /// Maps arrow functions to their $entryPointDC field (if they capture top-level vars).
    /// Used when creating capturing arrows to populate the reference to the entry-point display class.
    /// </summary>
    public Dictionary<ArrowFunction, FieldBuilder>? ArrowEntryPointDCFields { get; set; }

    /// <summary>
    /// When inside an arrow body, this is the field that holds the reference to the entry-point display class.
    /// Used for accessing captured top-level variables through the $entryPointDC field.
    /// </summary>
    public FieldBuilder? CurrentArrowEntryPointDCField { get; set; }
}
