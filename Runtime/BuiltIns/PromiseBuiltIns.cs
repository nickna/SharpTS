using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for Promise objects.
/// Provides both instance methods (.then, .catch, .finally) and
/// static methods (Promise.all, Promise.race, Promise.resolve, Promise.reject).
/// </summary>
public static class PromiseBuiltIns
{
    /// <summary>
    /// Gets an instance member of a Promise.
    /// The interpreter is passed at call time through BuiltInAsyncMethod.
    /// </summary>
    /// <remarks>
    /// For Promise subclass instances (#242), then/catch/finally construct
    /// their result promise through SpeciesConstructor(promise, %Promise%)
    /// (ECMA-262 §27.2.5.4 step 3, §7.3.22) — i.e. the value of
    /// <c>promise.constructor[Symbol.species]</c>, defaulting to the receiver's
    /// own class when not overridden and to <c>%Promise%</c> when the override
    /// yields <c>undefined</c>/<c>null</c> or <c>Promise</c> itself (#221).
    /// </remarks>
    public static object? GetMember(SharpTSPromise receiver, string name)
    {
        return name switch
        {
            // Per ECMA-262 the handler arguments are all optional: §27.2.5.4
            // then(onFulfilled?, onRejected?), §27.2.5.1 catch(onRejected?),
            // §27.2.5.3 finally(onFinally?). minArity is 0; maxArity pins the
            // spec shape. The impls null-default any missing args (#382).
            "then" => new BuiltInAsyncMethod("then", 0, 2, (interp, recv, args) =>
                ThenImpl((SharpTSPromise)recv!, args, interp), speciesResolver: SpeciesResolver).Bind(receiver),

            "catch" => new BuiltInAsyncMethod("catch", 0, 1, (interp, recv, args) =>
                CatchImpl((SharpTSPromise)recv!, args, interp), speciesResolver: SpeciesResolver).Bind(receiver),

            "finally" => new BuiltInAsyncMethod("finally", 0, 1, (interp, recv, args) =>
                FinallyImpl((SharpTSPromise)recv!, args, interp), speciesResolver: SpeciesResolver).Bind(receiver),

            // ECMA-262 §27.2.5.1: Promise.prototype.constructor is %Promise%.
            // (Subclass instances report their own class — see
            // Interpreter.Properties.cs. then/catch/finally now consult
            // SpeciesConstructor via the SpeciesResolver pre-step, #221/#350.)
            "constructor" => Interpreter.PromiseGlobalValue,

            _ => null
        };
    }

    /// <summary>
    /// The <c>Promise.prototype</c> reaction methods in unbound form — no receiver attached,
    /// so a read off the prototype object yields the function itself (and a later
    /// <c>.call(promise, …)</c> or member call supplies <c>this</c>). The receiver-bound
    /// variants live in <see cref="GetMember"/>.
    /// </summary>
    internal static ISharpTSCallable? GetPrototypeMethod(string name) => name switch
    {
        "then" => new BuiltInAsyncMethod("then", 0, 2, (interp, recv, args) =>
            ThenImpl(RequirePromiseReceiver(recv, "then"), args, interp),
            speciesResolver: SpeciesResolver).WithSpecLength(2),
        "catch" => BuiltInMethod.CreateV2("catch", 0, int.MaxValue, CatchInvoke)
            .WithSpecLength(1)
            .AsNonConstructor(),
        "finally" => BuiltInMethod.CreateV2("finally", 0, int.MaxValue, FinallyInvoke)
            .WithSpecLength(1)
            .AsNonConstructor(),
        "constructor" => Interpreter.PromiseGlobalValue as ISharpTSCallable,
        _ => null,
    };

    /// <summary>
    /// ECMA-262 §27.2.5.4 step 2: <c>Promise.prototype.then</c> and friends require a receiver
    /// with a [[PromiseState]] slot, so <c>Promise.prototype.then.call({}, …)</c> is a TypeError
    /// rather than a host cast failure.
    /// </summary>
    private static SharpTSPromise RequirePromiseReceiver(object? receiver, string methodName)
        => receiver as SharpTSPromise
            ?? throw new Runtime.Exceptions.ThrowException(new SharpTSTypeError(
                $"Promise.prototype.{methodName} called on a non-Promise receiver"));

