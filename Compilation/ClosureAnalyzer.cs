using SharpTS.Parsing;

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
public class ClosureAnalyzer
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
        {
            AnalyzeStmt(stmt);
        }
        _scopeStack.Pop();
    }

    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                DeclareVariable(v.Name.Lexeme);
                if (v.Initializer != null)
                    AnalyzeExpr(v.Initializer);
                break;

            case Stmt.Function f:
                DeclareVariable(f.Name.Lexeme);
                // Skip overload signatures (no body)
                if (f.Body != null)
                    AnalyzeFunction(f, f.Parameters, f.Body);
                break;

            case Stmt.Class c:
                DeclareVariable(c.Name.Lexeme);
                foreach (var method in c.Methods)
                {
                    // Skip overload signatures (no body)
                    if (method.Body != null)
                        AnalyzeFunction(method, method.Parameters, method.Body);
                }
                break;

            case Stmt.Expression e:
                AnalyzeExpr(e.Expr);
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
                EnterScope();
                DeclareVariable(f.Variable.Lexeme);
                AnalyzeExpr(f.Iterable);
                AnalyzeStmt(f.Body);
                ExitScope();
                break;

            case Stmt.Block b:
                EnterScope();
                foreach (var s in b.Statements)
                    AnalyzeStmt(s);
                ExitScope();
                break;

            case Stmt.Sequence seq:
                // No new scope for Sequence
                foreach (var s in seq.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeExpr(r.Value);
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

            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeStmt(ts);
                if (t.CatchBlock != null)
                {
                    EnterScope();
                    if (t.CatchParam != null)
                        DeclareVariable(t.CatchParam.Lexeme);
                    foreach (var cs in t.CatchBlock)
                        AnalyzeStmt(cs);
                    ExitScope();
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeStmt(fs);
                break;

            case Stmt.Throw th:
                AnalyzeExpr(th.Value);
                break;

            case Stmt.Print p:
                AnalyzeExpr(p.Expr);
                break;

            case Stmt.Break:
            case Stmt.Continue:
            case Stmt.Interface:
                // No analysis needed
                break;

            case Stmt.LabeledStatement labeledStmt:
                AnalyzeStmt(labeledStmt.Statement);
                break;
        }
    }

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Variable v:
                ReferenceVariable(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                ReferenceVariable(a.Name.Lexeme);
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
                ReferenceVariable(ca.Name.Lexeme);
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

            case Expr.ArrowFunction af:
                AnalyzeArrowFunction(af);
                break;

            case Expr.This:
            case Expr.Super:
            case Expr.Literal:
                // No analysis needed
                break;
        }
    }

    private void AnalyzeFunction(object funcNode, List<Stmt.Parameter> parameters, List<Stmt> body)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
        {
            foreach (var name in scope)
            {
                _outerVariables.Add(name);
            }
        }

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
                AnalyzeExpr(param.DefaultValue);
        }

        // Analyze body
        foreach (var stmt in body)
        {
            AnalyzeStmt(stmt);
        }

        ExitScope();

        // Restore context
        _currentFunction = previousFunction;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
        {
            _outerVariables.Add(name);
        }
    }

    private void AnalyzeArrowFunction(Expr.ArrowFunction af)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
        {
            foreach (var name in scope)
            {
                _outerVariables.Add(name);
            }
        }

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
                AnalyzeExpr(param.DefaultValue);
        }

        // Analyze body
        if (af.ExpressionBody != null)
        {
            AnalyzeExpr(af.ExpressionBody);
        }
        else if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                AnalyzeStmt(stmt);
            }
        }

        ExitScope();

        // Restore context
        _currentFunction = previousFunction;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
        {
            _outerVariables.Add(name);
        }
    }

    private void DeclareVariable(string name)
    {
        if (_scopeStack.Count > 0)
        {
            _scopeStack.Peek().Add(name);
        }

        // Track local variables for the current function
        if (_currentFunction != null && _localVars.TryGetValue(_currentFunction, out var locals))
        {
            locals.Add(name);
        }
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
        // Note: _outerVariables is set to all variables from outer scopes when we enter a function
        if (_outerVariables.Contains(name))
        {
            _captures[_currentFunction].Add(name);
        }
    }

    private void EnterScope()
    {
        _scopeStack.Push([]);
    }

    private void ExitScope()
    {
        _scopeStack.Pop();
    }
}
