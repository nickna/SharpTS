using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
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
public class SharpTSAsyncFunction : ISharpTSAsyncCallable, ISharpTSFunctionObject, ITypeCategorized
{
    // JS async functions are functions — categorize as Function so member
    // access routes through FunctionBuiltIns (call/apply/bind/length/name).
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    internal bool IsStaticMethod => _declaration.IsStatic;
    internal SharpTSClass? PrivateOwner { get; set; }
    private readonly bool _receiverBound;
    // Ordinary functions bind this in their existing activation so lexical distances
    // stay unchanged. Class methods retain their resolver-counted receiver scope.
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;
    internal bool HasBoundReceiver => _receiverBound || _hasBoundThis;
    // Receiver views retain one descriptor-backed function object, including
    // the absence of configurable metadata after deletion.
    private SharpTSObject _properties;
    SharpTSObject ISharpTSFunctionObject.OwnProperties => _properties;

    public SharpTSAsyncFunction(Stmt.Function declaration, RuntimeEnvironment closure, bool receiverBound = false)
    {
        _declaration = declaration;
        _closure = closure;
        _receiverBound = receiverBound;
        _arity = declaration.IsPrivate ? declaration.Parameters.TakeWhile(p => p.DefaultValue == null && !p.IsRest).Count()
            : declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
        _properties = FunctionObjectProperties.Create(declaration.RuntimeName ?? declaration.Name.Lexeme, _arity);
    }

    private SharpTSAsyncFunction(SharpTSAsyncFunction source, RuntimeEnvironment closure)
    {
        _declaration = source._declaration;
        _closure = closure;
        _arity = source._arity;
        _receiverBound = true;
        _properties = source._properties;
        PrivateOwner = source.PrivateOwner;
    }

    private SharpTSAsyncFunction(SharpTSAsyncFunction source, object? receiver)
    {
        _declaration = source._declaration;
        _closure = source._closure;
        _arity = source._arity;
        _boundThis = receiver;
        _hasBoundThis = true;
        _properties = source._properties;
        PrivateOwner = source.PrivateOwner;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.Fields.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    internal bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _properties.DefineProperty(name, descriptor);

    public int Arity() => _arity;

    /// <summary>
    /// Invokes the async function, returning a Promise immediately.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
        => StartInvocation(interpreter, () => ExecuteBodyAsync(interpreter, arguments));

    /// <summary>
    /// Asynchronously invokes the function and returns its adopted result.
    /// </summary>
    public Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
        => StartInvocation(interpreter, () => ExecuteBodyAsync(interpreter, arguments)).Task;

    internal static SharpTSPromise StartInvocation(
        Interpreter interpreter, Func<Task<object?>> executeBody)
    {
        // Allocate the exposed capability before running any guest code. Every
        // recursive return-adoption step must resolve this exact public task so
        // a thenable resolving with the current async result rejects itself.
        // Inline settlement keeps continuations visible to the event loop.
        var completion = new TaskCompletionSource<object?>();
        var promise = new SharpTSPromise(completion.Task);

        // An async function runs synchronously until its first suspension, but calling it
        // still returns to the caller's lexical environment immediately. ExecuteBlockAsync
        // keeps the function environment installed across awaits so its continuation can
        // resume correctly; restore the caller here before sibling expressions (notably a
        // chained .then(...) argument) are evaluated and capture the wrong closure.
        RuntimeEnvironment callerEnvironment = interpreter.Environment;
        try
        {
            _ = CompleteInvocationAsync(interpreter, completion, executeBody);
        }
        finally
        {
            interpreter.SetEnvironment(callerEnvironment);
        }
        return promise;
    }

    private static async Task CompleteInvocationAsync(
        Interpreter interpreter,
        TaskCompletionSource<object?> completion,
        Func<Task<object?>> executeBody)
    {
        try
        {
            object? value = await executeBody();
            // Body catches/finally have finished before reading the final
            // value's then. Its assimilation belongs to the public promise.
            PromiseBuiltIns.ResolveThenableIntoCapability(interpreter, completion, value);
        }
        catch (SharpTSPromiseRejectedException rejected)
        {
            completion.TrySetException(rejected);
        }
        catch (ThrowException thrown)
        {
            completion.TrySetException(new SharpTSPromiseRejectedException(thrown.Value));
        }
        catch (Exception ex)
        {
            object? reason = interpreter.CoerceCaughtValueForBinding(
                interpreter.TranslateException(ex));
            completion.TrySetException(new SharpTSPromiseRejectedException(reason));
        }
    }

    private async Task<object?> ExecuteBodyAsync(Interpreter interpreter, List<object?> arguments)
    {
        // Bound async methods already carry the resolver's class `this` scope.
        // Canonical methods invoked without a receiver need the same scope depth.
        bool isClassMethod = PrivateOwner is not null || _declaration.IsStatic;
        RuntimeEnvironment closure = isClassMethod && !_receiverBound
            ? new RuntimeEnvironment(_closure, strictMode: true) : _closure;
        RuntimeEnvironment environment = new(closure,
            strictMode: isClassMethod || _declaration.IsPrivate ? true : null)
            { PrivateClass = PrivateOwner, StaticSuper = _declaration.IsStatic };
        environment.NewTarget = RuntimeValue.Undefined;
        environment.ConstructionFrame = null;
        if (_hasBoundThis)
            environment.Define("this", _boundThis);
        else if (isClassMethod || _declaration.IsPrivate)
        {
            environment.Define("this", _receiverBound && _closure.TryGet("this", out var receiver)
                ? receiver.ToObject() : SharpTSUndefined.Instance);
        }
        // Snapshot the caller values before defaults; this binding survives every await.
        if (ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter));
        await ParameterBinder.BindAsync(_declaration.Parameters, arguments, environment, interpreter, _declaration.Body);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters, _declaration.Body, environment);

        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        using var debugFrame = interpreter.EnterDebugFrame(
            _declaration.Name.Lexeme, prepared.Environment, _declaration);
        var result = await interpreter.ExecuteBlockAsync(prepared.Statements, prepared.Environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            return result.Value.ToObject();
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

        return new SharpTSAsyncFunction(this, environment);
    }

