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
            ExecuteStatements(_declaration.Body);
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
    /// Recursively executes statements, collecting yields.
    /// </summary>
    private void ExecuteStatements(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            ExecuteStatement(stmt);
        }
    }

    /// <summary>
    /// Executes a single statement, handling yield expressions specially.
    /// </summary>
    private void ExecuteStatement(Stmt stmt)
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
                break;

            case Stmt.Block block:
                if (block.Statements != null)
                {
                    var blockEnv = new RuntimeEnvironment(_environment);
                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(blockEnv);
                    try
                    {
                        ExecuteStatements(block.Statements);
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
                        value = _interpreter.Evaluate(varStmt.Initializer);
                    }
                    catch (YieldException yield)
                    {
                        HandleYield(yield);
                        value = null;  // After yield, variable gets undefined
                    }
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                break;

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
                    ExecuteStatement(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    ExecuteStatement(ifStmt.ElseBranch);
                }
                break;

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

                    try
                    {
                        ExecuteStatement(whileStmt.Body);
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
                    var loopEnv = new RuntimeEnvironment(_environment);
                    loopEnv.Define(forOf.Variable.Lexeme, element);

                    RuntimeEnvironment prevEnv = _interpreter.Environment;
                    _interpreter.SetEnvironment(loopEnv);
                    try
                    {
                        ExecuteStatement(forOf.Body);
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
                        returnValue = _interpreter.Evaluate(returnStmt.Value);
                    }
                    catch (YieldException yield)
                    {
                        HandleYield(yield);
                    }
                }
                _returnValue = returnValue;
                throw new ReturnException(returnValue);

            case Stmt.TryCatch tryCatch:
                try
                {
                    ExecuteStatements(tryCatch.TryBlock);
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
                            ExecuteStatements(tryCatch.CatchBlock);
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
                        ExecuteStatements(tryCatch.FinallyBlock);
                    }
                }
                break;

            default:
                // For other statements, delegate to the interpreter
                // but catch any yields that might occur
                try
                {
                    _interpreter.ExecuteBlock([stmt], _environment);
                }
                catch (YieldException yield)
                {
                    HandleYield(yield);
                }
                break;
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
