using System.Collections.ObjectModel;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Selects top-level constant arrows whose names identify exactly one runtime binding.
/// Ambiguous names retain ordinary callable dispatch instead of substituting another owner.
/// </summary>
internal sealed class ConstArrowBindingAnalyzer : AstVisitorBase
{
    private readonly Dictionary<string, int> _declarations = new(StringComparer.Ordinal);
    private bool _countPatternNames;

    public static IReadOnlyDictionary<string, Expr.ArrowFunction> Collect(IEnumerable<Stmt> statements)
    {
        var program = statements.ToArray();
        var analyzer = new ConstArrowBindingAnalyzer();
        foreach (var statement in program)
            analyzer.Visit(statement);
        var result = new Dictionary<string, Expr.ArrowFunction>(StringComparer.Ordinal);
        foreach (var statement in program)
        {
            var declaration = statement is Stmt.Export export ? export.Declaration : statement;
            if (declaration is Stmt.Const { Initializer: Expr.ArrowFunction arrow } constant
                && analyzer._declarations.GetValueOrDefault(constant.Name.Lexeme) == 1)
                result.Add(constant.Name.Lexeme, arrow);
        }
        return new ReadOnlyDictionary<string, Expr.ArrowFunction>(result);
    }

    private void Count(Token? name)
    {
        if (name != null)
            _declarations[name.Lexeme] = _declarations.GetValueOrDefault(name.Lexeme) + 1;
    }

    private void CountParameters(IEnumerable<Stmt.Parameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            Count(parameter.Name);
            if (parameter.DestructuredProperties != null)
                foreach (var property in parameter.DestructuredProperties)
                {
                    Count(property.Binding);
                    if (property.DefaultValue != null)
                        Visit(property.DefaultValue);
                }
        }
    }

    protected override void VisitVar(Stmt.Var stmt) { Count(stmt.Name); base.VisitVar(stmt); }
    protected override void VisitConst(Stmt.Const stmt) { Count(stmt.Name); base.VisitConst(stmt); }
    protected override void VisitFunction(Stmt.Function stmt)
    {
        Count(stmt.Name);
        CountParameters(stmt.Parameters);
        if (stmt.ComputedKey != null)
            Visit(stmt.ComputedKey);
        base.VisitFunction(stmt);
    }
    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        Count(expr.Name);
        CountParameters(expr.Parameters);
        base.VisitArrowFunction(expr);
    }
    protected override void VisitAccessor(Stmt.Accessor stmt)
    {
        if (stmt.SetterParam != null)
            CountParameters([stmt.SetterParam]);
        base.VisitAccessor(stmt);
    }
    protected override void VisitForOf(Stmt.ForOf stmt) { Count(stmt.Variable); base.VisitForOf(stmt); }
    protected override void VisitForIn(Stmt.ForIn stmt) { Count(stmt.Variable); base.VisitForIn(stmt); }
    protected override void VisitTryCatch(Stmt.TryCatch stmt) { Count(stmt.CatchParam); base.VisitTryCatch(stmt); }
    protected override void VisitClass(Stmt.Class stmt)
    {
        Count(stmt.Name);
        base.VisitClass(stmt);
        VisitClassExtras(stmt.SuperclassExpr, stmt.Fields, stmt.AutoAccessors, stmt.StaticInitializers);
    }
    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        Count(expr.Name);
        base.VisitClassExpr(expr);
        VisitClassExtras(expr.SuperclassExpr, expr.Fields, expr.AutoAccessors, expr.StaticInitializers);
    }

    private void VisitClassExtras(Expr? superclass, IEnumerable<Stmt.Field> fields,
        IEnumerable<Stmt.AutoAccessor>? accessors, IEnumerable<Stmt>? initializers)
    {
        if (superclass != null)
            Visit(superclass);
        foreach (var field in fields)
            if (field.ComputedKey != null)
                Visit(field.ComputedKey);
        if (accessors != null)
            foreach (var accessor in accessors)
                Visit(accessor);
        if (initializers != null)
            foreach (var block in initializers.OfType<Stmt.StaticBlock>())
                Visit(block);
    }
    protected override void VisitEnum(Stmt.Enum stmt) { Count(stmt.Name); base.VisitEnum(stmt); }
    protected override void VisitNamespace(Stmt.Namespace stmt) { Count(stmt.Name); base.VisitNamespace(stmt); }
    protected override void VisitImportAlias(Stmt.ImportAlias stmt) { Count(stmt.AliasName); base.VisitImportAlias(stmt); }
    protected override void VisitImportRequire(Stmt.ImportRequire stmt) { Count(stmt.AliasName); base.VisitImportRequire(stmt); }
    protected override void VisitImport(Stmt.Import stmt)
    {
        if (!stmt.IsTypeOnly)
        {
            Count(stmt.DefaultImport);
            Count(stmt.NamespaceImport);
            if (stmt.NamedImports != null)
                foreach (var import in stmt.NamedImports.Where(import => !import.IsTypeOnly))
                    Count(import.LocalName ?? import.Imported);
        }
        base.VisitImport(stmt);
    }
    protected override void VisitUsing(Stmt.Using stmt)
    {
        foreach (var binding in stmt.Bindings)
        {
            Count(binding.Name);
            if (binding.DestructuringPattern != null)
            {
                _countPatternNames = true;
                Visit(binding.DestructuringPattern);
                _countPatternNames = false;
            }
        }
        base.VisitUsing(stmt);
    }
    protected override void VisitVariable(Expr.Variable expr)
    {
        if (_countPatternNames)
            Count(expr.Name);
    }
    protected override void VisitAssign(Expr.Assign expr) { Count(expr.Name); base.VisitAssign(expr); }
}