    /// <summary>
    /// ECMA-262 §27.2.5.1: catch is intentionally generic. It performs
    /// <c>Invoke(promise, "then", « undefined, onRejected »)</c>, so an own
    /// getter or replacement method is observed and its return value passes
    /// through unchanged.
    /// </summary>
    private static RuntimeValue CatchInvoke(
        Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        object? target = receiver.ToObject();
        object? then = interpreter.GetPropertyValue(target, "then");
        if (then is not ISharpTSCallable callable)
            throw new Runtime.Exceptions.ThrowException(new SharpTSTypeError(
                "Promise.prototype.catch: then is not callable"));

        object? onRejected = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
            interpreter,
            callable,
            target,
            [SharpTSUndefined.Instance, onRejected]));
    }

    /// <summary>
    /// Generic entry point for §27.2.5.3. A normal promise with its inherited
    /// <c>then</c> keeps the optimized async implementation. Thenables and
    /// promises that override <c>then</c> take the observable Invoke path.
    /// </summary>
    private static RuntimeValue FinallyInvoke(
        Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        object? target = receiver.ToObject();
        bool hasOwnThen = target is SharpTSPromise promise
            && (promise.TryGetAccessor("then", out _, out _)
                || promise.TryGetOwnProperty("then", out _));

        if (target is SharpTSPromise ordinaryPromise && !hasOwnThen)
        {
            var implementation = new BuiltInAsyncMethod(
                "finally",
                0,
                1,
                (interp, recv, callArgs) => FinallyImpl(
                    (SharpTSPromise)recv!, callArgs, interp),
                speciesResolver: SpeciesResolver).WithSpecLength(1);
            List<object?> callArgs = args.Length > 0 ? [args[0].ToObject()] : [];
            return RuntimeValue.FromBoxed(implementation.Bind(ordinaryPromise).Call(
                interpreter, callArgs));
        }

        object? then = interpreter.GetPropertyValue(target, "then");
        if (then is not ISharpTSCallable callable)
            throw new Runtime.Exceptions.ThrowException(new SharpTSTypeError(
                "Promise.prototype.finally: then is not callable"));

        object? onFinally = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        object? thenFinally = onFinally;
        object? catchFinally = onFinally;
        if (onFinally is ISharpTSCallable callback)
        {
            thenFinally = BuiltInMethod.CreateV2("", 1, (interp, _, thunkArgs) =>
            {
                object? value = thunkArgs.Length > 0
                    ? thunkArgs[0].ToObject()
                    : SharpTSUndefined.Instance;
                return RuntimeValue.FromBoxed(CreateFinallyContinuation(
                    interp, callback, value, reject: false));
            }).AsNonConstructor();
            catchFinally = BuiltInMethod.CreateV2("", 1, (interp, _, thunkArgs) =>
            {
                object? reason = thunkArgs.Length > 0
                    ? thunkArgs[0].ToObject()
                    : SharpTSUndefined.Instance;
                return RuntimeValue.FromBoxed(CreateFinallyContinuation(
                    interp, callback, reason, reject: true));
            }).AsNonConstructor();
        }
        return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
            interpreter, callable, target, [thenFinally, catchFinally]));
    }

    private static SharpTSPromise CreateFinallyContinuation(
        Interpreter interpreter,
        ISharpTSCallable callback,
        object? original,
        bool reject)
    {
        object? result = FunctionBuiltIns.CallWithThis(
            interpreter, callback, SharpTSUndefined.Instance, []);
        return new SharpTSPromise(ContinueAsync(result, original, reject));

        static async Task<object?> ContinueAsync(
            object? callbackResult,
            object? originalValue,
            bool shouldReject)
        {
            if (callbackResult is SharpTSPromise promise)
                await promise.GetValueAsync();
            if (shouldReject)
                throw new SharpTSPromiseRejectedException(originalValue);
            return originalValue;
        }
    }

    /// <summary>
    /// Gets a static method from the Promise namespace.
    /// </summary>
    public static ISharpTSCallable? GetStaticMethod(string name) => GetStaticMethod(name, null);

    /// <summary>
    /// Gets a static method from the Promise static side. When
    /// <paramref name="subclass"/> is a guest Promise subclass (#242), the
    /// returned method constructs its result promise through that class so
    /// inherited statics (e.g. <c>MyPromise.resolve(v)</c>) produce
    /// subclass-typed instances.
    /// </summary>
    public static ISharpTSCallable? GetStaticMethod(string name, SharpTSPromiseClass? subclass)
    {
        var factory = DerivedPromiseFactory(subclass);
        Func<Interpreter, object?, Func<Interpreter, Task<object?>, object?>?> receiverResolver =
            (interp, receiver) =>
            {
                RequireConstructorReceiver(receiver);
                if (ReferenceEquals(receiver, Interpreter.PromiseGlobalValue)
                    || receiver is SharpTSPromiseClass promiseClass
                        && ReferenceEquals(promiseClass, SharpTSPromiseClass.PromiseBase))
                {
                    return null;
                }

                return DerivedPromiseFactory(receiver)
                    ?? PreparePromiseCapability(interp, receiver);
            };
        return name switch
        {
            "all" => new BuiltInAsyncMethod("all", 1, 1, (interp, receiver, args) =>
                AllImpl(args, interp, receiver), factory, speciesResolver: receiverResolver),

            "race" => new BuiltInAsyncMethod("race", 1, 1, (interp, receiver, args) =>
                RaceImpl(args, interp, receiver), factory, speciesResolver: receiverResolver),

            "resolve" => new BuiltInMethod("resolve", 0, 1, (interp, receiver, args) =>
                ResolveStatic(interp, receiver, args, factory))
                .WithSpecLength(1)
                .AsNonConstructor(),

            "reject" => new BuiltInAsyncMethod("reject", 0, 1, (_, _, args) =>
                Task.FromResult(RejectImpl(args)), factory, speciesResolver: receiverResolver).WithSpecLength(1),

            "allSettled" => new BuiltInAsyncMethod("allSettled", 1, 1, (interp, receiver, args) =>
                AllSettledImpl(args, interp, receiver), factory, speciesResolver: receiverResolver),

            "any" => new BuiltInAsyncMethod("any", 1, 1, (interp, receiver, args) =>
                AnyImpl(args, interp, receiver), factory, speciesResolver: receiverResolver),

            "withResolvers" => BuiltInMethod.CreateV2("withResolvers", 0, (interp, _, _) =>
                RuntimeValue.FromBoxed(WithResolversImpl(interp, factory))),

            _ => null
        };
    }

    /// <summary>
    /// Promise static combinators run NewPromiseCapability on their <c>this</c>
    /// value before creating a result promise. That operation requires an
    /// Object; primitives must therefore throw synchronously rather than
    /// becoming a rejected promise in <see cref="BuiltInAsyncMethod"/>.
    /// </summary>
    private static void RequireConstructorReceiver(object? receiver)
    {
        if (receiver is null or SharpTSUndefined or bool or double or int or long
            or float or decimal or char or string or SharpTSSymbol
            or SharpTSBigInt or System.Numerics.BigInteger)
        {
            throw new Exceptions.ThrowException(new SharpTSTypeError(
                "Promise static method called on a non-object receiver"));
        }
    }

    /// <summary>
    /// Returns the derived-promise factory for a Promise subclass static-side
    /// constructor, or null for the base Promise (which keeps the default
    /// <see cref="SharpTSPromise"/> wrapping). Used by the static methods
    /// (<c>resolve</c>/<c>reject</c>/<c>all</c>/<c>race</c>/<c>allSettled</c>/
    /// <c>any</c>/<c>withResolvers</c>), which per ECMA-262 build their result
    /// through the receiver constructor <c>C</c> <em>directly</em> (e.g.
    /// §27.2.4.1 <c>Promise.all</c> step 2 <c>NewPromiseCapability(C)</c>) — no
    /// <c>@@species</c> indirection (that applies only to the prototype methods;
    /// see <see cref="SpeciesPromiseFactory"/>).
    /// </summary>
    private static Func<Interpreter, Task<object?>, SharpTSPromise>? DerivedPromiseFactory(object? receiverOrClass)
    {
        var klass = receiverOrClass switch
        {
            SharpTSPromiseSubclassInstance sub => sub.Klass,
            SharpTSPromiseClass pc => pc,
            _ => null
        };
        // PromiseBase itself (the bridge singleton for the built-in
        // constructor) keeps the plain wrapping.
        if (klass == null || ReferenceEquals(klass, SharpTSPromiseClass.PromiseBase))
            return null;
        return (interp, task) => klass.ConstructDerived(interp, task);
    }

    /// <summary>
    /// The synchronous SpeciesConstructor pre-step shared by
    /// <c>then</c>/<c>catch</c>/<c>finally</c> (passed to
    /// <see cref="BuiltInAsyncMethod"/> as its <c>speciesResolver</c>). Run at
    /// call time before the rejected-promise conversion, so a poisoned
    /// <c>constructor</c>/<c>@@species</c> getter throws synchronously (#350).
    /// </summary>
    /// <remarks>
    /// Doubles as the §27.2.5.4 step 2 receiver check. It runs synchronously, which is what
    /// the spec requires: <c>Promise.prototype.then.call({}, …)</c> throws a TypeError out of
    /// the call rather than returning a rejected promise. (A hard cast here surfaced an
    /// InvalidCastException, which reached guest <c>catch</c> as a bare string.)
    /// </remarks>
    private static readonly Func<Interpreter, object?, Func<Interpreter, Task<object?>, object?>?> SpeciesResolver =
        static (interp, recv) => ResolveResultPromiseFactory(
            recv as SharpTSPromise
                ?? throw new Runtime.Exceptions.ThrowException(new SharpTSTypeError(
                    "Promise.prototype method called on a non-Promise receiver")),
            interp);

    /// <summary>
    /// Computes the result-promise factory for the prototype methods
    /// (<c>then</c>/<c>catch</c>/<c>finally</c>) per ECMA-262 §27.2.5.4 step 3,
    /// <c>SpeciesConstructor(promise, %Promise%)</c> (§7.3.22). Returns null to
    /// mean <c>%Promise%</c> (plain wrapping) or a factory that constructs the
    /// result through a guest Promise class.
    /// </summary>
    /// <remarks>
    /// §7.3.22 step 1 is <c>Get(promise, "constructor")</c>: an own
    /// <c>constructor</c> accessor installed via <c>Object.defineProperty</c>
    /// (a poisoned getter, test262 then/ctor-poisoned, #350) is invoked here and
    /// a throw propagates synchronously. The getter's RETURN value does not
    /// redirect species — the receiver's own class still drives the result; an
    /// own <c>constructor</c> that resolves to a DIFFERENT constructor (or an own
    /// data property, or the general non-Promise NewPromiseCapability) is the
    /// #349/#350 remainder, and is kept symmetric with the compiled
    /// <c>WrapDerivedPromiseResult</c> path. Absent an own override, the inherited
    /// <c>Promise.prototype.constructor</c> is the receiver's own class (subclass)
    /// or <c>%Promise%</c> (plain); a subclass then reads its static
    /// <c>@@species</c> (#221).
    /// </remarks>
    private static Func<Interpreter, Task<object?>, object?>? ResolveResultPromiseFactory(
        SharpTSPromise receiver, Interpreter interp)
    {
        if (receiver.TryGetAccessor("constructor", out var ctorGetter, out _) && ctorGetter != null)
            ctorGetter.Call(interp, []);   // side effect only: a poisoned getter throws → propagates

        // The receiver's own class drives species: a subclass reads its static
        // @@species (#221); a plain promise stays %Promise% (null factory).
        return receiver is SharpTSPromiseSubclassInstance sub
            ? SpeciesMaterializer(ResolveSpeciesConstructor(sub.Klass, interp))
            : null;
    }

    /// <summary>
    /// Wraps a resolved species constructor into a result-promise factory, or
    /// returns null (meaning <c>%Promise%</c>, the plain wrapping) when species
    /// is null.
    /// </summary>
    /// <remarks>
    /// A <see cref="SharpTSPromiseClass"/> species takes the fast subclass path
    /// (<see cref="SharpTSPromiseClass.ConstructDerived"/>). Any other constructor
    /// — a general (non-Promise) guest class, or a plain function/function
    /// expression used via the <c>new</c> protocol — goes through the spec's
    /// NewPromiseCapability (§27.2.4.5): the result is
    /// <c>new S((resolve, reject) =&gt; …)</c> with the captured capability
    /// adopting the settled task (#349/#390). A species that is neither
    /// <c>undefined</c>/<c>null</c> (filtered earlier, → <c>%Promise%</c>) nor a
    /// constructor throws <c>TypeError</c> per SpeciesConstructor §7.3.22 step 5;
    /// this runs synchronously during the <c>then</c>/<c>catch</c>/<c>finally</c>
    /// call (the <c>SpeciesResolver</c> pre-step), so the throw propagates out of
    /// that call rather than rejecting the result (#390).
    /// </remarks>
    private static Func<Interpreter, Task<object?>, object?>? SpeciesMaterializer(object? species)
        => species switch
        {
            null => null,
            SharpTSPromiseClass pc => (interp, task) => pc.ConstructDerived(interp, task),
            _ when IsConstructorSpecies(species) =>
                (interp, task) => ConstructPromiseCapabilityAndAdopt(interp, species, task),
            _ => throw new Exceptions.ThrowException(new SharpTSTypeError(
                "Promise resolution species is not a constructor"))
        };

    /// <summary>
    /// ECMA-262 §7.2.4 IsConstructor for the values a Promise <c>@@species</c> can
    /// resolve to (the SpeciesConstructor §7.3.22 step 5 check). Classes, plain
    /// function declarations, function expressions (a <see cref="SharpTSArrowFunction"/>
    /// with its own <c>this</c>), and built-in constructors have a [[Construct]]
    /// slot; true arrow functions, bound functions, and non-callables do not. The
    /// allow-list deliberately mirrors what <see cref="Interpreter.Construct"/>
    /// (used by <see cref="ConstructPromiseCapabilityAndAdopt"/>) can build, so a
    /// value that passes here never reaches Construct's permissive callable
    /// fallback.
    /// </summary>
    private static bool IsConstructorSpecies(object? value) => value switch
    {
        SharpTSClass => true,
        SharpTSFunction => true,
        SharpTSArrowFunction arrow => arrow.HasOwnThis,
        SharpTSBuiltInConstructor => true,
        _ => false
    };

    /// <summary>
    /// General NewPromiseCapability over an arbitrary (non-Promise) guest
    /// constructor <c>S</c> (ECMA-262 §27.2.4.5 + §27.2.5.4 step 7). Constructs
    /// <c>new S(executor)</c> via the <c>new</c> protocol
    /// (<see cref="Interpreter.Construct"/>, which handles guest classes, plain
    /// functions, and function expressions, #390), capturing the resolve/reject
    /// the executor is handed, then adopts the settled <paramref name="source"/>
    /// task into that capability and returns the constructed object — which need
    /// not be a SharpTSPromise (it behaves as a promise downstream only insofar as
    /// it is a thenable; <c>await</c> adopts thenables, #349). The caller
    /// (<see cref="SpeciesMaterializer"/>) has already verified
    /// <paramref name="speciesCtor"/> is a constructor.
    /// </summary>
    private static object? ConstructPromiseCapabilityAndAdopt(
        Interpreter interp, object? speciesCtor, Task<object?> source)
    {
        var (promiseObject, resolveFn, rejectFn) = CreatePromiseCapability(interp, speciesCtor);

        // Adopt the source task into the captured capability. Awaiting inside this
        // helper captures the interpreter's SynchronizationContext, so the guest
        // resolve/reject callbacks resume on the event-loop thread rather than
        // escaping to the thread pool (#319/#320).
        _ = AdoptIntoCapability(source, resolveFn, rejectFn, interp);
        return promiseObject;
    }

    /// <summary>
    /// Performs NewPromiseCapability synchronously and returns a materializer
    /// that only adopts the operation task later. Promise combinators must run
    /// the constructor before GetPromiseResolve/iterator processing, and a
    /// custom constructor's no-op reject callback must own any later failure
    /// instead of creating an unhandled host-backed SharpTSPromise.
    /// </summary>
    private static Func<Interpreter, Task<object?>, object?> PreparePromiseCapability(
        Interpreter interp, object? constructor)
    {
        var (promiseObject, resolveFn, rejectFn) = CreatePromiseCapability(interp, constructor);
        return (adoptingInterpreter, source) =>
        {
            _ = AdoptIntoCapability(source, resolveFn, rejectFn, adoptingInterpreter);
            return promiseObject;
        };
    }

    private static (object? Promise, ISharpTSCallable Resolve, ISharpTSCallable Reject)
        CreatePromiseCapability(Interpreter interp, object? constructor)
    {
        var capability = new PromiseCapabilityExecutor();
        object? promiseObject = interp.Construct(constructor, [capability]);
        if (capability.ResolveFn is not { } resolveFn || capability.RejectFn is not { } rejectFn)
        {
            throw new Exceptions.ThrowException(new SharpTSTypeError(
                "Promise resolve or reject function is not callable"));
        }
        return (promiseObject, resolveFn, rejectFn);
    }

    private static async Task AdoptIntoCapability(
        Task<object?> source, ISharpTSCallable resolveFn, ISharpTSCallable rejectFn, Interpreter interp)
    {
        object? value;
        try
        {
            value = await source;
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            InvokeCapabilityCallback(rejectFn, ex.Reason, interp);
            return;
        }
        catch (Exception ex)
        {
            InvokeCapabilityCallback(rejectFn, ex.Message, interp);
            return;
        }
        InvokeCapabilityCallback(resolveFn, value, interp);
    }

    /// <summary>
    /// Invokes a captured capability resolve/reject callback. A throw from the
    /// guest callback has nowhere to propagate (the adopting task is detached),
    /// so it is reported like an uncaught microtask rather than crashing the loop.
    /// </summary>
    private static void InvokeCapabilityCallback(ISharpTSCallable callback, object? arg, Interpreter interp)
    {
        try
        {
            callback.Call(interp, [arg]);
        }
        catch (Exceptions.ThrowException tex)
        {
            Console.Error.WriteLine($"Uncaught (in promise capability): {interp.Stringify(tex.Value)}");
        }
    }

    /// <summary>
    /// The host executor handed to a general species constructor by
    /// <see cref="ConstructPromiseCapabilityAndAdopt"/>. Per
    /// NewPromiseCapability (§27.2.1.5) it captures the resolve/reject functions
    /// the constructor passes it; calling it more than once, or with already-set
    /// slots, is a TypeError.
    /// </summary>
    private sealed class PromiseCapabilityExecutor : ISharpTSCallable
    {
        public ISharpTSCallable? ResolveFn { get; private set; }
        public ISharpTSCallable? RejectFn { get; private set; }

        public int Arity() => 2;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            if (ResolveFn != null || RejectFn != null)
                throw new InterpreterException("Promise executor was already invoked");
            ResolveFn = arguments.Count > 0 ? arguments[0] as ISharpTSCallable : null;
            RejectFn = arguments.Count > 1 ? arguments[1] as ISharpTSCallable : null;
            return SharpTSUndefined.Instance;
        }
    }

    /// <summary>
    /// Computes SpeciesConstructor(promise, %Promise%) (ECMA-262 §7.3.22) for a
    /// Promise subclass receiver, returning the constructor to build the result
    /// through (a guest Promise subclass, or a general non-Promise class), or
    /// <c>null</c> to mean <c>%Promise%</c> (the plain built-in). Reads
    /// <c>C[@@species]</c> where <c>C</c> is the receiver's class: a declared
    /// <c>static get [Symbol.species]()</c> or an expando
    /// <c>(C as any)[Symbol.species]</c> (#262) override wins; absent either,
    /// the inherited <c>Promise[@@species]</c> (which returns <c>this</c>) makes
    /// the species the receiver's own class. An override that yields
    /// <c>undefined</c>/<c>null</c> or <c>Promise</c> itself resolves to
    /// <c>%Promise%</c>.
    /// </summary>
    /// <remarks>
    /// A species override that returns a general non-Promise class is materialized
    /// through NewPromiseCapability by <see cref="SpeciesMaterializer"/> (#349). A
    /// poisoned own <c>constructor</c> getter (<c>then/ctor-poisoned.js</c>, #350)
    /// is handled earlier, in <see cref="ResolveResultPromiseFactory"/>, which
    /// reads <c>Get(promise, "constructor")</c> before reaching here.
    /// </remarks>
    private static object? ResolveSpeciesConstructor(SharpTSPromiseClass klass, Interpreter interp)
    {
        object? species;
        if (klass.FindStaticSymbolGetter(SharpTSSymbol.Species) is { } getter)
            species = getter.BindStatic(klass).Call(interp, []);
        else if (klass.TryGetStaticBySymbol(SharpTSSymbol.Species, out var expando))
            species = expando;
        else
            // No own @@species: inherited Promise[@@species] returns `this`.
            return klass;

        return species switch
        {
            null or SharpTSUndefined => null,                                  // → %Promise%
            SharpTSBuiltInConstructor { Name: BuiltInNames.Promise } => null,  // `return Promise`
            SharpTSPromiseClass sc when ReferenceEquals(sc, SharpTSPromiseClass.PromiseBase) => null,
            // A Promise subclass or a general non-Promise class flows to
            // SpeciesMaterializer, which picks the subclass fast path or the
            // general NewPromiseCapability path (#349) respectively.
            _ => species
        };
    }

    #region Instance Methods

    /// <summary>
    /// Implementation of Promise.prototype.then(onFulfilled?, onRejected?)
    /// </summary>
    private static async Task<object?> ThenImpl(
        SharpTSPromise promise,
        List<object?> args,
        Interpreter interpreter)
    {
        var onFulfilled = args.Count > 0 ? args[0] as ISharpTSCallable : null;
        var onRejected = args.Count > 1 ? args[1] as ISharpTSCallable : null;

        // A rejection handler makes a previously-reported unhandled rejection
        // handled — fire process 'rejectionHandled' (#1080).
        if (onRejected != null)
            interpreter.NotifyRejectionHandlerAttached(promise);

        // ECMA-262 §27.2.5.4: onRejected only handles rejection of the INPUT
        // promise. Guard only the input await with the rejection dispatch —
        // a throwing onFulfilled (or a rejecting thenable it returned) must
        // reject the output promise, not invoke this same then's onRejected
        // (#195). Handler invocation happens after this try.
        object? value;
        try
        {
            value = await promise.GetValueAsync();
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            if (onRejected != null)
            {
                return await InvokeHandler(onRejected, ex.Reason, interpreter);
            }

            // No onRejected callback - re-throw to propagate rejection
            throw;
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            if (onRejected != null)
            {
                return await InvokeHandler(onRejected, rejEx.Reason, interpreter);
            }
            throw rejEx;
        }

        // Fulfilled: call onFulfilled (its throw rejects the output promise)
        if (onFulfilled != null)
        {
            return await InvokeHandler(onFulfilled, value, interpreter);
        }

        // No onFulfilled callback - pass through value
        return value;
    }

    /// <summary>
    /// Invokes a then/catch reaction handler. A throwing handler rejects the
    /// output promise with the thrown value (ECMA-262 §27.2.5.4) instead of
    /// letting the guest ThrowException fault the task as a host error.
    /// A rejected promise returned by the handler propagates unchanged.
    /// </summary>
    private static async Task<object?> InvokeHandler(
        ISharpTSCallable handler, object? arg, Interpreter interpreter)
    {
        try
        {
            var result = CallCallback(handler, [arg], interpreter);
            return await UnwrapResult(result);
        }
        catch (Exceptions.ThrowException tex)
        {
            throw new SharpTSPromiseRejectedException(tex.Value);
        }
    }

    /// <summary>
    /// Implementation of Promise.prototype.catch(onRejected)
    /// Equivalent to .then(undefined, onRejected)
    /// </summary>
    private static async Task<object?> CatchImpl(
        SharpTSPromise promise,
        List<object?> args,
        Interpreter interpreter)
    {
        var onRejected = args.Count > 0 ? args[0] as ISharpTSCallable : null;

        // See ThenImpl: a rejection handler may fire 'rejectionHandled' (#1080).
        if (onRejected != null)
            interpreter.NotifyRejectionHandlerAttached(promise);

        try
        {
            // Wait for the promise to settle
            return await promise.GetValueAsync();
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            if (onRejected != null)
            {
                return await InvokeHandler(onRejected, ex.Reason, interpreter);
            }
            throw;
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            if (onRejected != null)
            {
                return await InvokeHandler(onRejected, rejEx.Reason, interpreter);
            }
            throw rejEx;
        }
    }

    /// <summary>
    /// Implementation of Promise.prototype.finally(onFinally)
    /// Callback receives no arguments and does not alter the result.
    /// </summary>
    private static async Task<object?> FinallyImpl(
        SharpTSPromise promise,
        List<object?> args,
        Interpreter interpreter)
    {
        var onFinally = args.Count > 0 ? args[0] as ISharpTSCallable : null;
        object? value = null;
        Exception? error = null;

        try
        {
            value = await promise.GetValueAsync();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Call the finally callback (with no arguments)
        if (onFinally != null)
        {
            try
            {
                var result = CallCallback(onFinally, [], interpreter);
                // If callback returns a Promise, wait for it
                if (result is SharpTSPromise p)
                {
                    await p.GetValueAsync();
                }
            }
            catch (Exception callbackError)
            {
                // If callback throws, that becomes the new rejection reason
                throw new SharpTSPromiseRejectedException(callbackError.Message);
            }
        }

        // Re-throw original error or return original value
        if (error != null)
        {
            throw error;
        }

        return value;
    }

    #endregion

    #region Static Methods

    /// <summary>
    /// ECMA-262 §27.2.4.1/.2/.3/.7 step 3: <c>GetIterator(iterable)</c> for the Promise
    /// combinators, with an abrupt completion turned into a *rejection* of the returned
    /// promise (IfAbruptRejectPromise) rather than a synchronous throw. So
    /// <c>Promise.all(null)</c> hands back a promise rejected with a TypeError, and any
    /// iterable — not just an array — is accepted.
    /// </summary>
    private static List<object?> IterateCombinatorArgument(
        List<object?> args, Interpreter interpreter, string methodName)
    {
        var iterable = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        if (iterable is SharpTSArray array) return [.. array];
        try
        {
            return DrainIterable(interpreter, iterable, methodName);
        }
        catch (SharpTSPromiseRejectedException)
        {
            throw;
        }
        catch (Runtime.Exceptions.ThrowException ex)
        {
            // A guest throw from Symbol.iterator / next() becomes the returned
            // promise's rejection reason. Convert it here so the task wrapper
            // preserves the guest value instead of exposing a host message.
            throw new SharpTSPromiseRejectedException(ex.Value);
        }
        catch
        {
            throw new SharpTSPromiseRejectedException(new SharpTSTypeError(
                $"Promise.{methodName}: argument is not iterable"));
        }
    }

    /// <summary>
    /// Maximum number of elements drawn from a non-array iterable by the Promise combinators.
    /// </summary>
    /// <remarks>
    /// KNOWN LIMITATION. The spec interleaves iteration with <c>C.resolve(nextValue)</c> and
    /// stops at the first abrupt completion (§27.2.4.1 steps 6–8 via PerformPromiseAll), so an
    /// infinite iterator terminates as soon as resolve throws. This implementation materializes
    /// the iterable up front, which never terminates for such an iterator — Test262's
    /// <c>resolve-throws-iterator-return-*</c> / <c>invoke-then-*-close</c> cases build exactly
    /// that (a <c>next()</c> that always returns <c>{done: false}</c>). The cap converts the
    /// hang into a prompt rejection. Those tests still do not pass; they fail fast instead of
    /// burning the per-test timeout. Removing the cap requires making the combinators lazy.
    /// </remarks>
    private const int MaxCombinatorElements = 100_000;

    private static List<object?> DrainIterable(
        Interpreter interpreter, object? iterable, string methodName)
    {
        var elements = new List<object?>();
        foreach (var element in interpreter.GetIterableElements(iterable))
        {
            if (elements.Count >= MaxCombinatorElements)
            {
                throw new SharpTSPromiseRejectedException(new SharpTSRangeError(
                    $"Promise.{methodName}: iterable yielded more than "
                    + $"{MaxCombinatorElements} elements"));
            }
            elements.Add(element);
        }
        return elements;
    }


    /// <summary>
    /// Implementation of Promise.all(iterable)
    /// Resolves when all promises resolve, rejects on first rejection.
    /// </summary>
    private static async Task<object?> AllImpl(
        List<object?> args, Interpreter interpreter, object? constructor)
    {
        var promiseResolve = GetPromiseResolve(interpreter, constructor, "all");
        var array = IterateCombinatorArgument(args, interpreter, "all");

        // Empty array resolves immediately to empty array
        if (array.Count == 0)
        {
            return new SharpTSArray([]);
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in array)
        {
            var resolved = InvokePromiseResolve(
                interpreter, constructor, promiseResolve, element);
            tasks.Add(TaskFromResolvedValue(interpreter, resolved));
        }

        // Wait for all promises - will throw on first rejection
        var results = await Task.WhenAll(tasks);
        return new SharpTSArray(new List<object?>(results));
    }

    /// <summary>
    /// Implementation of Promise.race(iterable)
    /// Resolves/rejects with the first promise to settle.
    /// </summary>
    private static async Task<object?> RaceImpl(
        List<object?> args, Interpreter interpreter, object? constructor)
    {
        var promiseResolve = GetPromiseResolve(interpreter, constructor, "race");
        var array = IterateCombinatorArgument(args, interpreter, "race");

        // Empty array never settles — there are no competitors to race.
        // BuiltInAsyncMethod wraps this method's Task in the promise it hands
        // to the guest, so returning a SharpTSPromise here would settle that
        // outer promise immediately WITH a promise object (#196). Await a task
        // that never completes instead so the outer promise stays pending.
        if (array.Count == 0)
        {
            return await new TaskCompletionSource<object?>().Task;
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in array)
        {
            object? resolved = InvokePromiseResolve(
                interpreter, constructor, promiseResolve, element);
            tasks.Add(TaskFromResolvedValue(interpreter, resolved));
        }

        // Return the result of the first task to complete
        var completedTask = await Task.WhenAny(tasks);
        return await completedTask;
    }

    private static ISharpTSCallable GetPromiseResolve(
        Interpreter interpreter, object? constructor, string methodName)
    {
        object? resolve = interpreter.GetProperty(constructor, "resolve");
        if (resolve is ISharpTSCallable callable)
        {
            return callable;
        }
        throw new SharpTSPromiseRejectedException(new SharpTSTypeError(
            $"Promise.{methodName} resolve property is not callable"));
    }

    private static object? InvokePromiseResolve(
        Interpreter interpreter,
        object? constructor,
        ISharpTSCallable promiseResolve,
        object? element)
    {
        try
        {
            return FunctionBuiltIns.CallWithThis(
                interpreter, promiseResolve, constructor, [element]);
        }
        catch (Runtime.Exceptions.ThrowException ex)
        {
            throw new SharpTSPromiseRejectedException(ex.Value);
        }
    }

    private static Task<object?> TaskFromResolvedValue(
        Interpreter interpreter, object? resolved)
    {
        if (resolved is SharpTSPromise promise)
            return promise.GetValueAsync();

        if (resolved is SharpTSObject or SharpTSInstance)
        {
            var then = interpreter.GetProperty(resolved, "then");
            if (then is ISharpTSCallable thenCallable)
            {
                var completion = new TaskCompletionSource<object?>();
                var fulfill = new PromiseResolveCallback(value =>
                {
                    if (value is SharpTSPromise inner)
                        _ = AdoptResolvedPromise(inner, completion);
                    else
                        completion.TrySetResult(value);
                });
                var reject = new PromiseRejectCallback(reason =>
                    completion.TrySetException(new SharpTSPromiseRejectedException(reason)));
                try
                {
                    FunctionBuiltIns.CallWithThis(
                        interpreter, thenCallable, resolved, [fulfill, reject]);
                }
                catch (Runtime.Exceptions.ThrowException ex)
                {
                    completion.TrySetException(new SharpTSPromiseRejectedException(ex.Value));
                }
                return completion.Task;
            }
        }

        return Task.FromResult(resolved);
    }

    private static async Task AdoptResolvedPromise(
        SharpTSPromise promise, TaskCompletionSource<object?> completion)
    {
        try
        {
            completion.TrySetResult(await promise.GetValueAsync());
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// Implementation of Promise.allSettled(iterable)
    /// Returns array of outcome objects: {status: "fulfilled"|"rejected", value?: T, reason?: E}
    /// Never rejects - always resolves with all outcomes.
    /// </summary>
    private static async Task<object?> AllSettledImpl(
        List<object?> args, Interpreter interpreter, object? constructor)
    {
        var promiseResolve = GetPromiseResolve(interpreter, constructor, "allSettled");
        var array = IterateCombinatorArgument(args, interpreter, "allSettled");

        // Empty array resolves immediately to empty array
        if (array.Count == 0)
        {
            return new SharpTSArray([]);
        }

        var tasks = new List<Task<object?>>(array.Count);

        foreach (var element in array)
        {
            var resolved = InvokePromiseResolve(
                interpreter, constructor, promiseResolve, element);
            tasks.Add(SettleForAllSettled(TaskFromResolvedValue(interpreter, resolved)));
        }

        return new SharpTSArray([.. await Task.WhenAll(tasks)]);
    }

    private static async Task<object?> SettleForAllSettled(Task<object?> resolved)
    {
        try
        {
            object? value = await resolved;
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["status"] = "fulfilled",
                ["value"] = value
            });
        }
        catch (Exception ex)
        {
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["status"] = "rejected",
                ["reason"] = ExtractRejectionReason(ex)
            });
        }
    }

    /// <summary>
    /// State holder for Promise.any operation (used instead of ref since async methods can't have ref params)
    /// </summary>
    private class AnyState
    {
        public int PendingCount;
        public readonly List<object?> RejectionReasons = [];
        public readonly TaskCompletionSource<object?> Tcs = new();
        public readonly object Lock = new();
    }

    /// <summary>
    /// Implementation of Promise.any(iterable)
    /// First fulfilled promise wins. If all reject, throws AggregateError.
    /// </summary>
    private static async Task<object?> AnyImpl(
        List<object?> args, Interpreter interpreter, object? constructor)
    {
        var promiseResolve = GetPromiseResolve(interpreter, constructor, "any");
        var array = IterateCombinatorArgument(args, interpreter, "any");

        // Empty array rejects immediately with AggregateError. Must be a real
        // SharpTSAggregateError — the same representation `new AggregateError()`
        // produces — so `e instanceof AggregateError` holds (#232).
        if (array.Count == 0)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSAggregateError(new SharpTSArray([])));
        }

        var state = new AnyState { PendingCount = array.Count };

        foreach (var element in array)
        {
            var resolved = InvokePromiseResolve(
                interpreter, constructor, promiseResolve, element);
            _ = ProcessPromiseForAny(
                TaskFromResolvedValue(interpreter, resolved), state);
        }

        return await state.Tcs.Task;
    }

    /// <summary>
    /// Helper for Promise.any - processes a single promise.
    /// </summary>
    private static async Task ProcessPromiseForAny(Task<object?> task, AnyState state)
    {
        try
        {
            var result = await task;
            // First fulfillment wins
            state.Tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            HandleRejectionForAny(ExtractRejectionReason(ex), state);
        }
    }

    /// <summary>
    /// Extracts the guest rejection value from a faulted-promise exception:
    /// the rejection Reason, a guest-thrown value (ThrowException from a
    /// `throw` inside an async function), either of those wrapped in the
    /// AggregateException that Task faults arrive in, or — last resort —
    /// the host exception message. Keeps `e.errors` / allSettled `reason`
    /// carrying what the promise actually rejected with (#232).
    /// </summary>
    private static object? ExtractRejectionReason(Exception ex)
    {
        if (ex is AggregateException agg && agg.InnerException is Exception inner)
            ex = inner;
        return ex switch
        {
            SharpTSPromiseRejectedException rejected => rejected.Reason,
            Exceptions.ThrowException thrown => thrown.Value,
            _ => ex.Message
        };
    }

    /// <summary>
    /// Helper for Promise.any - handles a rejection.
    /// </summary>
    private static void HandleRejectionForAny(object? reason, AnyState state)
    {
        lock (state.Lock)
        {
            state.RejectionReasons.Add(reason);
            state.PendingCount--;

            // If all promises rejected, reject with a real SharpTSAggregateError
            // so `e instanceof AggregateError` / `instanceof Error` hold (#232).
            if (state.PendingCount == 0)
            {
                var aggregateError = new SharpTSAggregateError(
                    new SharpTSArray(state.RejectionReasons));

                state.Tcs.TrySetException(new SharpTSPromiseRejectedException(aggregateError));
            }
        }
    }

    /// <summary>
    /// Implementation of Promise.resolve(value?). Returns an existing promise
    /// unchanged when its constructor is the receiver; otherwise creates a
    /// promise that adopts the supplied value.
    /// </summary>
    private static object? ResolveStatic(
        Interpreter interpreter,
        object? receiver,
        List<object?> args,
        Func<Interpreter, Task<object?>, SharpTSPromise>? promiseFactory)
    {
        RequireConstructorReceiver(receiver);
        var value = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;

        if (value is SharpTSPromise promise
            && ReferenceEquals(interpreter.GetProperty(promise, "constructor"), receiver))
        {
            return promise;
        }

        var task = ResolveImplAsync(args);
        if (promiseFactory != null)
            return promiseFactory(interpreter, task);
        bool isBaseConstructor = ReferenceEquals(receiver, Interpreter.PromiseGlobalValue)
            || receiver is SharpTSPromiseClass promiseClass
                && ReferenceEquals(promiseClass, SharpTSPromiseClass.PromiseBase);
        if (!isBaseConstructor)
        {
            return ConstructPromiseCapabilityAndAdopt(interpreter, receiver, task);
        }
        return new SharpTSPromise(task);
    }

    private static async Task<object?> ResolveImplAsync(List<object?> args)
    {
        var value = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;

        // If already a Promise, await it to unwrap and avoid double-wrapping
        // (BuiltInAsyncMethod.Call will wrap the result in a new Promise)
        if (value is SharpTSPromise promise)
        {
            // Properly await the promise instead of blocking
            return await promise.GetValueAsync();
        }

        // Return the raw value - BuiltInAsyncMethod.Call will wrap it in a Promise
        return value;
    }

    /// <summary>
    /// Implementation of Promise.reject(reason)
    /// Throws an exception that BuiltInAsyncMethod.Call will convert to a rejected Promise.
    /// </summary>
    private static object? RejectImpl(List<object?> args)
    {
        var reason = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        // Throw to let BuiltInAsyncMethod.Call create the rejected Promise
        throw new SharpTSPromiseRejectedException(reason);
    }

    /// <summary>
    /// Implementation of Promise.withResolvers()
    /// Returns {promise, resolve, reject} for external promise resolution.
    /// The promise comes from <paramref name="promiseFactory"/> when called
    /// off a Promise subclass (#242), else is a plain SharpTSPromise.
    /// </summary>
    private static object? WithResolversImpl(
        Interpreter interpreter,
        Func<Interpreter, Task<object?>, SharpTSPromise>? promiseFactory)
    {
        var tcs = new TaskCompletionSource<object?>();

        var resolveMethod = BuiltInMethod.CreateV2("resolve", 1, (_, _, args) =>
        {
            var value = args.Length > 0 ? args[0].ToObject() : null;
            tcs.TrySetResult(value);
            return RuntimeValue.Null;
        });

        var rejectMethod = BuiltInMethod.CreateV2("reject", 1, (_, _, args) =>
        {
            var reason = args.Length > 0 ? args[0].ToObject() : null;
            tcs.TrySetException(new SharpTSPromiseRejectedException(reason));
            return RuntimeValue.Null;
        });

        var promise = promiseFactory != null
            ? promiseFactory(interpreter, tcs.Task)
            : new SharpTSPromise(tcs.Task);

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["promise"] = promise,
            ["resolve"] = resolveMethod,
            ["reject"] = rejectMethod
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Calls a callback function with the given arguments.
    /// Handles both sync and async callables.
    /// </summary>
    private static object? CallCallback(ISharpTSCallable callback, List<object?> args, Interpreter interpreter)
    {
        return callback.Call(interpreter, args);
    }

    /// <summary>
    /// Unwraps a result that might be a Promise.
    /// If the result is a Promise, awaits it and flattens.
    /// </summary>
    /// <remarks>
    /// GetValueAsync() already contains a while-loop to flatten arbitrarily nested
    /// Promises, so we only need a single check here.
    /// </remarks>
    private static async Task<object?> UnwrapResult(object? result)
    {
        if (result is SharpTSPromise promise)
        {
            return await promise.GetValueAsync();
        }
        return result;
    }

    #endregion
}
