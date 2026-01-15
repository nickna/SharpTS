using System.Collections;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime object representing an active generator instance.
/// </summary>
/// <remarks>
/// Created by <see cref="SharpTSGeneratorFunction"/> when called. Implements
/// execution of the generator body, collecting yielded values.
/// Also implements IEnumerable for seamless for...of integration.
///
/// This implementation uses eager evaluation - the entire generator body is
/// executed on the first call to Next(), collecting all yielded values into
/// a list. Subsequent Next() calls return values from this list.
/// </remarks>
/// <seealso cref="SharpTSGeneratorFunction"/>
/// <seealso cref="SharpTSIteratorResult"/>
public class SharpTSGenerator : IEnumerable<object?>
{
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _environment;
    private readonly Interpreter _interpreter;

    private List<object?>? _values = null;  // Collected yielded values (null = not yet executed)
    private int _index = 0;
    private object? _returnValue = null;
    private bool _closed = false;  // True if generator has been closed via .return()

    public SharpTSGenerator(Stmt.Function declaration, RuntimeEnvironment environment, Interpreter interpreter)
    {
        _declaration = declaration;
        _environment = environment;
        _interpreter = interpreter;
    }

    /// <summary>
    /// Advances the generator to the next yield point.
    /// Returns { value, done } result object.
    /// </summary>
    public SharpTSIteratorResult Next()
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
    /// Closes the generator and returns a result with the given value.
    /// </summary>
    /// <remarks>
    /// Note: In JavaScript, .return() would trigger finally blocks. This simplified
    /// implementation just closes the generator. Finally block support would require
    /// a full lazy evaluation model with proper state machine semantics.
    /// </remarks>
    public SharpTSIteratorResult Return(object? value = null)
    {
        _closed = true;
        _returnValue = value;
        return new SharpTSIteratorResult(value, done: true);
    }

    /// <summary>
    /// Throws an exception at the current yield point.
    /// </summary>
    /// <remarks>
    /// Since this generator uses eager evaluation, the throw happens after
    /// all values are already collected. The exception propagates to the caller.
    /// </remarks>
    public SharpTSIteratorResult Throw(object? error = null)
    {
        _closed = true;
        string message = error?.ToString() ?? "Generator.throw() called";
        throw new ThrowException(error ?? message);
    }

