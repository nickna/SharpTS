using System.Linq.Expressions;
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
    private readonly Dictionary<Type, Func<Stmt, TContext, ValueTask<TStmtResult>>>? _asyncStmtHandlers;

    private bool _frozen;
    private readonly bool _supportAsync;

    /// <summary>
    /// Creates a new NodeRegistry.
    /// </summary>
    /// <param name="supportAsync">Whether to support async expression and statement handlers.</param>
    public NodeRegistry(bool supportAsync = false)
    {
        _supportAsync = supportAsync;
        if (supportAsync)
        {
            _asyncExprHandlers = new();
            _asyncStmtHandlers = new();
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
    /// Registers an async handler for a specific statement type.
    /// </summary>
    /// <typeparam name="TStmt">The concrete statement type (must inherit from Stmt).</typeparam>
    /// <param name="handler">The async handler function.</param>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if async support is not enabled or registry is frozen.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> RegisterStmtAsync<TStmt>(
        Func<TStmt, TContext, ValueTask<TStmtResult>> handler) where TStmt : Stmt
    {
        ThrowIfFrozen();
        if (_asyncStmtHandlers == null)
        {
            throw new InvalidOperationException(
                "Async handlers require supportAsync=true in constructor.");
        }
        _asyncStmtHandlers[typeof(TStmt)] = (stmt, ctx) => handler((TStmt)stmt, ctx);
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
    /// Dispatches a statement to its async handler.
    /// Falls back to sync handler wrapped in ValueTask if no async handler is registered.
    /// </summary>
    /// <param name="stmt">The statement to dispatch.</param>
    /// <param name="context">The context for execution.</param>
    /// <returns>A ValueTask containing the result.</returns>
    /// <exception cref="InvalidOperationException">Thrown if async support is not enabled.</exception>
    public ValueTask<TStmtResult> DispatchStmtAsync(Stmt stmt, TContext context)
    {
        if (_asyncStmtHandlers == null)
        {
            throw new InvalidOperationException(
                "Async dispatch requires supportAsync=true in constructor.");
        }

        var stmtType = stmt.GetType();

        // Try async handler first
        if (_asyncStmtHandlers.TryGetValue(stmtType, out var asyncHandler))
        {
            return asyncHandler(stmt, context);
        }

        // Fall back to sync handler
        if (_stmtHandlers.TryGetValue(stmtType, out var syncHandler))
        {
            return new ValueTask<TStmtResult>(syncHandler(stmt, context));
        }

        throw new InvalidOperationException(
            $"No handler registered for statement type: {stmtType.Name}");
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

    /// <summary>
    /// Gets the number of registered async statement handlers.
    /// </summary>
    public int AsyncStmtHandlerCount => _asyncStmtHandlers?.Count ?? 0;

    /// <summary>
    /// Gets the number of registered async expression handlers.
    /// </summary>
    public int AsyncExprHandlerCount => _asyncExprHandlers?.Count ?? 0;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Cannot register handlers after registry is frozen.");
        }
    }

    /// <summary>
    /// Automatically registers handlers for all AST node types by discovering Visit* methods on TContext.
    /// For each Expr.Xxx type, looks for a method named VisitXxx(Expr.Xxx) returning TExprResult.
    /// For each Stmt.Xxx type, looks for a method named VisitXxx(Stmt.Xxx) returning TStmtResult.
    /// </summary>
    /// <returns>This registry for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the registry is frozen or if a Visit method is missing.</exception>
    public NodeRegistry<TContext, TExprResult, TStmtResult> AutoRegister()
    {
        ThrowIfFrozen();

        var contextType = typeof(TContext);
        var missingMethods = new List<string>();

        // Register all expression types
        var allExprTypes = typeof(Expr).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(Expr).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Expr))
            .ToList();

        foreach (var exprType in allExprTypes)
        {
            var methodName = $"Visit{exprType.Name}";
            var method = contextType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [exprType]);

            if (method == null || method.ReturnType != typeof(TExprResult))
            {
                missingMethods.Add($"{contextType.Name}.{methodName}({exprType.Name}) -> {typeof(TExprResult).Name}");
                continue;
            }

            var handler = CreateExprHandler(method, exprType);
            _exprHandlers[exprType] = handler;
        }

        // Register all statement types
        var allStmtTypes = typeof(Stmt).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(Stmt).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(Stmt))
            .ToList();

        foreach (var stmtType in allStmtTypes)
        {
            var methodName = $"Visit{stmtType.Name}";
            var method = contextType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [stmtType]);

            if (method == null || method.ReturnType != typeof(TStmtResult))
            {
                missingMethods.Add($"{contextType.Name}.{methodName}({stmtType.Name}) -> {typeof(TStmtResult).Name}");
                continue;
            }

            var handler = CreateStmtHandler(method, stmtType);
            _stmtHandlers[stmtType] = handler;
        }

        if (missingMethods.Count > 0)
        {
            throw new InvalidOperationException(
                $"AutoRegister failed. Missing or incorrectly typed Visit methods:\n" +
                string.Join("\n", missingMethods.OrderBy(m => m)));
        }

        return this;
    }

    /// <summary>
    /// Creates a compiled delegate for an expression handler method.
    /// </summary>
    private Func<Expr, TContext, TExprResult> CreateExprHandler(MethodInfo method, Type exprType)
    {
        // Build: (Expr expr, TContext ctx) => ctx.VisitXxx((Expr.Xxx)expr)
        var exprParam = Expression.Parameter(typeof(Expr), "expr");
        var ctxParam = Expression.Parameter(typeof(TContext), "ctx");

        var castExpr = Expression.Convert(exprParam, exprType);
        var callExpr = Expression.Call(ctxParam, method, castExpr);

        var lambda = Expression.Lambda<Func<Expr, TContext, TExprResult>>(callExpr, exprParam, ctxParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Creates a compiled delegate for a statement handler method.
    /// </summary>
    private Func<Stmt, TContext, TStmtResult> CreateStmtHandler(MethodInfo method, Type stmtType)
    {
        // Build: (Stmt stmt, TContext ctx) => ctx.VisitXxx((Stmt.Xxx)stmt)
        var stmtParam = Expression.Parameter(typeof(Stmt), "stmt");
        var ctxParam = Expression.Parameter(typeof(TContext), "ctx");

        var castStmt = Expression.Convert(stmtParam, stmtType);
        var callExpr = Expression.Call(ctxParam, method, castStmt);

        var lambda = Expression.Lambda<Func<Stmt, TContext, TStmtResult>>(callExpr, stmtParam, ctxParam);
        return lambda.Compile();
    }
}
