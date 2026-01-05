using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using static SharpTS.Parsing.Expr;

namespace SharpTS.Compilation;

/// <summary>
/// Represents the type currently on top of the IL evaluation stack.
/// Used for unboxed numeric optimization to avoid unnecessary boxing/unboxing.
/// </summary>
public enum StackType
{
    /// <summary>Object reference - could be any boxed type or reference type.</summary>
    Unknown,
    /// <summary>Native double (float64) - unboxed numeric value.</summary>
    Double,
    /// <summary>Native bool (int32 as 0/1) - unboxed boolean value.</summary>
    Boolean,
    /// <summary>String reference.</summary>
    String,
    /// <summary>Null reference.</summary>
    Null
}

/// <summary>
/// Holds compilation state passed between ILCompiler and ILEmitter.
/// </summary>
/// <remarks>
/// Central state container for IL compilation. Provides access to the current
/// <see cref="ILGenerator"/>, <see cref="TypeMapper"/>, <see cref="LocalsManager"/>,
/// and various lookup tables for functions, classes, static members, closures,
/// and enums. Also tracks parameters, loop labels for break/continue, and
/// display class state for closure capture. Passed to <see cref="ILEmitter"/> methods.
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="ILEmitter"/>
/// <seealso cref="LocalsManager"/>
public class CompilationContext
{
    public ILGenerator IL { get; }
    public TypeMapper TypeMapper { get; }
    public LocalsManager Locals { get; }
    public Dictionary<string, MethodBuilder> Functions { get; }
    public Dictionary<string, TypeBuilder> Classes { get; }

    // For instance methods
    public FieldInfo? FieldsField { get; set; }
    public bool IsInstanceMethod { get; set; }

    // For static members: current class being compiled
    public TypeBuilder? CurrentClassBuilder { get; set; }

    // For inheritance: current class's superclass name (if any)
    public string? CurrentSuperclassName { get; set; }

    // Static fields by class name -> field name -> FieldBuilder
    public Dictionary<string, Dictionary<string, FieldBuilder>>? StaticFields { get; set; }

    // Static methods by class name -> method name -> MethodBuilder
    public Dictionary<string, Dictionary<string, MethodBuilder>>? StaticMethods { get; set; }

    // Rest parameter info: function name -> (restParamIndex, regularParamCount)
    // If a function has a rest param, restParamIndex is its index, regularParamCount is non-rest param count
    public Dictionary<string, (int RestParamIndex, int RegularParamCount)>? FunctionRestParams { get; set; }

    // Class constructors by class name -> ConstructorBuilder
    public Dictionary<string, ConstructorBuilder>? ClassConstructors { get; set; }

    // Closure support
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

    // Enum support: enum name -> member name -> value (double or string)
    public Dictionary<string, Dictionary<string, object>>? EnumMembers { get; set; }

    // Enum reverse mapping: enum name -> value -> member name (only numeric values)
    public Dictionary<string, Dictionary<double, string>>? EnumReverse { get; set; }

    // Enum kinds: enum name -> kind
    public Dictionary<string, EnumKind>? EnumKinds { get; set; }

    // Emitted runtime types and methods (for standalone DLLs)
    public EmittedRuntime? Runtime { get; set; }

    // Type information from static analysis
    public TypeMap? TypeMap { get; set; }

    // Dead code analysis results
    public DeadCodeInfo? DeadCode { get; set; }

    // Generic type parameter tracking
    // Current scope's generic type parameters (name -> GenericTypeParameterBuilder or Type)
    public Dictionary<string, Type> GenericTypeParameters { get; set; } = [];

    // Track generic params per class for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? ClassGenericParams { get; set; }

    // Track generic params per function for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? FunctionGenericParams { get; set; }

    // Track which functions are generic definitions
    public Dictionary<string, bool>? IsGenericFunction { get; set; }

    // Instance methods for direct dispatch (class name -> method name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceMethods { get; set; }

    // Instance getters for direct dispatch (class name -> property name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceGetters { get; set; }

    // Instance setters for direct dispatch (class name -> property name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceSetters { get; set; }

    // Class superclass mapping (class name -> superclass name or null)
    public Dictionary<string, string?>? ClassSuperclass { get; set; }

    // Module support
    // Current module path being compiled
    public string? CurrentModulePath { get; set; }

    // Module export fields (module path -> export name -> FieldBuilder)
    public Dictionary<string, Dictionary<string, FieldBuilder>>? ModuleExportFields { get; set; }

    // Module types (module path -> TypeBuilder)
    public Dictionary<string, TypeBuilder>? ModuleTypes { get; set; }

    // Module resolver for import path resolution
    public ModuleResolver? ModuleResolver { get; set; }

    // Parameter tracking (name -> arg index)
    private readonly Dictionary<string, int> _parameters = [];

    // Loop control labels (with optional label name for labeled statements)
    public Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> LoopLabels { get; } = new();

    // Exception block tracking for proper return handling
    public int ExceptionBlockDepth { get; set; } = 0;
    public LocalBuilder? ReturnValueLocal { get; set; }
    public Label ReturnLabel { get; set; }

    public CompilationContext(
        ILGenerator il,
        TypeMapper typeMapper,
        Dictionary<string, MethodBuilder> functions,
        Dictionary<string, TypeBuilder> classes)
    {
        IL = il;
        TypeMapper = typeMapper;
        Functions = functions;
        Classes = classes;
        Locals = new LocalsManager(il);
    }

    public void DefineParameter(string name, int argIndex)
    {
        _parameters[name] = argIndex;
    }

    public bool TryGetParameter(string name, out int argIndex)
    {
        return _parameters.TryGetValue(name, out argIndex);
    }

    public void ClearParameters()
    {
        _parameters.Clear();
    }

    public void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
    {
        LoopLabels.Push((breakLabel, continueLabel, labelName));
    }

    public void ExitLoop()
    {
        LoopLabels.Pop();
    }

    public (Label BreakLabel, Label ContinueLabel, string? LabelName)? CurrentLoop =>
        LoopLabels.Count > 0 ? LoopLabels.Peek() : null;

    /// <summary>
    /// Find a loop label by name (for labeled break/continue).
    /// </summary>
    public (Label BreakLabel, Label ContinueLabel, string? LabelName)? FindLabeledLoop(string labelName)
    {
        foreach (var entry in LoopLabels)
        {
            if (entry.LabelName == labelName)
                return entry;
        }
        return null;
    }

    /// <summary>
    /// Resolve an instance method by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceMethod(string className, string methodName)
    {
        string? current = className;
        while (current != null)
        {
            if (InstanceMethods?.TryGetValue(current, out var methods) == true &&
                methods.TryGetValue(methodName, out var method))
                return method;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolve an instance getter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceGetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (InstanceGetters?.TryGetValue(current, out var getters) == true &&
                getters.TryGetValue(propertyName, out var getter))
                return getter;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolve an instance setter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceSetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (InstanceSetters?.TryGetValue(current, out var setters) == true &&
                setters.TryGetValue(propertyName, out var setter))
                return setter;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }
}
