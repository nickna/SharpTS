using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;
using SharpTS.TypeSystem;

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
public class SharpTSAsyncFunction : ISharpTSAsyncCallable, ITypeCategorized
{
    // JS async functions are functions — categorize as Function so member
    // access routes through FunctionBuiltIns (call/apply/bind/length/name).
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    // JS: functions (including async) are objects and support property assignment.
    private Dictionary<string, object?>? _properties;

    public SharpTSAsyncFunction(Stmt.Function declaration, RuntimeEnvironment closure)
    {
        _declaration = declaration;
        _closure = closure;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties != null && _properties.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        _properties ??= [];
        _properties[name] = value;
    }

    public int Arity() => _arity;

    /// <summary>
    /// Invokes the async function, returning a Promise immediately.
    /// The actual execution happens asynchronously.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // An async function runs synchronously until its first suspension, but calling it
        // still returns to the caller's lexical environment immediately. ExecuteBlockAsync
        // keeps the function environment installed across awaits so its continuation can
        // resume correctly; restore the caller here before sibling expressions (notably a
        // chained .then(...) argument) are evaluated and capture the wrong closure.
        RuntimeEnvironment callerEnvironment = interpreter.Environment;
        try
        {
            var task = NormalizePromiseRejection(CallAsync(interpreter, arguments), interpreter);
            return new SharpTSPromise(task);
        }
        finally
        {
            interpreter.SetEnvironment(callerEnvironment);
        }
    }

    internal static async Task<object?> NormalizePromiseRejection(
        Task<object?> task,
        Interpreter interpreter)
    {
        try
        {
            return await task;
        }
        catch (SharpTSPromiseRejectedException)
        {
            throw;
        }
        catch (ThrowException thrown)
        {
            throw new SharpTSPromiseRejectedException(thrown.Value);
        }
        catch (Exception ex)
        {
            object? reason = interpreter.CoerceCaughtValueForBinding(
                interpreter.TranslateException(ex));
            throw new SharpTSPromiseRejectedException(reason);
        }
    }

    /// <summary>
    /// Asynchronously executes the function body.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        await ParameterBinder.BindAsync(_declaration.Parameters, arguments, environment, interpreter);

        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        using var debugFrame = interpreter.EnterDebugFrame(
            _declaration.Name.Lexeme, environment, _declaration);
        var result = await interpreter.ExecuteBlockAsync(_declaration.Body, environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            // Unwrap Promise if returning a Promise from async function
            return await SharpTSPromise.UnwrapIfPromise(result.Value.ToObject());
        }
        if (result.Type == ExecutionResult.ResultType.Throw)
        {
            // Propagate the original throw value through ThrowException — see
            // SharpTSFunction.Call for the full rationale.
            throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
        }

        // Falling off the end of the body completes the async function with `undefined`, not
        // null (#587). A bare `return;` and `return <expr>` both take the ResultType.Return
        // path above (ExecuteReturnAsyncVT maps a value-less return to the undefined sentinel),
        // so `return null;` still resolves with null and `return;` with undefined.
        return SharpTSUndefined.Instance;
    }

    public SharpTSAsyncFunction Bind(SharpTSInstance instance)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", instance);

        // Propagate 'super' from closure if present (needed for async methods in derived classes)
        try
        {
            var superclass = _closure.Get(new Parsing.Token(Parsing.TokenType.SUPER, "super", null, 0));
            if (superclass != null)
                environment.Define("super", superclass);
        }
        catch
        {
            // 'super' not in scope - ignore
        }

        return new SharpTSAsyncFunction(_declaration, environment);
    }

    /// <summary>
    /// Rebinds <c>this</c> to an arbitrary value for <c>fn.call/apply/bind</c>.
    /// Unlike <see cref="Bind(SharpTSInstance)"/> (used for method binding), the
    /// receiver here may be any runtime value (a plain object, dictionary, etc.).
    /// </summary>
    public SharpTSAsyncFunction BindThisValue(object? thisObject)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", thisObject);
        return new SharpTSAsyncFunction(_declaration, environment);
    }

    public SharpTSAsyncFunction BindStatic(SharpTSClass klass)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", klass);
        if (klass.Superclass != null)
            environment.Define("super", klass.Superclass);
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
/// For arrow functions (<c>HasOwnThis=false</c>), <c>this</c> is captured from the enclosing scope.
/// For async function expressions (<c>HasOwnThis=true</c>), <c>this</c> is bound at call time.
/// </remarks>
public class SharpTSAsyncArrowFunction : ISharpTSAsyncCallable, ITypeCategorized
{
    // JS async arrows / async function expressions are functions — categorize
    // as Function so member access routes through FunctionBuiltIns.
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Expr.ArrowFunction _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    // JS: async arrows are objects and support property assignment.
    private Dictionary<string, object?>? _properties;

    /// <summary>
    /// Indicates whether this function has its own 'this' binding (function expressions)
    /// versus capturing 'this' from enclosing scope (arrow functions).
    /// </summary>
    public bool HasOwnThis { get; }

    public SharpTSAsyncArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool hasOwnThis = false)
    {
        _declaration = declaration;
        _closure = closure;
        HasOwnThis = hasOwnThis;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties != null && _properties.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    public void SetProperty(string name, object? value)
    {
        _properties ??= [];
        _properties[name] = value;
    }

    public int Arity() => _arity;

    /// <summary>
    /// Invokes the async arrow function, returning a Promise immediately.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Mirror SharpTSAsyncFunction.Call: a pending async arrow owns its suspended
        // environment, but must not leave that environment ambient in its caller.
        RuntimeEnvironment callerEnvironment = interpreter.Environment;
        try
        {
            var task = SharpTSAsyncFunction.NormalizePromiseRejection(
                CallAsync(interpreter, arguments), interpreter);
            return new SharpTSPromise(task);
        }
        finally
        {
            interpreter.SetEnvironment(callerEnvironment);
        }
    }

    /// <summary>
    /// Asynchronously executes the arrow function.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        // Named async function expression: bind self-reference alongside params.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, this);
        }
        await ParameterBinder.BindAsync(_declaration.Parameters, arguments, environment, interpreter);

        using var debugFrame = interpreter.EnterDebugFrame(
            _declaration.Name?.Lexeme ?? "<async arrow>", environment, _declaration);
        if (_declaration.ExpressionBody != null)
        {
            RuntimeEnvironment previous = interpreter.Environment;
            try
            {
                interpreter.SetEnvironment(environment);
                object? result = (await interpreter.EvaluateAsync(_declaration.ExpressionBody)).ToObject();
                // Unwrap Promise if returning a Promise from async arrow
                return await SharpTSPromise.UnwrapIfPromise(result);
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
                return await SharpTSPromise.UnwrapIfPromise(result.Value.ToObject());
            }
            if (result.Type == ExecutionResult.ResultType.Throw)
            {
                // Propagate the original throw value through ThrowException — see
            // SharpTSFunction.Call for the full rationale.
            throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
            }
        }

        // A block-bodied async arrow that runs off the end completes with `undefined`, not null
        // (#587). Bare/expression returns take the ResultType.Return path above; expression-
        // bodied arrows always return their value above, so this is the off-the-end default.
        return SharpTSUndefined.Instance;
    }

    public SharpTSAsyncArrowFunction Bind(object thisObject)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", thisObject);
        return new SharpTSAsyncArrowFunction(_declaration, environment, hasOwnThis: true);
    }

    public override string ToString() => "<async arrow fn>";
}
