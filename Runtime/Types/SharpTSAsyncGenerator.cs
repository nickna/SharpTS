using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime object representing an active async generator instance.
/// </summary>
/// <remarks>
/// Created by <see cref="SharpTSAsyncGeneratorFunction"/> when called.
/// Combines async execution (await) with generator semantics (yield).
/// Each call to next() returns a Promise that resolves to { value, done }.
/// </remarks>
/// <seealso cref="SharpTSAsyncGeneratorFunction"/>
/// <seealso cref="SharpTSGenerator"/>
public class SharpTSAsyncGenerator
{
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _environment;
    private readonly Interpreter _interpreter;

    private List<object?>? _values = null;  // Collected yielded values (null = not yet executed)
    private int _index = 0;
    private object? _returnValue = null;
    private bool _closed = false;

    public SharpTSAsyncGenerator(Stmt.Function declaration, RuntimeEnvironment environment, Interpreter interpreter)
    {
        _declaration = declaration;
        _environment = environment;
        _interpreter = interpreter;
    }

    /// <summary>
    /// Advances the async generator to the next yield point.
    /// Returns a Promise that resolves to { value, done } result object.
    /// </summary>
    public Task<object?> Next()
    {
        return Task.FromResult<object?>(NextSync());
    }

    /// <summary>
    /// Synchronous implementation of next() for simpler cases.
    /// </summary>
    private SharpTSIteratorResult NextSync()
    {
        // If generator is closed, always return done
        if (_closed)
        {
            return new SharpTSIteratorResult(_returnValue, done: true);
        }

        // Execute the generator body on first call
        if (_values == null)
        {
            ExecuteBody();
        }

        if (_index < _values!.Count)
        {
            return new SharpTSIteratorResult(_values[_index++], done: false);
        }

        return new SharpTSIteratorResult(_returnValue, done: true);
    }

    /// <summary>
    /// Closes the async generator and returns a Promise resolving to { value, done: true }.
    /// </summary>
    public Task<object?> Return(object? value = null)
    {
        _closed = true;
        _returnValue = value;
        return Task.FromResult<object?>(new SharpTSIteratorResult(value, done: true));
    }

    /// <summary>
    /// Throws an exception at the current yield point.
    /// Returns a Promise that rejects with the error.
    /// </summary>
    public Task<object?> Throw(object? error = null)
    {
        _closed = true;
        string message = error?.ToString() ?? "AsyncGenerator.throw() called";
        throw new ThrowException(error ?? message);
    }

    /// <summary>
    /// Executes the async generator body, collecting all yielded values.
    /// Handles both yield and await expressions.
    /// </summary>
    private void ExecuteBody()
    {
        _values = [];

        if (_declaration.Body == null || _declaration.Body.Count == 0)
        {
            return;
        }

        // Save and set the interpreter environment
        RuntimeEnvironment previousEnv = _interpreter.Environment;
        _interpreter.SetEnvironment(_environment);

        try
        {
            ExecuteStatementsAsync(_declaration.Body).GetAwaiter().GetResult();
        }
        catch (ReturnException ret)
        {
            _returnValue = ret.Value;
        }
        finally
        {
            _interpreter.SetEnvironment(previousEnv);
        }
    }

