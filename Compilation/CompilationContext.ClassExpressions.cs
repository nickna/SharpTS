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
    public Dictionary<Expr.ClassExpr, GenericTypeParameterBuilder[]>? ClassExprGenericParams { get; set; }
    public Dictionary<Expr.ClassExpr, string?>? ClassExprSuperclass { get; set; }

    // Current class expression being compiled
    public Expr.ClassExpr? CurrentClassExpr { get; set; }

    // Variable name to class expression mapping (for static member access)
    public Dictionary<string, Expr.ClassExpr>? VarToClassExpr { get; set; }
}
