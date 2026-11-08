using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime object representing an async generator function declaration.
/// </summary>
/// <remarks>
/// Created for declarations using both 'async' and 'function*' syntax.
/// When called, instantiates a <see cref="SharpTSAsyncGenerator"/>.
/// Async generators yield Promises and can use 'await' internally.
/// </remarks>
/// <seealso cref="SharpTSAsyncGenerator"/>
/// <seealso cref="SharpTSGeneratorFunction"/>
public class SharpTSAsyncGeneratorFunction : ISharpTSGeneratorFunctionValue, IReceiverBindable
{
    private GeneratorFunctionProperties _functionProperties;
    GeneratorFunctionProperties ISharpTSGeneratorFunctionValue.FunctionProperties => _functionProperties;
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private SharpTSAsyncGeneratorFunction? _selfReference;
    private readonly int _arity;
    internal bool IsStaticMethod => _declaration.IsStatic;
    internal SharpTSClass? PrivateOwner { get; set; }
    SharpTSObject ISharpTSFunctionObject.OwnProperties => _functionProperties.OwnProperties;
    private string? _computedName;
    internal string? ComputedName
    {
        get => _computedName;
        set
        {
            _computedName = value;
            _functionProperties.OwnProperties.DefineProperty("name", new SharpTSPropertyDescriptor(
                value ?? _declaration.RuntimeName ?? _declaration.Name.Lexeme,
                writable: false, enumerable: false, configurable: true));
        }
    }
    /// <summary>A dynamic receiver was bound, so <see cref="Call"/> binds <c>this</c> to
    /// <see cref="_boundThis"/> rather than defaulting it to undefined.</summary>
    private readonly bool _thisBound;
    private readonly object? _boundThis;
    private readonly SharpTSClass? _boundSuper;

    /// <summary>
    /// True when this is an async generator declaration lifted from a <c>HasOwnThis</c> async generator
    /// expression / object method (#775). Binds its own dynamic <c>this</c>.
    /// </summary>
    public bool HasDynamicThis => _declaration.HasDynamicThis;

    public SharpTSAsyncGeneratorFunction(Stmt.Function declaration, RuntimeEnvironment closure,
        bool thisBound = false, object? boundThis = null, SharpTSClass? boundSuper = null, Interpreter? creationRealm = null)
    {
        _declaration = declaration;
        _closure = closure;
        _thisBound = thisBound;
        _boundThis = boundThis;
        _boundSuper = boundSuper;
        _arity = declaration.IsPrivate ? declaration.Parameters.TakeWhile(p => p.DefaultValue == null && !p.IsRest).Count()
            : declaration.Parameters?.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional) ?? 0;
        _functionProperties = new(isAsync: true,
            name: declaration.RuntimeName ?? declaration.Name.Lexeme, length: _arity, creationRealm: creationRealm);
    }

    public int Arity() => _arity;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Match the resolver's one class receiver scope without adding a
        // parent scope to ordinary or lifted object generator declarations.
        bool isClassMethod = PrivateOwner != null || _declaration.IsStatic;
        bool hasStrictDirective = DirectivePrologue.HasUseStrict(_declaration.Body);
        RuntimeEnvironment closure = isClassMethod
            ? new RuntimeEnvironment(_closure, strictMode: true) : _closure;
        RuntimeEnvironment environment = new(closure,
            strictMode: isClassMethod || _declaration.IsPrivate || hasStrictDirective ? true : null)
            { PrivateClass = PrivateOwner, StaticSuper = _declaration.IsStatic };
        environment.NewTarget = RuntimeValue.Undefined;
        environment.ConstructionFrame = null;

        // Every generator declaration binds its own receiver without changing the closure chain.
        if (_thisBound)
        {
            environment.Define("this", _boundThis);
            if (_boundSuper != null)
                environment.Define("super", _boundSuper);
        }
        else
        {
            environment.Define("this",
                environment.IsStrictMode ? SharpTSUndefined.Instance : (object?)interpreter.GlobalThis);
        }

        // Snapshot before defaults and before the lazy async generator is returned.
        if (ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter, _selfReference ?? this,
                environment, _declaration.Parameters, _declaration.Body));
        // Bind parameters to arguments
        ParameterBinder.Bind(_declaration.Parameters ?? [], arguments, environment, interpreter, _declaration.Body);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters ?? [], _declaration.Body, environment, interpreter);

        // Return the async generator object (not yet started). It drives the same SharpTSAsyncGenerator
        // as a function expression — only the body and captured environment differ.
        return new SharpTSAsyncGenerator(
            prepared.Statements, prepared.Environment, interpreter,
            _declaration.Name?.Lexeme ?? "<async generator>", _declaration)
        { Prototype = _functionProperties.GetIteratorPrototype(interpreter) };
    }

    /// <summary>
    /// Creates a bound version with 'this' set for method calls.
    /// </summary>
    public SharpTSAsyncGeneratorFunction Bind(SharpTSInstance instance)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: instance));

    /// <summary>Binds an arbitrary receiver (object async generator method / <c>.call</c> / <c>.apply</c>) (#775).</summary>
    public ISharpTSCallable BindToReceiver(object receiver)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: receiver));

    public SharpTSAsyncGeneratorFunction BindStatic(SharpTSClass klass)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: klass,
            boundSuper: _declaration.IsStatic ? null : klass.Superclass));

    private SharpTSAsyncGeneratorFunction ShareObjectState(SharpTSAsyncGeneratorFunction bound)
    {
        bound._selfReference = _selfReference ?? this;
        bound._functionProperties = _functionProperties;
        bound.PrivateOwner = PrivateOwner;
        bound._computedName = _computedName;
        return bound;
    }

    public override string ToString() => $"[async function* {_declaration.Name?.Lexeme ?? "anonymous"}]";
}

