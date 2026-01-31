using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Execution;

/// <summary>
/// Pre-computes variable scope distances for O(1) lookups in the interpreter.
/// </summary>
/// <remarks>
/// Performs a single pass over the AST before interpretation to calculate the "distance"
/// (number of scope hops) from each variable reference to its declaration. These distances
/// are stored in the interpreter's <see cref="Interpreter._locals"/> dictionary, enabling
/// <see cref="Interpreter.LookupVariable"/> to use <see cref="RuntimeEnvironment.GetAt"/>
/// for O(1) access instead of O(N) scope chain traversal.
///
/// Variables not found in local scopes (globals, built-ins) are not resolved here;
/// they fall back to <see cref="RuntimeEnvironment.TryGet"/> at runtime.
/// </remarks>
/// <seealso cref="Interpreter"/>
/// <seealso cref="RuntimeEnvironment.GetAt"/>
public class VariableResolver : AstVisitorBase
{
    private readonly Interpreter _interpreter;

    // Stack of scopes - each scope maps variable names to their "initialized" status
    // (true = initialized, false = declared but not yet initialized)
    private readonly Stack<Dictionary<string, bool>> _scopes = new();

    // Track current class for 'this' and 'super' resolution
    private enum ClassType { None, Class, Subclass }
    private ClassType _currentClass = ClassType.None;

    // Track current function type for 'this' binding
    private enum FunctionType { None, Function, Method, Constructor }
    private FunctionType _currentFunction = FunctionType.None;

    public VariableResolver(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    /// <summary>
    /// Resolves variable references in the given statements.
    /// Call this before interpretation to enable O(1) variable lookups.
    /// </summary>
    public void Resolve(List<Stmt> statements)
    {
        foreach (var stmt in statements)
            Visit(stmt);
    }

    #region Scope Management

    private void BeginScope() => _scopes.Push(new Dictionary<string, bool>());

    private void EndScope() => _scopes.Pop();

    /// <summary>
    /// Declares a variable in the current scope (marks it as "not yet initialized").
    /// </summary>
    private void Declare(string name)
    {
        if (_scopes.Count == 0) return;
        var scope = _scopes.Peek();
        // Note: TypeScript allows redeclaration in some cases (var hoisting),
        // but we don't error here - type checker handles that
        scope[name] = false;
    }

    /// <summary>
    /// Marks a variable as initialized in the current scope.
    /// </summary>
    private void Define(string name)
    {
        if (_scopes.Count == 0) return;
        _scopes.Peek()[name] = true;
    }

    /// <summary>
    /// Calculates the scope distance and registers it with the interpreter.
    /// </summary>
    private void ResolveLocal(Expr expr, string name)
    {
        // Walk from innermost scope outward
        int i = 0;
        foreach (var scope in _scopes)
        {
            if (scope.ContainsKey(name))
            {
                _interpreter.Resolve(expr, i);
                return;
            }
            i++;
        }
        // Not found in any scope - it's a global or built-in
        // Don't register it; LookupVariable will use TryGet fallback
    }

    #endregion

    #region Statement Visitors

    protected override void VisitBlock(Stmt.Block stmt)
    {
        BeginScope();
        foreach (var s in stmt.Statements)
            Visit(s);
        EndScope();
    }

    protected override void VisitSequence(Stmt.Sequence stmt)
    {
        // Sequences don't create a new scope (used by for-loop desugaring)
        foreach (var s in stmt.Statements)
            Visit(s);
    }

    protected override void VisitVar(Stmt.Var stmt)
    {
        Declare(stmt.Name.Lexeme);
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);
        Define(stmt.Name.Lexeme);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        Declare(stmt.Name.Lexeme);
        Visit(stmt.Initializer);
        Define(stmt.Name.Lexeme);
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        // Declare function name in current scope (hoisted)
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);

