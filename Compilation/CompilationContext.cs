using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Compilation.Emitters.Modules;
using SharpTS.Compilation.Registries;
using SharpTS.TypeSystem;

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
public partial class CompilationContext
{
    // ============================================
    // Core Compilation Infrastructure
    // ============================================

    public ILGenerator IL { get; }
    public TypeMapper TypeMapper { get; }
    public LocalsManager Locals { get; }

    /// <summary>
    /// Validated IL builder that wraps the ILGenerator with compile-time checks.
    /// Use this for new code to catch label, stack, and exception block errors early.
    /// </summary>
    public ValidatedILBuilder ILBuilder { get; private set; }

    /// <summary>
    /// Type provider for resolving .NET types (runtime or reference assembly mode).
    /// Use this instead of typeof() for type resolution to support --ref-asm compilation.
    /// </summary>
    public TypeProvider Types { get; }

    // Emitted runtime types and methods (for standalone DLLs)
    public EmittedRuntime? Runtime { get; set; }

    // Type emitter registry for type-first method dispatch
    public TypeEmitterRegistry? TypeEmitterRegistry { get; set; }

    // Built-in module emitter registry for fs, path, os, etc.
    public BuiltInModuleEmitterRegistry? BuiltInModuleEmitterRegistry { get; set; }

    // Built-in module namespace variables (variable name -> module name)
    // Tracks which local variables are built-in module namespaces for direct dispatch
    public Dictionary<string, string>? BuiltInModuleNamespaces { get; set; }

    // ============================================
    // Registry Services
    // ============================================

    /// <summary>
    /// Registry for class-related compilation state lookups.
    /// Provides centralized methods for resolving class names, constructors,
    /// instance/static members, and inheritance chains.
    /// </summary>
    public ClassRegistry? ClassRegistry { get; set; }

    // ============================================
    // Enum Support
    // ============================================

    // Enum support: enum name -> member name -> value (double or string)
    public Dictionary<string, Dictionary<string, object>>? EnumMembers { get; set; }

    // Enum reverse mapping: enum name -> value -> member name (only numeric values)
    public Dictionary<string, Dictionary<double, string>>? EnumReverse { get; set; }

    // Enum kinds: enum name -> kind
    public Dictionary<string, EnumKind>? EnumKinds { get; set; }

    // ============================================
    // Generic Type Parameters
    // ============================================

    // Current scope's generic type parameters (name -> GenericTypeParameterBuilder or Type)
    public Dictionary<string, Type> GenericTypeParameters { get; set; } = [];

    // ============================================
    // Miscellaneous State
    // ============================================

    /// <summary>
    /// Whether the current compilation context is in JavaScript strict mode.
    /// Affects property assignment behavior on frozen/sealed objects.
    /// </summary>
    public bool IsStrictMode { get; set; }

    // Namespace support: namespace path -> static field
    public Dictionary<string, FieldBuilder>? NamespaceFields { get; set; }

    // Top-level variables captured by async functions (stored as static fields)
    public Dictionary<string, FieldBuilder>? TopLevelStaticVars { get; set; }

    // Type information from static analysis
    public TypeMap? TypeMap { get; set; }

    // Dead code analysis results
    public DeadCodeInfo? DeadCode { get; set; }

    // ============================================
    // Parameter Tracking
    // ============================================

    // Parameter tracking (name -> arg index)
    private readonly Dictionary<string, int> _parameters = [];
    private readonly Dictionary<string, Type> _parameterTypes = [];

    // ============================================
    // Loop and Exception Block Control
    // ============================================

    // Loop control labels (with optional label name for labeled statements)
    public Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> LoopLabels { get; } = new();

    // Exception block tracking for proper return handling
    public int ExceptionBlockDepth { get; set; } = 0;
    public LocalBuilder? ReturnValueLocal { get; set; }
    public Label ReturnLabel { get; set; }

    // ============================================
    // Constructor and Core Methods
    // ============================================

    public CompilationContext(
        ILGenerator il,
        TypeMapper typeMapper,
        Dictionary<string, MethodBuilder> functions,
        Dictionary<string, TypeBuilder> classes,
        TypeProvider? types = null)
    {
        IL = il;
        TypeMapper = typeMapper;
        Functions = functions;
        Classes = classes;
        Types = types ?? TypeProvider.Runtime;
        Locals = new LocalsManager(il);
        ILBuilder = new ValidatedILBuilder(il, Types);
    }

    /// <summary>
    /// Updates the validated IL builder for a new ILGenerator (when switching methods).
    /// Call this when the IL property changes to a new method's generator.
    /// </summary>
    public void UpdateILBuilder(ILGenerator newIL)
    {
        ILBuilder = new ValidatedILBuilder(newIL, Types);
    }

    public void DefineParameter(string name, int argIndex, Type? paramType = null)
    {
        _parameters[name] = argIndex;
        if (paramType != null)
        {
            _parameterTypes[name] = paramType;
        }
    }

    public bool TryGetParameter(string name, out int argIndex)
    {
        return _parameters.TryGetValue(name, out argIndex);
    }

    public bool TryGetParameterType(string name, out Type? paramType)
    {
        if (_parameterTypes.TryGetValue(name, out var type))
        {
            paramType = type;
            return true;
        }
        paramType = null;
        return false;
    }

    public void ClearParameters()
    {
        _parameters.Clear();
        _parameterTypes.Clear();
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
}