    /// <summary>
    /// Recursively executes statements asynchronously, collecting yields.
    /// </summary>
    private async Task ExecuteStatementsAsync(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            await ExecuteStatementAsync(stmt);
        }
    }

    /// <summary>
    /// Executes a single statement asynchronously, handling yield and await expressions.
    /// </summary>
    private async Task ExecuteStatementAsync(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression exprStmt:
                try
                {
                    await EvaluateAsync(exprStmt.Expr);
                }
                catch (YieldException yield)
                {
                    await HandleYieldAsync(yield);
                }
                break;

            case Stmt.Block block:
                if (block.Statements != null)
                {
                    var blockEnv = new RuntimeEnvironment(_environment);
                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(blockEnv);
                    try
                    {
                        await ExecuteStatementsAsync(block.Statements);
                    }
                    finally
                    {
                        _interpreter.SetEnvironment(prevEnv);
                    }
                }
                break;

            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    try
                    {
                        value = await EvaluateAsync(varStmt.Initializer);
                    }
                    catch (YieldException yield)
                    {
                        await HandleYieldAsync(yield);
                        value = null;
                    }
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                break;

            case Stmt.If ifStmt:
                object? condition;
                try
                {
                    condition = await EvaluateAsync(ifStmt.Condition);
                }
                catch (YieldException yield)
                {
                    await HandleYieldAsync(yield);
                    condition = false;
                }

                if (IsTruthy(condition))
                {
                    await ExecuteStatementAsync(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    await ExecuteStatementAsync(ifStmt.ElseBranch);
                }
                break;

            case Stmt.While whileStmt:
                while (true)
                {
                    object? whileCond;
                    try
                    {
                        whileCond = await EvaluateAsync(whileStmt.Condition);
                    }
                    catch (YieldException yield)
                    {
                        await HandleYieldAsync(yield);
                        whileCond = false;
                    }

                    if (!IsTruthy(whileCond)) break;

                    try
                    {
                        await ExecuteStatementAsync(whileStmt.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        continue;
                    }
                }
                break;

            case Stmt.ForOf forOf:
                object? iterable;
                try
                {
                    iterable = await EvaluateAsync(forOf.Iterable);
                }
                catch (YieldException yield)
                {
                    await HandleYieldAsync(yield);
                    iterable = new SharpTSArray([]);
                }

                IEnumerable<object?> elements = GetIterableElements(iterable);
                foreach (var element in elements)
                {
                    var loopEnv = new RuntimeEnvironment(_environment);
                    loopEnv.Define(forOf.Variable.Lexeme, element);

                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(loopEnv);
                    try
                    {
                        await ExecuteStatementAsync(forOf.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        _interpreter.SetEnvironment(prevEnv);
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        _interpreter.SetEnvironment(prevEnv);
                        continue;
                    }
                    finally
                    {
                        _interpreter.SetEnvironment(prevEnv);
                    }
                }
                break;

            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null)
                {
                    try
                    {
                        returnValue = await EvaluateAsync(returnStmt.Value);
                    }
                    catch (YieldException yield)
                    {
                        await HandleYieldAsync(yield);
                    }
                }
                _returnValue = returnValue;
                throw new ReturnException(returnValue);

            case Stmt.TryCatch tryCatch:
                try
                {
                    await ExecuteStatementsAsync(tryCatch.TryBlock);
                }
                catch (ThrowException ex)
                {
                    if (tryCatch.CatchBlock != null && tryCatch.CatchParam != null)
                    {
                        var catchEnv = new RuntimeEnvironment(_environment);
                        catchEnv.Define(tryCatch.CatchParam.Lexeme, ex.Value);
                        RuntimeEnvironment prevEnv = _interpreter.Environment;
                        _interpreter.SetEnvironment(catchEnv);
                        try
                        {
                            await ExecuteStatementsAsync(tryCatch.CatchBlock);
                        }
                        finally
                        {
                            _interpreter.SetEnvironment(prevEnv);
                        }
                    }
                }
                finally
                {
                    if (tryCatch.FinallyBlock != null)
                    {
                        await ExecuteStatementsAsync(tryCatch.FinallyBlock);
                    }
                }
                break;

            default:
                // For other statements, delegate to the interpreter's async handler
                try
                {
                    await _interpreter.ExecuteBlockAsync([stmt], _environment);
                }
                catch (YieldException yield)
                {
                    await HandleYieldAsync(yield);
                }
                break;
        }
    }

    /// <summary>
    /// Evaluates an expression asynchronously, handling await expressions.
    /// </summary>
    private async Task<object?> EvaluateAsync(Expr expr)
    {
        // Check for await expression
        if (expr is Expr.Await awaitExpr)
        {
            var value = _interpreter.Evaluate(awaitExpr.Expression);
            // Handle SharpTSPromise (wraps Task<object?>)
            if (value is SharpTSPromise promise)
            {
                return await promise.Task;
            }
            if (value is Task<object?> task)
            {
                return await task;
            }
            return value;
        }

        // For other expressions, evaluate synchronously but check for yield
        return _interpreter.Evaluate(expr);
    }

    /// <summary>
    /// Handles a yield exception by collecting the value.
    /// </summary>
    private async Task HandleYieldAsync(YieldException yield)
    {
        if (yield.IsDelegating)
        {
            // yield* - delegate to another iterable
            var value = yield.Value;

            // If delegating to an async iterable, await each value
            if (value is SharpTSAsyncGenerator asyncGen)
            {
                while (true)
                {
                    var result = await asyncGen.Next();
                    if (result is SharpTSIteratorResult ir)
                    {
                        if (ir.Done) break;
                        _values!.Add(ir.Value);
                    }
                    else
                    {
                        _values!.Add(result);
                    }
                }
            }
            else
            {
                var elements = GetIterableElements(value);
                foreach (var element in elements)
                {
                    _values!.Add(element);
                }
            }
        }
        else
        {
            // If yielding a promise, await it first
            var value = yield.Value;
            if (value is Task<object?> task)
            {
                value = await task;
            }
            _values!.Add(value);
        }
    }

    /// <summary>
    /// Gets elements from an iterable value.
    /// </summary>
    private static IEnumerable<object?> GetIterableElements(object? value)
    {
        return value switch
        {
            SharpTSArray array => array.Elements,
            SharpTSGenerator gen => gen,
            SharpTSIterator iter => iter.Elements,
            SharpTSMap map => map.Entries().Elements,
            SharpTSSet set => set.Values().Elements,
            string s => s.Select(c => (object?)c.ToString()),
            IEnumerable<object?> enumerable => enumerable,
            null => [],
            _ => throw new Exception($"Runtime Error: Cannot iterate over non-iterable value.")
        };
    }

    /// <summary>
    /// Checks if a value is truthy (JavaScript semantics).
    /// </summary>
    private static bool IsTruthy(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is double d) return d != 0;
        if (obj is string s) return s.Length > 0;
        return true;
    }

    public override string ToString() => "[object AsyncGenerator]";
}
