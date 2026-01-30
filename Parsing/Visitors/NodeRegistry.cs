using System.Reflection;

namespace SharpTS.Parsing.Visitors;

/// <summary>
/// A delegate-based handler registry for AST node dispatch.
/// Replaces monolithic visitor interfaces with a more flexible, extensible pattern.
/// </summary>
/// <typeparam name="TContext">The context type (e.g., Interpreter, TypeChecker).</typeparam>
/// <typeparam name="TExprResult">The result type for expression evaluation.</typeparam>
/// <typeparam name="TStmtResult">The result type for statement execution.</typeparam>
/// <remarks>
/// Key benefits over traditional visitor interfaces:
/// - Handlers can be organized by concern rather than by interface
/// - Supports async handlers natively
/// - Validates exhaustiveness at startup via Freeze()
/// - Adding new AST nodes requires fewer file edits
/// </remarks>
public sealed class NodeRegistry<TContext, TExprResult, TStmtResult>
{
    private readonly Dictionary<Type, Func<Expr, TContext, TExprResult>> _exprHandlers = new();
    private readonly Dictionary<Type, Func<Stmt, TContext, TStmtResult>> _stmtHandlers = new();
    private readonly Dictionary<Type, Func<Expr, TContext, ValueTask<TExprResult>>>? _asyncExprHandlers;

    private bool _frozen;
    private readonly bool _supportAsync;

    /// <summary>
    /// Creates a new NodeRegistry.
    /// </summary>
    /// <param name="supportAsync">Whether to support async expression handlers.</param>
    public NodeRegistry(bool supportAsync = false)
    {
        _supportAsync = supportAsync;
        if (supportAsync)
        {
            _asyncExprHandlers = new();
        }
    }

    /// <summary>
    /// Registers a handler for a specific expression type.
    /// </summary>
    /// <typeparam name="TExpr">The concrete expression type (must inherit from Expr).</typeparam>
    /// <param name="handler">The handler function that takes the expression and context.</param>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the registry is frozen.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> RegisterExpr<TExpr>(
        Func<TExpr, TContext, TExprResult> handler) where TExpr : Expr
    {
        ThrowIfFrozen();
        _exprHandlers[typeof(TExpr)] = (expr, ctx) => handler((TExpr)expr, ctx);
        return this;
    }

    /// <summary>
    /// Registers a handler for a specific statement type.
    /// </summary>
    /// <typeparam name="TStmt">The concrete statement type (must inherit from Stmt).</typeparam>
    /// <param name="handler">The handler function that takes the statement and context.</param>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the registry is frozen.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> RegisterStmt<TStmt>(
        Func<TStmt, TContext, TStmtResult> handler) where TStmt : Stmt
    {
        ThrowIfFrozen();
        _stmtHandlers[typeof(TStmt)] = (stmt, ctx) => handler((TStmt)stmt, ctx);
        return this;
    }

    /// <summary>
    /// Registers an async handler for a specific expression type.
    /// </summary>
    /// <typeparam name="TExpr">The concrete expression type (must inherit from Expr).</typeparam>
    /// <param name="handler">The async handler function.</param>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if async support is not enabled or registry is frozen.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> RegisterExprAsync<TExpr>(
        Func<TExpr, TContext, ValueTask<TExprResult>> handler) where TExpr : Expr
    {
        ThrowIfFrozen();
        if (_asyncExprHandlers == null)
        {
            throw new InvalidOperationException(
                "Async handlers require supportAsync=true in constructor.");
        }
        _asyncExprHandlers[typeof(TExpr)] = (expr, ctx) => handler((TExpr)expr, ctx);
        return this;
    }

    /// <summary>
    /// Dispatches an expression to its registered handler.
    /// </summary>
    /// <param name="expr">The expression to dispatch.</param>
    /// <param name="context">The context for evaluation.</param>
    /// <returns>The result from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the expression type.</exception>
    public TExprResult DispatchExpr(Expr expr, TContext context)
    {
        var exprType = expr.GetType();
        if (_exprHandlers.TryGetValue(exprType, out var handler))
        {
            return handler(expr, context);
        }
        throw new InvalidOperationException(
            $"No handler registered for expression type: {exprType.Name}");
    }

    /// <summary>
    /// Dispatches a statement to its registered handler.
    /// </summary>
    /// <param name="stmt">The statement to dispatch.</param>
    /// <param name="context">The context for execution.</param>
    /// <returns>The result from the handler.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no handler is registered for the statement type.</exception>
    public TStmtResult DispatchStmt(Stmt stmt, TContext context)
    {
        var stmtType = stmt.GetType();
        if (_stmtHandlers.TryGetValue(stmtType, out var handler))
        {
            return handler(stmt, context);
        }
        throw new InvalidOperationException(
            $"No handler registered for statement type: {stmtType.Name}");
    }

    /// <summary>
    /// Dispatches an expression to its async handler.
    /// Falls back to sync handler wrapped in ValueTask if no async handler is registered.
    /// </summary>
    /// <param name="expr">The expression to dispatch.</param>
    /// <param name="context">The context for evaluation.</param>
    /// <returns>A ValueTask containing the result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if async support is not enabled.</exception>
    public ValueTask<TExprResult> DispatchExprAsync(Expr expr, TContext context)
    {
        if (_asyncExprHandlers == null)
        {
            throw new InvalidOperationException(
                "Async dispatch requires supportAsync=true in constructor.");
        }

        var exprType = expr.GetType();

        // Try async handler first
        if (_asyncExprHandlers.TryGetValue(exprType, out var asyncHandler))
        {
            return asyncHandler(expr, context);
        }

        // Fall back to sync handler
        if (_exprHandlers.TryGetValue(exprType, out var syncHandler))
        {
            return new ValueTask<TExprResult>(syncHandler(expr, context));
        }

        throw new InvalidOperationException(
            $"No handler registered for expression type: {exprType.Name}");
    }

    /// <summary>
    /// Freezes the registry and validates that all AST node types have handlers.
    /// After this call, no more handlers can be registered.
    /// </summary>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if handlers are missing for any node types.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> Freeze()
    {
        if (_frozen) return this;

        var missingTypes = new List<string>();

        // Get all concrete Expr types (nested types in Expr that inherit from Expr)
        var allExprTypes = typeof(Expr).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(Expr).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Expr))
            .ToList();

        foreach (var exprType in allExprTypes)
        {
            if (!_exprHandlers.ContainsKey(exprType))
            {
                missingTypes.Add($"Expr.{exprType.Name}");
            }
        }

        // Get all concrete Stmt types (nested types in Stmt that inherit from Stmt)
        var allStmtTypes = typeof(Stmt).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(Stmt).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Stmt))
            .ToList();

        foreach (var stmtType in allStmtTypes)
        {
            if (!_stmtHandlers.ContainsKey(stmtType))
            {
                missingTypes.Add($"Stmt.{stmtType.Name}");
            }
        }

        if (missingTypes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing handlers for the following AST node types:\n" +
                string.Join("\n", missingTypes.OrderBy(t => t)));
        }

        _frozen = true;
        return this;
    }

    /// <summary>
    /// Gets whether the registry has been frozen.
    /// </summary>
    public bool IsFrozen => _frozen;

    /// <summary>
    /// Gets the number of registered expression handlers.
    /// </summary>
    public int ExprHandlerCount => _exprHandlers.Count;

    /// <summary>
    /// Gets the number of registered statement handlers.
    /// </summary>
    public int StmtHandlerCount => _stmtHandlers.Count;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Cannot register handlers after registry is frozen.");
        }
    }
}
