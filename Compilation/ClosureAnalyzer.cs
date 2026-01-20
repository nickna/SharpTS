using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes the AST to determine which variables are captured by closures.
/// </summary>
/// <remarks>
/// Traverses the AST before IL emission to identify variables defined in outer scopes
/// that are referenced inside nested functions or arrow functions. These "captured"
/// variables require special handling via display classes during IL compilation.
/// Used by <see cref="ILCompiler"/> to decide whether arrow functions need closure
/// support or can be compiled as simple static methods.
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="CompilationContext"/>
public class ClosureAnalyzer : AstVisitorBase
{
    // Maps each function AST node to the set of variable names it captures
    private readonly Dictionary<object, HashSet<string>> _captures = [];

    // Maps each function AST node to the set of variable names defined within it (including params)
    private readonly Dictionary<object, HashSet<string>> _localVars = [];

    // Stack of scopes - each scope tracks variables declared at that level
    private readonly Stack<HashSet<string>> _scopeStack = new();

    // Current function being analyzed (for tracking captures)
    private object? _currentFunction;

    // Set of variables defined in outer scopes relative to current function
    private readonly HashSet<string> _outerVariables = [];

    /// <summary>
    /// Gets the captured variables for a given function/arrow AST node.
    /// </summary>
    public HashSet<string> GetCaptures(object functionNode)
    {
        return _captures.TryGetValue(functionNode, out var captures)
            ? captures
            : [];
    }

    /// <summary>
    /// Checks if a variable is captured by any inner function in the current scope.
    /// </summary>
    public bool IsVariableCaptured(string name)
    {
        return _captures.Values.Any(set => set.Contains(name));
    }

    /// <summary>
    /// Analyze the entire program to detect captures.
    /// </summary>
    public void Analyze(List<Stmt> statements)
    {
        _scopeStack.Push([]);
        foreach (var stmt in statements)
            Visit(stmt);
        _scopeStack.Pop();
    }

    #region Scope management

    private void EnterScope() => _scopeStack.Push([]);
    private void ExitScope() => _scopeStack.Pop();

    private void DeclareVariable(string name)
    {
        if (_scopeStack.Count > 0)
            _scopeStack.Peek().Add(name);

        // Track local variables for the current function
        if (_currentFunction != null && _localVars.TryGetValue(_currentFunction, out var locals))
            locals.Add(name);
    }

    private void ReferenceVariable(string name)
    {
        if (_currentFunction == null) return;

        // Skip built-ins
        if (name is "console.log" or "Math" or "console" or "undefined" or "NaN" or "Infinity" or "Symbol")
            return;

        // Check if this is a local variable in the current function
        if (_localVars.TryGetValue(_currentFunction, out var locals) && locals.Contains(name))
            return;

        // Check if it's an outer variable - this means it's captured
        if (_outerVariables.Contains(name))
            _captures[_currentFunction].Add(name);
    }

    #endregion

    #region Statement visitors

    protected override void VisitVar(Stmt.Var stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        base.VisitConst(stmt);
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        // Skip overload signatures (no body)
        if (stmt.Body != null)
            AnalyzeFunctionBody(stmt, stmt.Parameters, stmt.Body);
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        foreach (var method in stmt.Methods)
        {
            // Skip overload signatures (no body)
            if (method.Body != null)
                AnalyzeFunctionBody(method, method.Parameters, method.Body);
        }
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        // Class expressions don't declare the class name in the outer scope
        // (unlike class declarations), but we still need to analyze all bodies

        // Analyze field initializers for captured variables
        foreach (var field in expr.Fields)
        {
            if (field.Initializer != null)
                Visit(field.Initializer);
        }

        // Analyze methods
        foreach (var method in expr.Methods)
        {
            // Skip overload signatures (no body)
            if (method.Body != null)
                AnalyzeFunctionBody(method, method.Parameters, method.Body);
        }

        // Analyze accessors
        if (expr.Accessors != null)
        {
            foreach (var accessor in expr.Accessors)
            {
                var parameters = accessor.SetterParam != null
                    ? [accessor.SetterParam]
                    : new List<Stmt.Parameter>();
                AnalyzeFunctionBody(accessor, parameters, accessor.Body);
            }
        }
    }

    protected override void VisitBlock(Stmt.Block stmt)
    {
        EnterScope();
        base.VisitBlock(stmt);
        ExitScope();
    }

    // Sequence intentionally uses base implementation (no new scope)

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        EnterScope();
        DeclareVariable(stmt.Variable.Lexeme);
        Visit(stmt.Iterable);
        Visit(stmt.Body);
        ExitScope();
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        EnterScope();
        DeclareVariable(stmt.Variable.Lexeme);
        Visit(stmt.Object);
        Visit(stmt.Body);
        ExitScope();
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // For loops create a scope for the loop variable (e.g., let i in "for (let i = 0; ...)")
        EnterScope();
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);
        if (stmt.Condition != null)
            Visit(stmt.Condition);
        if (stmt.Increment != null)
            Visit(stmt.Increment);
        Visit(stmt.Body);
        ExitScope();
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        foreach (var s in stmt.TryBlock)
            Visit(s);

        if (stmt.CatchBlock != null)
        {
            EnterScope();
            if (stmt.CatchParam != null)
                DeclareVariable(stmt.CatchParam.Lexeme);
            foreach (var s in stmt.CatchBlock)
                Visit(s);
            ExitScope();
        }

        if (stmt.FinallyBlock != null)
            foreach (var s in stmt.FinallyBlock)
                Visit(s);
    }

    #endregion

    #region Expression visitors

    protected override void VisitVariable(Expr.Variable expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitLogicalAssign(expr);
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        AnalyzeArrowFunctionBody(expr);
    }

    protected override void VisitThis(Expr.This expr)
    {
        // Arrow functions capture 'this' from their lexical scope
        // Track this as a captured variable so display classes include a field for it
        // EXCEPT for object methods which receive 'this' via the __this parameter
        if (_currentFunction != null && _currentFunction is Expr.ArrowFunction arrowFunc && !arrowFunc.IsObjectMethod)
            _captures[_currentFunction].Add("this");
    }

    #endregion

    #region Function/Arrow body analysis

    private void AnalyzeFunctionBody(object funcNode, List<Stmt.Parameter> parameters, List<Stmt> body)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
            foreach (var name in scope)
                _outerVariables.Add(name);

        // Set up new function context
        _currentFunction = funcNode;
        _captures[funcNode] = [];
        _localVars[funcNode] = [];

        // Enter function scope and declare parameters
        EnterScope();
        foreach (var param in parameters)
        {
            DeclareVariable(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }

        // Analyze body
        foreach (var stmt in body)
            Visit(stmt);

        ExitScope();

        // Restore context
        _currentFunction = previousFunction;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
            _outerVariables.Add(name);
    }

    private void AnalyzeArrowFunctionBody(Expr.ArrowFunction af)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
            foreach (var name in scope)
                _outerVariables.Add(name);

        // Set up new function context
        _currentFunction = af;
        _captures[af] = [];
        _localVars[af] = [];

        // Enter function scope and declare parameters
        EnterScope();
        foreach (var param in af.Parameters)
        {
            DeclareVariable(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }

        // Analyze body
        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        else if (af.BlockBody != null)
            foreach (var stmt in af.BlockBody)
                Visit(stmt);

        ExitScope();

        // Restore context
        _currentFunction = previousFunction;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
            _outerVariables.Add(name);
    }

    #endregion
}