    /// <summary>
    /// Rebinds <c>this</c> to an arbitrary value for <c>fn.call/apply/bind</c>.
    /// Unlike <see cref="Bind(SharpTSInstance)"/> (used for method binding), the
    /// receiver here may be any runtime value (a plain object, dictionary, etc.).
    /// </summary>
    public SharpTSAsyncFunction BindThisValue(object? thisObject)
    {
        if (PrivateOwner is null && !_declaration.IsStatic)
            return new SharpTSAsyncFunction(this, thisObject);

        RuntimeEnvironment closure = _receiverBound ? _closure.Enclosing! : _closure;
        RuntimeEnvironment environment = new(closure);
        environment.Define("this", thisObject);
        return new SharpTSAsyncFunction(this, environment);
    }

    public SharpTSAsyncFunction BindStatic(SharpTSClass klass)
    {
        if (_declaration.IsStatic) return BindThisValue(klass);
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", klass);
        if (klass.Superclass != null)
            environment.Define("super", klass.Superclass);
        return new SharpTSAsyncFunction(this, environment);
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
public class SharpTSAsyncArrowFunction : ISharpTSAsyncCallable, ISharpTSFunctionObject, ITypeCategorized
{
    // JS async arrows / async function expressions are functions — categorize
    // as Function so member access routes through FunctionBuiltIns.
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Expr.ArrowFunction _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    // Receiver views keep the resolver's original closure depth.
    private readonly SharpTSAsyncArrowFunction? _selfReference;
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;
    private SharpTSObject _properties;
    SharpTSObject ISharpTSFunctionObject.OwnProperties => _properties;

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
        _properties = FunctionObjectProperties.Create(declaration.RuntimeName ?? declaration.Name?.Lexeme ?? "", _arity);
    }

    private SharpTSAsyncArrowFunction(SharpTSAsyncArrowFunction source, object receiver)
    {
        _selfReference = source._selfReference ?? source;
        _declaration = source._declaration;
        _closure = source._closure;
        _boundThis = receiver;
        _hasBoundThis = true;
        _arity = source._arity;
        HasOwnThis = true;
        _properties = source._properties;
    }

    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.Fields.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    public int Arity() => _arity;

    /// <summary>
    /// Invokes the async arrow function, returning a Promise immediately.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
        => SharpTSAsyncFunction.StartInvocation(interpreter, () => ExecuteBodyAsync(interpreter, arguments));

    /// <summary>
    /// Asynchronously invokes the arrow function and returns its adopted result.
    /// </summary>
    public Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
        => SharpTSAsyncFunction.StartInvocation(interpreter, () => ExecuteBodyAsync(interpreter, arguments)).Task;

    private async Task<object?> ExecuteBodyAsync(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        if (HasOwnThis)
        {
            environment.NewTarget = RuntimeValue.Undefined;
            environment.ConstructionFrame = null;
        }
        // Named async function expression: bind self-reference alongside params.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, _selfReference ?? this);
        }
        // Install the receiver before defaults without inserting a lexical scope.
        if (_hasBoundThis)
            environment.Define("this", _boundThis);
        // Function expressions own arguments; true arrows retain the enclosing binding.
        if (HasOwnThis && ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter));
        await ParameterBinder.BindAsync(_declaration.Parameters, arguments, environment, interpreter, _declaration.BlockBody);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters, _declaration.BlockBody, environment);

        using var debugFrame = interpreter.EnterDebugFrame(
            _declaration.Name?.Lexeme ?? "<async arrow>", prepared.Environment, _declaration);
        if (_declaration.ExpressionBody != null)
        {
            RuntimeEnvironment previous = interpreter.Environment;
            try
            {
                interpreter.SetEnvironment(prepared.Environment);
                return (await interpreter.EvaluateAsync(_declaration.ExpressionBody)).ToObject();
            }
            finally
            {
                interpreter.SetEnvironment(previous);
            }
        }
        else if (_declaration.BlockBody != null)
        {
            var result = await interpreter.ExecuteBlockAsync(prepared.Statements, prepared.Environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                return result.Value.ToObject();
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
        return new SharpTSAsyncArrowFunction(this, thisObject);
    }

    public override string ToString() => "<async arrow fn>";
}
