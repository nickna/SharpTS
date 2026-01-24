using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Context for function-level type checking state.
/// </summary>
public sealed class FunctionContext
{
    /// <summary>
    /// The expected return type of the current function.
    /// </summary>
    public TypeInfo? ReturnType { get; set; }

    /// <summary>
    /// The declared 'this' type for explicit this parameter.
    /// </summary>
    public TypeInfo? ThisType { get; set; }

    /// <summary>
    /// Whether the current function is async.
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// Whether the current function is a generator.
    /// </summary>
    public bool IsGenerator { get; set; }
}

/// <summary>
/// Context for class-level type checking state.
/// </summary>
public sealed class ClassContext
{
    /// <summary>
    /// The class type being checked.
    /// </summary>
    public TypeInfo.Class? ClassType { get; set; }

    /// <summary>
    /// Whether currently in a static method.
    /// </summary>
    public bool InStaticMethod { get; set; }

    /// <summary>
    /// Whether currently in a static block.
    /// </summary>
    public bool InStaticBlock { get; set; }
}

/// <summary>
/// Context for control flow tracking during type checking.
/// </summary>
public sealed class ControlFlowContext
{
    /// <summary>
    /// Current loop nesting depth.
    /// </summary>
    public int LoopDepth { get; set; }

    /// <summary>
    /// Current switch statement nesting depth.
    /// </summary>
    public int SwitchDepth { get; set; }

    /// <summary>
    /// Active labels for labeled statements (label name -> isOnLoop).
    /// </summary>
    public Dictionary<string, bool> ActiveLabels { get; } = [];

    /// <summary>
    /// Enters a loop, incrementing the depth counter.
    /// </summary>
    public void EnterLoop() => LoopDepth++;

    /// <summary>
    /// Exits a loop, decrementing the depth counter.
    /// </summary>
    public void ExitLoop() => LoopDepth--;

    /// <summary>
    /// Enters a switch statement, incrementing the depth counter.
    /// </summary>
    public void EnterSwitch() => SwitchDepth++;

    /// <summary>
    /// Exits a switch statement, decrementing the depth counter.
    /// </summary>
    public void ExitSwitch() => SwitchDepth--;

    /// <summary>
    /// Whether currently inside a loop.
    /// </summary>
    public bool InLoop => LoopDepth > 0;

    /// <summary>
    /// Whether currently inside a switch statement.
    /// </summary>
    public bool InSwitch => SwitchDepth > 0;
}

/// <summary>
/// Records where a type parameter appears (input vs output positions).
/// Used for variance analysis.
/// </summary>
public sealed record VariancePositions(bool AppearsInOutput, bool AppearsInInput);

/// <summary>
/// Comparer for compatibility cache keys using TypeInfoEqualityComparer.
/// Ensures structurally equivalent types (including those with List fields)
/// are treated as equal keys.
/// </summary>
public sealed class CompatibilityCacheKeyComparer
    : IEqualityComparer<(TypeInfo Expected, TypeInfo Actual)>
{
    public static readonly CompatibilityCacheKeyComparer Instance = new();

    public bool Equals((TypeInfo Expected, TypeInfo Actual) x,
                       (TypeInfo Expected, TypeInfo Actual) y)
    {
        return TypeInfoEqualityComparer.Instance.Equals(x.Expected, y.Expected)
            && TypeInfoEqualityComparer.Instance.Equals(x.Actual, y.Actual);
    }

    public int GetHashCode((TypeInfo Expected, TypeInfo Actual) obj)
    {
        return HashCode.Combine(
            TypeInfoEqualityComparer.Instance.GetHashCode(obj.Expected),
            TypeInfoEqualityComparer.Instance.GetHashCode(obj.Actual)
        );
    }
}

/// <summary>
/// Shared context object containing all state for type checking.
/// Extracted from TypeChecker to enable service-based decomposition.
/// </summary>
/// <remarks>
/// This context object consolidates the 26+ fields previously scattered across
/// the TypeChecker partial class files. Services receive this context and use
/// IDisposable scope helpers for safe state management.
/// </remarks>
public sealed class TypeCheckingContext
{
    /// <summary>
    /// The type environment for scope management.
    /// </summary>
    public TypeEnvironment Environment { get; set; } = new();

    /// <summary>
    /// The type map storing resolved types for expressions.
    /// </summary>
    public TypeMap TypeMap { get; } = new();

    /// <summary>
    /// Diagnostic collector for error recovery support.
    /// </summary>
    public DiagnosticCollector Diagnostics { get; } = new();

    /// <summary>
    /// Context for the current function being checked.
    /// </summary>
    public FunctionContext? CurrentFunction { get; set; }

