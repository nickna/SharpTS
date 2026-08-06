using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;
using SharpTS.TypeSystem;

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

    /// <summary>
    /// Invokes the callable with RuntimeValue arguments, avoiding boxing for
    /// implementations that override it. The default bridges to the boxed
    /// <see cref="Call"/> so unmigrated implementors work unchanged.
    /// </summary>
    RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        var boxed = new List<object?>(arguments.Length);
        foreach (var arg in arguments)
            boxed.Add(arg.ToObject());
        return RuntimeValue.FromBoxed(Call(interpreter, boxed));
    }
}

/// <summary>
/// Runtime wrapper for named function declarations.
/// </summary>
/// <remarks>
/// Wraps a <see cref="Stmt.Function"/> AST node along with its closure environment.
/// Handles parameter binding (including default values and rest parameters),
/// executes the function body, and propagates return values via <see cref="ExecutionResult"/>.
/// The <see cref="Bind"/> method creates a new function with <c>this</c> bound for method calls.
/// </remarks>
/// <seealso cref="SharpTSArrowFunction"/>
/// <seealso cref="RuntimeEnvironment"/>
public class SharpTSFunction : ISharpTSCallable, ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    // `this` value stored on the function itself (from BindThis) rather than in an
    // extra closure scope — otherwise the resolver's scope-distance count wouldn't
    // match the runtime chain and outer-variable captures would be off-by-one.
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;
    // Functions are ordinary objects for their user-defined string properties.
    // Reuse the descriptor-aware object store for assignment, accessors, and
    // Object.defineProperty invariants.
    private SharpTSObject _properties = new([]);

    public SharpTSFunction(Stmt.Function declaration, RuntimeEnvironment closure)
        : this(declaration, closure, boundThis: null, hasBoundThis: false)
    {
    }

    private SharpTSFunction(Stmt.Function declaration, RuntimeEnvironment closure, object? boundThis, bool hasBoundThis)
    {
        _declaration = declaration;
        _closure = closure;
        _boundThis = boundThis;
        _hasBoundThis = hasBoundThis;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    /// <summary>JS function-as-object property access.</summary>
    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.Fields.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    /// <summary>Returns the names of JS user-assigned properties on this function
    /// (not built-in members like name/length/bind). Used by for...in and Object.keys —
    /// lodash enumerates its own utility map by iterating `for (var key in lodash)`.</summary>
    public IEnumerable<string> PropertyKeys => _properties.OwnEnumerableKeys();

    /// <summary>Sets a JS-object property on this function.</summary>
    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);
    public bool HasProperty(string name)
        => _properties.HasProperty(name) || _properties.HasSetter(name);
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _properties.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.GetOwnPropertyDescriptor(name);
    public bool IsPropertyEnumerable(string name)
        => _properties.GetOwnPropertyDescriptor(name) is { Enumerable: true };
    internal void FreezeOwnProperties() => _properties.Freeze();
    internal void SealOwnProperties() => _properties.Seal();

    // JS functions are objects — they accept symbol-keyed property
    // assignment too (`fn[Symbol.species] = ...`). Without per-instance
    // symbol storage, the spec patterns that install Symbol.species on
    // a constructor function (test262 RegExp Symbol.split species-* tests)
    // can't round-trip the value.
    private Dictionary<SharpTSSymbol, object?>? _symbolProperties;

    /// <summary>Sets a symbol-keyed property on this function.</summary>
    public void SetBySymbol(SharpTSSymbol key, object? value)
    {
        _symbolProperties ??= [];
        _symbolProperties[key] = value;
    }

    /// <summary>Reads a symbol-keyed property; returns true and the value
    /// when one is registered. Used by SpeciesConstructor lookup, etc.</summary>
    public bool TryGetSymbolProperty(SharpTSSymbol key, out object? value)
    {
        if (_symbolProperties != null && _symbolProperties.TryGetValue(key, out value))
            return true;
        value = null;
        return false;
    }

    // Symbol-keyed accessor pairs from `Object.defineProperty(fn, sym, {get, set})`.
    // Test262 RegExp Symbol.split/.../species-ctor-species-get-err.js installs a
    // throwing getter on Symbol.species via this path; the spec-driven
    // SpeciesConstructor lookup must invoke that getter and propagate the throw.
    private Dictionary<SharpTSSymbol, (ISharpTSCallable? Get, ISharpTSCallable? Set)>? _symbolAccessors;

    /// <summary>Installs a symbol-keyed accessor pair from defineProperty.</summary>
    public void DefineSymbolAccessor(SharpTSSymbol key, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _symbolAccessors ??= [];
        _symbolAccessors[key] = (getter, setter);
    }

    /// <summary>Returns the accessor pair for the symbol if defined.</summary>
    public bool TryGetSymbolAccessor(SharpTSSymbol key, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_symbolAccessors != null && _symbolAccessors.TryGetValue(key, out var pair))
        {
            getter = pair.Get;
            setter = pair.Set;
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>Removes a JS-object property from this function.</summary>
    public bool DeleteProperty(string name)
        => _properties.DeleteProperty(name);

    /// <summary>
    /// Defines a property with a getter and/or setter on this function.
    /// Supports <c>Object.defineProperty(fn, 'name', { get, set })</c>.
    /// </summary>
    public void DefineAccessor(string name, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _properties.DefineProperty(name, new SharpTSPropertyDescriptor
        {
            Get = getter,
            Set = setter,
            HasGet = true,
            HasSet = true,
            Configurable = true,
        });
    }

    /// <summary>Returns the accessor pair for <paramref name="name"/> if defined.</summary>
    public bool TryGetAccessor(string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_properties.HasGetter(name) || _properties.HasSetter(name))
        {
            getter = _properties.GetGetter(name);
            setter = _properties.GetSetter(name);
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    public int Arity() => _arity;

    internal bool IsStrict
        => _closure.IsStrictMode
            || (_declaration.Body is not null
                && Parsing.DirectivePrologue.HasUseStrict(_declaration.Body));

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        // Check for function-level "use strict" directive
        bool functionStrict = IsStrict;
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        // JS calling convention: if `this` isn't bound by a receiver
        // (bare call `foo()`), it defaults to the global object in
        // sloppy mode and to `undefined` in strict mode. A receiver set
        // via BindThis is stored on the function itself (not an extra
        // closure scope) so scope distances stay aligned with the resolver.
        if (_hasBoundThis)
        {
            environment.Define("this", _boundThis);
        }
        else if (!_closure.TryGet("this", out _))
        {
            environment.Define("this",
                functionStrict ? SharpTSUndefined.Instance : (object?)interpreter.GlobalThis);
        }

        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter);
        // Bind the JS-spec `arguments` array-like to the current call's args.
        // Arrow functions do NOT bind `arguments` — they inherit from the
        // enclosing non-arrow function (handled by SharpTSArrowFunction).
        // Materialized only when the body can observe it (ArgumentsUsage scan).
        if (ArgumentsUsage.UsesArguments(_declaration))
        {
            environment.Define("arguments", new SharpTSArguments(arguments));
        }

        var result = interpreter.ExecuteBlock(_declaration.Body, environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            return result.Value.ToObject();
        }
        if (result.Type == ExecutionResult.ResultType.Throw)
        {
            // Preserve the original thrown value so the outer catch receives the actual
            // Error/object. Wrapping in `new Exception(Stringify(...))` flattens it to a
            // string, which breaks `e.message`/`e.name` at any .NET-interop boundary
            // (delegate callbacks, reflected calls) where the error can't round-trip
            // through ExecutionResult.
            throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
        }

        // A function that completes without an explicit `return <expr>` evaluates
        // to JS `undefined`, not `null`. Return the sentinel (matching CallV2's
        // RuntimeValue.Undefined) so getters/private methods invoked via this
        // boxed path observe `undefined` for off-the-end completion (#603).
        return SharpTSUndefined.Instance;
    }

    public SharpTSFunction Bind(SharpTSInstance instance)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", instance);

        // Propagate 'super' from closure if present (needed for methods in derived classes)
        // Use TryGet to avoid exceptions - 'super' may not be in scope for non-derived classes
        if (_closure.TryGet("super", out var superclass) && superclass != null)
        {
            environment.Define("super", superclass);
        }

        return ShareObjectState(new SharpTSFunction(_declaration, environment));
    }

    /// <summary>
    /// Binds this function to a class receiver (for static methods and static
    /// accessors, where `this` should refer to the class itself).
    /// </summary>
    public SharpTSFunction BindStatic(SharpTSClass klass)
    {
        RuntimeEnvironment environment = new(_closure);
        environment.Define("this", klass);
        if (klass.Superclass != null)
        {
            environment.Define("super", klass.Superclass);
        }
        return ShareObjectState(new SharpTSFunction(_declaration, environment));
    }

    /// <summary>
    /// Binds this function to an arbitrary `this` value. Used by `new Func()`
    /// to bind the fresh instance, and by `Function.prototype.call/apply`.
    /// </summary>
    public SharpTSFunction BindThis(object? thisValue)
    {
        return ShareObjectState(new SharpTSFunction(
            _declaration, _closure, boundThis: thisValue, hasBoundThis: true));
    }

    private SharpTSFunction ShareObjectState(SharpTSFunction bound)
    {
        _symbolProperties ??= [];
        _symbolAccessors ??= [];
        bound._properties = _properties;
        bound._symbolProperties = _symbolProperties;
        bound._symbolAccessors = _symbolAccessors;
        return bound;
    }

    /// <summary>
    /// V2 call path — avoids boxing at parameter and return boundaries.
    /// </summary>
    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        if (_declaration.Body == null)
        {
            throw new Exception($"Cannot invoke abstract method '{_declaration.Name.Lexeme}'.");
        }

        bool functionStrict = IsStrict;
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        // See Call() for rationale on bound-this and default-this.
        if (_hasBoundThis)
        {
            environment.Define("this", _boundThis);
        }
        else if (!_closure.TryGet("this", out _))
        {
            environment.Define("this",
                functionStrict ? SharpTSUndefined.Instance : (object?)interpreter.GlobalThis);
        }

        ParameterBinder.BindRV(_declaration.Parameters, arguments, environment, interpreter);
        // See Call for the JS-spec rationale; materialize the args span into a
        // SharpTSArray so `arguments[i]` and `arguments.length` work — but only
        // when the body can observe it (ArgumentsUsage scan).
        if (ArgumentsUsage.UsesArguments(_declaration))
        {
            var argsList = new List<object?>(arguments.Length);
            for (int i = 0; i < arguments.Length; i++) argsList.Add(arguments[i].ToObject());
            environment.Define("arguments", new SharpTSArguments(argsList));
        }

        var result = interpreter.ExecuteBlock(_declaration.Body, environment);
        if (result.Type == ExecutionResult.ResultType.Return)
        {
            return result.Value;
        }
        if (result.Type == ExecutionResult.ResultType.Throw)
        {
            // Propagate the original throw value (SharpTSError, SharpTSInstance,
            // string, etc.) through ThrowException so try/catch blocks up the
            // stack see the actual thrown object rather than a stringified
            // message. Without this, `catch (e) { e.constructor === TypeError }`
            // breaks for any throw that crosses a function-call boundary.
            throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
        }

        return RuntimeValue.Undefined;
    }

    public override string ToString() => $"<fn {_declaration.Name.Lexeme}>";
}

