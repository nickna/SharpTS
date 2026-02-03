using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// State container classes for organizing ILCompiler's compilation state.
/// These group related dictionaries into semantic containers for better maintainability.
/// </summary>
public partial class ILCompiler
{
    #region State Container Classes

    /// <summary>
    /// State for class declaration compilation.
    /// </summary>
    private sealed class ClassCompilationState
    {
        public Dictionary<string, TypeBuilder> Builders { get; } = [];
        public Dictionary<string, Type> ExternalTypes { get; } = [];
        public Dictionary<string, string?> Superclass { get; } = [];
        public Dictionary<string, ConstructorBuilder> Constructors { get; } = [];
        public Dictionary<string, List<ConstructorBuilder>> ConstructorOverloads { get; } = [];
        public Dictionary<string, Dictionary<string, FieldBuilder>> StaticFields { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticMethods { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceMethods { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceGetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceSetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticGetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticSetters { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> PreDefinedMethods { get; } = [];
        public Dictionary<string, Dictionary<string, MethodBuilder>> PreDefinedAccessors { get; } = [];
        public Dictionary<string, FieldBuilder> InstanceFieldsField { get; } = [];
        public Dictionary<string, GenericTypeParameterBuilder[]> GenericParams { get; } = [];

        // ES2022 Private Class Elements Support
        // Static field holding ConditionalWeakTable<object, Dictionary<string, object?>> for instance private fields
        public Dictionary<string, FieldBuilder> PrivateFieldStorage { get; } = [];
        // List of private field names per class for initialization order
        public Dictionary<string, List<string>> PrivateFieldNames { get; } = [];
        // Static private fields: class name -> field name (without #) -> FieldBuilder
        public Dictionary<string, Dictionary<string, FieldBuilder>> StaticPrivateFields { get; } = [];
        // Private instance methods: class name -> method name (without #) -> MethodBuilder
        public Dictionary<string, Dictionary<string, MethodBuilder>> PrivateMethods { get; } = [];
        // Static private methods: class name -> method name (without #) -> MethodBuilder
        public Dictionary<string, Dictionary<string, MethodBuilder>> StaticPrivateMethods { get; } = [];
    }

    /// <summary>
    /// State for function declaration compilation.
    /// </summary>
    private sealed class FunctionCompilationState
    {
        public Dictionary<string, MethodBuilder> Builders { get; } = [];
        public Dictionary<string, List<MethodBuilder>> Overloads { get; } = [];
        public Dictionary<string, Dictionary<string, List<MethodBuilder>>> MethodOverloads { get; } = [];
        public Dictionary<string, (int RestParamIndex, int RegularParamCount)> RestParams { get; } = [];
        public Dictionary<string, GenericTypeParameterBuilder[]> GenericParams { get; } = [];
        public Dictionary<string, bool> IsGeneric { get; } = [];
    }

    /// <summary>
    /// State for closure and arrow function compilation.
    /// </summary>
    private sealed class ClosureCompilationState
    {
        public ClosureAnalyzer Analyzer { get; set; } = null!;
        public Dictionary<Expr.ArrowFunction, MethodBuilder> ArrowMethods { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, TypeBuilder> DisplayClasses { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, Dictionary<string, FieldBuilder>> DisplayClassFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, ConstructorBuilder> DisplayClassConstructors { get; } = new(ReferenceEqualityComparer.Instance);
        public int ArrowMethodCounter { get; set; }
        public int DisplayClassCounter { get; set; }

        // Entry-point display class for captured top-level variables
        // When a top-level variable is captured by a closure, it's stored here
        // so modifications in the closure are visible to the outer code
        public TypeBuilder? EntryPointDisplayClass { get; set; }
        public ConstructorBuilder? EntryPointDisplayClassCtor { get; set; }
        public Dictionary<string, FieldBuilder> EntryPointDisplayClassFields { get; } = [];
        public HashSet<string> CapturedTopLevelVars { get; } = [];

        // Static field on $Program that holds the entry-point display class instance
        // This allows module init methods to access the same display class
        public FieldBuilder? EntryPointDisplayClassStaticField { get; set; }

        // Maps arrow functions to their $entryPointDC field (if they capture top-level vars)
        public Dictionary<Expr.ArrowFunction, FieldBuilder> ArrowEntryPointDCFields { get; } = new(ReferenceEqualityComparer.Instance);

        // ============================================
        // Function-level display classes (for captured local variables)
        // ============================================

        // Maps function (by qualified name) to its display class type
        public Dictionary<string, TypeBuilder> FunctionDisplayClasses { get; } = [];

        // Maps function to its display class constructor
        public Dictionary<string, ConstructorBuilder> FunctionDisplayClassCtors { get; } = [];

        // Maps function to its display class fields (variable name -> field)
        public Dictionary<string, Dictionary<string, FieldBuilder>> FunctionDisplayClassFields { get; } = [];

        // Maps function to its AST node (needed for ClosureAnalyzer lookups)
        public Dictionary<string, object> FunctionAstNodes { get; } = [];

        // Maps arrow functions to their $functionDC field (if they capture function-level vars)
        public Dictionary<Expr.ArrowFunction, FieldBuilder> ArrowFunctionDCFields { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps arrow functions to the function display class they need access to
        public Dictionary<Expr.ArrowFunction, string> ArrowFunctionDCSource { get; } = new(ReferenceEqualityComparer.Instance);

        // Maps arrow functions to their resolved parameter types (for typed parameter optimization)
        // When parameters have type annotations, this stores the resolved .NET types to avoid boxing
        public Dictionary<Expr.ArrowFunction, Type[]> ArrowParameterTypes { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// State for async function compilation.
    /// </summary>
    private sealed class AsyncCompilationState
    {
        public AsyncStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, AsyncStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public int StateMachineCounter { get; set; }
        public int ArrowCounter { get; set; }

        // Arrow function async support
        public Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> ArrowBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, AsyncStateMachineBuilder> ArrowOuterBuilders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ArrowFunction, AsyncArrowStateMachineBuilder> ArrowParentBuilders { get; } = new(ReferenceEqualityComparer.Instance);

        // Pooled HashSets for async arrow analysis
        public HashSet<string> DeclaredVars { get; } = [];
        public HashSet<string> UsedAfterAwait { get; } = [];
        public HashSet<string> DeclaredBeforeAwait { get; } = [];
    }

    /// <summary>
    /// State for generator function compilation.
    /// </summary>
    private sealed class GeneratorCompilationState
    {
        public GeneratorStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, GeneratorStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public int StateMachineCounter { get; set; }
    }

    /// <summary>
    /// State for async generator function compilation.
    /// </summary>
    private sealed class AsyncGeneratorCompilationState
    {
        public AsyncGeneratorStateAnalyzer Analyzer { get; } = new();
        public Dictionary<string, AsyncGeneratorStateMachineBuilder> StateMachines { get; } = [];
        public Dictionary<string, Stmt.Function> Functions { get; } = [];
        public int StateMachineCounter { get; set; }
    }

    /// <summary>
    /// State for module compilation.
    /// </summary>
    private sealed class ModuleCompilationState
    {
        public Dictionary<string, TypeBuilder> Types { get; } = [];
        public Dictionary<string, Dictionary<string, FieldBuilder>> ExportFields { get; } = [];
        public Dictionary<string, MethodBuilder> InitMethods { get; } = [];
        public Dictionary<string, string> ClassToModule { get; } = [];
        public Dictionary<string, string> FunctionToModule { get; } = [];
        public Dictionary<string, string> EnumToModule { get; } = [];
        public Dictionary<string, string?> Namespaces { get; } = [];
        public ModuleResolver? Resolver { get; set; }
        public string? CurrentPath { get; set; }
        public string? CurrentDotNetNamespace { get; set; }
        /// <summary>
        /// Maps module path to the qualified class name when the module uses `export = ClassName`.
        /// Used to enable compile-time static member resolution for imported classes.
        /// </summary>
        public Dictionary<string, string> ExportAssignmentClasses { get; } = [];

        /// <summary>
        /// Maps module path to a dictionary of export name to qualified class name.
        /// Used for resolving named class imports to direct constructor calls.
        /// Example: ExportedClasses["./person.ts"]["Person"] = "$M_person_Person"
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ExportedClasses { get; } = [];

        /// <summary>
        /// Maps module path to the qualified class name for default class exports.
        /// Used for resolving default class imports to direct constructor calls.
        /// Example: DefaultExportClasses["./counter.ts"] = "$M_counter_Counter"
        /// </summary>
        public Dictionary<string, string> DefaultExportClasses { get; } = [];
    }

    /// <summary>
    /// State for enum compilation.
    /// </summary>
    private sealed class EnumCompilationState
    {
        public Dictionary<string, Dictionary<string, object>> Members { get; } = [];
        public Dictionary<string, Dictionary<double, string>> Reverse { get; } = [];
        public Dictionary<string, EnumKind> Kinds { get; } = [];
        public HashSet<string> ConstEnums { get; } = [];
    }

    /// <summary>
    /// State for class expression compilation.
    /// </summary>
    private sealed class ClassExpressionCompilationState
    {
        public Dictionary<Expr.ClassExpr, TypeBuilder> Builders { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, string> Names { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, Expr.ClassExpr> VarToClassExpr { get; } = [];
        public List<Expr.ClassExpr> ToDefine { get; } = [];
        public int Counter { get; set; }

        // Extended tracking (mirrors class compilation state)
        public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>> BackingFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, PropertyBuilder>> Properties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, Type>> PropertyTypes { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, HashSet<string>> DeclaredProperties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, HashSet<string>> ReadonlyProperties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>> StaticFields { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> StaticMethods { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> InstanceMethods { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> Getters { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>> Setters { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, ConstructorBuilder> Constructors { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, GenericTypeParameterBuilder[]> GenericParams { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<Expr.ClassExpr, string?> Superclass { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// State for typed interop (real .NET properties).
    /// </summary>
    private sealed class TypedInteropState
    {
        public Dictionary<string, Dictionary<string, FieldBuilder>> PropertyBackingFields { get; } = [];
        public Dictionary<string, Dictionary<string, PropertyBuilder>> ClassProperties { get; } = [];
        public Dictionary<string, HashSet<string>> DeclaredPropertyNames { get; } = [];
        public Dictionary<string, HashSet<string>> ReadonlyPropertyNames { get; } = [];
        public Dictionary<string, Dictionary<string, Type>> PropertyTypes { get; } = [];
        public Dictionary<string, FieldBuilder> ExtrasFields { get; } = [];
        public Dictionary<string, Dictionary<string, (MethodBuilder? Getter, MethodBuilder? Setter, Type PropertyType)>> ExplicitAccessors { get; } = [];
    }

    /// <summary>
    /// State for @lock decorator support.
    /// </summary>
    private sealed class LockDecoratorState
    {
        // Instance locks
        public Dictionary<string, FieldBuilder> SyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> AsyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> ReentrancyFields { get; } = [];

        // Static locks
        public Dictionary<string, FieldBuilder> StaticSyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> StaticAsyncLockFields { get; } = [];
        public Dictionary<string, FieldBuilder> StaticReentrancyFields { get; } = [];
    }

    #endregion
}
