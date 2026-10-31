using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Class Expression Support (inline class definitions)
    // ============================================

    // Class expression builders (class expr node -> type builder)
    public Dictionary<Expr.ClassExpr, TypeBuilder>? ClassExprBuilders { get; set; }
    internal Dictionary<Expr.ClassExpr, ClassExpressionFactory>? ClassExprFactories { get; set; }
    internal Dictionary<Expr.ClassExpr, FieldBuilder>? ClassExprDefinitionFields { get; set; }
    internal Dictionary<Stmt.Class, Expr.ClassExpr>? RuntimeClassDeclarations { get; set; }

    // Class expression extended tracking
    public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>>? ClassExprBackingFields { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, PropertyBuilder>>? ClassExprProperties { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, Type>>? ClassExprPropertyTypes { get; set; }
    public Dictionary<Expr.ClassExpr, HashSet<string>>? ClassExprDeclaredProperties { get; set; }
    public Dictionary<Expr.ClassExpr, HashSet<string>>? ClassExprReadonlyProperties { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, FieldBuilder>>? ClassExprStaticFields { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>>? ClassExprStaticMethods { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>>? ClassExprInstanceMethods { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>>? ClassExprGetters { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, MethodBuilder>>? ClassExprSetters { get; set; }
    public Dictionary<Expr.ClassExpr, ConstructorBuilder>? ClassExprConstructors { get; set; }
    public ClassGenericParameterRegistry? ClassGenericParameters { get; set; }
    internal NativeClassInitializationEmission? NativeClassInitialization { get; set; }
    internal NativeClrAdmission? NativeClrAdmission { get; init; }
    internal NativeClosedSourceFamilies? NativeClosedFamilies { get; init; }
    public Dictionary<Expr.ClassExpr, string?>? ClassExprSuperclass { get; set; }
    public Dictionary<Expr.ClassExpr, Dictionary<string, int>>? ClassExprCaptureSlots { get; set; }
    internal const string PrivateOwnerCaptureName = "'privateOwner";
    internal const string SuperOwnerCaptureName = "'superOwner";
    // This key cannot collide with a source identifier such as `__this`.
    internal const string ThisParameterName = "'this";
    internal IReadOnlyDictionary<Expr.ClassExpr, string>? ClassExprEnclosingClasses { get; set; }

    // Current class expression being compiled
    public Expr.ClassExpr? CurrentClassExpr { get; set; }
    internal int? ClassDefinitionParameterIndex { get; set; }
    internal int? ClassDefinitionThisParameterIndex { get; set; }
    internal string? ClassDefinitionVariableName { get; set; }
    internal FieldBuilder? ClassDefinitionOwnerField { get; set; }
    internal string? GuestThisVariableName { get; set; }
    internal bool HasGuestReceiver { get; set; }
    internal bool CapturesClassConstructorReturn { get; set; }
    internal bool IsDerivedClassConstructor { get; set; }
    internal LocalBuilder? ConstructorSuperBound { get; set; }
    internal LocalBuilder? ClassConstructorReceiver { get; set; }
    internal LocalBuilder? ClassConstructorReturnValue { get; set; }
    internal LocalBuilder? ClassConstructorThisCell { get; set; }
    internal bool CapturesConstructorThisCell { get; set; }
    internal Action? EmitConstructorOwnElements { get; set; }
    internal bool IsRuntimeReceiverInitializer { get; set; }
    internal bool UseNominalSuperCallBridge { get; set; }

    // Variable name to class expression mapping (for static member access)
    public Dictionary<string, Expr.ClassExpr>? VarToClassExpr { get; set; }

    public DeferredClassDefinitionRegistry? DeferredClassDefinitions { get; set; }
}
