namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Base class for AST visitors that perform analysis without returning values.
/// Provides default implementations that traverse all children.
/// </summary>
/// <remarks>
/// Subclasses override specific Visit* methods to add custom behavior.
/// The default implementation for each node type traverses its children,
/// so subclasses only need to override the nodes they care about.
/// </remarks>
public abstract class AstVisitorBase
{
    /// <summary>
    /// Controls whether traversal continues. Set to false to stop visiting.
    /// </summary>
    protected bool ShouldContinue { get; set; } = true;

    /// <summary>
    /// Visit an expression node using the dispatcher.
    /// </summary>
    public virtual void Visit(Expr expr)
    {
        if (!ShouldContinue) return;

        switch (expr)
        {
            case Expr.Binary e: VisitBinary(e); break;
            case Expr.Logical e: VisitLogical(e); break;
            case Expr.NullishCoalescing e: VisitNullishCoalescing(e); break;
            case Expr.Ternary e: VisitTernary(e); break;
            case Expr.Grouping e: VisitGrouping(e); break;
            case Expr.Literal e: VisitLiteral(e); break;
            case Expr.Unary e: VisitUnary(e); break;
            case Expr.Variable e: VisitVariable(e); break;
            case Expr.Assign e: VisitAssign(e); break;
            case Expr.Call e: VisitCall(e); break;
            case Expr.Get e: VisitGet(e); break;
            case Expr.Set e: VisitSet(e); break;
            case Expr.This e: VisitThis(e); break;
            case Expr.New e: VisitNew(e); break;
            case Expr.ArrayLiteral e: VisitArrayLiteral(e); break;
            case Expr.ObjectLiteral e: VisitObjectLiteral(e); break;
            case Expr.GetIndex e: VisitGetIndex(e); break;
            case Expr.SetIndex e: VisitSetIndex(e); break;
            case Expr.Super e: VisitSuper(e); break;
            case Expr.CompoundAssign e: VisitCompoundAssign(e); break;
            case Expr.CompoundSet e: VisitCompoundSet(e); break;
            case Expr.CompoundSetIndex e: VisitCompoundSetIndex(e); break;
            case Expr.PrefixIncrement e: VisitPrefixIncrement(e); break;
            case Expr.PostfixIncrement e: VisitPostfixIncrement(e); break;
            case Expr.ArrowFunction e: VisitArrowFunction(e); break;
            case Expr.TemplateLiteral e: VisitTemplateLiteral(e); break;
            case Expr.Spread e: VisitSpread(e); break;
            case Expr.TypeAssertion e: VisitTypeAssertion(e); break;
            case Expr.NonNullAssertion e: VisitNonNullAssertion(e); break;
            case Expr.Await e: VisitAwait(e); break;
            case Expr.DynamicImport e: VisitDynamicImport(e); break;
            case Expr.ImportMeta e: VisitImportMeta(e); break;
            case Expr.Yield e: VisitYield(e); break;
            case Expr.RegexLiteral e: VisitRegexLiteral(e); break;
            case Expr.ClassExpr e: VisitClassExpr(e); break;
        }
    }

    /// <summary>
    /// Visit a statement node using the dispatcher.
    /// </summary>
    public virtual void Visit(Stmt stmt)
    {
        if (!ShouldContinue) return;

        switch (stmt)
        {
            case Stmt.Expression s: VisitExpressionStmt(s); break;
            case Stmt.Var s: VisitVar(s); break;
            case Stmt.Function s: VisitFunction(s); break;
            case Stmt.Field s: VisitField(s); break;
            case Stmt.Accessor s: VisitAccessor(s); break;
            case Stmt.Class s: VisitClass(s); break;
            case Stmt.Interface s: VisitInterface(s); break;
            case Stmt.Block s: VisitBlock(s); break;
            case Stmt.Sequence s: VisitSequence(s); break;
            case Stmt.Return s: VisitReturn(s); break;
            case Stmt.While s: VisitWhile(s); break;
            case Stmt.DoWhile s: VisitDoWhile(s); break;
            case Stmt.ForOf s: VisitForOf(s); break;
            case Stmt.ForIn s: VisitForIn(s); break;
            case Stmt.If s: VisitIf(s); break;
            case Stmt.Print s: VisitPrint(s); break;
            case Stmt.Break s: VisitBreak(s); break;
            case Stmt.Continue s: VisitContinue(s); break;
            case Stmt.LabeledStatement s: VisitLabeledStatement(s); break;
            case Stmt.Switch s: VisitSwitch(s); break;
            case Stmt.TryCatch s: VisitTryCatch(s); break;
            case Stmt.Throw s: VisitThrow(s); break;
            case Stmt.TypeAlias s: VisitTypeAlias(s); break;
            case Stmt.Enum s: VisitEnum(s); break;
            case Stmt.Namespace s: VisitNamespace(s); break;
            case Stmt.Import s: VisitImport(s); break;
            case Stmt.Export s: VisitExport(s); break;
            case Stmt.FileDirective s: VisitFileDirective(s); break;
        }
    }

