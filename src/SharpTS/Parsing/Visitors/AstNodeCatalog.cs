namespace SharpTS.Parsing.Visitors;

/// <summary>
/// The single canonical list of concrete AST node types declared by <see cref="Expr"/>
/// and <see cref="Stmt"/> in <c>Parsing/AST.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the convergence point for the AST dispatch guard tests (issue #1243, epic #1094).
/// Production dispatch uses compile-time switches; the managed test suite reflects the directly
/// nested <see cref="Expr"/>/<see cref="Stmt"/> types and requires this catalog to match them
/// exactly. A new node therefore requires an explicit catalog entry and dispatch arm in the same
/// change, without retaining open-ended nested-type reflection in the Native AOT compiler.
/// </para>
/// <para>
/// "Node type" is defined exactly as the dispatch tables historically defined it: a directly
/// nested type of <see cref="Expr"/>/<see cref="Stmt"/> that is concrete and inherits from the
/// base. This excludes helper records nested under the bases that are not themselves dispatched
/// nodes — <c>Expr.PropertyKey</c>/<c>Property</c>, <c>Stmt.Parameter</c>, the interface-member
/// records, and so on.
/// </para>
/// </remarks>
public static class AstNodeCatalog
{
    /// <summary>Every concrete <see cref="Expr"/> node type, in declaration order.</summary>
    public static readonly IReadOnlyList<Type> ExprTypes =
    [
        typeof(Expr.Comma),
        typeof(Expr.DestructuringAssign),
        typeof(Expr.Binary),
        typeof(Expr.Logical),
        typeof(Expr.NullishCoalescing),
        typeof(Expr.Ternary),
        typeof(Expr.Grouping),
        typeof(Expr.Literal),
        typeof(Expr.Unary),
        typeof(Expr.Delete),
        typeof(Expr.Variable),
        typeof(Expr.Assign),
        typeof(Expr.Call),
        typeof(Expr.Get),
        typeof(Expr.Set),
        typeof(Expr.GetPrivate),
        typeof(Expr.SetPrivate),
        typeof(Expr.CallPrivate),
        typeof(Expr.This),
        typeof(Expr.New),
        typeof(Expr.ArrayLiteral),
        typeof(Expr.ObjectLiteral),
        typeof(Expr.GetIndex),
        typeof(Expr.SetIndex),
        typeof(Expr.Super),
        typeof(Expr.CompoundAssign),
        typeof(Expr.CompoundSet),
        typeof(Expr.CompoundSetIndex),
        typeof(Expr.LogicalAssign),
        typeof(Expr.LogicalSet),
        typeof(Expr.LogicalSetIndex),
        typeof(Expr.PrefixIncrement),
        typeof(Expr.PostfixIncrement),
        typeof(Expr.ArrowFunction),
        typeof(Expr.TemplateLiteral),
        typeof(Expr.TaggedTemplateLiteral),
        typeof(Expr.Spread),
        typeof(Expr.TypeAssertion),
        typeof(Expr.Satisfies),
        typeof(Expr.Await),
        typeof(Expr.DynamicImport),
        typeof(Expr.ImportMeta),
        typeof(Expr.Yield),
        typeof(Expr.RegexLiteral),
        typeof(Expr.NonNullAssertion),
        typeof(Expr.ClassExpr),
    ];

    /// <summary>Every concrete <see cref="Stmt"/> node type, in declaration order.</summary>
    public static readonly IReadOnlyList<Type> StmtTypes =
    [
        typeof(Stmt.Expression),
        typeof(Stmt.Var),
        typeof(Stmt.Const),
        typeof(Stmt.Function),
        typeof(Stmt.Field),
        typeof(Stmt.Accessor),
        typeof(Stmt.AutoAccessor),
        typeof(Stmt.Class),
        typeof(Stmt.StaticBlock),
        typeof(Stmt.Interface),
        typeof(Stmt.Block),
        typeof(Stmt.Sequence),
        typeof(Stmt.Return),
        typeof(Stmt.While),
        typeof(Stmt.For),
        typeof(Stmt.DoWhile),
        typeof(Stmt.ForOf),
        typeof(Stmt.ForIn),
        typeof(Stmt.If),
        typeof(Stmt.Break),
        typeof(Stmt.Continue),
        typeof(Stmt.LabeledStatement),
        typeof(Stmt.Switch),
        typeof(Stmt.TryCatch),
        typeof(Stmt.Throw),
        typeof(Stmt.TypeAlias),
        typeof(Stmt.Enum),
        typeof(Stmt.Namespace),
        typeof(Stmt.ImportAlias),
        typeof(Stmt.ImportRequire),
        typeof(Stmt.Import),
        typeof(Stmt.Export),
        typeof(Stmt.FileDirective),
        typeof(Stmt.Directive),
        typeof(Stmt.DeclareModule),
        typeof(Stmt.DeclareGlobal),
        typeof(Stmt.Using),
    ];
}