    /// <summary>
    /// Executes the generator body, collecting all yielded values.
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
            var result = ExecuteStatements(_declaration.Body);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                _returnValue = result.Value;
            }
            else if (result.Type == ExecutionResult.ResultType.Throw)
            {
                throw new Exception(_interpreter.Stringify(result.Value));
            }
        }
        finally
        {
            _interpreter.SetEnvironment(previousEnv);
        }
    }

    /// <summary>
    /// Recursively executes statements, collecting yields.
    /// </summary>
    private ExecutionResult ExecuteStatements(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            var result = ExecuteStatement(stmt);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes a single statement, handling yield expressions specially.
    /// </summary>
    private ExecutionResult ExecuteStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression exprStmt:
                try
                {
                    _interpreter.Evaluate(exprStmt.Expr);
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                }
                return ExecutionResult.Success();

            case Stmt.Block block:
                if (block.Statements != null)
                {
                    var blockEnv = new RuntimeEnvironment(_interpreter.Environment);
                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(blockEnv);
                    try
                    {
                        return ExecuteStatements(block.Statements);
                    }
                    finally
                    {
                        _interpreter.SetEnvironment(prevEnv);
                    }
                }
                return ExecutionResult.Success();

            case Stmt.Var varStmt:
                object? value = null;
                try
                {
                    if (varStmt.Initializer != null)
                    {
                        value = _interpreter.Evaluate(varStmt.Initializer);
                    }
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                    value = null;  // After yield, variable gets undefined
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                return ExecutionResult.Success();

            case Stmt.If ifStmt:
                object? condition;
                try
                {
                    condition = _interpreter.Evaluate(ifStmt.Condition);
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                    condition = false;
                }

                if (IsTruthy(condition))
                {
                    return ExecuteStatement(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    return ExecuteStatement(ifStmt.ElseBranch);
                }
                return ExecutionResult.Success();

            case Stmt.While whileStmt:
                while (true)
                {
                    object? whileCond;
                    try
                    {
                        whileCond = _interpreter.Evaluate(whileStmt.Condition);
                    }
                    catch (YieldException yield)
                    {
                        HandleYield(yield);
                        whileCond = false;
                    }

                    if (!IsTruthy(whileCond)) break;

                    var result = ExecuteStatement(whileStmt.Body);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) break;
                    if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null) continue;
                    if (result.IsAbrupt) return result;
                }
                return ExecutionResult.Success();

            case Stmt.For forStmt:
                // Execute initializer once
                if (forStmt.Initializer != null)
                {
                    var initResult = ExecuteStatement(forStmt.Initializer);
                    if (initResult.IsAbrupt) return initResult;
                }

                // Loop
                while (true)
                {
                    // Check condition
                    if (forStmt.Condition != null)
                    {
                        object? forCond;
                        try
                        {
                            forCond = _interpreter.Evaluate(forStmt.Condition);
                        }
                        catch (YieldException yield)
                        {
                            HandleYield(yield);
                            forCond = false;
                        }

                        if (!IsTruthy(forCond)) break;
                    }

                    // Execute body
                    var result = ExecuteStatement(forStmt.Body);

                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null)
                        break;

                    // On continue OR normal completion, execute increment
                    if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null)
                    {
                        // Execute increment before continuing
                        if (forStmt.Increment != null)
                        {
                            try
                            {
                                _interpreter.Evaluate(forStmt.Increment);
                            }
                            catch (YieldException yield)
                            {
                                HandleYield(yield);
                            }
                        }
                        continue;
                    }

                    if (result.IsAbrupt) return result;

                    // Normal completion: execute increment
                    if (forStmt.Increment != null)
                    {
                        try
                        {
                            _interpreter.Evaluate(forStmt.Increment);
                        }
                        catch (YieldException yield)
                        {
                            HandleYield(yield);
                        }
                    }
                }
                return ExecutionResult.Success();

            case Stmt.ForOf forOf:
                object? iterable;
                try
                {
                    iterable = _interpreter.Evaluate(forOf.Iterable);
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                    iterable = new SharpTSArray([]);
                }

                IEnumerable<object?> elements = GetIterableElements(iterable);
                foreach (var element in elements)
                {
                    var loopEnv = new RuntimeEnvironment(_interpreter.Environment);
                    loopEnv.Define(forOf.Variable.Lexeme, element);

                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(loopEnv);
                    try
                    {
                        var result = ExecuteStatement(forOf.Body);
                        if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) break;
                        if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null) continue;
                        if (result.IsAbrupt) return result;
                    }
                    finally
                    {
                        _interpreter.SetEnvironment(prevEnv);
                    }
                }
                return ExecutionResult.Success();

            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null)
                {
                    try
                    {
                        returnValue = _interpreter.Evaluate(returnStmt.Value);
                    }
                    catch (YieldException yield)
                    {
                        HandleYield(yield);
                    }
                }
                return ExecutionResult.Return(returnValue);

            case Stmt.TryCatch tryCatch:
                ExecutionResult tryResult = ExecuteStatements(tryCatch.TryBlock);
                if (tryResult.Type == ExecutionResult.ResultType.Throw)
                {
                    if (tryCatch.CatchBlock != null && tryCatch.CatchParam != null)
                    {
                        var catchEnv = new RuntimeEnvironment(_interpreter.Environment);
                        catchEnv.Define(tryCatch.CatchParam.Lexeme, tryResult.Value);
                        RuntimeEnvironment prevEnv = _interpreter.Environment;
                        _interpreter.SetEnvironment(catchEnv);
                        try
                        {
                            tryResult = ExecuteStatements(tryCatch.CatchBlock);
                        }
                        finally
                        {
                            _interpreter.SetEnvironment(prevEnv);
                        }
                    }
                }

                if (tryCatch.FinallyBlock != null)
                {
                    var finallyResult = ExecuteStatements(tryCatch.FinallyBlock);
                    if (finallyResult.IsAbrupt) return finallyResult;
                }
                return tryResult;

            default:
                // For other statements, delegate to the interpreter
                // but catch any yields that might occur
                try
                {
                    return _interpreter.ExecuteBlock([stmt], _environment);
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                    return ExecutionResult.Success();
                }
        }
    }

    /// <summary>
    /// Handles a yield exception by collecting the value.
    /// </summary>
    private void HandleYield(YieldException yield)
    {
        if (yield.IsDelegating)
        {
            // yield* - delegate to another iterable
            var elements = GetIterableElements(yield.Value);
            foreach (var element in elements)
            {
                _values!.Add(element);
            }
        }
        else
        {
            _values!.Add(yield.Value);
        }
    }

    /// <summary>
    /// Gets elements from an iterable value, including custom iterables with Symbol.iterator.
    /// </summary>
    private IEnumerable<object?> GetIterableElements(object? value)
    {
        // Use the interpreter's GetIterableElements which handles Symbol.iterator protocol
        return _interpreter.GetIterableElements(value);
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

    // IEnumerable implementation for for...of integration
    public IEnumerator<object?> GetEnumerator()
    {
        while (true)
        {
            var result = Next();
            if (result.Done) yield break;
            yield return result.Value;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[object Generator]";
}