    #region Expression Visitors - Default implementations traverse children

    protected virtual void VisitBinary(Expr.Binary expr)
    {
        Visit(expr.Left);
        Visit(expr.Right);
    }

    protected virtual void VisitLogical(Expr.Logical expr)
    {
        Visit(expr.Left);
        Visit(expr.Right);
    }

    protected virtual void VisitNullishCoalescing(Expr.NullishCoalescing expr)
    {
        Visit(expr.Left);
        Visit(expr.Right);
    }

    protected virtual void VisitTernary(Expr.Ternary expr)
    {
        Visit(expr.Condition);
        Visit(expr.ThenBranch);
        Visit(expr.ElseBranch);
    }

    protected virtual void VisitGrouping(Expr.Grouping expr)
    {
        Visit(expr.Expression);
    }

    protected virtual void VisitLiteral(Expr.Literal expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitUnary(Expr.Unary expr)
    {
        Visit(expr.Right);
    }

    protected virtual void VisitVariable(Expr.Variable expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitAssign(Expr.Assign expr)
    {
        Visit(expr.Value);
    }

    protected virtual void VisitCall(Expr.Call expr)
    {
        Visit(expr.Callee);
        foreach (var arg in expr.Arguments)
            Visit(arg);
    }

    protected virtual void VisitGet(Expr.Get expr)
    {
        Visit(expr.Object);
    }

    protected virtual void VisitSet(Expr.Set expr)
    {
        Visit(expr.Object);
        Visit(expr.Value);
    }

    protected virtual void VisitThis(Expr.This expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitNew(Expr.New expr)
    {
        foreach (var arg in expr.Arguments)
            Visit(arg);
    }

    protected virtual void VisitArrayLiteral(Expr.ArrayLiteral expr)
    {
        foreach (var elem in expr.Elements)
            Visit(elem);
    }

    protected virtual void VisitObjectLiteral(Expr.ObjectLiteral expr)
    {
        foreach (var prop in expr.Properties)
        {
            if (prop.Key is Expr.ComputedKey ck)
                Visit(ck.Expression);
            Visit(prop.Value);
        }
    }

    protected virtual void VisitGetIndex(Expr.GetIndex expr)
    {
        Visit(expr.Object);
        Visit(expr.Index);
    }

    protected virtual void VisitSetIndex(Expr.SetIndex expr)
    {
        Visit(expr.Object);
        Visit(expr.Index);
        Visit(expr.Value);
    }

    protected virtual void VisitSuper(Expr.Super expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        Visit(expr.Value);
    }

    protected virtual void VisitCompoundSet(Expr.CompoundSet expr)
    {
        Visit(expr.Object);
        Visit(expr.Value);
    }

    protected virtual void VisitCompoundSetIndex(Expr.CompoundSetIndex expr)
    {
        Visit(expr.Object);
        Visit(expr.Index);
        Visit(expr.Value);
    }

    protected virtual void VisitPrefixIncrement(Expr.PrefixIncrement expr)
    {
        Visit(expr.Operand);
    }

    protected virtual void VisitPostfixIncrement(Expr.PostfixIncrement expr)
    {
        Visit(expr.Operand);
    }

    protected virtual void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Visit default parameter values
        foreach (var p in expr.Parameters)
            if (p.DefaultValue != null)
                Visit(p.DefaultValue);

        // Visit body
        if (expr.ExpressionBody != null)
            Visit(expr.ExpressionBody);
        if (expr.BlockBody != null)
            foreach (var s in expr.BlockBody)
                Visit(s);
    }

    protected virtual void VisitTemplateLiteral(Expr.TemplateLiteral expr)
    {
        foreach (var e in expr.Expressions)
            Visit(e);
    }

    protected virtual void VisitSpread(Expr.Spread expr)
    {
        Visit(expr.Expression);
    }

    protected virtual void VisitTypeAssertion(Expr.TypeAssertion expr)
    {
        Visit(expr.Expression);
    }

    protected virtual void VisitNonNullAssertion(Expr.NonNullAssertion expr)
    {
        Visit(expr.Expression);
    }

    protected virtual void VisitAwait(Expr.Await expr)
    {
        Visit(expr.Expression);
    }

    protected virtual void VisitDynamicImport(Expr.DynamicImport expr)
    {
        Visit(expr.PathExpression);
    }

    protected virtual void VisitImportMeta(Expr.ImportMeta expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitYield(Expr.Yield expr)
    {
        if (expr.Value != null)
            Visit(expr.Value);
    }

    protected virtual void VisitRegexLiteral(Expr.RegexLiteral expr)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitClassExpr(Expr.ClassExpr expr)
    {
        // Visit field initializers
        foreach (var f in expr.Fields)
            if (f.Initializer != null)
                Visit(f.Initializer);

        // Visit methods
        foreach (var m in expr.Methods)
            VisitFunction(m);

        // Visit accessors
        if (expr.Accessors != null)
            foreach (var a in expr.Accessors)
                VisitAccessor(a);
    }

    #endregion

    #region Statement Visitors - Default implementations traverse children

    protected virtual void VisitExpressionStmt(Stmt.Expression stmt)
    {
        Visit(stmt.Expr);
    }

    protected virtual void VisitVar(Stmt.Var stmt)
    {
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);
    }

    protected virtual void VisitFunction(Stmt.Function stmt)
    {
        // Visit default parameter values
        foreach (var p in stmt.Parameters)
            if (p.DefaultValue != null)
                Visit(p.DefaultValue);

        // Visit body
        if (stmt.Body != null)
            foreach (var s in stmt.Body)
                Visit(s);
    }

    protected virtual void VisitField(Stmt.Field stmt)
    {
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);
    }

    protected virtual void VisitAccessor(Stmt.Accessor stmt)
    {
        foreach (var s in stmt.Body)
            Visit(s);
    }

    protected virtual void VisitClass(Stmt.Class stmt)
    {
        // Visit field initializers
        foreach (var f in stmt.Fields)
            if (f.Initializer != null)
                Visit(f.Initializer);

        // Visit methods
        foreach (var m in stmt.Methods)
            VisitFunction(m);

        // Visit accessors
        if (stmt.Accessors != null)
            foreach (var a in stmt.Accessors)
                VisitAccessor(a);
    }

    protected virtual void VisitInterface(Stmt.Interface stmt)
    {
        // Type-only - no runtime expressions to visit
    }

    protected virtual void VisitBlock(Stmt.Block stmt)
    {
        foreach (var s in stmt.Statements)
            Visit(s);
    }

    protected virtual void VisitSequence(Stmt.Sequence stmt)
    {
        foreach (var s in stmt.Statements)
            Visit(s);
    }

    protected virtual void VisitReturn(Stmt.Return stmt)
    {
        if (stmt.Value != null)
            Visit(stmt.Value);
    }

    protected virtual void VisitWhile(Stmt.While stmt)
    {
        Visit(stmt.Condition);
        Visit(stmt.Body);
    }

    protected virtual void VisitDoWhile(Stmt.DoWhile stmt)
    {
        Visit(stmt.Body);
        Visit(stmt.Condition);
    }

    protected virtual void VisitForOf(Stmt.ForOf stmt)
    {
        Visit(stmt.Iterable);
        Visit(stmt.Body);
    }

    protected virtual void VisitForIn(Stmt.ForIn stmt)
    {
        Visit(stmt.Object);
        Visit(stmt.Body);
    }

    protected virtual void VisitIf(Stmt.If stmt)
    {
        Visit(stmt.Condition);
        Visit(stmt.ThenBranch);
        if (stmt.ElseBranch != null)
            Visit(stmt.ElseBranch);
    }

    protected virtual void VisitPrint(Stmt.Print stmt)
    {
        Visit(stmt.Expr);
    }

    protected virtual void VisitBreak(Stmt.Break stmt)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitContinue(Stmt.Continue stmt)
    {
        // Leaf node - no children to visit
    }

    protected virtual void VisitLabeledStatement(Stmt.LabeledStatement stmt)
    {
        Visit(stmt.Statement);
    }

    protected virtual void VisitSwitch(Stmt.Switch stmt)
    {
        Visit(stmt.Subject);
        foreach (var c in stmt.Cases)
        {
            Visit(c.Value);
            foreach (var s in c.Body)
                Visit(s);
        }
        if (stmt.DefaultBody != null)
            foreach (var s in stmt.DefaultBody)
                Visit(s);
    }

    protected virtual void VisitTryCatch(Stmt.TryCatch stmt)
    {
        foreach (var s in stmt.TryBlock)
            Visit(s);
        if (stmt.CatchBlock != null)
            foreach (var s in stmt.CatchBlock)
                Visit(s);
        if (stmt.FinallyBlock != null)
            foreach (var s in stmt.FinallyBlock)
                Visit(s);
    }

    protected virtual void VisitThrow(Stmt.Throw stmt)
    {
        Visit(stmt.Value);
    }

    protected virtual void VisitTypeAlias(Stmt.TypeAlias stmt)
    {
        // Type-only - no runtime expressions to visit
    }

    protected virtual void VisitEnum(Stmt.Enum stmt)
    {
        foreach (var m in stmt.Members)
            if (m.Value != null)
                Visit(m.Value);
    }

    protected virtual void VisitNamespace(Stmt.Namespace stmt)
    {
        foreach (var m in stmt.Members)
            Visit(m);
    }

    protected virtual void VisitImport(Stmt.Import stmt)
    {
        // Declaration-only - no runtime expressions to visit
    }

    protected virtual void VisitExport(Stmt.Export stmt)
    {
        if (stmt.Declaration != null)
            Visit(stmt.Declaration);
        if (stmt.DefaultExpr != null)
            Visit(stmt.DefaultExpr);
    }

    protected virtual void VisitFileDirective(Stmt.FileDirective stmt)
    {
        // Metadata-only - no runtime expressions to visit
    }

    #endregion
}