/// <summary>
/// Runtime wrapper for async generator function EXPRESSIONS (<c>async function*() { }</c> as an
/// expression). The async analogue of <see cref="SharpTSArrowGeneratorFunction"/>.
/// </summary>
/// <remarks>
/// Wraps an <see cref="Expr.ArrowFunction"/> with <c>IsAsync = true</c> and <c>IsGenerator = true</c>
/// instead of a <see cref="Stmt.Function"/>. <see cref="GeneratorArrowLifter"/> lifts most async
/// generator expressions to declarations, but leaves in place those that close over a block-scoped
/// binding (loop variable, catch parameter, nested-block <c>let</c>/<c>const</c>); this native path runs
/// those (#734). Drives the same <see cref="SharpTSAsyncGenerator"/> as a declaration.
/// </remarks>
/// <seealso cref="SharpTSAsyncGenerator"/>
/// <seealso cref="SharpTSArrowGeneratorFunction"/>
public class SharpTSAsyncArrowGeneratorFunction : ISharpTSGeneratorFunctionValue, IReceiverBindable
{
    // A receiver view keeps the named expression's original callable identity.
    private SharpTSAsyncArrowGeneratorFunction? _selfReference;
    private GeneratorFunctionProperties _functionProperties;
    GeneratorFunctionProperties ISharpTSGeneratorFunctionValue.FunctionProperties => _functionProperties;
    private readonly Expr.ArrowFunction _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    SharpTSObject ISharpTSFunctionObject.OwnProperties => _functionProperties.OwnProperties;
    /// <summary>A dynamic receiver was bound (via <see cref="Bind"/>), so <see cref="Call"/> binds
    /// <c>this</c> to <see cref="_boundThis"/> rather than defaulting it to undefined.</summary>
    private readonly bool _thisBound;
    private readonly object? _boundThis;

    /// <summary>
    /// Whether this has its own <c>this</c> binding (a <c>function*</c> expression) rather than
    /// capturing <c>this</c> from the enclosing scope (an arrow — which cannot be a generator anyway).
    /// </summary>
    public bool HasOwnThis { get; }

    public SharpTSAsyncArrowGeneratorFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool hasOwnThis = false,
        bool thisBound = false, object? boundThis = null, Interpreter? creationRealm = null)
    {
        _declaration = declaration;
        _closure = closure;
        HasOwnThis = hasOwnThis;
        _thisBound = thisBound;
        _boundThis = boundThis;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
        _functionProperties = new(isAsync: true,
            name: declaration.Name?.Lexeme ?? "", length: _arity, creationRealm: creationRealm);
    }

    public int Arity() => _arity;

    /// <summary>Creates a new async generator instance. Does NOT execute the function body.</summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        if (HasOwnThis)
        {
            environment.NewTarget = RuntimeValue.Undefined;
            environment.ConstructionFrame = null;
        }
        // Named async generator function expression: bind self-reference alongside params.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, _selfReference ?? this);
        }
        // #775: a `function*` expression binds its own dynamic `this`; the bound receiver (or globalThis for
        // a plain call) is defined in the invocation environment, NOT a parent scope.
        if (_thisBound)
            environment.Define("this", _boundThis);
        else if (HasOwnThis)
            environment.Define("this", interpreter.GlobalThis); // sloppy-mode `this` = globalThis (#775)
        if (HasOwnThis && ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter, _selfReference ?? this,
                environment, _declaration.Parameters, _declaration.BlockBody));
        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter, _declaration.BlockBody);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters, _declaration.BlockBody, environment, interpreter);

        // Generator expressions always have a block body (the parser never produces an
        // expression-bodied generator).
        return new SharpTSAsyncGenerator(
            prepared.Statements, prepared.Environment, interpreter,
            _declaration.Name?.Lexeme ?? "<async generator>", _declaration)
        { Prototype = _functionProperties.GetIteratorPrototype(interpreter) };
    }

    /// <summary>Binds <c>this</c>. Only applicable for function expressions with HasOwnThis=true.</summary>
    public SharpTSAsyncArrowGeneratorFunction Bind(object thisObject)
    {
        var bound = new SharpTSAsyncArrowGeneratorFunction(
            _declaration, _closure, hasOwnThis: true, thisBound: true, boundThis: thisObject);
        bound._functionProperties = _functionProperties;
        bound._selfReference = _selfReference ?? this;
        return bound;
    }

    /// <summary>Receiver-binding entry point for the method-call / <c>.call</c> / <c>.apply</c> path (#775).</summary>
    public ISharpTSCallable BindToReceiver(object receiver) => Bind(receiver);

    public override string ToString() => $"[async function* {_declaration.Name?.Lexeme ?? "anonymous"}]";
}