/// <summary>
/// Runtime wrapper for arrow function expressions.
/// </summary>
/// <remarks>
/// Wraps an <see cref="Expr.ArrowFunction"/> AST node along with its closure environment.
/// Supports both expression bodies (<c>x =&gt; x + 1</c>) and block bodies (<c>x =&gt; { return x + 1; }</c>).
/// For arrow functions (<c>HasOwnThis=false</c>), <c>this</c> is captured from the enclosing scope via the closure.
/// For function expressions and object method shorthand (<c>HasOwnThis=true</c>), <c>this</c> is bound at call time.
/// </remarks>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSArrowFunction : ISharpTSCallable, ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    private readonly Expr.ArrowFunction _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;
    // Receiver bound via Bind(). Stored on the function itself rather than
    // wrapping _closure with an extra scope — otherwise runtime scope depth
    // wouldn't match the resolver's static distances and outer captures break.
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;
    // Arrow/function expressions are ordinary objects for user-defined string
    // properties, including descriptor attributes and accessors.
    private SharpTSObject _properties = new([]);

    /// <summary>
    /// Indicates whether this function has its own 'this' binding (function expressions)
    /// versus capturing 'this' from enclosing scope (arrow functions).
    /// </summary>
    public bool HasOwnThis { get; }

    public SharpTSArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool hasOwnThis = false)
        : this(declaration, closure, hasOwnThis, boundThis: null, hasBoundThis: false)
    {
    }

    private SharpTSArrowFunction(Expr.ArrowFunction declaration, RuntimeEnvironment closure, bool hasOwnThis, object? boundThis, bool hasBoundThis)
    {
        _declaration = declaration;
        _closure = closure;
        HasOwnThis = hasOwnThis;
        _boundThis = boundThis;
        _hasBoundThis = hasBoundThis;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    /// <summary>JS function-as-object property access.</summary>
    public bool TryGetProperty(string name, out object? value)
    {
        if (_properties.Fields.TryGetValue(name, out value))
            return true;
        value = null;
        return false;
    }

    /// <summary>User-assigned property names on this arrow/function-expression.</summary>
    public IEnumerable<string> PropertyKeys => _properties.OwnEnumerableKeys();

    /// <summary>Sets a JS-object property on this arrow function.</summary>
    public void SetProperty(string name, object? value) => _properties.SetProperty(name, value);
    public bool HasProperty(string name)
        => _properties.HasProperty(name) || _properties.HasSetter(name);
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _properties.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.GetOwnPropertyDescriptor(name);
    public bool IsPropertyEnumerable(string name)
        => _properties.GetOwnPropertyDescriptor(name) is { Enumerable: true };
    internal void FreezeOwnProperties() => _properties.Freeze();
    internal void SealOwnProperties() => _properties.Seal();

    // Symbol-keyed property storage — same rationale as SharpTSFunction
    // above (test262 species-* patterns install Symbol.species on a
    // constructor expression).
    private Dictionary<SharpTSSymbol, object?>? _symbolProperties;

    /// <summary>Sets a symbol-keyed property on this arrow function.</summary>
    public void SetBySymbol(SharpTSSymbol key, object? value)
    {
        _symbolProperties ??= [];
        _symbolProperties[key] = value;
    }

    /// <summary>Reads a symbol-keyed property; returns true and the value
    /// when one is registered.</summary>
    public bool TryGetSymbolProperty(SharpTSSymbol key, out object? value)
    {
        if (_symbolProperties != null && _symbolProperties.TryGetValue(key, out value))
            return true;
        value = null;
        return false;
    }

    // Symbol-keyed accessor pairs (mirrors SharpTSFunction).
    private Dictionary<SharpTSSymbol, (ISharpTSCallable? Get, ISharpTSCallable? Set)>? _symbolAccessors;

    /// <summary>Installs a symbol-keyed accessor pair from defineProperty.</summary>
    public void DefineSymbolAccessor(SharpTSSymbol key, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _symbolAccessors ??= [];
        _symbolAccessors[key] = (getter, setter);
    }

    /// <summary>Returns the accessor pair for the symbol if defined.</summary>
    public bool TryGetSymbolAccessor(SharpTSSymbol key, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_symbolAccessors != null && _symbolAccessors.TryGetValue(key, out var pair))
        {
            getter = pair.Get;
            setter = pair.Set;
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>Removes a JS-object property from this arrow function.</summary>
    public bool DeleteProperty(string name)
        => _properties.DeleteProperty(name);

    /// <summary>Defines a getter/setter pair via Object.defineProperty.</summary>
    public void DefineAccessor(string name, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _properties.DefineProperty(name, new SharpTSPropertyDescriptor
        {
            Get = getter,
            Set = setter,
            HasGet = true,
            HasSet = true,
            Configurable = true,
        });
    }

    /// <summary>Returns the accessor pair for <paramref name="name"/> if defined.</summary>
    public bool TryGetAccessor(string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_properties.HasGetter(name) || _properties.HasSetter(name))
        {
            getter = _properties.GetGetter(name);
            setter = _properties.GetSetter(name);
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    public int Arity() => _arity;

    internal bool IsStrict
        => _closure.IsStrictMode
            || (_declaration.BlockBody is not null
                && Parsing.DirectivePrologue.HasUseStrict(_declaration.BlockBody));

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Check for function-level "use strict" directive in block body
        bool functionStrict = IsStrict;
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        // Named function expression: bind the self-reference in the same scope
        // as parameters so recursion works (`function f(n) { return f(n-1); }`)
        // while keeping outer-variable distances consistent with the resolver.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, this);
        }

        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter);

        // Receiver bound via Bind() is stored on the function itself, not in an
        // extra closure scope — see field comment on _boundThis.
        if (_hasBoundThis)
        {
            environment.Define("this", _boundThis);
        }
        else if (HasOwnThis && !_closure.TryGet("this", out _))
        {
            // ECMA-262: a function expression invoked as `fn()` (bare call) has
            // its own `this` — globalThis in sloppy mode, undefined in strict
            // mode. Without this binding, the harness-loading IIFE
            // `(function(){...})()` (which writes to `this.name = ...`) throws
            // "Undefined variable 'this'" at the resolver level, taking out
            // the entire test262 harness and cascading to ~830 tests showing
            // as RuntimeError rather than their real outcome.
            //
            // True arrow functions (HasOwnThis=false) inherit `this` from the
            // enclosing closure per spec, so this branch only fires for
            // function expressions.
            environment.Define("this",
                functionStrict ? SharpTSUndefined.Instance : (object?)interpreter.GlobalThis);
        }

        // Function expressions (HasOwnThis) bind their own `arguments`; true arrows
        // (HasOwnThis=false) inherit it from the enclosing scope per JS spec. Needed
        // for lodash-style wrappers: `function outer() { return function() {
        // return func.apply(this, arguments); }; }` — the returned function is a
        // function expression, not an arrow, and must see its own `arguments`.
        // Materialized only when the body can observe it (ArgumentsUsage scan).
        if (HasOwnThis && ArgumentsUsage.UsesArguments(_declaration))
        {
            environment.Define("arguments", new SharpTSArguments(new List<object?>(arguments)));
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
            var result = interpreter.ExecuteBlock(_declaration.BlockBody, environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                return result.Value.ToObject();
            }
            if (result.Type == ExecutionResult.ResultType.Throw)
            {
                // See SharpTSFunction.Call — preserve original thrown value.
                throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
            }
        }

        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// V2 call path — avoids boxing at parameter and return boundaries.
    /// </summary>
    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        bool functionStrict = IsStrict;
        RuntimeEnvironment environment = functionStrict
            ? new RuntimeEnvironment(_closure, strictMode: true)
            : new RuntimeEnvironment(_closure);

        // Named function expression: bind the self-reference in the same scope as
        // parameters so recursion works (`function f(n) { return f(n-1); }`) while
        // keeping outer-variable distances consistent with the resolver.
        if (_declaration.Name != null)
        {
            environment.Define(_declaration.Name.Lexeme, this);
        }

        ParameterBinder.BindRV(_declaration.Parameters, arguments, environment, interpreter);

        if (_hasBoundThis)
        {
            environment.Define("this", _boundThis);
        }
        else if (HasOwnThis && !_closure.TryGet("this", out _))
        {
            // ECMA-262: function-expression's own `this` is globalThis (sloppy)
            // or undefined (strict). Mirrors the legacy Call path above.
            environment.Define("this",
                functionStrict ? SharpTSUndefined.Instance : (object?)interpreter.GlobalThis);
        }

        // Function expressions (HasOwnThis) bind their own `arguments`; true arrows do not.
        // Materialized only when the body can observe it (ArgumentsUsage scan).
        if (HasOwnThis && ArgumentsUsage.UsesArguments(_declaration))
        {
            var argsList = new List<object?>(arguments.Length);
            for (int i = 0; i < arguments.Length; i++) argsList.Add(arguments[i].ToObject());
            environment.Define("arguments", new SharpTSArguments(argsList));
        }

        if (_declaration.ExpressionBody != null)
        {
            RuntimeEnvironment previous = interpreter.Environment;
            try
            {
                interpreter.SetEnvironment(environment);
                return interpreter.EvaluateRV(_declaration.ExpressionBody);
            }
            finally
            {
                interpreter.SetEnvironment(previous);
            }
        }
        else if (_declaration.BlockBody != null)
        {
            var result = interpreter.ExecuteBlock(_declaration.BlockBody, environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                return result.Value;
            }
            if (result.Type == ExecutionResult.ResultType.Throw)
            {
                // Propagate the original throw value (SharpTSError, SharpTSInstance,
            // string, etc.) through ThrowException so try/catch blocks up the
            // stack see the actual thrown object rather than a stringified
            // message. Without this, `catch (e) { e.constructor === TypeError }`
            // breaks for any throw that crosses a function-call boundary.
            throw ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
            }
        }

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Binds 'this' to the given object. Only applicable for function expressions with HasOwnThis=true.
    /// </summary>
    /// <param name="thisObject">The object to bind as 'this'.</param>
    /// <returns>A new SharpTSArrowFunction with 'this' bound.</returns>
    public SharpTSArrowFunction Bind(object thisObject)
    {
        var bound = new SharpTSArrowFunction(_declaration, _closure, hasOwnThis: true,
                                             boundThis: thisObject, hasBoundThis: true);
        // Share user-property storage so `obj.fn[k] = v` round-trips across
        // multiple `obj.fn` reads (each read produces a fresh bound copy via
        // EvaluateGetOnRecordRV's Bind-on-read for method-call binding;
        // SpeciesConstructor and similar protocols depend on
        // `re.constructor[Symbol.species]` surviving from set to subsequent
        // get). Eagerly materialize so a later mutation on either copy lands
        // in the same dict instead of branching into a private one via the
        // lazy `??=` init.
        _symbolProperties ??= [];
        bound._properties = _properties;
        bound._symbolProperties = _symbolProperties;
        _symbolAccessors ??= [];
        bound._symbolAccessors = _symbolAccessors;
        return bound;
    }

    public override string ToString()
    {
        if (_declaration.Name != null)
            return $"<fn {_declaration.Name.Lexeme}>";
        return "<arrow fn>";
    }
}