    /// <summary>
    /// Context for the current class being checked.
    /// </summary>
    public ClassContext? CurrentClass { get; set; }

    /// <summary>
    /// Contextual 'this' type for object literal accessor bodies.
    /// Set during CheckObject two-pass.
    /// </summary>
    public TypeInfo? PendingObjectThisType { get; set; }

    /// <summary>
    /// Control flow tracking for loops, switches, and labels.
    /// </summary>
    public ControlFlowContext ControlFlow { get; } = new();

    /// <summary>
    /// Memoization cache for IsCompatible checks.
    /// Cleared per Check() call.
    /// </summary>
    public Dictionary<(TypeInfo Expected, TypeInfo Actual), bool>? CompatibilityCache { get; set; }

    /// <summary>
    /// Cache for variance position analysis results.
    /// Key: "{TypeName}:{TypeParamName}", Value: positions where param appears.
    /// </summary>
    public Dictionary<string, VariancePositions> VariancePositionCache { get; } = new();

    /// <summary>
    /// File path for source location reporting.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Decorator mode configuration.
    /// </summary>
    public DecoratorMode DecoratorMode { get; set; } = DecoratorMode.None;

    /// <summary>
    /// The current module being type-checked.
    /// </summary>
    public ParsedModule? CurrentModule { get; set; }

    /// <summary>
    /// Module resolver for path resolution.
    /// </summary>
    public ModuleResolver? ModuleResolver { get; set; }

    /// <summary>
    /// Track dynamic import paths discovered during type checking.
    /// </summary>
    public HashSet<string> DynamicImportPaths { get; } = [];

    /// <summary>
    /// Pending overload signatures for top-level functions.
    /// </summary>
    public Dictionary<string, List<TypeInfo.Function>> PendingOverloadSignatures { get; } = [];

    /// <summary>
    /// Type parameters for generic overloaded functions.
    /// </summary>
    public Dictionary<string, List<TypeInfo.TypeParameter>> PendingOverloadTypeParams { get; } = [];

    /// <summary>
    /// Clears the compatibility cache for a fresh check.
    /// </summary>
    public void ClearCompatibilityCache() => CompatibilityCache = null;

    /// <summary>
    /// Ensures the compatibility cache is initialized.
    /// </summary>
    public void EnsureCompatibilityCache()
    {
        CompatibilityCache ??= new(CompatibilityCacheKeyComparer.Instance);
    }

    /// <summary>
    /// Creates a new child environment scope.
    /// </summary>
    public TypeEnvironment CreateChildEnvironment()
    {
        return new TypeEnvironment(Environment);
    }

    /// <summary>
    /// Gets or creates the current function context.
    /// </summary>
    public FunctionContext GetOrCreateFunctionContext()
    {
        CurrentFunction ??= new FunctionContext();
        return CurrentFunction;
    }

    /// <summary>
    /// Gets or creates the current class context.
    /// </summary>
    public ClassContext GetOrCreateClassContext()
    {
        CurrentClass ??= new ClassContext();
        return CurrentClass;
    }
}

/// <summary>
/// RAII-style helper for safely managing TypeEnvironment scope changes.
/// Automatically restores the previous environment on disposal.
/// </summary>
public readonly struct EnvironmentScope : IDisposable
{
    private readonly TypeCheckingContext _context;
    private readonly TypeEnvironment _previous;

    public EnvironmentScope(TypeCheckingContext context, TypeEnvironment newEnv)
    {
        _context = context;
        _previous = context.Environment;
        context.Environment = newEnv;
    }

    public void Dispose() => _context.Environment = _previous;
}

/// <summary>
/// RAII-style helper for safely managing function context changes.
/// Automatically restores the previous function context on disposal.
/// </summary>
public readonly struct FunctionScope : IDisposable
{
    private readonly TypeCheckingContext _context;
    private readonly FunctionContext? _previous;

    public FunctionScope(TypeCheckingContext context, FunctionContext newContext)
    {
        _context = context;
        _previous = context.CurrentFunction;
        context.CurrentFunction = newContext;
    }

    public void Dispose() => _context.CurrentFunction = _previous;
}

/// <summary>
/// RAII-style helper for safely managing class context changes.
/// Automatically restores the previous class context on disposal.
/// </summary>
public readonly struct ClassScope : IDisposable
{
    private readonly TypeCheckingContext _context;
    private readonly ClassContext? _previous;

    public ClassScope(TypeCheckingContext context, ClassContext newContext)
    {
        _context = context;
        _previous = context.CurrentClass;
        context.CurrentClass = newContext;
    }

    public void Dispose() => _context.CurrentClass = _previous;
}
