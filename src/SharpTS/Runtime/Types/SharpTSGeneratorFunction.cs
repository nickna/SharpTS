using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for generator function declarations (function*).
/// </summary>
/// <remarks>
/// When called, this does NOT execute the function body immediately.
/// Instead, it returns a <see cref="SharpTSGenerator"/> instance that
/// lazily executes the body as next() is called.
/// </remarks>
/// <seealso cref="SharpTSGenerator"/>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSGeneratorFunction : ISharpTSGeneratorFunctionValue, IReceiverBindable
{
    private GeneratorFunctionProperties _functionProperties;
    GeneratorFunctionProperties ISharpTSGeneratorFunctionValue.FunctionProperties => _functionProperties;
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
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
    /// <summary>A dynamic receiver was bound (via <see cref="BindToReceiver"/>), so <see cref="Call"/>
    /// binds <c>this</c> to <see cref="_boundThis"/> rather than defaulting it to undefined.</summary>
    private readonly bool _thisBound;
    private readonly object? _boundThis;
    private readonly SharpTSClass? _boundSuper;

    /// <summary>
    /// True when this is a generator declaration lifted from a <c>HasOwnThis</c> generator expression /
    /// object generator method (#775). Such a generator binds its own dynamic <c>this</c>: when invoked
    /// as a method the receiver is bound (<see cref="BindToReceiver"/>), and a plain call defaults
    /// <c>this</c> to <c>undefined</c> rather than leaving it unbound.
    /// </summary>
    public bool HasDynamicThis => _declaration.HasDynamicThis;

    public SharpTSGeneratorFunction(Stmt.Function declaration, RuntimeEnvironment closure,
        bool thisBound = false, object? boundThis = null, SharpTSClass? boundSuper = null, Interpreter? creationRealm = null)
    {
        _declaration = declaration;
        _closure = closure;
        _thisBound = thisBound;
        _boundThis = boundThis;
        _boundSuper = boundSuper;
        _arity = declaration.IsPrivate ? declaration.Parameters.TakeWhile(p => p.DefaultValue == null && !p.IsRest).Count()
            : declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
        _functionProperties = new(isAsync: false,
            name: declaration.RuntimeName ?? declaration.Name.Lexeme, length: _arity, creationRealm: creationRealm);
    }

    public int Arity() => _arity;

    /// <summary>
    /// Creates a new generator instance. Does NOT execute the function body.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // The resolver places a class receiver scope between a method and its
        // enclosing closure. PrivateOwner also identifies public class methods.
        bool isClassMethod = PrivateOwner != null || _declaration.IsStatic;
        bool ownsStrictThis = isClassMethod || _declaration.IsPrivate;
        RuntimeEnvironment closure = isClassMethod
            ? new RuntimeEnvironment(_closure, strictMode: true) : _closure;
        RuntimeEnvironment environment = new(closure,
            strictMode: ownsStrictThis ? true : null)
            { PrivateClass = PrivateOwner, StaticSuper = _declaration.IsStatic };
        environment.NewTarget = RuntimeValue.Undefined;
        environment.ConstructionFrame = null;
        // #775: a generator expression / object generator method binds its own dynamic `this`. The bound
        // receiver (or globalThis for a plain call) is defined in the generator's invocation environment —
        // NOT a new parent scope inserted above the closure — so a captured enclosing-function local keeps
        // its lexical scope distance and still resolves. A plain call defaults `this` to undefined
        // (strict-mode function-expression semantics) rather than leaving it unbound.
        if (_thisBound)
        {
            environment.Define("this", _boundThis);
            if (_boundSuper != null)
                environment.Define("super", _boundSuper);
        }
        else if (ownsStrictThis)
        {
            environment.Define("this", SharpTSUndefined.Instance);
        }
        else if (_declaration.HasDynamicThis)
        {
            environment.Define("this", interpreter.GlobalThis); // sloppy-mode `this` = globalThis (#775)
        }
        // Capture at invocation, before defaults and before the lazy body can suspend.
        if (ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter,
                environment, _declaration.Parameters, _declaration.Body));
        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter, _declaration.Body);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters, _declaration.Body, environment, interpreter);

        // Return a generator that will execute the body lazily
        return new SharpTSGenerator(
            prepared.Statements, prepared.Environment, interpreter,
            _declaration.Name.Lexeme, _declaration)
        { Prototype = _functionProperties.GetIteratorPrototype(interpreter) };
    }

    /// <summary>
    /// Creates a bound version with 'this' set for method calls.
    /// </summary>
    public SharpTSGeneratorFunction Bind(SharpTSInstance instance)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: instance));

    /// <summary>
    /// Binds an arbitrary receiver (object-literal generator method / <c>.call</c> / <c>.apply</c>),
    /// not just a <see cref="SharpTSInstance"/> (#775).
    /// </summary>
    public ISharpTSCallable BindToReceiver(object receiver)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: receiver));

    public SharpTSGeneratorFunction BindStatic(SharpTSClass klass)
        => ShareObjectState(new(_declaration, _closure, thisBound: true, boundThis: klass,
            boundSuper: _declaration.IsStatic ? null : klass.Superclass));

    private SharpTSGeneratorFunction ShareObjectState(SharpTSGeneratorFunction bound)
    {
        bound._functionProperties = _functionProperties;
        bound.PrivateOwner = PrivateOwner;
        bound._computedName = _computedName;
        return bound;
    }

    public override string ToString() => $"<generator fn {_declaration.Name.Lexeme}>";
}

