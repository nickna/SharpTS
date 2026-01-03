using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using static SharpTS.Parsing.Expr;

namespace SharpTS.Compilation;

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

    // Generic type parameter tracking
    // Current scope's generic type parameters (name -> GenericTypeParameterBuilder or Type)
    public Dictionary<string, Type> GenericTypeParameters { get; set; } = [];

    // Track generic params per class for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? ClassGenericParams { get; set; }

    // Track generic params per function for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? FunctionGenericParams { get; set; }

    // Track which functions are generic definitions
    public Dictionary<string, bool>? IsGenericFunction { get; set; }

    // Parameter tracking (name -> arg index)
    private readonly Dictionary<string, int> _parameters = [];

    // Loop control labels
    public Stack<(Label BreakLabel, Label ContinueLabel)> LoopLabels { get; } = new();

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

    public void EnterLoop(Label breakLabel, Label continueLabel)
    {
        LoopLabels.Push((breakLabel, continueLabel));
    }

    public void ExitLoop()
    {
        LoopLabels.Pop();
    }

    public (Label BreakLabel, Label ContinueLabel)? CurrentLoop =>
        LoopLabels.Count > 0 ? LoopLabels.Peek() : null;
}
