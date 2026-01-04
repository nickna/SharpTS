using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Interface for all callable objects in the SharpTS runtime.
/// </summary>
/// <remarks>
/// Implemented by <see cref="SharpTSFunction"/>, <see cref="SharpTSArrowFunction"/>,
/// and <see cref="SharpTSClass"/>. Enables uniform function invocation regardless
/// of whether the callee is a named function, arrow function, or class constructor.
/// </remarks>
public interface ISharpTSCallable
{
    int Arity();
    object? Call(Interpreter interpreter, List<object?> arguments);
}

/// <summary>
/// Runtime wrapper for named function declarations.
/// </summary>
/// <remarks>
/// Wraps a <see cref="Stmt.Function"/> AST node along with its closure environment.
/// Handles parameter binding (including default values and rest parameters),
/// executes the function body, and catches <see cref="ReturnException"/> to return values.
/// The <see cref="Bind"/> method creates a new function with <c>this</c> bound for method calls.
/// </remarks>
/// <seealso cref="SharpTSArrowFunction"/>
/// <seealso cref="RuntimeEnvironment"/>
public class SharpTSFunction(Stmt.Function declaration, RuntimeEnvironment closure) : ISharpTSCallable
{
    private readonly Stmt.Function _declaration = declaration;
    private readonly RuntimeEnvironment _closure = closure;

    public int Arity() => _declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            var param = _declaration.Parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break; // Rest is always last
            }

            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                // Evaluate default value in the function's environment
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.Evaluate(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
                // Optional parameter with no argument and no default - use null
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
        }

        try
        {
            interpreter.ExecuteBlock(_declaration.Body, environment);
        }
        catch (ReturnException returnValue)
        {
            return returnValue.Value;
        }

        return null;
    }

    public SharpTSFunction Bind(SharpTSInstance instance)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", instance);
        return new SharpTSFunction(_declaration, environment);
    }

    public override string ToString() => $"<fn {_declaration.Name.Lexeme}>";
}

/// <summary>
/// Runtime wrapper for arrow function expressions.
/// </summary>
/// <remarks>
/// Wraps an <see cref="Expr.ArrowFunction"/> AST node along with its closure environment.
/// Supports both expression bodies (<c>x =&gt; x + 1</c>) and block bodies (<c>x =&gt; { return x + 1; }</c>).
/// Unlike <see cref="SharpTSFunction"/>, arrow functions do not have their own <c>this</c> binding;
/// they capture <c>this</c> from the enclosing scope via the closure.
/// </remarks>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure) : ISharpTSCallable
{
    private readonly Expr.ArrowFunction _declaration = declaration;
    private readonly RuntimeEnvironment _closure = closure;

    public int Arity() => _declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            var param = _declaration.Parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break; // Rest is always last
            }

            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                // Evaluate default value in the function's environment
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.Evaluate(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
                // Optional parameter with no argument and no default - use null
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
        }

        if (_declaration.ExpressionBody != null)
        {
            // Expression body - evaluate and return directly
            RuntimeEnvironment previous = interpreter.Environment;
            try
            {
                interpreter.SetEnvironment(environment);
                return interpreter.Evaluate(_declaration.ExpressionBody);
            }
            finally
            {
                interpreter.SetEnvironment(previous);
            }
        }
        else if (_declaration.BlockBody != null)
        {
            // Block body - execute statements, catch return
            try
            {
                interpreter.ExecuteBlock(_declaration.BlockBody, environment);
            }
            catch (ReturnException returnValue)
            {
                return returnValue.Value;
            }
        }

        return null;
    }

    public override string ToString() => "<arrow fn>";
}
