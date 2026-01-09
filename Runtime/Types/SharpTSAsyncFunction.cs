using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Interface for async callable objects in the SharpTS runtime.
/// </summary>
/// <remarks>
/// Extends <see cref="ISharpTSCallable"/> to provide async execution semantics.
/// The Call method returns a <see cref="SharpTSPromise"/> immediately.
/// </remarks>
public interface ISharpTSAsyncCallable : ISharpTSCallable
{
    /// <summary>
    /// Asynchronously invokes this callable and returns the result.
    /// </summary>
    Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments);
}

/// <summary>
/// Runtime wrapper for async function declarations.
/// </summary>
/// <remarks>
/// Wraps a <see cref="Stmt.Function"/> AST node with IsAsync=true.
/// The synchronous Call method returns a <see cref="SharpTSPromise"/> immediately,
/// while CallAsync executes the function body asynchronously.
/// </remarks>
public class SharpTSAsyncFunction(Stmt.Function declaration, RuntimeEnvironment closure) : ISharpTSAsyncCallable
{
    private readonly Stmt.Function _declaration = declaration;
    private readonly RuntimeEnvironment _closure = closure;

    public int Arity() => _declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);

    /// <summary>
    /// Invokes the async function, returning a Promise immediately.
    /// The actual execution happens asynchronously.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Start async execution and wrap in Promise
        var task = CallAsync(interpreter, arguments);
        return new SharpTSPromise(task);
    }

    /// <summary>
    /// Asynchronously executes the function body.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);

        // Bind parameters
        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            var param = _declaration.Parameters[i];

            if (param.IsRest)
            {
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break;
            }

            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = await interpreter.EvaluateAsync(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
        }

        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        var result = await interpreter.ExecuteBlockAsync(_declaration.Body, environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            // Unwrap Promise if returning a Promise from async function
            object? val = result.Value;
            if (val is SharpTSPromise promise)
            {
                val = await promise.GetValueAsync();
            }
            return val;
        }
        if (result.Type == ExecutionResult.ResultType.Throw)
        {
            throw new Exception(interpreter.Stringify(result.Value));
        }

        return null;
    }

    public SharpTSAsyncFunction Bind(SharpTSInstance instance)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", instance);
        return new SharpTSAsyncFunction(_declaration, environment);
    }

    public override string ToString() => $"<async fn {_declaration.Name.Lexeme}>";
}

/// <summary>
/// Runtime wrapper for async arrow function expressions.
/// </summary>
/// <remarks>
/// Wraps an <see cref="Expr.ArrowFunction"/> AST node with IsAsync=true.
/// Supports both expression bodies and block bodies.
/// Like regular arrow functions, async arrows capture 'this' from the enclosing scope.
/// </remarks>
public class SharpTSAsyncArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool isObjectMethod = false) : ISharpTSAsyncCallable
{
    private readonly Expr.ArrowFunction _declaration = declaration;
    private readonly RuntimeEnvironment _closure = closure;

    public bool IsObjectMethod { get; } = isObjectMethod;

    public int Arity() => _declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);

    /// <summary>
    /// Invokes the async arrow function, returning a Promise immediately.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var task = CallAsync(interpreter, arguments);
        return new SharpTSPromise(task);
    }

    /// <summary>
    /// Asynchronously executes the arrow function.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);

        // Bind parameters
        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            var param = _declaration.Parameters[i];

            if (param.IsRest)
            {
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break;
            }

            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = await interpreter.EvaluateAsync(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
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
            RuntimeEnvironment previous = interpreter.Environment;
            try
                {
                interpreter.SetEnvironment(environment);
                object? result = await interpreter.EvaluateAsync(_declaration.ExpressionBody);
                // Unwrap Promise if returning a Promise from async arrow
                if (result is SharpTSPromise promise)
                {
                    result = await promise.GetValueAsync();
                }
                return result;
            }
            finally
            {
                interpreter.SetEnvironment(previous);
            }
        }
        else if (_declaration.BlockBody != null)
        {
            var result = await interpreter.ExecuteBlockAsync(_declaration.BlockBody, environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                object? val = result.Value;
                if (val is SharpTSPromise promise)
                {
                    val = await promise.GetValueAsync();
                }
                return val;
            }
            if (result.Type == ExecutionResult.ResultType.Throw)
            {
                throw new Exception(interpreter.Stringify(result.Value));
            }
        }

        return null;
    }

    public SharpTSAsyncArrowFunction Bind(object thisObject)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", thisObject);
        return new SharpTSAsyncArrowFunction(_declaration, environment, isObjectMethod: true);
    }

    public override string ToString() => "<async arrow fn>";
}
