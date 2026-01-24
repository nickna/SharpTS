using SharpTS.Parsing;
using SharpTS.Runtime;

namespace SharpTS.Execution;

/// <summary>
/// Provides a unified abstraction for sync and async expression evaluation and statement execution.
/// This enables shared core logic while avoiding code duplication between Evaluate/EvaluateAsync
/// and Execute/ExecuteAsync methods.
/// </summary>
/// <remarks>
/// Two implementations exist:
/// <list type="bullet">
///   <item><description><see cref="SyncEvaluationContext"/> - Returns completed ValueTasks for sync execution</description></item>
///   <item><description><see cref="AsyncEvaluationContext"/> - Returns proper async ValueTasks</description></item>
/// </list>
///
/// The pattern uses ValueTask for efficient sync path (no heap allocation when returning synchronously).
/// </remarks>
public interface IEvaluationContext
{
    /// <summary>
    /// Gets the interpreter instance.
    /// </summary>
    Interpreter Interpreter { get; }

    /// <summary>
    /// Gets the current runtime environment.
    /// </summary>
    RuntimeEnvironment Environment { get; }

    /// <summary>
    /// Gets whether this context is async-capable (can properly await promises).
    /// </summary>
    bool IsAsync { get; }

    /// <summary>
    /// Evaluates an expression, returning the result as a ValueTask.
    /// </summary>
    /// <param name="expr">The expression to evaluate.</param>
    /// <returns>A ValueTask containing the evaluation result.</returns>
    ValueTask<object?> EvaluateExprAsync(Expr expr);

    /// <summary>
    /// Executes a statement, returning the result as a ValueTask.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>A ValueTask containing the execution result.</returns>
    ValueTask<ExecutionResult> ExecuteStmtAsync(Stmt stmt);
}

/// <summary>
/// Synchronous evaluation context that wraps sync calls in completed ValueTasks.
/// Used for non-async code paths where we want to use the unified core methods
/// without the overhead of actual async execution.
/// </summary>
internal sealed class SyncEvaluationContext : IEvaluationContext
{
    private readonly Interpreter _interpreter;

    public SyncEvaluationContext(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    public Interpreter Interpreter => _interpreter;
    public RuntimeEnvironment Environment => _interpreter.Environment;
    public bool IsAsync => false;

    public ValueTask<object?> EvaluateExprAsync(Expr expr)
    {
        // Sync path: evaluate immediately and return completed ValueTask
        var result = _interpreter.Evaluate(expr);
        return new ValueTask<object?>(result);
    }

    public ValueTask<ExecutionResult> ExecuteStmtAsync(Stmt stmt)
    {
        // Sync path: execute immediately and return completed ValueTask
        var result = _interpreter.ExecuteStatement(stmt);
        return new ValueTask<ExecutionResult>(result);
    }
}

/// <summary>
/// Asynchronous evaluation context that properly awaits async operations.
/// Used for async code paths (async functions, await expressions).
/// </summary>
internal sealed class AsyncEvaluationContext : IEvaluationContext
{
    private readonly Interpreter _interpreter;

    public AsyncEvaluationContext(Interpreter interpreter)
    {
        _interpreter = interpreter;
    }

    public Interpreter Interpreter => _interpreter;
    public RuntimeEnvironment Environment => _interpreter.Environment;
    public bool IsAsync => true;

    public ValueTask<object?> EvaluateExprAsync(Expr expr)
    {
        // Async path: use the async evaluator
        return new ValueTask<object?>(_interpreter.EvaluateAsync(expr));
    }

    public ValueTask<ExecutionResult> ExecuteStmtAsync(Stmt stmt)
    {
        // Async path: use the async executor
        return new ValueTask<ExecutionResult>(_interpreter.ExecuteStatementAsync(stmt));
    }
}

/// <summary>
/// Extension methods for IEvaluationContext to simplify common operations.
/// </summary>
internal static class EvaluationContextExtensions
{
    /// <summary>
    /// Evaluates multiple expressions and returns the results as a list.
    /// </summary>
    public static async ValueTask<List<object?>> EvaluateAllAsync(
        this IEvaluationContext ctx,
        IEnumerable<Expr> exprs)
    {
        var results = new List<object?>();
        foreach (var expr in exprs)
        {
            results.Add(await ctx.EvaluateExprAsync(expr));
        }
        return results;
    }

    /// <summary>
    /// Executes multiple statements sequentially, stopping on abrupt completion.
    /// </summary>
    public static async ValueTask<ExecutionResult> ExecuteAllAsync(
        this IEvaluationContext ctx,
        IEnumerable<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            var result = await ctx.ExecuteStmtAsync(stmt);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }
}
