using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation.Visitors;

/// <summary>
/// Visitor that analyzes an arrow function to determine which variables it captures
/// from the enclosing scope. This visitor is reusable - call Reset() before each analysis.
/// Also provides efficient 'this' detection with early termination.
/// </summary>
internal class CaptureAnalysisVisitor : ControlledAstVisitor
{
    private HashSet<string> _outerScopeVariables = [];
    private HashSet<string> _locals = [];
    private HashSet<string> _captures = [];

    // Stack for re-entrancy when analyzing nested arrows
    private readonly Stack<(HashSet<string> Outer, HashSet<string> Locals, HashSet<string> Captures)> _stateStack = new();

    // For efficient 'this' detection with early termination
    private bool _detectingThisOnly;
    private bool _thisDetected;

    /// <summary>
    /// Gets the set of captured variable names after analysis.
    /// </summary>
    public HashSet<string> Captures => _captures;

    /// <summary>
    /// Gets whether 'this' is captured.
    /// </summary>
    public bool CapturesThis => _captures.Contains("this");

    /// <summary>
    /// Quick check if arrow function uses 'this' or 'super' (stops at first occurrence).
    /// </summary>
    public bool UsesThis(Expr.ArrowFunction af)
    {
        Reset();
        _detectingThisOnly = true;
        _thisDetected = false;

        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        if (!_thisDetected && af.BlockBody != null)
        {
            foreach (var s in af.BlockBody)
            {
                Visit(s);
                if (_thisDetected) break;
            }
        }

        _detectingThisOnly = false;
        return _thisDetected;
    }

    /// <summary>
    /// Resets the visitor to initial state (for UsesThis checks).
    /// </summary>
    protected override void Reset()
    {
        base.Reset();
        _outerScopeVariables = [];
        _locals.Clear();
        _captures.Clear();
        _stateStack.Clear();
        _detectingThisOnly = false;
        _thisDetected = false;
    }

    /// <summary>
    /// Resets the visitor for analyzing an arrow with the given outer scope variables.
    /// </summary>
    /// <param name="outerScopeVariables">Variables declared in the enclosing scope that can be captured.</param>
    public void Reset(HashSet<string> outerScopeVariables)
    {
        Reset();
        _outerScopeVariables = outerScopeVariables;
    }

    /// <summary>
    /// Analyze an arrow function to determine its captures.
    /// Returns ownership of the HashSet to the caller.
    /// </summary>
    public HashSet<string> Analyze(Expr.ArrowFunction af)
    {
        _locals.Clear();
        _captures.Clear();

        // Add arrow parameters as locals (not captures)
        foreach (var param in af.Parameters)
            _locals.Add(param.Name.Lexeme);

        // Analyze arrow body
        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        if (af.BlockBody != null)
            foreach (var stmt in af.BlockBody)
                Visit(stmt);

        // Transfer ownership - caller now owns the set
        var result = _captures;
        _captures = [];
        return result;
    }

    #region Statement visitors - track local declarations

    protected override void VisitVar(Stmt.Var stmt)
    {
        _locals.Add(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        _locals.Add(stmt.Variable.Lexeme);
        base.VisitForOf(stmt);
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        _locals.Add(stmt.Variable.Lexeme);
        base.VisitForIn(stmt);
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        // Visit try block
        foreach (var s in stmt.TryBlock)
            Visit(s);

        // Catch parameter is a local in the catch block
        if (stmt.CatchBlock != null)
        {
            if (stmt.CatchParam != null)
                _locals.Add(stmt.CatchParam.Lexeme);
            foreach (var s in stmt.CatchBlock)
                Visit(s);
        }

        // Visit finally block
        if (stmt.FinallyBlock != null)
            foreach (var s in stmt.FinallyBlock)
                Visit(s);
    }

    #endregion

    #region Expression visitors - detect captures

    protected override void VisitVariable(Expr.Variable expr)
    {
        var name = expr.Name.Lexeme;
        // If not a local and is declared in outer scope, it's a capture
        if (!_locals.Contains(name) && _outerScopeVariables.Contains(name))
            _captures.Add(name);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        var name = expr.Name.Lexeme;
        if (!_locals.Contains(name) && _outerScopeVariables.Contains(name))
            _captures.Add(name);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        var name = expr.Name.Lexeme;
        if (!_locals.Contains(name) && _outerScopeVariables.Contains(name))
            _captures.Add(name);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        var name = expr.Name.Lexeme;
        if (!_locals.Contains(name) && _outerScopeVariables.Contains(name))
            _captures.Add(name);
        base.VisitLogicalAssign(expr);
    }

    protected override void VisitThis(Expr.This expr)
    {
        _captures.Add("this");
        if (_detectingThisOnly)
        {
            _thisDetected = true;
            StopTraversal();
        }
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        // 'super' implicitly uses 'this'
        _captures.Add("this");
        if (_detectingThisOnly)
        {
            _thisDetected = true;
            StopTraversal();
        }
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Nested arrow functions may also capture - analyze them recursively
        // Their captures become our captures if they're from our outer scope

        // Save current state for re-entrancy
        _stateStack.Push((_outerScopeVariables, _locals, _captures));

        // Build nested outer scope (includes current locals)
        var nestedOuter = new HashSet<string>(_outerScopeVariables);
        foreach (var local in _locals)
            nestedOuter.Add(local);

        // Reinitialize for nested analysis
        _outerScopeVariables = nestedOuter;
        _locals = [];
        _captures = [];

        // Add nested arrow parameters as locals
        foreach (var param in expr.Parameters)
            _locals.Add(param.Name.Lexeme);

        // Analyze nested arrow body
        if (expr.ExpressionBody != null)
            Visit(expr.ExpressionBody);
        if (expr.BlockBody != null)
            foreach (var stmt in expr.BlockBody)
                Visit(stmt);

        var nestedCaptures = _captures;

        // Restore state
        (_outerScopeVariables, _locals, _captures) = _stateStack.Pop();

        // Add nested captures that are not our locals (they come from outer scope)
        foreach (var cap in nestedCaptures)
        {
            if (!_locals.Contains(cap))
                _captures.Add(cap);
        }
    }

    #endregion
}
