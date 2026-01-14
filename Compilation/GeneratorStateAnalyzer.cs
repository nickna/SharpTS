using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes generator functions to identify yield points and variables that must be hoisted
/// to the state machine struct. Uses the visitor pattern for AST traversal.
/// </summary>
public class GeneratorStateAnalyzer : AstVisitorBase
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
        bool HasYieldStar,
        HashSet<string> CapturedVariables  // Variables from outer scopes that need to be captured
    );

    // State during analysis
    private readonly List<YieldPoint> _yieldPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterYield = [];
    private readonly HashSet<string> _variablesDeclaredBeforeYield = [];
    private readonly HashSet<string> _capturedVariables = [];  // Variables from outer scopes
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
        HashSet<string> parameters = [];
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeYield.Add(param.Name.Lexeme);
        }

        // Analyze the function body using visitor pattern
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                Visit(stmt);
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
            HasYieldStar: _hasYieldStar,
            CapturedVariables: [.. _capturedVariables]
        );
    }

    private void Reset()
    {
        _yieldPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterYield.Clear();
        _variablesDeclaredBeforeYield.Clear();
        _capturedVariables.Clear();
        _yieldCounter = 0;
        _seenYield = false;
        _usesThis = false;
        _hasYieldStar = false;
    }

    #region Statement Visitor Overrides

    protected override void VisitVar(Stmt.Var stmt)
    {
        _declaredVariables.Add(stmt.Name.Lexeme);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(stmt.Variable.Lexeme);
        base.VisitForOf(stmt);
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenYield)
            _variablesDeclaredBeforeYield.Add(stmt.Variable.Lexeme);
        base.VisitForIn(stmt);
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        // Visit try block
        foreach (var ts in stmt.TryBlock)
            Visit(ts);

        // Track catch parameter and visit catch block
        if (stmt.CatchBlock != null)
        {
            if (stmt.CatchParam != null)
            {
                _declaredVariables.Add(stmt.CatchParam.Lexeme);
                if (!_seenYield)
                    _variablesDeclaredBeforeYield.Add(stmt.CatchParam.Lexeme);
            }
            foreach (var cs in stmt.CatchBlock)
                Visit(cs);
        }

        // Visit finally block
        if (stmt.FinallyBlock != null)
            foreach (var fs in stmt.FinallyBlock)
                Visit(fs);
    }

    // Don't traverse into nested declarations - they don't affect our analysis
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitInterface(Stmt.Interface stmt) { }
    protected override void VisitTypeAlias(Stmt.TypeAlias stmt) { }
    protected override void VisitEnum(Stmt.Enum stmt) { }
    protected override void VisitNamespace(Stmt.Namespace stmt) { }

    #endregion

    #region Expression Visitor Overrides

    protected override void VisitYield(Expr.Yield expr)
    {
        var liveVars = new HashSet<string>(_declaredVariables);
        _yieldPoints.Add(new YieldPoint(_yieldCounter++, expr, liveVars));
        _seenYield = true;
        if (expr.IsDelegating)
            _hasYieldStar = true;
        base.VisitYield(expr);
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        var name = expr.Name.Lexeme;

        // Detect outer scope capture - variable not declared in this function
        if (!_declaredVariables.Contains(name))
        {
            _capturedVariables.Add(name);
        }

        if (_seenYield && _declaredVariables.Contains(name))
            _variablesUsedAfterYield.Add(name);
        // No base call needed - leaf node
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        if (_seenYield && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterYield.Add(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        if (_seenYield && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterYield.Add(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitThis(Expr.This expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Arrow functions inside generators don't affect yield analysis
        // Don't traverse into them
    }

    #endregion
}
