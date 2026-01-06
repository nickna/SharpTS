using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes generator functions to identify yield points and variables that must be hoisted
/// to the state machine struct.
/// </summary>
public class GeneratorStateAnalyzer
{
    /// <summary>
    /// Represents a single yield point in a generator function.
    /// </summary>
    public record YieldPoint(
        int StateNumber,
        Expr.Yield YieldExpr,
        HashSet<string> LiveVariables
    );

    /// <summary>
    /// Complete analysis results for a generator function.
    /// </summary>
    public record GeneratorFunctionAnalysis(
        int YieldPointCount,
        List<YieldPoint> YieldPoints,
        HashSet<string> HoistedLocals,
        HashSet<string> HoistedParameters,
        bool UsesThis,
        bool HasYieldStar
    );

    // State during analysis
    private readonly List<YieldPoint> _yieldPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterYield = [];
    private readonly HashSet<string> _variablesDeclaredBeforeYield = [];
    private int _yieldCounter = 0;
    private bool _seenYield = false;
    private bool _usesThis = false;
    private bool _hasYieldStar = false;

    /// <summary>
    /// Analyzes a generator function to determine yield points and hoisted variables.
    /// </summary>
    public GeneratorFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Collect parameters as variables that need hoisting
        var parameters = new HashSet<string>();
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeYield.Add(param.Name.Lexeme);
        }

        // Analyze the function body
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                AnalyzeStmt(stmt);
            }
        }

        // Variables that need hoisting: declared before yield AND used after yield
        var hoistedLocals = new HashSet<string>(_variablesDeclaredBeforeYield);
        hoistedLocals.IntersectWith(_variablesUsedAfterYield);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately

        return new GeneratorFunctionAnalysis(
            YieldPointCount: _yieldPoints.Count,
            YieldPoints: [.. _yieldPoints],
            HoistedLocals: hoistedLocals,
            HoistedParameters: parameters,
            UsesThis: _usesThis,
            HasYieldStar: _hasYieldStar
        );
    }

    private void Reset()
    {
        _yieldPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterYield.Clear();
        _variablesDeclaredBeforeYield.Clear();
        _yieldCounter = 0;
        _seenYield = false;
        _usesThis = false;
        _hasYieldStar = false;
    }

    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                _declaredVariables.Add(v.Name.Lexeme);
                if (!_seenYield)
                    _variablesDeclaredBeforeYield.Add(v.Name.Lexeme);
                if (v.Initializer != null)
                    AnalyzeExpr(v.Initializer);
                break;

            case Stmt.Expression e:
                AnalyzeExpr(e.Expr);
                break;

            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeExpr(r.Value);
                break;

            case Stmt.If i:
                AnalyzeExpr(i.Condition);
                AnalyzeStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    AnalyzeStmt(i.ElseBranch);
                break;

            case Stmt.While w:
                AnalyzeExpr(w.Condition);
                AnalyzeStmt(w.Body);
                break;

            case Stmt.ForOf f:
                _declaredVariables.Add(f.Variable.Lexeme);
                if (!_seenYield)
                    _variablesDeclaredBeforeYield.Add(f.Variable.Lexeme);
                AnalyzeExpr(f.Iterable);
                AnalyzeStmt(f.Body);
                break;

            case Stmt.Block b:
                if (b.Statements != null)
                    foreach (var s in b.Statements)
                        AnalyzeStmt(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeStmt(ts);
                if (t.CatchBlock != null)
                {
                    if (t.CatchParam != null)
                    {
                        _declaredVariables.Add(t.CatchParam.Lexeme);
                        if (!_seenYield)
                            _variablesDeclaredBeforeYield.Add(t.CatchParam.Lexeme);
                    }
                    foreach (var cs in t.CatchBlock)
                        AnalyzeStmt(cs);
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeStmt(fs);
                break;

            case Stmt.Switch s:
                AnalyzeExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    AnalyzeExpr(c.Value);
                    foreach (var cs in c.Body)
                        AnalyzeStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeStmt(ds);
                break;

            case Stmt.Throw th:
                AnalyzeExpr(th.Value);
                break;

            case Stmt.Print p:
                AnalyzeExpr(p.Expr);
                break;

            case Stmt.LabeledStatement ls:
                AnalyzeStmt(ls.Statement);
                break;

            case Stmt.Break:
            case Stmt.Continue:
            case Stmt.Function:
            case Stmt.Class:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
                break;
        }
    }

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Yield y:
                var liveVars = new HashSet<string>(_declaredVariables);
                _yieldPoints.Add(new YieldPoint(_yieldCounter++, y, liveVars));
                _seenYield = true;
                if (y.IsDelegating)
                    _hasYieldStar = true;
                if (y.Value != null)
                    AnalyzeExpr(y.Value);
                break;

            case Expr.Variable v:
                if (_seenYield && _declaredVariables.Contains(v.Name.Lexeme))
                    _variablesUsedAfterYield.Add(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                if (_seenYield && _declaredVariables.Contains(a.Name.Lexeme))
                    _variablesUsedAfterYield.Add(a.Name.Lexeme);
                AnalyzeExpr(a.Value);
                break;

            case Expr.Binary b:
                AnalyzeExpr(b.Left);
                AnalyzeExpr(b.Right);
                break;

            case Expr.Logical l:
                AnalyzeExpr(l.Left);
                AnalyzeExpr(l.Right);
                break;

            case Expr.Unary u:
                AnalyzeExpr(u.Right);
                break;

            case Expr.Grouping g:
                AnalyzeExpr(g.Expression);
                break;

            case Expr.Call c:
                AnalyzeExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.Get g:
                AnalyzeExpr(g.Object);
                break;

            case Expr.Set s:
                AnalyzeExpr(s.Object);
                AnalyzeExpr(s.Value);
                break;

            case Expr.GetIndex gi:
                AnalyzeExpr(gi.Object);
                AnalyzeExpr(gi.Index);
                break;

            case Expr.SetIndex si:
                AnalyzeExpr(si.Object);
                AnalyzeExpr(si.Index);
                AnalyzeExpr(si.Value);
                break;

            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeExpr(elem);
                break;

            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeExpr(prop.Value);
                break;

            case Expr.Ternary t:
                AnalyzeExpr(t.Condition);
                AnalyzeExpr(t.ThenBranch);
                AnalyzeExpr(t.ElseBranch);
                break;

            case Expr.NullishCoalescing nc:
                AnalyzeExpr(nc.Left);
                AnalyzeExpr(nc.Right);
                break;

            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeExpr(e);
                break;

            case Expr.CompoundAssign ca:
                if (_seenYield && _declaredVariables.Contains(ca.Name.Lexeme))
                    _variablesUsedAfterYield.Add(ca.Name.Lexeme);
                AnalyzeExpr(ca.Value);
                break;

            case Expr.CompoundSet cs:
                AnalyzeExpr(cs.Object);
                AnalyzeExpr(cs.Value);
                break;

            case Expr.CompoundSetIndex csi:
                AnalyzeExpr(csi.Object);
                AnalyzeExpr(csi.Index);
                AnalyzeExpr(csi.Value);
                break;

            case Expr.PrefixIncrement pi:
                AnalyzeExpr(pi.Operand);
                break;

            case Expr.PostfixIncrement poi:
                AnalyzeExpr(poi.Operand);
                break;

            case Expr.ArrowFunction:
                // Arrow functions inside generators don't affect yield analysis
                break;

            case Expr.This:
                _usesThis = true;
                break;

            case Expr.Super:
                _usesThis = true;
                break;

            case Expr.Literal:
            case Expr.Spread:
            case Expr.TypeAssertion:
            case Expr.Await:
            case Expr.RegexLiteral:
                break;
        }
    }
}
