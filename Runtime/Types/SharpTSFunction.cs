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
public class SharpTSFunction : ISharpTSCallable
{
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;

    public SharpTSFunction(Stmt.Function declaration, RuntimeEnvironment closure)
    {
        _declaration = declaration;
        _closure = closure;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    public int Arity() => _arity;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        // Check for function-level "use strict" directive
        bool functionStrict = CheckForUseStrict(_declaration.Body);
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter);

        var result = interpreter.ExecuteBlock(_declaration.Body, environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            return result.Value;
        }
        if (result.Type == ExecutionResult.ResultType.Throw)
        {
            throw new Exception(interpreter.Stringify(result.Value));
        }

        return null;
    }

    /// <summary>
    /// Checks if the statements begin with a "use strict" directive.
    /// </summary>
    private static bool CheckForUseStrict(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
            }
            else
            {
                break;
            }
        }
        return false;
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
/// Object method arrow functions (IsObjectMethod=true) support binding <c>this</c> when accessed as object properties.
/// </remarks>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSArrowFunction : ISharpTSCallable
{
    private readonly Expr.ArrowFunction _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;

    /// <summary>
    /// Indicates whether this arrow function was created from object method shorthand
    /// and should bind 'this' when accessed as a property.
    /// </summary>
    public bool IsObjectMethod { get; }

    public SharpTSArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool isObjectMethod = false)
    {
        _declaration = declaration;
        _closure = closure;
        IsObjectMethod = isObjectMethod;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    public int Arity() => _arity;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Check for function-level "use strict" directive in block body
        bool functionStrict = _declaration.BlockBody != null && CheckForUseStrict(_declaration.BlockBody);
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter);

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
            var result = interpreter.ExecuteBlock(_declaration.BlockBody, environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                return result.Value;
            }
            if (result.Type == ExecutionResult.ResultType.Throw)
            {
                throw new Exception(interpreter.Stringify(result.Value));
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the statements begin with a "use strict" directive.
    /// </summary>
    private static bool CheckForUseStrict(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
            }
            else
            {
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// Binds 'this' to the given object. Only applicable for object method arrow functions.
    /// </summary>
    /// <param name="thisObject">The object to bind as 'this'.</param>
    /// <returns>A new SharpTSArrowFunction with 'this' bound in its closure.</returns>
    public SharpTSArrowFunction Bind(object thisObject)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", thisObject);
        return new SharpTSArrowFunction(_declaration, environment, isObjectMethod: true);
    }

    public override string ToString() => "<arrow fn>";
}