/// <summary>
/// Runtime wrapper for generator function expressions (function*() { } as expression).
/// </summary>
/// <remarks>
/// Similar to <see cref="SharpTSGeneratorFunction"/> but wraps an <see cref="Expr.ArrowFunction"/>
/// with IsGenerator=true instead of a <see cref="Stmt.Function"/>.
/// </remarks>
public class SharpTSArrowGeneratorFunction : ISharpTSGeneratorFunctionValue, IReceiverBindable
{
    // A receiver view keeps the named expression's original callable identity.
    private SharpTSArrowGeneratorFunction? _selfReference;
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
    /// Indicates whether this function has its own 'this' binding (function expressions)
    /// versus capturing 'this' from enclosing scope (arrow functions).
    /// </summary>
    public bool HasOwnThis { get; }

    public SharpTSArrowGeneratorFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool hasOwnThis = false,
        bool thisBound = false, object? boundThis = null, Interpreter? creationRealm = null)
    {
        _declaration = declaration;
        _closure = closure;
        HasOwnThis = hasOwnThis;
        _thisBound = thisBound;
        _boundThis = boundThis;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
        _functionProperties = new(isAsync: false,
            name: declaration.Name?.Lexeme ?? "", length: _arity, creationRealm: creationRealm);
    }

    public int Arity() => _arity;

    /// <summary>
    /// Creates a new generator instance. Does NOT execute the function body.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        RuntimeEnvironment environment = new(_closure);
        if (HasOwnThis)
        {
            environment.NewTarget = RuntimeValue.Undefined;
            environment.ConstructionFrame = null;
        }
        // Named generator function expression: bind self-reference alongside params.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, _selfReference ?? this);
        }
        // #775: a `function*` expression binds its own dynamic `this`. The bound receiver (or undefined
        // for a plain call) is defined in the invocation environment, NOT a parent scope, so a
        // captured enclosing-scope binding keeps its lexical scope distance.
        if (_thisBound)
            environment.Define("this", _boundThis);
        else if (HasOwnThis)
            environment.Define("this", interpreter.GlobalThis); // sloppy-mode `this` = globalThis (#775)
        if (HasOwnThis && ArgumentsUsage.UsesArguments(_declaration))
            environment.Define("arguments", SharpTSArguments.Create(arguments, interpreter,
                environment, _declaration.Parameters, _declaration.BlockBody));
        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter, _declaration.BlockBody);
        var prepared = ParameterBinder.PrepareBody(_declaration.Parameters, _declaration.BlockBody, environment, interpreter);

        // A generator function expression drives the same SharpTSGenerator as a declaration — only the
        // block body and the captured environment differ. Generator expressions always have a block
        // body (the parser never produces an expression-bodied generator). This native path runs the
        // generator expressions the GeneratorArrowLifter leaves in place because they close over a
        // block-scoped binding (#678); all others are lifted to declarations.
        return new SharpTSGenerator(
            prepared.Statements, prepared.Environment, interpreter,
            _declaration.Name?.Lexeme ?? "<generator>", _declaration)
        { Prototype = _functionProperties.GetIteratorPrototype(interpreter) };
    }

    /// <summary>
    /// Binds 'this' to the given object. Only applicable for function expressions with HasOwnThis=true.
    /// </summary>
    public SharpTSArrowGeneratorFunction Bind(object thisObject)
    {
        var bound = new SharpTSArrowGeneratorFunction(
            _declaration, _closure, hasOwnThis: true, thisBound: true, boundThis: thisObject);
        bound._functionProperties = _functionProperties;
        bound._selfReference = _selfReference ?? this;
        return bound;
    }

    /// <summary>Receiver-binding entry point for the method-call / <c>.call</c> / <c>.apply</c> path (#775).</summary>
    public ISharpTSCallable BindToReceiver(object receiver) => Bind(receiver);

    public override string ToString()
    {
        if (_declaration.Name != null)
            return $"<generator fn {_declaration.Name.Lexeme}>";
        return "<generator fn>";
    }
}
