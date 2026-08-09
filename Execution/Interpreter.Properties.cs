using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    internal static SharpTSObject CreateFunctionPrototype(object constructor)
    {
        var prototype = new SharpTSObject([]);
        prototype.DefineProperty("constructor", new SharpTSPropertyDescriptor(
            value: constructor,
            writable: true,
            enumerable: false,
            configurable: true));
        return prototype;
    }

    private static SharpTSObject CreateConstructedThis(object? prototype)
        => new([]) { Prototype = prototype };

    /// <summary>
    /// Extracts the simple class name from a new expression callee for runtime use.
    /// </summary>
    private static string? GetSimpleClassName(Expr callee)
    {
        return callee is Expr.Variable v ? v.Name.Lexeme : null;
    }

    /// <summary>
    /// Checks if the callee is a simple identifier (not a member access or complex expression).
    /// </summary>
    private static bool IsSimpleIdentifier(Expr callee) => callee is Expr.Variable;

    private async ValueTask<List<object?>> EvaluateNewArgumentsCore(
        IEvaluationContext ctx, IReadOnlyList<Expr> arguments)
    {
        List<object?> result = [];
        foreach (var argument in arguments)
        {
            if (argument is Expr.Spread spread)
            {
                var value = (await ctx.EvaluateExprAsync(spread.Expression)).ToObject();
                result.AddRange(GetIterableElements(value));
            }
            else
            {
                result.Add((await ctx.EvaluateExprAsync(argument)).ToObject());
            }
        }
        return result;
    }

    private List<object?> EvaluateNewArguments(IReadOnlyList<Expr> arguments)
    {
        List<object?> result = [];
        foreach (var argument in arguments)
        {
            if (argument is Expr.Spread spread)
                result.AddRange(GetIterableElements(Evaluate(spread.Expression)));
            else
                result.Add(Evaluate(argument));
        }
        return result;
    }

    /// <summary>
    /// Core implementation for evaluating 'new' expressions, shared between sync and async paths.
    /// Handles all built-in types (Date, RegExp, Map, Set, WeakMap, WeakSet, Error) and user classes.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating arguments.</param>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A ValueTask containing the instantiated object.</returns>
    private async ValueTask<object?> EvaluateNewCore(IEvaluationContext ctx, Expr.New newExpr)
    {
        // Built-in types only apply when callee is a simple identifier
        bool isSimpleName = IsSimpleIdentifier(newExpr.Callee);
        string? simpleClassName = GetSimpleClassName(newExpr.Callee);

        // Handle built-in constructors via factory
        if (isSimpleName && simpleClassName != null)
        {
            // Special case: Promise needs executor function evaluation, not standard arg evaluation
            if (simpleClassName == BuiltInNames.Promise)
            {
                var promiseArgs = await EvaluateNewArgumentsCore(ctx, newExpr.Arguments);
                if (promiseArgs.Count != 1)
                {
                    throw new InterpreterException($"{BuiltInNames.Promise} constructor requires exactly 1 argument (executor function), got {promiseArgs.Count}.");
                }
                return CreatePromiseFromExecutor(promiseArgs[0]);
            }

            // Try factory for all other built-in constructors
            if (BuiltInConstructorFactory.IsBuiltIn(simpleClassName))
            {
                List<object?> args = await EvaluateNewArgumentsCore(ctx, newExpr.Arguments);
                return BuiltInConstructorFactory.TryCreate(simpleClassName, args, this);
            }
        }

        // Evaluate the callee expression to get the class/constructor
        object? klass = (await ctx.EvaluateExprAsync(newExpr.Callee)).ToObject();
        List<object?> evaluatedArguments = await EvaluateNewArgumentsCore(ctx, newExpr.Arguments);

        // Handle Proxy construct trap
        if (klass is SharpTSProxy proxy)
        {
            return proxy.TrapConstruct(evaluatedArguments, this);
        }

        // Constructor-function pattern: `function Foo() { if (!(this instanceof Foo)) return new Foo(); this.x = 1; }`.
        // Node's CJS packages (e.g. yallist) rely on JS `new` semantics
        // binding `this` to a fresh object whose prototype is Foo.prototype.
        // Without this, `self instanceof Yallist` is false and packages
        // recurse infinitely.
        if (klass is SharpTSFunction userFn)
        {
            // Build a new `this` object backed by the function's prototype.
            if (!userFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(userFn);
                userFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = userFn.BindThis(newThis);
            var result = bound.CallBoxed(this, evaluatedArguments);
            // JS spec: if the constructor returns an object (incl. a function), use
            // it; otherwise use the new `this` (#446).
            return IsConstructorReturnObject(result) ? result : newThis;
        }

        // Function expressions (named or anonymous) used as constructors:
        // `var Ctor = function(x) { this.x = x; }; new Ctor(1);` or
        // `(function Foo(){...})()`-defined constructors. Same semantics as
        // SharpTSFunction case above — without this, `new fnExpr(...)` falls
        // through to the generic ISharpTSCallable branch which loses the
        // `this`-binding/prototype-link/return-value-fallback dance and
        // returns null. The harness's
        // `Test262Error = function(m) { __orig.call(this,m); this.name = ... }`
        // wrapper depends on this — without it, `new Test262Error("x")` is
        // null and every assert.* throws "Only instances and objects have
        // properties" instead of the spec'd Test262Error.
        if (klass is SharpTSArrowFunction userArrowFn && userArrowFn.HasOwnThis)
        {
            // Function expressions have prototype too — lazy-create on first read.
            if (!userArrowFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(userArrowFn);
                userArrowFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = userArrowFn.Bind(newThis);
            var result = bound.CallBoxed(this, evaluatedArguments);
            return IsConstructorReturnObject(result) ? result : newThis;
        }

        // Prototype-method wrappers and Object.prototype unbound methods are
        // not constructors per ECMA-262. They look callable but `new` on them
        // must surface a TypeError (covers \`new Error.prototype.toString()\`
        // and the not-a-constructor.js Test262 cluster). BuiltInMethod
        // explicitly stays constructable here because Intl.DateTimeFormat,
        // RegExp, Date, Map, Set, etc. are registered as BuiltInMethod and
        // need to support \`new\`.
        if (IsNonConstructorWrapper(klass))
        {
            throw new ThrowException(new SharpTSTypeError("X is not a constructor"));
        }

        if (klass is SharpTSStringNamespace)
            return BuiltInConstructorFactory.TryCreate(
                BuiltInNames.String, evaluatedArguments, this);
        if (klass is SharpTSNumberNamespace)
            return BuiltInConstructorFactory.TryCreate(
                BuiltInNames.Number, evaluatedArguments, this);
        if (klass is SharpTSBooleanNamespace)
            return BuiltInConstructorFactory.TryCreate(
                BuiltInNames.Boolean, evaluatedArguments, this);

        // Handle callable constructors (like SharpTSEventEmitterConstructor)
        // These implement ISharpTSCallable and are used for module-imported types.
        if (klass is ISharpTSCallable callable && klass is not SharpTSClass && klass is not BoundFunction)
        {
            try
            {
                return callable.CallBoxed(this, evaluatedArguments);
            }
            catch (Exception ex) when (IsNativeConstructorFailure(ex))
            {
                throw new ThrowException(new SharpTSError(ex.Message));
            }
        }

        // Bound functions cannot be used as constructors (JS spec compliance)
        if (klass is BoundFunction)
        {
            throw new InterpreterException("Bound functions cannot be used as constructors.");
        }

        if (klass is not SharpTSClass sharpClass)
        {
            // ECMA-262: invoking `new X` on a non-constructor surfaces as
            // TypeError. Routed through ThrowException so guest code sees a
            // real TypeError instance for `assert.throws(TypeError, ...)`.
            throw new ThrowException(new SharpTSTypeError("X is not a constructor"));
        }

        // Runtime check for abstract class instantiation (backup to type checker)
        if (sharpClass.IsAbstract)
        {
            throw new InterpreterException($"Cannot create an instance of abstract class '{sharpClass.Name}'.");
        }

        return sharpClass.CallBoxed(this, evaluatedArguments);
    }

    /// <summary>
    /// Evaluates a <c>new</c> expression, instantiating a class.
    /// </summary>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A new <see cref="SharpTSInstance"/> of the class.</returns>
    /// <remarks>
    /// Looks up the class by evaluating the callee expression,
    /// and invokes the class's <see cref="SharpTSClass.Call"/> method.
    /// Supports new on expressions: new ctor(), new Namespace.Class(), new (expr)()
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#constructors">TypeScript Constructors</seealso>
    /// <summary>
    /// ECMA-262 §7.3.13 Construct(F, argumentsList) abstract operation.
    /// Invokes <paramref name="callable"/> as a constructor with
    /// <paramref name="args"/>, returning the constructed instance.
    /// Mirrors the user-function arm of <see cref="EvaluateNew"/>: builds a
    /// fresh `this` linked to F's prototype, runs the body, returns the
    /// body's object return (or the fresh `this` when the body returns a
    /// primitive). Used by spec algorithms in built-in helpers (e.g.
    /// SpeciesConstructor in RegExp Symbol.split / Symbol.matchAll).
    /// </summary>
    internal object? Construct(object? callable, IList<object?> args)
    {
        if (callable is SharpTSFunction userFn)
        {
            if (!userFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(userFn);
                userFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = userFn.BindThis(newThis);
            var result = bound.CallBoxed(this, [.. args]);
            return IsConstructorReturnObject(result) ? result : newThis;
        }
        if (callable is SharpTSArrowFunction arrowFn && arrowFn.HasOwnThis)
        {
            if (!arrowFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(arrowFn);
                arrowFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = arrowFn.Bind(newThis);
            var result = bound.CallBoxed(this, [.. args]);
            return IsConstructorReturnObject(result) ? result : newThis;
        }
        // Built-in constructor (e.g. RegExp via SharpTSBuiltInConstructor).
        // BuiltInConstructorFactory handles the factory dispatch.
        if (callable is SharpTSBuiltInConstructor builtIn)
        {
            return BuiltInConstructorFactory.TryCreate(builtIn.Name, [.. args], this);
        }
        // Fallback: treat as a callable; Reflect.construct shape.
        if (callable is ISharpTSCallable c)
        {
            return c.CallBoxed(this, [.. args]);
        }
        throw new InterpreterException("TypeError: Construct called on non-callable.");
    }

    /// <summary>
    /// ECMA-262 §10.2.2 [[Construct]]: when a constructor body returns a value,
    /// <c>new</c> yields that value only if it is an <em>Object</em>; a primitive
    /// return (number, string, boolean, symbol, bigint, null, undefined) is ignored
    /// and the freshly-constructed <c>this</c> is used instead. Functions and arrays
    /// are ordinary objects, so a returned function/arrow wins (#446) — matching
    /// tsc/node and the compiled <c>NewOnFunction</c> path. Centralizes the decision
    /// so every construct-return site stays consistent.
    /// </summary>
    private static bool IsConstructorReturnObject(object? result) => result switch
    {
        null => false,
        SharpTSUndefined => false,
        double => false,
        bool => false,
        string => false,
        SharpTSSymbol => false,
        SharpTSBigInt => false,
        System.Numerics.BigInteger => false,
        _ => true,
    };

    private RuntimeValue EvaluateNew(Expr.New newExpr)
    {
        // Built-in types only apply when callee is a simple identifier
        bool isSimpleName = IsSimpleIdentifier(newExpr.Callee);
        string? simpleClassName = GetSimpleClassName(newExpr.Callee);

        // Handle built-in constructors via factory
        if (isSimpleName && simpleClassName != null)
        {
            // Special case: Promise needs executor function evaluation, not standard arg evaluation
            if (simpleClassName == BuiltInNames.Promise)
            {
                var promiseArgs = EvaluateNewArguments(newExpr.Arguments);
                if (promiseArgs.Count != 1)
                {
                    throw new InterpreterException($"{BuiltInNames.Promise} constructor requires exactly 1 argument (executor function), got {promiseArgs.Count}.");
                }
                return RuntimeValue.FromObject(CreatePromiseFromExecutor(promiseArgs[0]));
            }

            // Try factory for all other built-in constructors
            if (BuiltInConstructorFactory.IsBuiltIn(simpleClassName))
            {
                List<object?> args = EvaluateNewArguments(newExpr.Arguments);
                return BuiltInConstructorFactory.TryCreateRV(simpleClassName, args, this);
            }
        }

        // Evaluate the callee expression to get the class/constructor
        object? klass = Evaluate(newExpr.Callee);
        List<object?> evaluatedArguments = EvaluateNewArguments(newExpr.Arguments);

        // Handle Proxy construct trap
        if (klass is SharpTSProxy proxy)
        {
            return proxy.TrapConstructRV(evaluatedArguments, this);
        }

        // Constructor-function pattern: `function Foo() { this.x = 1 }` called with `new`.
        // Build a fresh `this` with prototype linkage, bind it, then call —
        // so `this instanceof Foo` returns true (Node CJS pattern used by
        // e.g. yallist, EventEmitter sub-classes).
        if (klass is SharpTSFunction userFn)
        {
            if (!userFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(userFn);
                userFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = userFn.BindThis(newThis);
            var result = bound.CallBoxed(this, evaluatedArguments);
            return RuntimeValue.FromBoxed(IsConstructorReturnObject(result) ? result : newThis);
        }

        // Function expressions used as constructors — same shape as
        // SharpTSFunction case above. The Test262 harness's Test262Error
        // wrapper relies on this (`new Test262Error("...")`); without the
        // case, every assert.* throw lands as null instead of a usable error.
        if (klass is SharpTSArrowFunction userArrowFn && userArrowFn.HasOwnThis)
        {
            if (!userArrowFn.TryGetProperty("prototype", out var protoObj))
            {
                protoObj = CreateFunctionPrototype(userArrowFn);
                userArrowFn.SetProperty("prototype", protoObj);
            }
            var newThis = CreateConstructedThis(protoObj);
            var bound = userArrowFn.Bind(newThis);
            var result = bound.CallBoxed(this, evaluatedArguments);
            return RuntimeValue.FromBoxed(IsConstructorReturnObject(result) ? result : newThis);
        }

        // Prototype-method wrappers and Object.prototype unbound methods are
        // not constructors per ECMA-262 — see async path above.
        if (IsNonConstructorWrapper(klass))
        {
            throw new ThrowException(new SharpTSTypeError("X is not a constructor"));
        }

        // A constructor obtained through an alias still performs [[Construct]];
        // calling the namespace would incorrectly return a primitive String.
        if (klass is SharpTSStringNamespace)
            return BuiltInConstructorFactory.TryCreateRV(
                BuiltInNames.String, evaluatedArguments, this);
        if (klass is SharpTSNumberNamespace)
            return BuiltInConstructorFactory.TryCreateRV(
                BuiltInNames.Number, evaluatedArguments, this);
        if (klass is SharpTSBooleanNamespace)
            return BuiltInConstructorFactory.TryCreateRV(
                BuiltInNames.Boolean, evaluatedArguments, this);

        // Handle callable constructors. Many built-in constructors are
        // registered as BuiltInMethod, so we accept any ISharpTSCallable here.
        if (klass is ISharpTSCallable callable && klass is not SharpTSClass && klass is not BoundFunction)
        {
            try
            {
                return RuntimeValue.FromBoxed(callable.CallBoxed(this, evaluatedArguments));
            }
            catch (Exception ex) when (IsNativeConstructorFailure(ex))
            {
                throw new ThrowException(new SharpTSError(ex.Message));
            }
        }

        // Bound functions cannot be used as constructors (JS spec compliance)
        if (klass is BoundFunction)
        {
            throw new InterpreterException("Bound functions cannot be used as constructors.");
        }

        if (klass is not SharpTSClass sharpClass)
        {
            // ECMA-262: invoking `new X` on a non-constructor surfaces as
            // TypeError. Routed through ThrowException so guest code sees a
            // real TypeError instance for `assert.throws(TypeError, ...)`.
            throw new ThrowException(new SharpTSTypeError("X is not a constructor"));
        }

        // Runtime check for abstract class instantiation (backup to type checker)
        if (sharpClass.IsAbstract)
        {
            throw new InterpreterException($"Cannot create an instance of abstract class '{sharpClass.Name}'.");
        }

        return sharpClass.CallRV(this, evaluatedArguments);
    }

    /// <summary>
    /// True when a host exception escaping a native built-in constructor (a
    /// <c>new</c> on an <see cref="ISharpTSCallable"/> that is not a user class)
    /// should be re-surfaced to guest code as a real <see cref="SharpTSError"/>
    /// instead of the bare message string the host-exception boundary would
    /// otherwise bind to the catch variable (#464).
    /// </summary>
    /// <remarks>
    /// Native constructors such as <c>Worker</c> and <c>MessageChannel</c> validate
    /// their input by throwing a plain <see cref="Exception"/> (e.g. <c>new Worker(…)</c>
    /// failing the <c>workerData</c> structured-clone). In interpreter mode a guest
    /// <c>try/catch</c> previously bound that exception's message <em>string</em>, so
    /// <c>e.message</c> was <c>undefined</c> and <c>e instanceof Error</c> was false;
    /// compiled mode already surfaces a proper <c>Error</c>. Two kinds pass through
    /// unwrapped: a <see cref="ThrowException"/> (a guest throw already carrying its
    /// value, e.g. routed out through a constructor that consumed a guest iterable)
    /// and any <see cref="Diagnostics.Exceptions.SharpTSException"/> (interpreter/
    /// runtime errors deliberately kept as message strings per the
    /// <see cref="ThrowException.FromResult"/> backward-compat contract).
    /// </remarks>
    private static bool IsNativeConstructorFailure(Exception ex) =>
        ex is not ThrowException && ex is not Diagnostics.Exceptions.SharpTSException;

    /// <summary>
    /// Evaluates a <c>this</c> expression, returning the current binding.
    /// </summary>
    /// <param name="expr">The this expression AST node.</param>
    /// <returns>The current <c>this</c> value, or the global object for script code.</returns>
    /// <remarks>
    /// Functions and methods install an explicit binding in their call
    /// environment (including strict functions, which bind it to undefined).
    /// Global script code has no environment entry, so it falls back to this
    /// realm's global object per ECMA-262 Global Environment Records.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#this-at-runtime-in-classes">TypeScript this in Classes</seealso>
    private RuntimeValue EvaluateThis(Expr.This expr)
    {
        return _environment.TryGet("this", out var value)
            ? value
            : RuntimeValue.FromBoxed(GlobalThis);
    }

    /// <summary>
    /// Evaluates a property access expression (dot notation).
    /// </summary>
    /// <param name="get">The property access expression AST node.</param>
    /// <returns>The value of the property, or a bound method.</returns>
    /// <remarks>
    /// Handles optional chaining (<c>?.</c>), static member access on classes,
    /// enum member access, instance properties/methods, object properties,
    /// string methods, array methods, and Math object members.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-7.html#optional-chaining">TypeScript Optional Chaining</seealso>
    private RuntimeValue EvaluateGet(Expr.Get get)
        => EvaluateGetCore(_syncContext, get).GetAwaiter().GetResult();

    /// <summary>
    /// Core property-access logic shared by the sync and async evaluators; the evaluation
    /// context supplies the receiver-evaluation strategy so a single body serves both paths.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateGetCore(IEvaluationContext ctx, Expr.Get get)
    {
        // Handle namespace static property access (e.g., Number.MAX_VALUE, Number.NaN)
        // These namespaces don't have runtime values, but have static properties.
        // Per-realm intrinsics (Math) are skipped here so they resolve through the
        // normal object path onto this realm's instance — that keeps member
        // identity stable across access forms (`Math.max === Math.max`, and the
        // value-form `const m = Math; m.max === Math.max`, #288) and lets a user
        // `let Math = …` shadow correctly, since the static fast-path binds to a
        // process-wide singleton that the realm instance no longer matches.
        if (get.Object is Expr.Variable nsVar
            && !IsRealmIntrinsicName(nsVar.Name.Lexeme)
            && nsVar.Name.Lexeme != BuiltInNames.Promise)
        {
            var member = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (member != null)
            {
                // Only invoke when the method is marked as wrapping a constant (e.g. Number.MAX_VALUE).
                // Previously the check was `MinArity == 0 && MaxArity == 0`, which also matched real
                // zero-arity methods like Date.now — breaking the `const nativeNow = Date.now;
                // nativeNow()` aliasing idiom (used by lodash/minimatch/etc.). IsConstant is set
                // by BuiltInMethod.CreateConstant / BuiltInStaticBuilder.CallableConstant.
                if (member is BuiltInMethod bm && bm.IsConstant)
                {
                    return RuntimeValue.FromBoxed(bm.CallBoxed(this, []));
                }
                return RuntimeValue.FromObject(member);
            }
        }

        object? obj = (await ctx.EvaluateExprAsync(get.Object)).ToObject();
        return EvaluateGetOnObject(get, obj);
    }

    /// <summary>
    /// ECMA-262 §7.3.3 Get(O, P) abstract operation. Reads a string-named
    /// property from <paramref name="obj"/>, honoring user-defined getters
    /// and propagating their thrown errors. Used by spec algorithms in
    /// built-in helpers that need real Get semantics rather than the
    /// type-specific shortcut accessors (e.g.
    /// <see cref="Runtime.BuiltIns.RegExpBuiltIns"/>'s §22.2.5 protocol
    /// methods read flags / lastIndex / exec / unicode / global via Get
    /// so user-installed getters fire and throw upstream).
    /// </summary>
    // Synthetic Get nodes depend only on the property name and are immutable records the
    // evaluator never keys by identity, so they are shared process-wide instead of allocating
    // a Token + Expr.Get per internal Get() (the RegExp protocol methods call this per
    // flags/lastIndex/exec read). Bounded by the set of names spec algorithms read.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Expr.Get> _syntheticGets =
        new(StringComparer.Ordinal);

    internal object? GetProperty(object? obj, string name)
    {
        var syntheticGet = _syntheticGets.GetOrAdd(name, static n =>
            new Expr.Get(null!, new Token(TokenType.IDENTIFIER, n, null, 0), Optional: false));
        return EvaluateGetOnObject(syntheticGet, obj).ToObject();
    }

    /// <summary>
    /// ECMA-262 Get(O, P) for built-in algorithms. Unlike expression member
    /// access, reading a callable data property does not bind it to its owner;
    /// the exact stored function identity is returned. Accessor getters still
    /// run with the original receiver as <c>this</c>.
    /// </summary>
    internal object? GetPropertyValue(object? obj, string name)
        => GetPropertyValueFromChain(obj, name, obj);

    internal object? GetPropertyValue(
        object? obj, string name, object? receiver)
        => GetPropertyValueFromChain(obj, name, receiver);

    private object? GetPropertyValueFromChain(
        object? current, string name, object? receiver)
    {
        for (int depth = 0; depth < 64 && current is not (null or SharpTSUndefined); depth++)
        {
            if (current is bool)
            {
                var prototype = GetBooleanPrototype();
                if (prototype.GetExtraGetter(name) is { } getter)
                    return BindAccessorToObject(getter, receiver!).CallBoxed(this, []);
                if (prototype.HasExtra(name))
                    return prototype.TryGetExtra(name);
                if (prototype.GetMember(name) is { } member)
                    return member;
                return GetObjectPrototype().GetMember(name)
                    ?? SharpTSUndefined.Instance;
            }

            if (current is SharpTSBigInt)
            {
                var prototype = GetBigIntPrototype();
                if (prototype.GetExtraGetter(name) is { } getter)
                    return BindAccessorToObject(getter, receiver!).CallBoxed(this, []);
                if (prototype.HasExtra(name))
                    return prototype.TryGetExtra(name);
                if (prototype.GetMember(name) is { } member)
                    return member;
                return GetObjectPrototype().GetMember(name)
                    ?? SharpTSUndefined.Instance;
            }

            if (current is SharpTSArray array)
            {
                if (name == "length") return (double)array.LongLength;
                if (long.TryParse(name, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long index)
                    && index >= 0 && array.HasIndex(index))
                {
                    return GetArrayIndexValue(array, index).ToObject();
                }
                if (array.HasNamedProperty(name))
                    return array.GetNamedProperty(name);
                if (array.HasExplicitPrototype)
                {
                    current = array.ExplicitPrototype;
                    continue;
                }
                if (GetArrayPrototype().GetExtraGetter(name) is { } arrayGetter)
                    return BindAccessorToObject(arrayGetter, receiver!).CallBoxed(this, []);
                if (GetArrayPrototype().HasExtra(name))
                    return GetArrayPrototype().TryGetExtra(name);
                if (GetArrayPrototype().GetMember(name) is { } arrayMember)
                    return arrayMember;
                if (GetObjectPrototype().GetExtraGetter(name) is { } objectGetter)
                    return BindAccessorToObject(objectGetter, receiver!).CallBoxed(this, []);
                if (GetObjectPrototype().GetMember(name) is { } objectMember)
                    return objectMember;
                return SharpTSUndefined.Instance;
            }

            if (current is SharpTSObject record)
            {
                if (record.GetGetter(name) is { } getter)
                    return BindAccessorToObject(getter, receiver!).CallBoxed(this, []);
                if (record.HasSetter(name)) return SharpTSUndefined.Instance;
                if (record.Fields.TryGetValue(name, out var value)) return value;
                current = GetRecordPrototype(record);
                continue;
            }

            if (current is ISharpTSCallable callable)
            {
                // Return inherited Function.prototype methods unbound. A
                // member call binds the original receiver afterwards; binding
                // to a proxy target here would bypass the proxy [[Call]] trap.
                return FunctionBuiltIns.GetPrototypeMethod(name)
                    ?? FunctionBuiltIns.GetMember(callable, name)
                    ?? SharpTSUndefined.Instance;
            }

            return GetProperty(current, name);
        }
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Gets a symbol-keyed property for well-known-symbol protocols while
    /// preserving accessor invocation and ordinary object prototype lookup.
    /// </summary>
    internal object? GetSymbolPropertyValue(object obj, SharpTSSymbol symbol)
    {
        if (obj is string or bool or double or SharpTSBigInt or SharpTSSymbol)
            return SharpTSUndefined.Instance;

        if (obj is SharpTSProxy proxy)
            return proxy.TrapGet(symbol, this);

        object receiver = obj;
        object? current = obj;
        for (int depth = 0; depth < 64 && current is not (null or SharpTSUndefined); depth++)
        {
            if (current is SharpTSObject record)
            {
                if (record.TryGetSymbolAccessor(symbol, out var getter, out _))
                    return getter is null
                        ? SharpTSUndefined.Instance
                        : BindAccessorToObject(getter, receiver).CallBoxed(this, []);
                if (record.HasSymbolProperty(symbol))
                    return record.GetBySymbol(symbol);
                current = GetRecordPrototype(record);
                continue;
            }

            if (current is SharpTSFunction function)
            {
                if (function.TryGetSymbolAccessor(symbol, out var getter, out _))
                    return getter is null
                        ? SharpTSUndefined.Instance
                        : BindAccessorToObject(getter, receiver).CallBoxed(this, []);
                if (function.TryGetSymbolProperty(symbol, out var value))
                    return value;
                current = GetFunctionPrototype();
                continue;
            }

            if (current is SharpTSArrowFunction arrow)
            {
                if (arrow.TryGetSymbolAccessor(symbol, out var getter, out _))
                    return getter is null
                        ? SharpTSUndefined.Instance
                        : BindAccessorToObject(getter, receiver).CallBoxed(this, []);
                if (arrow.TryGetSymbolProperty(symbol, out var value))
                    return value;
                current = GetFunctionPrototype();
                continue;
            }

            if (current is SharpTSArray array)
            {
                if (array.TryGetSymbolAccessor(symbol, out var getter, out _))
                    return getter is null
                        ? SharpTSUndefined.Instance
                        : BindAccessorToObject(getter, receiver).CallBoxed(this, []);
                if (array.HasSymbolProperty(symbol))
                    return array.GetBySymbol(symbol);
                if (ReferenceEquals(symbol, SharpTSSymbol.Iterator))
                    return PerformIndexGet(null!, array, symbol).ToObject();
                current = array.HasExplicitPrototype
                    ? array.ExplicitPrototype
                    : GetArrayPrototype();
                continue;
            }

            if (current is ISharpTSSymbolPropertyBag symbolBag)
            {
                if (symbolBag.TryGetSymbolAccessor(symbol, out var getter, out _))
                    return getter is null
                        ? SharpTSUndefined.Instance
                        : BindAccessorToObject(getter, receiver).CallBoxed(this, []);
                return symbolBag.HasSymbolProperty(symbol)
                    ? symbolBag.GetBySymbol(symbol)
                    : SharpTSUndefined.Instance;
            }

            return PerformIndexGet(null!, current, symbol).ToObject();
        }
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// ECMA-262 §7.3.11 HasProperty(O, P): true when <paramref name="obj"/> or
    /// anything on its prototype chain has an own data field OR an accessor
    /// (getter or setter) named <paramref name="name"/>. Unlike the type-specific
    /// own-only checks, this walks the prototype chain and — crucially for
    /// §6.2.5.5 ToPropertyDescriptor (#801) — counts a setter-only accessor as
    /// present even though <see cref="GetProperty"/> would read <c>undefined</c>
    /// for it. Used to distinguish an omitted descriptor field from one explicitly
    /// set to undefined.
    /// </summary>
    internal bool HasProperty(object? obj, string name)
    {
        if (obj is SharpTSProxy proxy)
            return proxy.TrapHas(name, this);

        for (int depth = 0; depth < 64 && obj is not (null or SharpTSUndefined); depth++)
        {
            if (obj is SharpTSProxy prototypeProxy)
                return prototypeProxy.TrapHas(name, this);

            if (obj is SharpTSArray array)
            {
                if (array.HasOwnProperty(name)) return true;
                if (array.HasExplicitPrototype)
                {
                    obj = array.ExplicitPrototype;
                    continue;
                }
                if (GetArrayPrototype().HasOwnProperty(name)) return true;
                return GetObjectPrototype().HasOwnProperty(name);
            }
            if (obj is SharpTSObject so)
            {
                // SharpTSObject.HasProperty covers own fields + getters; add setters
                // so a setter-only accessor registers as present.
                if (so.HasProperty(name) || so.HasSetter(name)) return true;
                obj = GetRecordPrototype(so);
                continue;
            }
            // Non-record receivers (instances, built-ins like RegExp, boxed
            // primitives): defer to Get semantics over the full chain from here.
            // A defined result means present; this misses only a setter-only
            // accessor whose Get yields undefined, which does not arise for these
            // receiver kinds in practice.
            return GetProperty(obj, name) is not (null or SharpTSUndefined);
        }
        return false;
    }

    /// <summary>Symbol-keyed counterpart of <see cref="HasProperty(object?, string)"/>.</summary>
    internal bool HasSymbolProperty(object? obj, SharpTSSymbol symbol)
    {
        if (obj is SharpTSProxy proxy)
            return proxy.TrapHas(symbol, this);

        object? current = obj;
        for (int depth = 0; depth < 64 && current is not (null or SharpTSUndefined); depth++)
        {
            if (current is SharpTSProxy prototypeProxy)
                return prototypeProxy.TrapHas(symbol, this);
            if (current is SharpTSObject record)
            {
                if (record.HasSymbolProperty(symbol)
                    || record.TryGetSymbolAccessor(symbol, out _, out _)) return true;
                current = GetRecordPrototype(record);
                continue;
            }
            if (current is SharpTSArray array)
            {
                if (array.HasSymbolProperty(symbol)
                    || array.TryGetSymbolAccessor(symbol, out _, out _)
                    || ReferenceEquals(symbol, SharpTSSymbol.Iterator)) return true;
                current = array.HasExplicitPrototype
                    ? array.ExplicitPrototype
                    : GetArrayPrototype();
                continue;
            }
            if (current is ISharpTSSymbolPropertyBag symbolBag
                && symbolBag.HasSymbolProperty(symbol)) return true;

            return GetSymbolPropertyValue(current, symbol) is not SharpTSUndefined;
        }
        return false;
    }

    /// <summary>
    /// Reads a descriptor field with ECMA-262 §7.3.2 Get semantics AND correct
    /// presence (§7.3.11 HasProperty), used by ToPropertyDescriptor (#801).
    /// Walks the prototype chain and stops at the first level that owns the
    /// property: an own getter is invoked; an own SETTER-only accessor is present
    /// with value undefined (and shadows any inherited getter — the case plain
    /// <see cref="GetProperty"/> misses); an own data field returns its value.
    /// Returns true when the field is present (so an explicit <c>value: undefined</c>
    /// or a setter-only <c>value</c> is distinguished from an omitted field).
    /// </summary>
    internal bool TryGetDescriptorField(object? obj, string name, out object? value)
    {
        value = null;
        for (int depth = 0; depth < 64 && obj is not (null or SharpTSUndefined); depth++)
        {
            if (obj is SharpTSObject so)
            {
                var getter = so.GetGetter(name);
                if (getter != null)
                {
                    value = BindAccessorToObject(getter, so).CallBoxed(this, []);
                    return true;
                }
                if (so.HasSetter(name))
                {
                    value = SharpTSUndefined.Instance; // setter-only accessor shadows inherited getter
                    return true;
                }
                if (so.Fields.ContainsKey(name))
                {
                    value = so.GetProperty(name);
                    return true;
                }
                obj = GetRecordPrototype(so);
                continue;
            }
            // Non-record receivers: fall back to HasProperty/Get from this level up.
            if (HasProperty(obj, name))
            {
                value = GetProperty(obj, name);
                return true;
            }
            return false;
        }
        return false;
    }

    /// <summary>
    /// Returns the explicit or implicit prototype for an interpreter record.
    /// Boxed primitive wrappers carry internal type markers rather than a
    /// materialized <c>__proto__</c>, so route those to the matching realm
    /// prototype; ordinary records inherit the realm's Object.prototype.
    /// </summary>
    private object? GetRecordPrototype(SharpTSObject obj)
    {
        if (obj.HasProperty("__proto__"))
            return obj.GetProperty("__proto__");
        if (obj.IsNullPrototype)
            return null;
        if (obj.Fields.TryGetValue("__primitiveType", out var type))
        {
            return type switch
            {
                "String" => GetStringPrototype(),
                "Number" => GetNumberPrototype(),
                "Boolean" => GetBooleanPrototype(),
                "BigInt" => GetBigIntPrototype(),
                _ => GetObjectPrototype(),
            };
        }
        return GetObjectPrototype();
    }

    /// <summary>
    /// Core property access logic, shared between sync and async evaluation.
    /// Uses TypeCategoryResolver for unified type dispatch.
    /// </summary>
    private RuntimeValue EvaluateGetOnObject(Expr.Get get, object? obj)
    {
        // Handle optional chaining - return undefined if object is null or undefined
        if (get.Optional && (obj == null || obj is Runtime.Types.SharpTSUndefined))
        {
            return RuntimeValue.Undefined;
        }

        // ECMA-262 §13.3.2.1 RequireObjectCoercible: a non-optional member read on
        // a nullish base throws a guest TypeError ("Cannot read properties of
        // <null|undefined> (reading '<key>')"), so a guest try/catch binds a real
        // TypeError rather than a host "Runtime Error" string — matching Node, tsc
        // and the compiled path (#676).
        if (obj == null || obj is SharpTSUndefined)
        {
            ThrowCannotReadProperty(obj, get.Name.Lexeme);
        }

        // Proxy interception - must be before any other dispatch
        if (obj is SharpTSProxy proxy)
        {
            return proxy.TrapGetRV(get.Name.Lexeme, this);
        }

        // String.prototype / Number.prototype / Boolean.prototype resolve to
        // this realm's prototype instance (per-realm built-in-prototype
        // mutability, like RegExp.prototype #101) so guest writes stay
        // realm-local and don't race across worker threads. The namespace
        // objects themselves stay shared singletons.
        if (get.Name.Lexeme == "prototype" && TryGetRealmPrototypeForNamespace(obj, out var nsPrototype))
        {
            return RuntimeValue.FromBoxed(nsPrototype);
        }

        if (obj is SharpTSGlobalFunction { Name: BuiltInNames.BigInt }
            && BigIntBuiltIns.GetStaticMember(get.Name.Lexeme) is { } bigIntStatic)
        {
            return RuntimeValue.FromBoxed(bigIntStatic);
        }

        // Object is a per-realm mutable constructor object. Its static methods
        // are already callables with no receiver semantics, so return the exact
        // stored value instead of routing through instance-member dispatch,
        // which binds BuiltInMethod and breaks identity with descriptor.value.
        if (obj is SharpTSObjectNamespace objectNamespace)
        {
            if (objectNamespace.HasOwnProperty(get.Name.Lexeme)
                && objectNamespace.GetMember(get.Name.Lexeme) is { } ownMember)
            {
                return RuntimeValue.FromBoxed(ownMember);
            }
        }

        // String/Number/Boolean are the same shape — per-realm constructor objects whose
        // statics carry no receiver semantics. Going through instance-member dispatch would
        // hand out a freshly bound copy per read, so `String.fromCharCode` would not equal
        // the `value` of its own descriptor.
        if (obj is SharpTSStringNamespace or SharpTSNumberNamespace or SharpTSBooleanNamespace)
        {
            var nsMember = obj switch
            {
                SharpTSStringNamespace ns => ns.GetMember(get.Name.Lexeme),
                SharpTSNumberNamespace ns => ns.GetMember(get.Name.Lexeme),
                _ => ((SharpTSBooleanNamespace)obj).GetMember(get.Name.Lexeme),
            };
            if (nsMember != null) return RuntimeValue.FromBoxed(nsMember);
        }

        var category = TypeCategoryResolver.ClassifyRuntime(obj);
        string memberName = get.Name.Lexeme;

        // A primitive's inherited members come from THIS realm's prototype, so `constructor`
        // must be this realm's constructor object — `("x").constructor === String`. Resolving
        // it off the process-wide singleton breaks that identity now that the bare globals are
        // per-realm. Placed here, ahead of category routing, so every primitive shape is
        // covered regardless of which fallback its category lands in.
        if (memberName == "constructor" && obj is string or double or int or bool)
        {
            object? primitiveCtor = obj switch
            {
                string => GetStringPrototype().GetMember(memberName),
                bool => GetBooleanPrototype().GetMember(memberName),
                _ => GetNumberPrototype().GetMember(memberName),
            };
            if (primitiveCtor != null) return RuntimeValue.FromBoxed(primitiveCtor);
        }

        // Category-based dispatch
        return category switch
        {
            // @DotNetType external types
            TypeCategory.External when obj is DotNetInstance dotNetInstance =>
                EvaluateGetOnDotNetInstance(dotNetInstance, memberName),
            TypeCategory.External when obj is DotNetClass dotNetClass =>
                RuntimeValue.FromBoxed(dotNetClass.GetStaticMember(memberName)),

            // User-defined types
            TypeCategory.Class when obj is SharpTSClass klass =>
                EvaluateGetOnClassRV(klass, memberName),
            TypeCategory.Namespace when obj is SharpTSNamespace nsObj =>
                EvaluateGetOnNamespaceRV(nsObj, memberName),
            TypeCategory.Enum when obj is SharpTSEnum enumObj =>
                enumObj.GetMemberRV(memberName),
            TypeCategory.Enum when obj is ConstEnumValues constEnumObj =>
                constEnumObj.GetMemberRV(memberName),
            TypeCategory.Instance when obj is SharpTSInstance instance =>
                EvaluateGetOnInstanceRV(instance, get.Name),
            TypeCategory.Record when obj is SharpTSObject simpleObj =>
                EvaluateGetOnRecordRV(simpleObj, memberName),

            // Array: needs override checks (named properties, ISharpTSPropertyAccessor)
            TypeCategory.Array => EvaluateGetOnArrayRV(obj!, memberName),

            // Fast path: built-in types with category-indexed dispatch
            _ when BuiltInRegistry.Instance.HasCategoryType(category) =>
                EvaluateGetOnBuiltInRV(category, obj!, memberName),

            // Fallback for remaining types (IDictionary, ISharpTSPropertyAccessor, unknown types)
            _ => RuntimeValue.FromBoxed(EvaluateGetOnFallback(obj, memberName))
        };
    }

    private RuntimeValue EvaluateGetOnDotNetInstance(
        DotNetInstance instance,
        string memberName)
    {
        object? value = instance.GetMember(memberName);
        if (value is SharpTSUndefined &&
            _currentModule is { DotNetExtensionTypes.Count: > 0 } module &&
            DotNetExtensionMethodResolver.GetReceiverClosedCandidates(
                module.DotNetExtensionTypes, memberName, instance.Type).Length > 0)
        {
            value = new DotNetExtensionMethod(
                instance, module.DotNetExtensionTypes, memberName);
        }
        return RuntimeValue.FromBoxed(value);
    }

    /// <summary>
    /// ECMA-262 RequireObjectCoercible failure on a member read: throws a guest
    /// <see cref="SharpTSTypeError"/> ("Cannot read properties of
    /// &lt;null|undefined&gt; (reading '&lt;key&gt;')") wrapped in a
    /// <see cref="ThrowException"/> so a guest <c>try/catch</c> binds a real
    /// <c>TypeError</c> (with the correct name/message and
    /// <c>instanceof TypeError</c>) rather than a host "Runtime Error" string.
    /// Shared by the dot-access (<see cref="EvaluateGetOnObject"/>) and
    /// bracket-access (<see cref="PerformIndexGet"/>) paths (#676).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowCannotReadProperty(object? nullishReceiver, string key)
    {
        string what = nullishReceiver is SharpTSUndefined ? "undefined" : "null";
        throw new ThrowException(new SharpTSTypeError(
            $"Cannot read properties of {what} (reading '{key}')"));
    }

    /// <summary>
    /// ECMA-262 PutValue RequireObjectCoercible failure on a member write: throws
    /// a guest <see cref="SharpTSTypeError"/> ("Cannot set properties of
    /// &lt;null|undefined&gt; (setting '&lt;key&gt;')") wrapped in a
    /// <see cref="ThrowException"/> so a guest <c>try/catch</c> binds a real
    /// <c>TypeError</c>. Write-path counterpart of <see cref="ThrowCannotReadProperty"/>
    /// (#733). Mirrors Node: <c>null.x = 1</c> throws even in sloppy mode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowCannotSetProperty(object? nullishReceiver, string key)
    {
        string what = nullishReceiver is SharpTSUndefined ? "undefined" : "null";
        throw new ThrowException(new SharpTSTypeError(
            $"Cannot set properties of {what} (setting '{key}')"));
    }

    /// <summary>
    /// Evaluates property access on a class (static members).
    /// </summary>
    private RuntimeValue EvaluateGetOnClassRV(SharpTSClass klass, string memberName)
        => RuntimeValue.FromBoxed(EvaluateGetOnClass(klass, memberName));

    private static RuntimeValue EvaluateGetOnNamespaceRV(SharpTSNamespace nsObj, string memberName)
        => RuntimeValue.FromBoxed(EvaluateGetOnNamespace(nsObj, memberName));

    /// <summary>
    /// Fast path for property access on built-in types (string, number, map, set, etc.).
    /// Uses TypeCategory-indexed array dispatch instead of GetType() + Dictionary lookup.
    /// </summary>
    private RuntimeValue EvaluateGetOnBuiltInRV(TypeCategory category, object obj, string memberName)
    {
        if (obj is SharpTSError error)
        {
            if (error.TryGetAccessor(memberName, out var getter, out _) && getter != null)
                return RuntimeValue.FromBoxed(
                    FunctionBuiltIns.CallWithThis(this, getter, error, []));
            if (error.TryGetProperty(memberName, out var value))
                return RuntimeValue.FromBoxed(value);
        }
        // Native errors created by runtime helpers carry their built-in name but
        // not a constructor reference. Resolve `constructor` through this realm;
        // the old process-static SharpTSErrorClass registry leaked the latest
        // interpreter's constructor into every other interpreter in the process.
        if (obj is SharpTSError nativeError && memberName == "constructor")
            return RuntimeValue.FromObject(GetErrorClass(nativeError.ErrorTypeName));

        // JS functions are objects — surface user-set properties before
        // falling through to built-in members (e.g. `bind`, `call`).
        if (obj is SharpTSFunction fn)
        {
            // Accessor defined via Object.defineProperty(fn, name, {get, set}).
            if (fn.TryGetAccessor(memberName, out var getter, out _) && getter != null)
            {
                return RuntimeValue.FromBoxed(getter.CallBoxed(this, []));
            }
            if (fn.TryGetProperty(memberName, out var userProp))
                return RuntimeValue.FromBoxed(userProp);
            // Lazy-init `fn.prototype` on first access (JS semantics).
            if (memberName == "prototype")
            {
                var proto = CreateFunctionPrototype(fn);
                fn.SetProperty("prototype", proto);
                return RuntimeValue.FromBoxed(proto);
            }
        }
        if (obj is SharpTSArrowFunction arrowFn)
        {
            if (arrowFn.TryGetAccessor(memberName, out var getter, out _) && getter != null)
                return RuntimeValue.FromBoxed(getter.CallBoxed(this, []));
            if (arrowFn.TryGetProperty(memberName, out var arrowProp))
                return RuntimeValue.FromBoxed(arrowProp);
            // Lazy-init `fn.prototype` for function expressions (HasOwnThis).
            // Arrow functions (() => ...) don't get one per spec, but
            // function expressions (`function(){}`) do — and `instanceof`
            // walks the prototype chain looking for ctor.prototype, so
            // without this lazy init `obj instanceof FnExpr` always
            // returns false.
            if (memberName == "prototype" && arrowFn.HasOwnThis)
            {
                var proto = CreateFunctionPrototype(arrowFn);
                arrowFn.SetProperty("prototype", proto);
                return RuntimeValue.FromBoxed(proto);
            }
        }
        if (obj is SharpTSAsyncFunction asyncFn && asyncFn.TryGetProperty(memberName, out var asyncProp))
            return RuntimeValue.FromBoxed(asyncProp);
        if (obj is SharpTSAsyncArrowFunction asyncArrowFn && asyncArrowFn.TryGetProperty(memberName, out var asyncArrowProp))
            return RuntimeValue.FromBoxed(asyncArrowProp);

        // Callable objects inherit guest-defined fields from the realm's
        // Function.prototype. Own metadata/properties above still win.
        if (obj is ISharpTSCallable
            && memberName is not ("name" or "length" or "prototype")
            && GetFunctionPrototype().HasExtra(memberName))
        {
            return RuntimeValue.FromBoxed(GetFunctionPrototype().TryGetExtra(memberName));
        }
        if (obj is ISharpTSCallable && memberName == "constructor")
            return RuntimeValue.FromBoxed(GetFunctionPrototype().GetMember(memberName));

        // Date instances are ordinary extensible objects. Their built-in
        // methods remain registry-backed, while guest-defined own fields live
        // in the instance overlay.
        if (obj is SharpTSDate date && date.HasExtra(memberName))
            return RuntimeValue.FromBoxed(date.TryGetExtra(memberName));

        // RegExp instance: user-installed accessor (Object.defineProperty)
        // wins over the built-in slot. ECMA-262 §22.2 declares
        // flags/global/unicode configurable, so a throwing getter
        // must fire and propagate. Has to live above the category-handler
        // dispatch because the registered handler's `(obj, name) => member`
        // shape can't reach the interpreter to invoke the user callable.
        // Same channel handles `flags`, which by spec dynamically reads
        // each individual flag accessor via Get — we route through the
        // interpreter-aware overload of GetMember so user data-property
        // overrides (`r.global = false`) propagate.
        if (obj is SharpTSRegExp regex)
        {
            if (regex.TryGetAccessor(memberName, out var rxGetter, out _) && rxGetter != null)
                return RuntimeValue.FromBoxed(rxGetter.CallBoxed(this, []));
            if (regex.TryGetProperty(memberName, out var ownValue))
                return RuntimeValue.FromBoxed(ownValue);
            if (memberName == "flags")
            {
                var regexpPrototype = GetRegExpPrototype();
                if (regexpPrototype.GetGetter(memberName) is { } prototypeGetter)
                    return RuntimeValue.FromBoxed(
                        BindAccessorToObject(prototypeGetter, regex).CallBoxed(this, []));
                if (regexpPrototype.Fields.ContainsKey(memberName))
                    return RuntimeValue.FromBoxed(regexpPrototype.GetProperty(memberName));
                return RuntimeValue.FromBoxed(Runtime.BuiltIns.RegExpBuiltIns.GetMember(regex, memberName, this));
            }
            // ECMA-262 §22.2.6.1: `constructor` is inherited from
            // RegExp.prototype and must be the RegExp constructor itself, so
            // `(/x/).constructor === RegExp` and the §22.2.4.1 IsRegExp
            // brand check (`SameValue(newTarget, Get(O, "constructor"))`)
            // hold. Return the singleton directly — faster than a prototype
            // dictionary walk, and matches the compiled `$RegExp` behavior.
            // An own `constructor` set by user code (`re.constructor = fn`,
            // as RegExp.prototype[@@split]'s SpeciesConstructor test exercises)
            // shadows the inherited one, so yield to it when present. The
            // TryGetProperty probe is a cheap null check for the common case
            // (no own properties) and only hits the dict when one was set.
            if (memberName == "constructor")
                return RuntimeValue.FromBoxed(RegExpConstructorObject);
        }

        // Promise instances: own accessor/data properties installed via
        // Object.defineProperty resolve first, so a poisoned `constructor`
        // getter fires and propagates (test262 then/ctor-poisoned, #350). For
        // subclass instances (#242), declared fields/class getters/methods then
        // resolve before the built-in Promise members so user overrides win;
        // then/catch/finally fall through to the category dispatch
        // (PromiseBuiltIns wraps their results per SpeciesConstructor).
        if (obj is SharpTSPromise promise)
        {
            if (promise.TryGetAccessor(memberName, out var ownGetter, out _) && ownGetter != null)
                return RuntimeValue.FromBoxed(ownGetter.CallBoxed(this, []));
            if (promise.TryGetOwnProperty(memberName, out var ownProp))
                return RuntimeValue.FromBoxed(ownProp);
            if (promise is SharpTSPromiseSubclassInstance promiseSub)
            {
                if (memberName == "constructor")
                    return RuntimeValue.FromObject(promiseSub.Klass);
                var promiseGetter = promiseSub.Klass.FindGetter(memberName);
                if (promiseGetter != null)
                    return RuntimeValue.FromBoxed(promiseGetter.BindThis(promiseSub).CallBoxed(this, []));
                var promiseMethod = promiseSub.Klass.FindMethod(memberName);
                if (promiseMethod != null)
                    return RuntimeValue.FromObject(SharpTSClass.BindMethodToReceiver(promiseMethod, promiseSub));
            }
            if (memberName is "then" or "catch" or "finally")
            {
                return RuntimeValue.FromBoxed(
                    GetPromisePrototype().GetMember(memberName)
                    ?? SharpTSUndefined.Instance);
            }
        }

        // Number primitives inherit from this realm's mutable Number.prototype.
        // Consult it before the process-wide category registry so borrowed methods
        // and user expandos (for example Number.prototype.toLowerCase =
        // String.prototype.toLowerCase) are observable on numeric values.
        if (category == TypeCategory.Number)
        {
            var numberPrototype = GetNumberPrototype();
            if (numberPrototype.GetExtraGetter(memberName) is { } numberGetter)
            {
                return RuntimeValue.FromBoxed(
                    FunctionBuiltIns.CallWithThis(this, numberGetter, obj, []));
            }
            if (numberPrototype.GetMember(memberName) is { } prototypeMember)
            {
                return RuntimeValue.FromBoxed(prototypeMember switch
                {
                    StringPrototypeMethodWrapper stringMethod => stringMethod.Bind(obj),
                    NumberPrototypeMethodWrapper numberMethod => numberMethod.Bind(obj),
                    _ => prototypeMember,
                });
            }
        }

        var member = BuiltInRegistry.Instance.GetMemberByCategory(category, obj, memberName);
        if (member != null)
        {
            // Constant-wrapping members (Number.MAX_SAFE_INTEGER, …) must yield their value,
            // not the wrapper. The namespace static fast-path in EvaluateGet does this, but
            // a per-realm intrinsic deliberately bypasses that path.
            if (member is BuiltInMethod { IsConstant: true } constantMember)
                return RuntimeValue.FromBoxed(constantMember.CallBoxed(this, []));
            return RuntimeValue.FromBoxed(BindBuiltInMember(member, obj));
        }

        // RegExp instances inherit user-set properties from the realm
        // RegExp.prototype (built-in prototype mutability, #801/#474-adjacent).
        // Own properties/accessors and built-in members were already resolved
        // above; this final fallback reaches user assignments such as
        // `RegExp.prototype.enumerable = true` and user-defined prototype getters,
        // so they are visible on `new RegExp()` (and on descriptor reads, which
        // route through this same path). Built-in prototype accessors/methods are
        // handled earlier, so they take precedence and are never shadowed here.
        if (obj is SharpTSRegExp)
        {
            var rxProto = GetRegExpPrototype();
            var rxProtoGetter = rxProto.GetGetter(memberName);
            if (rxProtoGetter != null)
            {
                var bound = rxProtoGetter is SharpTSFunction rxFn ? rxFn.BindThis(obj) : rxProtoGetter;
                return RuntimeValue.FromBoxed(bound.CallBoxed(this, []));
            }
            if (rxProto.Fields.ContainsKey(memberName))
                return RuntimeValue.FromBoxed(rxProto.GetProperty(memberName));
        }

        // Every non-nullish built-in object ultimately inherits Object.prototype.
        var objectPrototypeMember = GetObjectPrototype().GetMember(memberName);
        if (objectPrototypeMember is SharpTSObjectUnboundMethod objectMethod)
            return RuntimeValue.FromObject(objectMethod.BindTo(obj));
        if (objectPrototypeMember != null)
            return RuntimeValue.FromBoxed(objectPrototypeMember);

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Property access on arrays. Checks named properties and ISharpTSPropertyAccessor
    /// before falling through to built-in array members.
    /// </summary>
    private RuntimeValue EvaluateGetOnArrayRV(object obj, string memberName)
    {
        if (obj is SharpTSArray lengthArray && memberName == "length")
            return RuntimeValue.FromNumber(lengthArray.LongLength);

        // ISharpTSPropertyAccessor check (handles SharpTSTemplateStringsArray.raw)
        if (obj is ISharpTSPropertyAccessor accessor && accessor.HasProperty(memberName))
            return RuntimeValue.FromBoxed(accessor.GetProperty(memberName));

        // Named properties from Object.defineProperty
        if (obj is SharpTSArray array && array.HasNamedProperty(memberName))
            return RuntimeValue.FromBoxed(array.GetNamedProperty(memberName));

        // Array subclass instances (#233): class getters and methods resolve
        // before built-in Array members so user overrides win; declared fields
        // were handled above as named properties (own props shadow methods).
        if (obj is SharpTSArraySubclassInstance subclassArray)
        {
            if (memberName == "constructor")
                return RuntimeValue.FromObject(subclassArray.Klass);
            var classGetter = subclassArray.Klass.FindGetter(memberName);
            if (classGetter != null)
                return RuntimeValue.FromBoxed(classGetter.BindThis(subclassArray).CallBoxed(this, []));
            var classMethod = subclassArray.Klass.FindMethod(memberName);
            if (classMethod != null)
                return RuntimeValue.FromObject(SharpTSClass.BindMethodToReceiver(classMethod, subclassArray));
        }

        // Numeric-string index on $Array — `arr["0"]` is equivalent to
        // `arr[0]` per JS semantics. ECMA-262 §10.4.2 (Array exotic objects)
        // makes string-coerced canonical numeric indices behave like ordinary
        // index access. Built-in spec algorithms (e.g. RegExp Symbol.match's
        // `Get(result, "0")`) rely on this — without it, numeric-string Get
        // returns undefined and the algorithm reads the wrong value.
        if (obj is SharpTSArray arrIdx
            && long.TryParse(memberName, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long idx)
            && idx >= 0 && idx < arrIdx.Length
            && arrIdx.HasIndex(idx))
        {
            return GetArrayIndexValue(arrIdx, idx);
        }

        if (obj is SharpTSArray arrayWithPrototype
            && arrayWithPrototype.HasExplicitPrototype)
        {
            object? inherited = GetPropertyValueFromChain(
                arrayWithPrototype.ExplicitPrototype, memberName, obj);
            return RuntimeValue.FromBoxed(
                TryBindReceiverForMethodAccess(inherited, obj) ?? inherited);
        }

        if (GetArrayPrototype().GetExtraGetter(memberName) is { } prototypeGetter)
            return BindAccessorToObject(prototypeGetter, obj).CallV2(
                this, ReadOnlySpan<RuntimeValue>.Empty);
        if (GetArrayPrototype().HasExtra(memberName))
            return RuntimeValue.FromBoxed(GetArrayPrototype().TryGetExtra(memberName));

        // Ordinary arrays inherit Array.prototype.constructor. Keep this
        // lookup after the prototype expando/accessor checks so a guest
        // override wins, and route through GetMember so deleting the built-in
        // constructor from Array.prototype is still observable.
        if (memberName == "constructor"
            && GetArrayPrototype().GetMember(memberName) is { } arrayConstructor)
        {
            return RuntimeValue.FromBoxed(arrayConstructor);
        }

        if (memberName is "toReversed" or "toSorted" or "toSpliced" or "with"
            or "find" or "findIndex"
            or "findLast" or "findLastIndex"
            && GetArrayPrototype().GetMember(memberName) is ArrayPrototypeMethodWrapper copyingMethod)
        {
            return RuntimeValue.FromObject(copyingMethod.Bind(obj));
        }

        // Standard array built-in members via category dispatch
        var member = BuiltInRegistry.Instance.GetMemberByCategory(TypeCategory.Array, obj, memberName);
        if (member != null)
            return RuntimeValue.FromBoxed(BindBuiltInMember(member, obj));

        if (GetObjectPrototype().GetMember(memberName) is SharpTSObjectUnboundMethod objectMethod)
            return RuntimeValue.FromObject(objectMethod.BindTo(obj));
        if (GetObjectPrototype().HasExtra(memberName))
            return RuntimeValue.FromBoxed(GetObjectPrototype().TryGetExtra(memberName));

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Binds a method to its receiver if needed. Methods from BuiltInTypeMemberLookup
    /// are already bound; inline BuiltInMethod instances need binding.
    /// </summary>
    private static object BindBuiltInMember(object member, object receiver)
    {
        if (member is BuiltInMethod m && !m.IsBound)
            return m.Bind(receiver);
        if (member is BuiltInAsyncMethod am)
            return am.Bind(receiver);
        return member;
    }

    /// <summary>
    /// True when <paramref name="callable"/> is a prototype-method wrapper or
    /// other ISharpTSCallable whose only role is method-dispatch — never a
    /// constructor. Used to surface TypeError on <c>new SomeMethod()</c> per
    /// ECMA-262. <see cref="BuiltInMethod"/> participates through its explicit
    /// per-method <see cref="BuiltInMethod.IsConstructor"/> flag, preserving
    /// constructor registrations while rejecting ordinary built-in methods.
    /// </summary>
    private static bool IsNonConstructorWrapper(object? callable) => callable
        is ArrayPrototypeMethodWrapper
        or StringPrototypeMethodWrapper
        or NumberPrototypeMethodWrapper
        or BooleanPrototypeMethodWrapper
        or SymbolPrototypeMethodWrapper
        or BigIntPrototypeMethodWrapper
        or SharpTSGlobalFunction
        or PromiseResolveCallback
        or PromiseRejectCallback
        or SharpTSObjectUnboundMethod
        or SharpTSArrayUnboundMethod
        or ErrorToStringCallable
        or BuiltInAsyncMethod
        or BuiltInMethod { IsConstructor: false };

    private object? EvaluateGetOnClass(SharpTSClass klass, string memberName)
    {
        // ECMA-262: every class has exactly one `prototype` (an ordinary object whose
        // props are the instance methods + constructor back-ref). Without this,
        // `Error.prototype.toString` and friends throw "Static member 'prototype' does
        // not exist on class 'Error'". The object is created lazily and cached on the
        // class, so repeated reads are reference-equal — `X.prototype === X.prototype`
        // and `Object.getPrototypeOf(new X()) === X.prototype`.
        if (memberName == "prototype")
        {
            return klass.Prototype;
        }

        // Try static auto-accessor first (TypeScript 4.9+)
        if (klass.HasStaticAutoAccessor(memberName))
        {
            return klass.GetStaticAutoAccessorValue(memberName);
        }

        // Try static getter (`static get name() { ... }`).
        var staticGetter = klass.FindStaticGetter(memberName);
        if (staticGetter != null)
        {
            return staticGetter.BindStatic(klass).CallBoxed(this, []);
        }

        // Try static method
        ISharpTSCallable? staticMethod = klass.FindStaticMethod(memberName);
        if (staticMethod != null) return staticMethod switch
        {
            SharpTSFunction fn => fn.BindStatic(klass),
            SharpTSAsyncFunction afn => afn.BindStatic(klass),
            SharpTSGeneratorFunction gfn => gfn.BindStatic(klass),
            SharpTSAsyncGeneratorFunction agfn => agfn.BindStatic(klass),
            _ => staticMethod,
        };

        // Try static property
        if (klass.HasStaticProperty(memberName))
        {
            return klass.GetStaticProperty(memberName);
        }

        // Function-object slots: every class exposes `name` and `length` per
        // spec. Falls back to the class name / 0 only when the user hasn't
        // shadowed them with a static property (handled above).
        if (memberName == "name") return klass.Name;
        if (memberName == "length") return (double)klass.Arity();

        // Class constructors are function objects and therefore inherit the
        // ordinary Object.prototype methods through Function.prototype.
        if (InheritedObjectPrototypeMember(klass, memberName) is { } inherited)
            return inherited;

        // Promise subclasses (#242) inherit the Promise static side
        // (resolve/reject/all/race/allSettled/any/withResolvers); inherited
        // statics construct subclass-typed result promises.
        if (klass is SharpTSPromiseClass promiseKlass)
        {
            var promiseStatic = Runtime.BuiltIns.PromiseBuiltIns.GetStaticMethod(memberName, promiseKlass);
            if (promiseStatic != null) return promiseStatic;
        }

        // ECMA-262 §7.3.2 (Get): reading an absent own/inherited property
        // returns `undefined` — it never throws. A statically-typed
        // `Klass.missing` is already rejected at compile time (TS2339), so
        // reaching here means the read came through an `any`/dynamic value
        // position; mirror compiled mode and JS by yielding `undefined`.
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Evaluates property access on a namespace.
    /// </summary>
    private static object? EvaluateGetOnNamespace(SharpTSNamespace nsObj, string memberName)
    {
        if (nsObj.HasMember(memberName))
        {
            return nsObj.Get(memberName);
        }
        throw new InterpreterException($"'{memberName}' does not exist on namespace '{nsObj.Name}'.");
    }

    /// <summary>
    /// Evaluates property access on a class instance, returning RuntimeValue directly.
    /// </summary>
    private RuntimeValue EvaluateGetOnInstanceRV(SharpTSInstance instance, Token memberName)
    {
        instance.SetInterpreter(this);
        if (instance.GetOwnPropertyDescriptor(memberName.Lexeme) is not null)
            return instance.GetRV(memberName);

        // The mutable prototype overlay shadows the class's declared method
        // table. This is observable for built-ins such as
        // `Error.prototype.toString = Object.prototype.toString`.
        var prototype = instance.RuntimeClass.Prototype;
        if (prototype.GetExtraGetter(memberName.Lexeme) is { } getter)
            return BindAccessorToObject(getter, instance).CallV2(
                this, ReadOnlySpan<RuntimeValue>.Empty);
        if (prototype.HasExtra(memberName.Lexeme))
        {
            object? value = prototype.TryGetExtra(memberName.Lexeme);
            return RuntimeValue.FromBoxed(
                TryBindReceiverForMethodAccess(value, instance) ?? value);
        }

        RuntimeValue resolved = instance.GetRV(memberName);
        if (instance.HasProperty(memberName.Lexeme)) return resolved;

        return RuntimeValue.FromBoxed(
            InheritedObjectPrototypeMember(instance, memberName.Lexeme)
                ?? SharpTSUndefined.Instance);
    }

    /// <summary>
    /// Binds the dynamic receiver when a method-bearing value is read off an object literal as
    /// <c>obj.method</c>. Covers ordinary function-expression / object-method shorthand
    /// (<see cref="SharpTSArrowFunction"/> with <c>HasOwnThis</c>) and — for #775 — generator function
    /// expressions / object generator methods in both their lifted declaration form
    /// (<see cref="SharpTSGeneratorFunction"/> / <see cref="SharpTSAsyncGeneratorFunction"/> with
    /// <c>HasDynamicThis</c>) and their in-place expression form
    /// (<see cref="SharpTSArrowGeneratorFunction"/> / <see cref="SharpTSAsyncArrowGeneratorFunction"/> with
    /// <c>HasOwnThis</c>, left in place when they close over a block-scoped binding). Returns null when the
    /// value is not a receiver-bound method (caller returns it unchanged).
    /// </summary>
    private static ISharpTSCallable? TryBindReceiverForMethodAccess(object? value, object receiver) => value switch
    {
        SharpTSArrowFunction af when af.HasOwnThis => af.Bind(receiver),
        SharpTSArrowGeneratorFunction sag when sag.HasOwnThis => sag.Bind(receiver),
        SharpTSAsyncArrowGeneratorFunction saag when saag.HasOwnThis => saag.Bind(receiver),
        SharpTSGeneratorFunction gf when gf.HasDynamicThis => gf.BindToReceiver(receiver),
        SharpTSAsyncGeneratorFunction agf when agf.HasDynamicThis => agf.BindToReceiver(receiver),
        StringPrototypeMethodWrapper stringMethod => stringMethod.Bind(receiver),
        NumberPrototypeMethodWrapper numberMethod => numberMethod.Bind(receiver),
        BooleanPrototypeMethodWrapper booleanMethod => booleanMethod.Bind(receiver),
        _ => null,
    };

    /// <summary>
    /// Supplies <c>this</c> to an *unbound* built-in prototype method reached through a member
    /// call (<c>obj.m()</c>). <see cref="SharpTSObjectUnboundMethod"/> and
    /// <see cref="SharpTSArrayUnboundMethod"/> take their receiver as the first argument when
    /// they have no bound <c>this</c>, so the classic
    /// <c>arr.getClass = Object.prototype.toString; arr.getClass()</c> idiom (pervasive in
    /// Test262's Sputnik suite) otherwise throws "requires a receiver". ECMA-262 §13.3.6.1
    /// builds a Reference Record for a member call, and its base is <c>this</c>.
    /// <para>
    /// Deliberately separate from <see cref="TryBindReceiverForMethodAccess"/>: that helper also
    /// runs on plain property *reads*, where rebinding would break reference identity
    /// (<c>obj.toString === Object.prototype.toString</c> must hold). This one is call-site only.
    /// </para>
    /// </summary>
    private static ISharpTSCallable? TryBindUnboundBuiltInReceiver(object? callee, object? receiver)
    {
        if (receiver is null or SharpTSUndefined) return null;
        return callee switch
        {
            SharpTSObjectUnboundMethod m when !m.HasBoundThis => m.BindTo(receiver),
            SharpTSArrayUnboundMethod m when !m.HasBoundThis => m.BindTo(receiver),
            // These methods have complete generic array-like dispatch in the wrapper. Other
            // methods need additional live Get/Set semantics before copied calls can
            // be rebound without changing their observable behavior.
            ArrayPrototypeMethodWrapper m when m.FunctionName is
                "join" or "slice" or "concat" or "pop" or "push" or "shift" or "unshift" or "reverse" or "fill" or "copyWithin" or "sort" or "splice" or "toLocaleString" or "toReversed" or "toSpliced"
                => m.Bind(receiver),
            StringPrototypeMethodWrapper m => m.Bind(receiver),
            NumberPrototypeMethodWrapper m => m.Bind(receiver),
            BooleanPrototypeMethodWrapper m => m.Bind(receiver),
            ErrorToStringCallable m => m.Bind(receiver),
            BuiltInAsyncMethod m => m.Bind(receiver),
            BuiltInMethod m when !m.IsBound
                && m.FunctionName is "call" or "apply" or "bind"
                    or "catch" or "finally" or "resolve" => m.Bind(receiver),
            _ => null,
        };
    }

    /// <summary>
    /// Applies both receiver-binding passes to a member-call callee
    /// (<c>obj.m()</c> / <c>obj[k]()</c>). Returns the callee unchanged when neither applies.
    /// </summary>
    private static object? BindMemberCallReceiver(object? callee, object? receiver)
    {
        if (receiver is null) return callee;
        callee = TryBindReceiverForMethodAccess(callee, receiver) ?? callee;
        return TryBindUnboundBuiltInReceiver(callee, receiver) ?? callee;
    }

    /// <summary>
    /// Resolves <paramref name="memberName"/> on this realm's <c>Object.prototype</c>, bound to
    /// <paramref name="receiver"/>. Every built-in object — including the built-in *prototype*
    /// objects themselves (<c>Array.prototype</c>, <c>Function.prototype</c>, …) and the
    /// constructors — sits at the bottom of the ordinary-object prototype chain, so
    /// <c>Array.prototype.hasOwnProperty(…)</c> and <c>Array.prototype.isPrototypeOf(x)</c> must
    /// resolve. Those arms of the fallback dispatcher return early with their own member table,
    /// so each one calls this before giving up. Returns null when Object.prototype has no such
    /// member (caller yields <c>undefined</c>).
    /// </summary>
    private object? InheritedObjectPrototypeMember(object receiver, string memberName)
    {
        var protoMember = GetObjectPrototype().GetMember(memberName);
        return protoMember is SharpTSObjectUnboundMethod unbound
            ? unbound.BindTo(receiver)
            : protoMember;
    }

    /// <summary>
    /// Boxed adapter over <see cref="EvaluateGetOnRecordRV"/> — the single implementation of
    /// record property reads. (A previous hand-maintained boxed copy had drifted: it stopped the
    /// __proto__ walk at a <see cref="SharpTSInstance"/> prototype, so field reads through
    /// `Object.create(new Point())`-style chains resolved to undefined for boxed callers.)
    /// </summary>
    private object? EvaluateGetOnRecord(SharpTSObject simpleObj, string memberName) =>
        EvaluateGetOnRecordRV(simpleObj, memberName).ToObject();

    /// <summary>
    /// Evaluates property access on a record/object literal, walking the __proto__ chain when the
    /// property is not an own property (JS spec: property access traverses the prototype chain
    /// until a match or null). The constructor-function pattern relies on this so methods assigned
    /// via `Foo.prototype.x = ...` are reachable on `new Foo()` instances — Lodash's MapCache
    /// (lodash.js ~2177) does `this.clear()` in its ctor where `clear` lives on
    /// `MapCache.prototype`; without the walk it resolves to undefined.
    /// </summary>
    private RuntimeValue EvaluateGetOnRecordRV(SharpTSObject simpleObj, string memberName)
    {
        // Check for getter first on the own object
        var getter = simpleObj.GetGetter(memberName);
        if (getter != null)
        {
            var boundGetter = BindAccessorToObject(getter, simpleObj);
            return boundGetter.CallV2(this, ReadOnlySpan<RuntimeValue>.Empty);
        }

        if (simpleObj.HasProperty(memberName))
        {
            var value = simpleObj.GetProperty(memberName);
            if (!simpleObj.ShouldPreserveCallableValueIdentity(memberName)
                && TryBindReceiverForMethodAccess(value, simpleObj) is { } boundMethod)
                return RuntimeValue.FromObject(boundMethod);
            return RuntimeValue.FromBoxed(value);
        }

        // Prototype-chain fallback. Walks via __proto__ (which SharpTSObject
        // maps from its Prototype property for Object.create-linked objects).
        // Handles both SharpTSObject and SharpTSInstance prototypes — the
        // latter is the case for `Object.create(new Point(...))`.
        object? current = simpleObj.HasProperty("__proto__") ? simpleObj.GetProperty("__proto__") : null;
        for (int i = 0; i < 64 && current != null; i++)
        {
            if (current is SharpTSProxy proxy)
                return RuntimeValue.FromBoxed(
                    proxy.TrapGet(memberName, this, simpleObj));

            if (current is SharpTSObject proto)
            {
                var protoGetter = proto.GetGetter(memberName);
                if (protoGetter != null)
                {
                    var boundProtoGetter = BindAccessorToObject(protoGetter, simpleObj);
                    return boundProtoGetter.CallV2(this, ReadOnlySpan<RuntimeValue>.Empty);
                }
                if (proto.HasProperty(memberName))
                {
                    var value = proto.GetProperty(memberName);
                    if (TryBindReceiverForMethodAccess(value, simpleObj) is { } boundMethod)
                        return RuntimeValue.FromObject(boundMethod);
                    return RuntimeValue.FromBoxed(value);
                }
                object? next = proto.HasProperty("__proto__") ? proto.GetProperty("__proto__") : null;
                if (ReferenceEquals(next, proto)) break;
                current = next;
                continue;
            }
            // `function Foo(){}; Foo.prototype = new Array(1,2,3)` — an array standing in as a
            // prototype. Instances must inherit both its own indexed/named data and the
            // Array.prototype methods, the latter applied to the *original* receiver so
            // `f.every(cb)` reads `f.length` (ECMA-262 §23.1.3 starts every method with
            // `O = ToObject(this value)`).
            if (current is SharpTSArray protoArray)
            {
                if (protoArray.HasNamedProperty(memberName))
                    return RuntimeValue.FromBoxed(protoArray.GetNamedProperty(memberName));
                if (GetArrayPrototype().HasExtra(memberName))
                    return RuntimeValue.FromBoxed(GetArrayPrototype().TryGetExtra(memberName));
                if (GetArrayPrototype().GetMember(memberName) is { } arrayProtoMember)
                {
                    return RuntimeValue.FromBoxed(
                        arrayProtoMember is ArrayPrototypeMethodWrapper wrapper
                            ? wrapper.Bind(simpleObj)
                            : arrayProtoMember);
                }
                break;
            }
            if (current is SharpTSInstance protoInst)
            {
                if (protoInst.HasField(memberName))
                {
                    return RuntimeValue.FromBoxed(protoInst.GetRawField(memberName));
                }
                // Walk past the instance to its class's prototype chain.
                // For simplicity stop here — class-method dispatch is handled
                // elsewhere by SharpTSInstance's runtime methods.
                break;
            }
            break;
        }

        // Boxed primitive method dispatch: `(new Number(5)).toFixed(2)` etc.
        // Delegate through to the underlying primitive value's built-in methods.
        if (simpleObj.HasProperty("__primitiveType")
            && simpleObj.GetProperty("__primitiveType") is string primitiveType
            && simpleObj.HasProperty("__primitiveValue"))
        {
            if (primitiveType == "String")
            {
                if (GetStringPrototype().GetExtraGetter(memberName) is { } stringGetter)
                    return BindAccessorToObject(stringGetter, simpleObj).CallV2(
                        this, ReadOnlySpan<RuntimeValue>.Empty);
                if (GetStringPrototype().GetMember(memberName) is { } stringMember)
                    return RuntimeValue.FromBoxed(stringMember);
            }
            if (primitiveType == "Number")
            {
                if (GetNumberPrototype().GetExtraGetter(memberName) is { } numberGetter)
                    return BindAccessorToObject(numberGetter, simpleObj).CallV2(
                        this, ReadOnlySpan<RuntimeValue>.Empty);
                if (GetNumberPrototype().GetMember(memberName) is { } numberMember)
                    return RuntimeValue.FromBoxed(numberMember);
            }
            if (primitiveType == "Boolean"
                && GetBooleanPrototype().GetMember(memberName) is { } booleanMember)
                return RuntimeValue.FromBoxed(booleanMember);
            if (primitiveType == "BigInt"
                && GetBigIntPrototype().GetMember(memberName) is { } bigIntMember)
            {
                return RuntimeValue.FromBoxed(
                    bigIntMember is BigIntPrototypeMethodWrapper wrapper
                        ? wrapper.Bind(simpleObj)
                        : bigIntMember);
            }

            // These wrappers resolve exclusively through their mutable realm
            // prototype. Falling back to the primitive registry would resurrect
            // a deleted method such as Number.prototype.toString.
            if (primitiveType is not ("String" or "Number" or "Boolean"))
            {
                var pv = simpleObj.GetProperty("__primitiveValue");
                if (pv != null)
                {
                    var dispatched = BuiltInRegistry.Instance.GetInstanceMember(pv, memberName);
                    if (dispatched != null) return RuntimeValue.FromBoxed(dispatched);
                }
            }
        }

        // ECMA-262: every ORDINARY object inherits Object.prototype's methods
        // (hasOwnProperty, propertyIsEnumerable, isPrototypeOf, toString,
        // valueOf). Resolve them as a FINAL fallback — after own properties and
        // the __proto__ chain — so a user override always wins, and bound to the
        // receiver so `obj.hasOwnProperty(k)` passes `obj` as the target.
        // A genuine null-prototype object (Object.create(null), groupBy result)
        // inherits nothing, so it is excluded.
        if (!simpleObj.IsNullPrototype)
        {
            // A guest-installed accessor on Object.prototype is inherited like any other:
            // its getter runs with the *receiver* as `this`, not the prototype.
            if (GetObjectPrototype().GetExtraGetter(memberName) is { } inheritedGetter)
                return BindAccessorToObject(inheritedGetter, simpleObj)
                    .CallV2(this, ReadOnlySpan<RuntimeValue>.Empty);
            var prototypeMember = GetObjectPrototype().GetMember(memberName);
            if (prototypeMember is SharpTSObjectUnboundMethod protoMethod)
                return RuntimeValue.FromObject(protoMethod.BindTo(simpleObj));
            if (prototypeMember is not null)
                return RuntimeValue.FromBoxed(prototypeMember);
        }

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Binds an accessor function to an object for 'this' binding.
    /// </summary>
    private static ISharpTSCallable BindAccessorToObject(ISharpTSCallable accessor, object obj)
    {
        if (accessor is SharpTSFunction function)
        {
            return function.BindThis(obj);
        }
        if (accessor is SharpTSArrowFunction arrow && arrow.HasOwnThis)
        {
            return arrow.Bind(obj);
        }
        // Built-in accessor getters (e.g. the generic RegExp.prototype `flags`
        // getter) carry their receiver through Bind so direct access like
        // `RegExp.prototype.flags` invokes them with the right `this`.
        if (accessor is Runtime.BuiltIns.BuiltInMethod bm && !bm.IsBound)
        {
            return bm.Bind(obj);
        }
        // For callables that don't support binding, return as-is
        return accessor;
    }

    /// <summary>
    /// Fallback for property access on built-in types and ISharpTSPropertyAccessor.
    /// </summary>
    private object? EvaluateGetOnFallback(object? obj, string memberName)
    {
        if (obj is BoundFunction boundFunction)
        {
            if (boundFunction.TryGetAccessor(memberName, out var getter, out _)
                && getter != null)
                return FunctionBuiltIns.CallWithThis(this, getter, boundFunction, []);
            if (boundFunction.TryGetProperty(memberName, out var ownValue))
                return ownValue;
            if (FunctionBuiltIns.GetMember(boundFunction, memberName) is { } functionMember)
                return functionMember;
            if (GetFunctionPrototype().GetExtraGetter(memberName) is { } prototypeGetter)
                return BindAccessorToObject(prototypeGetter, boundFunction).CallBoxed(this, []);
            if (GetFunctionPrototype().HasExtra(memberName))
                return GetFunctionPrototype().TryGetExtra(memberName);
            return InheritedObjectPrototypeMember(boundFunction, memberName)
                ?? SharpTSUndefined.Instance;
        }

        // JS functions are objects — support arbitrary property access on
        // user-defined functions. Built-in keys (`name`, `length`) come
        // from the function itself; user keys come from the property bag.
        if (obj is SharpTSFunction fn)
        {
            if (fn.TryGetAccessor(memberName, out var getter, out _) && getter != null)
                return getter.CallBoxed(this, []);
            if (fn.TryGetProperty(memberName, out var v)) return v;
            if (memberName == "name") return fn.TryGetProperty("name", out var n) ? n : "";
            if (memberName == "length") return (double)fn.Arity();
            if (memberName == "prototype")
            {
                if (!fn.TryGetProperty("prototype", out var proto))
                {
                    proto = CreateFunctionPrototype(fn);
                    fn.SetProperty("prototype", proto);
                }
                return proto;
            }
            if (memberName == "constructor")
                return GetFunctionPrototype().GetMember(memberName);
            if (GetFunctionPrototype().HasExtra(memberName))
                return GetFunctionPrototype().TryGetExtra(memberName);
            if (GetObjectPrototype().HasExtra(memberName))
                return GetObjectPrototype().TryGetExtra(memberName);
            return SharpTSUndefined.Instance;
        }
        if (obj is SharpTSArrowFunction arrowFn2)
        {
            if (arrowFn2.TryGetAccessor(memberName, out var arrowGetter, out _) && arrowGetter != null)
                return arrowGetter.CallBoxed(this, []);
            if (arrowFn2.TryGetProperty(memberName, out var arrowProp2)) return arrowProp2;
            if (memberName == "length") return (double)arrowFn2.Arity();
            if (memberName == "constructor")
                return GetFunctionPrototype().GetMember(memberName);
            if (GetFunctionPrototype().HasExtra(memberName))
                return GetFunctionPrototype().TryGetExtra(memberName);
            if (GetObjectPrototype().HasExtra(memberName))
                return GetObjectPrototype().TryGetExtra(memberName);
            return SharpTSUndefined.Instance;
        }
        if (obj is SharpTSAsyncFunction asyncFn2)
        {
            if (asyncFn2.TryGetProperty(memberName, out var asyncProp2)) return asyncProp2;
            return SharpTSUndefined.Instance;
        }
        if (obj is SharpTSAsyncArrowFunction asyncArrowFn2)
        {
            if (asyncArrowFn2.TryGetProperty(memberName, out var asyncArrowProp2)) return asyncArrowProp2;
            return SharpTSUndefined.Instance;
        }

        // Array global constructor: resolves `Array.prototype`, `Array.from`, etc.
        if (obj is SharpTSArrayGlobal arrGlobal)
        {
            if (arrGlobal.GetMember(memberName) is { } ownMember)
                return ownMember;
            var functionPrototype = GetFunctionPrototype();
            if (functionPrototype.GetExtraGetter(memberName) is { } inheritedGetter)
                return BindAccessorToObject(inheritedGetter, arrGlobal).CallBoxed(this, []);
            if (functionPrototype.HasExtra(memberName))
                return functionPrototype.TryGetExtra(memberName);
            return InheritedObjectPrototypeMember(arrGlobal, memberName)
                ?? SharpTSUndefined.Instance;
        }
        if (obj is SharpTSArrayPrototype arrProto)
        {
            if (arrProto.GetExtraGetter(memberName) is { } getter)
                return BindAccessorToObject(getter, arrProto).CallBoxed(this, []);
            return arrProto.GetMember(memberName)
                ?? InheritedObjectPrototypeMember(arrProto, memberName)
                ?? SharpTSUndefined.Instance;
        }
        if (obj is SharpTSArrayUnboundMethod unbound)
        {
            // call/apply/bind on unbound prototype methods go through FunctionBuiltIns.
            var fnMember = FunctionBuiltIns.GetMember(unbound, memberName);
            if (fnMember != null) return fnMember;
            return SharpTSUndefined.Instance;
        }
        if (obj is SharpTSFunctionGlobal fnGlobal)
        {
            return fnGlobal.GetMember(memberName)
                ?? InheritedObjectPrototypeMember(fnGlobal, memberName)
                ?? SharpTSUndefined.Instance;
        }
        if (obj is SharpTSFunctionPrototype fnProto)
        {
            return fnProto.GetMember(memberName)
                ?? InheritedObjectPrototypeMember(fnProto, memberName)
                ?? SharpTSUndefined.Instance;
        }
        if (obj is SharpTSFunctionProtoToString fnToStr)
        {
            var fnMember = FunctionBuiltIns.GetMember(fnToStr, memberName);
            if (fnMember != null) return fnMember;
            return SharpTSUndefined.Instance;
        }
        if (obj is SharpTSObjectUnboundMethod objUnbound)
        {
            var fnMember = FunctionBuiltIns.GetMember(objUnbound, memberName);
            if (fnMember != null) return fnMember;
            return SharpTSUndefined.Instance;
        }
        // Built-in constructor passed through a variable (e.g. `var D = Date; D.now()`).
        // Resolve static methods via the constructor's own GetMember.
        if (obj is SharpTSBuiltInConstructor ctor)
        {
            if (TryGetBuiltInConstructorProperty(ctor, memberName, out var ownValue))
                return ownValue;
            if (IsBuiltInConstructorPropertyDeleted(ctor, memberName))
                return SharpTSUndefined.Instance;
            // RegExp.prototype is realm-local: route through the Interpreter's
            // own prototype object so `delete RegExp.prototype[Symbol.split]`
            // and `Object.defineProperty(RegExp.prototype, …)` don't leak
            // across Interpreter instances (the constructor is held in a
            // process-wide static FrozenDictionary).
            if (memberName == "prototype" && ctor.Name == BuiltInNames.RegExp)
                return GetRegExpPrototype();
            // ECMA-262 §27.2.3.1: %Promise%.prototype. Without this, `Promise.prototype`
            // read as undefined and every Promise/prototype/* access died on it.
            if (memberName == "prototype" && ctor.Name == BuiltInNames.Promise)
                return GetPromisePrototype();
            if (memberName == "prototype" && ctor.Name == BuiltInNames.Symbol)
                return GetSymbolPrototype();
            if (memberName == "prototype" && ctor.Name == BuiltInNames.Date)
                return GetDatePrototype();
            var ctorMember = ctor.GetMember(memberName);
            // Materialize constant-wrapping members (e.g. Symbol.species via an
            // alias: `const S = Symbol; S.species`) the same way the syntactic
            // path in EvaluateGet does — otherwise the alias path returns the
            // BuiltInMethod wrapper and identity with the direct form breaks.
            if (ctorMember is BuiltInMethod { IsConstant: true } ctorConstant)
                return ctorConstant.CallBoxed(this, []);
            return ctorMember
                ?? FunctionBuiltIns.GetMember(ctor, memberName)
                ?? SharpTSUndefined.Instance;
        }

        // Handle plain Dictionary<string, object?> objects (e.g., segment items from Intl.Segments)
        if (obj is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(memberName, out var val) ? val : SharpTSUndefined.Instance;
        }

        // globalThis.Math (and other per-realm intrinsics) must resolve to this
        // realm's instance, not the process-global singleton, so
        // `globalThis.Math === Math` within a realm. A guest own-assignment
        // (`globalThis.Math = x`) takes precedence per ECMA-262.
        if (obj is SharpTSGlobalThis globalObject
            && globalObject.TryGetUserAccessor(memberName, out var globalGetter, out _))
        {
            return globalGetter is null
                ? SharpTSUndefined.Instance
                : BindAccessorToObject(globalGetter, globalObject).CallBoxed(this, []);
        }
        if (obj is SharpTSGlobalThis gtAccessor
            && !gtAccessor.HasUserProperty(memberName)
            && TryGetRealmIntrinsic(memberName, out var gtIntrinsic))
        {
            return gtIntrinsic;
        }

        // Handle objects that implement ISharpTSPropertyAccessor (e.g., SharpTSTemplateStringsArray)
        // Only return if the accessor has this property, otherwise fall through to built-ins
        if (obj is ISharpTSPropertyAccessor accessor && accessor.HasProperty(memberName))
        {
            return accessor.GetProperty(memberName);
        }

        // Handle named properties on arrays (added via Object.defineProperty)
        if (obj is SharpTSArray array && array.HasNamedProperty(memberName))
        {
            return array.GetNamedProperty(memberName);
        }

        // Array subclass instances (#233): class getters and methods resolve
        // before the built-in Array members so user overrides win; fields were
        // handled above as named properties (own properties shadow methods).
        if (obj is SharpTSArraySubclassInstance subclassArray)
        {
            if (memberName == "constructor")
                return subclassArray.Klass;
            var classGetter = subclassArray.Klass.FindGetter(memberName);
            if (classGetter != null)
                return classGetter.BindThis(subclassArray).CallBoxed(this, []);
            var classMethod = subclassArray.Klass.FindMethod(memberName);
            if (classMethod != null)
                return SharpTSClass.BindMethodToReceiver(classMethod, subclassArray);
        }

        if (obj is SharpTSArray)
        {
            if (GetArrayPrototype().GetExtraGetter(memberName) is { } getter)
                return BindAccessorToObject(getter, obj).CallBoxed(this, []);
            if (GetArrayPrototype().HasExtra(memberName))
                return GetArrayPrototype().TryGetExtra(memberName);
            if (memberName == "constructor"
                && GetArrayPrototype().GetMember(memberName) is { } arrayConstructor)
            {
                return arrayConstructor;
            }
        }

        // Promise instances: mirror the RV-path arm — own accessor/data
        // properties (poisoned `constructor` getter, #350) before subclass
        // fields/getters/methods, all before the built-in Promise members.
        if (obj is SharpTSPromise promise)
        {
            if (promise.TryGetAccessor(memberName, out var ownGetter, out _) && ownGetter != null)
                return ownGetter.CallBoxed(this, []);
            if (promise.TryGetOwnProperty(memberName, out var promiseOwnProp))
                return promiseOwnProp;
            if (promise is SharpTSPromiseSubclassInstance promiseSub)
            {
                if (memberName == "constructor")
                    return promiseSub.Klass;
                var promiseGetter = promiseSub.Klass.FindGetter(memberName);
                if (promiseGetter != null)
                    return promiseGetter.BindThis(promiseSub).CallBoxed(this, []);
                var promiseMethod = promiseSub.Klass.FindMethod(memberName);
                if (promiseMethod != null)
                    return SharpTSClass.BindMethodToReceiver(promiseMethod, promiseSub);
            }
        }

        // Callable objects inherit guest-defined fields from this realm's
        // Function.prototype. Resolve that layer before the registry: registered
        // callable types otherwise report "known type, missing member" and return
        // undefined before the generic callable fallback can see the prototype.
        if (obj is ISharpTSCallable
            && memberName is not ("name" or "length" or "prototype"))
        {
            if (GetFunctionPrototype().GetExtraGetter(memberName) is { } getter)
                return BindAccessorToObject(getter, obj).CallBoxed(this, []);
            if (GetFunctionPrototype().HasExtra(memberName))
                return GetFunctionPrototype().TryGetExtra(memberName);
        }

        // Handle built-in instance members: strings, arrays, Math, Promise
        if (obj != null)
        {
            // Single registry lookup - TryGetInstanceMember returns both member and whether type is known
            var (member, isBuiltInType) = BuiltInRegistry.Instance.TryGetInstanceMember(obj, memberName);
            if (member != null)
            {
                // Bind methods to their receiver, return properties and prototype
                // adapters directly. Prototype adapters receive `this` at the
                // member-call site so ordinary reads preserve function identity.
                if (member is BuiltInMethod m)
                    return obj is SharpTSPromisePrototype ? m : m.Bind(obj);
                if (member is BuiltInAsyncMethod am)
                    return obj is SharpTSPromisePrototype ? am : am.Bind(obj);
                return member;
            }

            // If we have a built-in type but didn't find the member, return undefined
            // (JavaScript semantics: accessing a non-existent property returns undefined)
            if (isBuiltInType)
            {
                var objectPrototypeMember = GetObjectPrototype().GetMember(memberName);
                if (objectPrototypeMember is SharpTSObjectUnboundMethod objectMethod)
                    return objectMethod.BindTo(obj);
                if (objectPrototypeMember != null)
                    return objectPrototypeMember;
                return SharpTSUndefined.Instance;
            }
        }

        // Generic callable fallback: ISharpTSCallable values that aren't
        // covered by a specific arm (e.g. raw `BuiltInMethod` reached as the
        // value of `RegExp.prototype.exec`) inherit Function.prototype + the
        // Object.prototype chain. propertyHelper.js's verifyNotWritable etc.
        // call `.hasOwnProperty('length')` on these callables, so resolve
        // call/apply/bind/length/name/toString here, plus the relevant
        // Object.prototype methods, before throwing.
        if (obj is ISharpTSCallable callable)
        {
            if (memberName == "constructor")
                return GetFunctionPrototype().GetMember(memberName);
            if (GetFunctionPrototype().HasExtra(memberName))
                return GetFunctionPrototype().TryGetExtra(memberName);
            var fnMember = FunctionBuiltIns.GetMember(callable, memberName);
            if (fnMember != null) return fnMember;
            var protoMember = GetObjectPrototype().GetMember(memberName);
            if (protoMember is Runtime.Types.SharpTSObjectUnboundMethod ub)
                return ub.BindTo(obj);
            if (protoMember != null) return protoMember;
            return SharpTSUndefined.Instance;
        }

        throw new InterpreterException("Only instances and objects have properties.");
    }

    /// <summary>
    /// Evaluates a property assignment expression (dot notation with assignment).
    /// </summary>
    /// <param name="set">The property assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Supports static property assignment on classes, instance field assignment,
    /// and simple object property assignment.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    private RuntimeValue EvaluateSet(Expr.Set set)
        => EvaluateSetCore(_syncContext, set).GetAwaiter().GetResult();

    /// <summary>
    /// Core property-assignment logic shared by the sync and async evaluators.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateSetCore(IEvaluationContext ctx, Expr.Set set)
    {
        object? obj = (await ctx.EvaluateExprAsync(set.Object)).ToObject();
        object? value = (await ctx.EvaluateExprAsync(set.Value)).ToObject();
        return EvaluateSetOnObjectRV(set, obj, value);
    }

    /// <summary>
    /// ECMA-262 §7.3.4 Set(O, P, V, Throw) abstract operation. Writes a
    /// string-named property on <paramref name="obj"/>, honoring user-defined
    /// setters and propagating their thrown errors. Mirrors
    /// <see cref="GetProperty"/> for the write side. Used by spec algorithms
    /// in built-in helpers (RegExp Symbol.* set lastIndex via this).
    /// </summary>
    internal void SetProperty(object? obj, string name, object? value)
    {
        var syntheticName = new Token(TokenType.IDENTIFIER, name, null, 0);
        var syntheticSet = new Expr.Set(null!, syntheticName, null!);
        EvaluateSetOnObject(syntheticSet, obj, value, forceStrict: true);
    }

    /// <summary>
    /// Core property assignment logic, shared between sync and async evaluation.
    /// Returns RuntimeValue directly to avoid boxing.
    /// </summary>
    private RuntimeValue EvaluateSetOnObjectRV(Expr.Set set, object? obj, object? value)
    {
        return RuntimeValue.FromBoxed(EvaluateSetOnObject(set, obj, value));
    }

    /// <summary>
    /// Core property assignment logic, shared between sync and async evaluation.
    /// Uses TypeCategoryResolver for fast dispatch on common types.
    /// </summary>
    private object? EvaluateSetOnObject(
        Expr.Set set,
        object? obj,
        object? value,
        bool forceStrict = false)
    {
        // ECMA-262 PutValue: RequireObjectCoercible throws a guest TypeError on a
        // null/undefined base before any setter dispatch (#733). The RHS value is
        // already evaluated by EvaluateSet, matching the spec's PutValue-after-RHS
        // ordering, so side effects in the RHS have run by this point.
        if (obj == null || obj is SharpTSUndefined)
        {
            ThrowCannotSetProperty(obj, set.Name.Lexeme);
        }

        bool strictMode = forceStrict || _environment.IsStrictMode;

        bool hasWritableOwnLength = obj switch
        {
            SharpTSFunction function => function.GetOwnPropertyDescriptor("length") is { Writable: true },
            SharpTSArrowFunction function => function.GetOwnPropertyDescriptor("length") is { Writable: true },
            _ => false
        };

        if (strictMode && set.Name.Lexeme == "length" && !hasWritableOwnLength
            && obj is SharpTSFunction or SharpTSArrowFunction
                or SharpTSAsyncFunction or SharpTSAsyncArrowFunction)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Cannot assign to read only property 'length' of function"));
        }

        // Proxy interception - must be before any other dispatch
        if (obj is SharpTSProxy proxy)
        {
            bool assigned = proxy.TrapSetProperty(
                set.Name.Lexeme, value, this, proxy);
            if (!assigned && strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Proxy set trap rejected property '{set.Name.Lexeme}'"));
            return value;
        }

        // JS functions are objects — allow property assignment on them.
        if (obj is SharpTSFunction userFn)
        {
            // Accessor set path (Object.defineProperty setter).
            if (userFn.TryGetAccessor(set.Name.Lexeme, out _, out var setter) && setter != null)
            {
                setter.CallBoxed(this, [value]);
                return value;
            }
            userFn.SetProperty(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSArrowFunction arrowFn)
        {
            if (arrowFn.TryGetAccessor(set.Name.Lexeme, out _, out var arrowSetter) && arrowSetter != null)
            {
                arrowSetter.CallBoxed(this, [value]);
                return value;
            }
            arrowFn.SetProperty(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSAsyncFunction asyncFn)
        {
            asyncFn.SetProperty(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSAsyncArrowFunction asyncArrowFn)
        {
            asyncArrowFn.SetProperty(set.Name.Lexeme, value);
            return value;
        }

        // Math is an extensible object per ECMA-262 — user code is allowed
        // to attach arbitrary properties to it (Test262 tests exercise this
        // pattern by assigning `Math.length` / `Math[i]` before calling
        // Array.prototype.* with Math as the receiver). Route to the
        // backing dictionary on SharpTSMath.
        if (obj is SharpTSMath math)
        {
            math.SetExtra(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSJSON json)
        {
            json.SetExtra(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSBuiltInConstructor
            { Name: BuiltInNames.Promise or BuiltInNames.RegExp }
            && set.Name.Lexeme == "prototype")
        {
            if (forceStrict || _environment.IsStrictMode)
                throw new ThrowException(new SharpTSTypeError(
                    "Cannot assign to read only property 'prototype' of function"));
            return value;
        }
        if (obj is SharpTSBuiltInConstructor builtInConstructor)
        {
            SetBuiltInConstructorProperty(builtInConstructor, set.Name.Lexeme, value);
            return value;
        }

        // Every built-in prototype singleton is an ordinary mutable object per ECMA-262
        // (Object/Array/String/Number/Boolean/Function.prototype). Test262 assigns
        // indexed elements and `length` onto them before calling Array.prototype.* with
        // a primitive receiver, and patches them to exercise inherited-property paths.
        if (obj is ISharpTSMutableBuiltIn builtInProto)
        {
            builtInProto.SetExtra(set.Name.Lexeme, value);
            return value;
        }
        if (obj is SharpTSDate date)
        {
            date.SetExtra(set.Name.Lexeme, value);
            return value;
        }

        var category = TypeCategoryResolver.ClassifyRuntime(obj);
        string memberName = set.Name.Lexeme;
        switch (category)
        {
            case TypeCategory.External when obj is DotNetInstance dotNetInstance:
                dotNetInstance.SetMember(memberName, value, this);
                return value;

            case TypeCategory.External when obj is DotNetClass dotNetClass:
                dotNetClass.SetStaticMember(memberName, value, this);
                return value;

            case TypeCategory.Class when obj is SharpTSClass klass:
                if (klass.HasStaticAutoAccessor(memberName))
                {
                    klass.SetStaticAutoAccessorValue(memberName, value);
                    return value;
                }
                var staticSetterClass = klass.FindStaticSetter(memberName);
                if (staticSetterClass != null)
                {
                    staticSetterClass.BindStatic(klass).CallBoxed(this, [value]);
                    return value;
                }
                klass.SetStaticProperty(memberName, value);
                return value;

            case TypeCategory.Instance when obj is SharpTSInstance instance:
                instance.SetInterpreter(this);
                if (strictMode)
                    instance.SetStrict(set.Name, value, strictMode);
                else
                    instance.Set(set.Name, value);
                return value;

            case TypeCategory.Record:
                return EvaluateSetOnRecord(set, obj!, memberName, value, strictMode);

            case TypeCategory.RegExp when obj is SharpTSRegExp regex:
                // User-installed accessor (Object.defineProperty path) wins
                // over configurable built-in slots, so a throwing setter MUST
                // fire and propagate.
                if (regex.TryGetAccessor(memberName, out _, out var userSetter)
                    && userSetter != null)
                {
                    userSetter.CallBoxed(this, [value]);
                    return value;
                }
                if (memberName == "lastIndex")
                {
                    // Route through RegExpBuiltIns.SetMember so the setter does
                    // ECMA ToLength coercion (handles `undefined`, string, bool,
                    // boxed objects) instead of a hard (double) cast that throws.
                    Runtime.BuiltIns.RegExpBuiltIns.SetMember(
                        regex, memberName, value, strictMode);
                    return value;
                }
                // JS: RegExp instances are objects; allow arbitrary property assignment
                // (minimatch stores `_src`/`_glob` this way).
                regex.SetPropertyStrict(memberName, value, strictMode);
                return value;

            case TypeCategory.Error when obj is SharpTSError error:
                if (error.TryGetAccessor(memberName, out _, out var setter) && setter != null)
                {
                    FunctionBuiltIns.CallWithThis(this, setter, error, [value]);
                    return value;
                }
                if (ErrorBuiltIns.SetMember(error, memberName, value))
                    return value;
                error.SetProperty(memberName, value);
                return value;

            case TypeCategory.Promise when obj is SharpTSPromiseSubclassInstance promiseSub:
                // Promise subclass instances (#242): class setters win, then
                // own properties (declared fields and expando assignments).
                var promiseSetter = promiseSub.Klass.FindSetter(memberName);
                if (promiseSetter != null)
                {
                    promiseSetter.BindThis(promiseSub).CallBoxed(this, [value]);
                    return value;
                }
                promiseSub.SetOwnProperty(memberName, value);
                return value;

            case TypeCategory.Promise when obj is SharpTSPromise promise:
                promise.SetOwnProperty(memberName, value);
                return value;

            case TypeCategory.Array when obj is SharpTSArray array:
                if (memberName == "length")
                {
                    if (strictMode && (array.IsFrozen || !array.IsLengthWritable))
                    {
                        throw new ThrowException(new SharpTSTypeError(
                            "Cannot assign to read only property 'length' of array"));
                    }
                    // ECMA-262: `a.length = N` truncates (if N < length) or extends
                    // with holes (if N > length). SharpTSArray.SetLength handles both
                    // paths and transitions to sparse storage for large extensions.
                    double newLength = ArrayBuiltIns.CoerceArrayLength(this, value);
                    array.SetLength((long)newLength);
                    return value;
                }
                if (uint.TryParse(memberName,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out uint arrayIndex)
                    && arrayIndex < uint.MaxValue)
                {
                    if (array.TryGetIndexAccessor(
                            arrayIndex, out _, out var ownSetter))
                    {
                        if (ownSetter != null)
                        {
                            BindAccessorToObject(ownSetter, array)
                                .CallBoxed(this, [value]);
                            return value;
                        }
                        if (strictMode)
                        {
                            throw new ThrowException(new SharpTSTypeError(
                                $"Cannot set property '{memberName}' which has only a getter."));
                        }
                        return value;
                    }
                    if (!array.HasIndex(arrayIndex))
                    {
                        var arrayPrototype = GetArrayPrototype();
                        var inherited = arrayPrototype.GetOwnPropertyDescriptor(memberName);
                        var inheritedSetter = arrayPrototype.GetExtraSetter(memberName);
                        if (inherited is null)
                        {
                            var objectPrototype = GetObjectPrototype();
                            inherited = objectPrototype.GetOwnPropertyDescriptor(memberName);
                            inheritedSetter = objectPrototype.GetExtraSetter(memberName);
                        }

                        if (inheritedSetter != null)
                        {
                            BindAccessorToObject(inheritedSetter, array)
                                .CallBoxed(this, [value]);
                            return value;
                        }
                        if (inherited is not null
                            && (inherited.Get != null || inherited.Set != null
                                || !inherited.Writable))
                        {
                            if (strictMode)
                            {
                                throw new ThrowException(new SharpTSTypeError(
                                    $"Cannot assign to inherited read only property '{memberName}' of array"));
                            }
                            return value;
                        }
                    }
                    array.SetStrict(arrayIndex, value, strictMode);
                    return value;
                }
                array.SetNamedProperty(memberName, value);
                return value;

            default:
                return EvaluateSetFallback(obj, memberName, value);
        }
    }

    /// <summary>
    /// Handles property assignment on Record-category types (SharpTSObject, HttpResponse,
    /// NetServer, TlsServer).
    /// </summary>
    private object? EvaluateSetOnRecord(Expr.Set set, object obj, string memberName, object? value, bool strictMode)
    {
        if (obj is SharpTSObject simpleObj)
        {
            var setter = simpleObj.GetSetter(memberName);
            if (setter != null)
            {
                var boundSetter = BindAccessorToObject(setter, simpleObj);
                boundSetter.CallBoxed(this, [value]);
                return value;
            }

            if (simpleObj.HasGetter(memberName))
            {
                if (strictMode)
                    throw new ThrowException(new SharpTSTypeError(
                        $"Cannot set property '{memberName}' which has only a getter."));
                return value;
            }

            if (simpleObj.GetOwnPropertyDescriptor(memberName) is null
                && simpleObj.Prototype is SharpTSProxy prototypeProxy)
            {
                bool assigned = prototypeProxy.TrapSetProperty(
                    memberName, value, this, simpleObj);
                if (!assigned && strictMode)
                    throw new ThrowException(new SharpTSTypeError(
                        $"Proxy set trap rejected property '{memberName}'"));
                return value;
            }

            // Boxed primitives inherit user-defined descriptors from their
            // realm-local prototype. An inherited setter handles the write;
            // non-writable data and getter-only accessors block own shadowing.
            if (!simpleObj.HasProperty(memberName)
                && !simpleObj.HasSetter(memberName)
                && TrySetBoxedPrimitiveInheritedProperty(
                    simpleObj, memberName, value, strictMode))
                return value;

            if (strictMode)
                simpleObj.SetPropertyStrict(memberName, value, strictMode);
            else
                simpleObj.SetProperty(memberName, value);
            return value;
        }

        if (obj is SharpTSHttpResponse httpRes)
        {
            httpRes.SetMember(memberName, value);
            return value;
        }

        if (obj is SharpTSHttpServer httpServer)
        {
            httpServer.SetMember(memberName, value);
            return value;
        }

        if (obj is SharpTSHttpsServerResponse httpsRes)
        {
            httpsRes.SetMember(memberName, value);
            return value;
        }

        if (obj is SharpTSNetServer netServer)
        {
            netServer.SetMember(memberName, value);
            return value;
        }

        if (obj is SharpTSTlsServer tlsServer)
        {
            tlsServer.SetMember(memberName, value);
            return value;
        }

        throw new InterpreterException($"Only instances and objects have fields. Cannot set '{memberName}' on {obj?.GetType().Name ?? "null"}.");
    }

    private SharpTSPropertyDescriptor? GetBoxedPrimitivePrototypeDescriptor(
        SharpTSObject obj, string memberName)
    {
        if (!obj.HasProperty("__primitiveType")) return null;
        return obj.GetProperty("__primitiveType") switch
        {
            "String" => GetStringPrototype().GetOwnPropertyDescriptor(memberName),
            "Number" => GetNumberPrototype().GetOwnPropertyDescriptor(memberName),
            _ => null,
        };
    }

    private bool TrySetBoxedPrimitiveInheritedProperty(
        SharpTSObject obj, string memberName, object? value, bool strictMode)
    {
        if (GetBoxedPrimitivePrototypeDescriptor(obj, memberName) is not { } inherited)
            return false;

        var inheritedSetter = obj.GetProperty("__primitiveType") switch
        {
            "String" => GetStringPrototype().GetExtraSetter(memberName),
            "Number" => GetNumberPrototype().GetExtraSetter(memberName),
            _ => null,
        };
        if (inheritedSetter != null)
        {
            BindAccessorToObject(inheritedSetter, obj).CallBoxed(this, [value]);
            return true;
        }
        if (inherited.Get != null || inherited.Set != null || !inherited.Writable)
        {
            if (strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Cannot assign to read only property '{memberName}'."));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fallback for property assignment on types without TypeCategory dispatch
    /// (GlobalThis, process, Agent, AbortSignal).
    /// </summary>
    private object? EvaluateSetFallback(object? obj, string memberName, object? value)
    {
        if (obj is SharpTSGlobalThis globalThis)
        {
            if (globalThis.TryGetUserAccessor(memberName, out _, out var setter))
            {
                if (setter != null)
                    BindAccessorToObject(setter, globalThis).CallBoxed(this, [value]);
                return value;
            }
            globalThis.SetProperty(memberName, value);
            return value;
        }

        if (obj is SharpTSObjectNamespace objectNamespace)
        {
            objectNamespace.SetProperty(memberName, value);
            return value;
        }

        if (obj is SharpTSFunctionPrototype functionPrototype)
        {
            functionPrototype.SetExtra(memberName, value);
            return value;
        }

        // process.exitCode = 5, process.title = "x", plus expando assignment —
        // routed through the process-managed setters (Runtime/BuiltIns/ProcessBuiltIns.cs).
        if (obj is SharpTSProcess process)
        {
            process.SetProcessMember(memberName, value);
            return value;
        }

        if (obj is SharpTSAgent agent)
        {
            agent.SetMember(memberName, value);
            return value;
        }

        if (obj is SharpTSAbortSignal signal)
        {
            if (memberName == "onabort")
            {
                signal.OnAbort = value;
                return value;
            }
            throw new InterpreterException($"Cannot set property '{memberName}' on AbortSignal.");
        }

        if (obj is SharpTSBroadcastChannel bc)
        {
            if (bc.SetMember(memberName, value))
                return value;
            throw new InterpreterException($"Cannot set property '{memberName}' on BroadcastChannel.");
        }

        throw new InterpreterException($"Only instances and objects have fields. Cannot set '{memberName}' on {obj?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Evaluates a variable assignment expression.
    /// </summary>
    /// <param name="assign">The assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Evaluates the right-hand side value and updates the variable
    /// in the current <see cref="RuntimeEnvironment"/>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html">TypeScript Variable Declarations</seealso>
    private RuntimeValue EvaluateAssign(Expr.Assign assign)
    {
        RuntimeValue value = EvaluateRV(assign.Value);

        if (_locals.TryGetValue(assign, out int distance))
        {
            _environment.AssignAt(distance, assign.Name, value);
        }
        else
        {
            _environment.Assign(assign.Name, value);
        }

        return value;
    }

    #region ES2022 Private Class Elements

    /// <summary>
    /// Evaluates a private field access expression (obj.#field).
    /// </summary>
    private RuntimeValue EvaluateGetPrivate(Expr.GetPrivate expr)
        => GetPrivateCore(Evaluate(expr.Object), expr.Name.Lexeme);

    /// <summary>
    /// Async variant — the object expression may contain await.
    /// </summary>
    private async Task<RuntimeValue> EvaluateGetPrivateAsync(Expr.GetPrivate expr)
        => GetPrivateCore((await EvaluateAsync(expr.Object)).ToObject(), expr.Name.Lexeme);

    private RuntimeValue GetPrivateCore(object? obj, string fieldName)
    {
        // Handle static private field access on class
        if (obj is SharpTSClass klass)
        {
            // For static private fields, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            if (klass.HasStaticPrivateField(fieldName))
            {
                return klass.GetStaticPrivateFieldRV(fieldName);
            }

            throw new InterpreterException($"Static private field '{fieldName}' does not exist on class '{klass.Name}'.");
        }

        // Instance private field access
        if (obj is SharpTSInstance instance)
        {
            // For instance private fields, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            return declaringClass.GetPrivateFieldRV(instance, fieldName);
        }

        throw new InterpreterException($"Cannot read private field '{fieldName}' from non-class value.");
    }

    /// <summary>
    /// Evaluates a private field assignment expression (obj.#field = value).
    /// </summary>
    private RuntimeValue EvaluateSetPrivate(Expr.SetPrivate expr)
        => SetPrivateCore(Evaluate(expr.Object), EvaluateRV(expr.Value), expr.Name.Lexeme);

    /// <summary>
    /// Async variant — the value (or object) expression may contain await,
    /// which the sync evaluator rejects ("'await' can only be used inside
    /// async functions") even when the enclosing method is async.
    /// </summary>
    private async Task<RuntimeValue> EvaluateSetPrivateAsync(Expr.SetPrivate expr)
    {
        object? obj = (await EvaluateAsync(expr.Object)).ToObject();
        RuntimeValue value = await EvaluateAsync(expr.Value);
        return SetPrivateCore(obj, value, expr.Name.Lexeme);
    }

    private RuntimeValue SetPrivateCore(object? obj, RuntimeValue value, string fieldName)
    {
        // Handle static private field assignment on class
        if (obj is SharpTSClass klass)
        {
            // For static private fields, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            if (klass.HasStaticPrivateField(fieldName))
            {
                klass.SetStaticPrivateField(fieldName, value.ToObject());
                return value;
            }

            throw new InterpreterException($"Static private field '{fieldName}' does not exist on class '{klass.Name}'.");
        }

        // Instance private field assignment
        if (obj is SharpTSInstance instance)
        {
            // For instance private fields, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            declaringClass.SetPrivateField(instance, fieldName, value.ToObject());
            return value;
        }

        throw new InterpreterException($"Cannot write private field '{fieldName}' to non-class value.");
    }

    /// <summary>
    /// Evaluates a private method call expression (obj.#method(...)).
    /// </summary>
    private RuntimeValue EvaluateCallPrivate(Expr.CallPrivate expr)
    {
        object? obj = Evaluate(expr.Object);

        List<object?> arguments = [];
        foreach (var arg in expr.Arguments)
        {
            arguments.Add(Evaluate(arg));
        }

        return CallPrivateCore(obj, arguments, expr.Name.Lexeme);
    }

    /// <summary>
    /// Async variant — arguments (or the object) may contain await.
    /// </summary>
    private async Task<RuntimeValue> EvaluateCallPrivateAsync(Expr.CallPrivate expr)
    {
        object? obj = (await EvaluateAsync(expr.Object)).ToObject();

        List<object?> arguments = [];
        foreach (var arg in expr.Arguments)
        {
            arguments.Add((await EvaluateAsync(arg)).ToObject());
        }

        return CallPrivateCore(obj, arguments, expr.Name.Lexeme);
    }

    private RuntimeValue CallPrivateCore(object? obj, List<object?> arguments, string methodName)
    {
        // Handle static private method call on class
        if (obj is SharpTSClass klass)
        {
            // For static private methods, the class being accessed IS the declaring class
            // The type checker already verified we're inside this class
            var method = klass.GetStaticPrivateMethod(methodName);
            if (method == null)
            {
                throw new InterpreterException($"Static private method '{methodName}' does not exist on class '{klass.Name}'.");
            }

            return RuntimeValue.FromBoxed(method.CallBoxed(this, arguments));
        }

        // Instance private method call
        if (obj is SharpTSInstance instance)
        {
            // For instance private methods, use the instance's class as the declaring class
            // The type checker already verified brand checking
            var declaringClass = instance.RuntimeClass;
            var method = declaringClass.GetPrivateMethod(methodName);
            if (method == null)
            {
                throw new InterpreterException($"Private method '{methodName}' does not exist on class '{declaringClass.Name}'.");
            }

            // Bind method to instance
            return RuntimeValue.FromBoxed(SharpTSClass.BindMethod(method, instance).CallBoxed(this, arguments));
        }

        throw new InterpreterException($"Cannot call private method '{methodName}' on non-class value.");
    }

    #endregion
}