        // Skip overload signatures (no body)
        if (stmt.Body != null)
            ResolveFunction(stmt.Parameters, stmt.Body, FunctionType.Function);
    }

    private void ResolveFunction(List<Stmt.Parameter> parameters, List<Stmt> body, FunctionType type)
    {
        var enclosingFunction = _currentFunction;
        _currentFunction = type;

        BeginScope();
        foreach (var param in parameters)
        {
            Declare(param.Name.Lexeme);
            Define(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }
        foreach (var statement in body)
            Visit(statement);
        EndScope();

        _currentFunction = enclosingFunction;
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        var enclosingClass = _currentClass;
        _currentClass = stmt.Superclass != null ? ClassType.Subclass : ClassType.Class;

        // Declare class name in current scope
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);

        // If there's a superclass, create a scope for 'super'
        if (stmt.Superclass != null)
        {
            BeginScope();
            _scopes.Peek()["super"] = true;
        }

        // Create scope for 'this' in class body
        BeginScope();
        _scopes.Peek()["this"] = true;

        // Resolve field initializers
        foreach (var field in stmt.Fields)
        {
            if (field.Initializer != null)
                Visit(field.Initializer);
        }

        // Resolve methods
        foreach (var method in stmt.Methods)
        {
            // Skip overload signatures
            if (method.Body == null) continue;

            var funcType = method.Name.Lexeme == "constructor"
                ? FunctionType.Constructor
                : FunctionType.Method;
            ResolveFunction(method.Parameters, method.Body, funcType);
        }

        // Resolve accessors
        if (stmt.Accessors != null)
        {
            foreach (var accessor in stmt.Accessors)
            {
                var parameters = accessor.SetterParam != null
                    ? new List<Stmt.Parameter> { accessor.SetterParam }
                    : new List<Stmt.Parameter>();
                ResolveFunction(parameters, accessor.Body, FunctionType.Method);
            }
        }

        // Resolve auto-accessors
        if (stmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in stmt.AutoAccessors)
            {
                if (autoAccessor.Initializer != null)
                    Visit(autoAccessor.Initializer);
            }
        }

        // Resolve static initializers
        if (stmt.StaticInitializers != null)
        {
            foreach (var initializer in stmt.StaticInitializers)
                Visit(initializer);
        }

        EndScope(); // 'this' scope

        if (stmt.Superclass != null)
            EndScope(); // 'super' scope

        _currentClass = enclosingClass;
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // For loops create a scope for the loop variable (ES6 let/const block scoping).
        // The initializer defines the loop variable in this scope, and the condition,
        // increment, and body all have access to it.
        BeginScope();
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);
        if (stmt.Condition != null)
            Visit(stmt.Condition);
        if (stmt.Increment != null)
            Visit(stmt.Increment);
        Visit(stmt.Body);
        EndScope();
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        // Resolve iterable BEFORE creating loop scope - matches interpreter behavior
        // where Evaluate(forOf.Iterable) happens before the loop environment is created
        Visit(stmt.Iterable);

        BeginScope();
        Declare(stmt.Variable.Lexeme);
        Define(stmt.Variable.Lexeme);
        Visit(stmt.Body);
        EndScope();
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        // Resolve object BEFORE creating loop scope - matches interpreter behavior
        // where Evaluate(forIn.Object) happens before the loop environment is created
        Visit(stmt.Object);

        BeginScope();
        Declare(stmt.Variable.Lexeme);
        Define(stmt.Variable.Lexeme);
        Visit(stmt.Body);
        EndScope();
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        foreach (var s in stmt.TryBlock)
            Visit(s);

        if (stmt.CatchBlock != null)
        {
            BeginScope();
            if (stmt.CatchParam != null)
            {
                Declare(stmt.CatchParam.Lexeme);
                Define(stmt.CatchParam.Lexeme);
            }
            foreach (var s in stmt.CatchBlock)
                Visit(s);
            EndScope();
        }

        if (stmt.FinallyBlock != null)
        {
            foreach (var s in stmt.FinallyBlock)
                Visit(s);
        }
    }

    protected override void VisitImport(Stmt.Import stmt)
    {
        // Declare imported bindings in current scope
        if (stmt.DefaultImport != null)
        {
            Declare(stmt.DefaultImport.Lexeme);
            Define(stmt.DefaultImport.Lexeme);
        }

        if (stmt.NamespaceImport != null)
        {
            Declare(stmt.NamespaceImport.Lexeme);
            Define(stmt.NamespaceImport.Lexeme);
        }

        if (stmt.NamedImports != null)
        {
            foreach (var spec in stmt.NamedImports)
            {
                string localName = spec.LocalName?.Lexeme ?? spec.Imported.Lexeme;
                Declare(localName);
                Define(localName);
            }
        }
    }

    protected override void VisitImportRequire(Stmt.ImportRequire stmt)
    {
        Declare(stmt.AliasName.Lexeme);
        Define(stmt.AliasName.Lexeme);
    }

    protected override void VisitEnum(Stmt.Enum stmt)
    {
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);
        // Visit member initializers
        foreach (var member in stmt.Members)
        {
            if (member.Value != null)
                Visit(member.Value);
        }
    }

    protected override void VisitNamespace(Stmt.Namespace stmt)
    {
        // Namespaces are declared in the environment
        Declare(stmt.Name.Lexeme);
        Define(stmt.Name.Lexeme);

        BeginScope();
        foreach (var member in stmt.Members)
            Visit(member);
        EndScope();
    }

    protected override void VisitUsing(Stmt.Using stmt)
    {
        foreach (var binding in stmt.Bindings)
        {
            if (binding.Name != null)
            {
                Declare(binding.Name.Lexeme);
            }
            if (binding.DestructuringPattern != null)
                Visit(binding.DestructuringPattern);
            Visit(binding.Initializer);
            if (binding.Name != null)
            {
                Define(binding.Name.Lexeme);
            }
        }
    }

    #endregion

    #region Expression Visitors

    protected override void VisitVariable(Expr.Variable expr)
    {
        // Check for use-before-initialization in current scope
        if (_scopes.Count > 0)
        {
            var scope = _scopes.Peek();
            if (scope.TryGetValue(expr.Name.Lexeme, out bool initialized) && !initialized)
            {
                // Variable is declared but not yet initialized in this scope
                // This is a TDZ (temporal dead zone) error in TypeScript/JavaScript
                // We still resolve it but the runtime will handle the error
            }
        }

        ResolveLocal(expr, expr.Name.Lexeme);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        Visit(expr.Value);
        ResolveLocal(expr, expr.Name.Lexeme);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        Visit(expr.Value);
        ResolveLocal(expr, expr.Name.Lexeme);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        Visit(expr.Value);
        ResolveLocal(expr, expr.Name.Lexeme);
    }

    protected override void VisitThis(Expr.This expr)
    {
        if (_currentClass == ClassType.None)
        {
            // 'this' outside of a class - might be in a function or global
            // Don't resolve; let runtime handle it
            return;
        }
        ResolveLocal(expr, "this");
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        if (_currentClass == ClassType.Subclass)
        {
            ResolveLocal(expr, "super");
        }
        // If not in a subclass, don't resolve - runtime will error
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        var enclosingFunction = _currentFunction;
        _currentFunction = FunctionType.Function;

        BeginScope();

        // For named function expressions, declare the name in the function scope
        if (expr.Name != null)
        {
            Declare(expr.Name.Lexeme);
            Define(expr.Name.Lexeme);
        }

        foreach (var param in expr.Parameters)
        {
            Declare(param.Name.Lexeme);
            Define(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }

        if (expr.ExpressionBody != null)
            Visit(expr.ExpressionBody);
        if (expr.BlockBody != null)
        {
            foreach (var s in expr.BlockBody)
                Visit(s);
        }

        EndScope();

        _currentFunction = enclosingFunction;
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        var enclosingClass = _currentClass;
        _currentClass = expr.Superclass != null ? ClassType.Subclass : ClassType.Class;

        // If there's a superclass, create a scope for 'super'
        if (expr.Superclass != null)
        {
            BeginScope();
            _scopes.Peek()["super"] = true;
        }

        // Create scope for 'this' in class body
        BeginScope();
        _scopes.Peek()["this"] = true;

        // Resolve field initializers
        foreach (var field in expr.Fields)
        {
            if (field.Initializer != null)
                Visit(field.Initializer);
        }

        // Resolve methods
        foreach (var method in expr.Methods)
        {
            if (method.Body == null) continue;

            var funcType = method.Name.Lexeme == "constructor"
                ? FunctionType.Constructor
                : FunctionType.Method;
            ResolveFunction(method.Parameters, method.Body, funcType);
        }

        // Resolve accessors
        if (expr.Accessors != null)
        {
            foreach (var accessor in expr.Accessors)
            {
                var parameters = accessor.SetterParam != null
                    ? new List<Stmt.Parameter> { accessor.SetterParam }
                    : new List<Stmt.Parameter>();
                ResolveFunction(parameters, accessor.Body, FunctionType.Method);
            }
        }

        EndScope(); // 'this' scope

        if (expr.Superclass != null)
            EndScope(); // 'super' scope

        _currentClass = enclosingClass;
    }

    #endregion
}
