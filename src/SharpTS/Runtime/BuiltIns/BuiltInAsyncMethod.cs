using SharpTS.Execution;
using SharpTS.Runtime.Types;
using SharpTS.Runtime.Exceptions;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Callable wrapper for native C# async implementations of built-in methods.
/// Used by Promise methods that must return Promises.
/// </summary>
/// <remarks>
/// PROMISE WRAPPING CONTRACT:
///
/// This wrapper automatically wraps the implementation's result in a SharpTSPromise.
/// Therefore, implementation functions should return RAW VALUES, not Promises.
///
/// ✓ CORRECT: Implementation returns Task&lt;object?&gt; containing the raw value
///   Example: Task.FromResult(42) → Call() returns SharpTSPromise(Task&lt;42&gt;)
///
/// ✗ WRONG: Implementation returns Task&lt;SharpTSPromise&gt;
///   This causes double-wrapping: SharpTSPromise(Task&lt;SharpTSPromise(Task&lt;value&gt;)&gt;)
///   which leads to infinite loops when awaited in async iterators.
///
/// For Promise.reject(), throw SharpTSPromiseRejectedException instead of
/// returning a rejected Promise - the catch block will create the rejected Promise.
/// </remarks>
public class BuiltInAsyncMethod : ISharpTSCallable, ISharpTSAsyncCallable, IBuiltInFunctionMetadata
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, Task<object?>> _implementation;
    private readonly Func<Interpreter, Task<object?>, object?>? _promiseFactory;
    private readonly Func<Interpreter, object?, Func<Interpreter, Task<object?>, object?>?>? _speciesResolver;
    private readonly bool _refsEventLoopWhileInFlight;
    private readonly BuiltInFunctionMetadata _metadata;
    private int _specLength = -1;
    private object? _receiver;

    // Cache for bound methods - uses weak references to avoid memory leaks
    private ConditionalWeakTable<object, BuiltInAsyncMethod>? _boundMethodCache;

    public BuiltInAsyncMethod(
        string name,
        int arity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation)
        : this(name, arity, arity, implementation) { }

    /// <param name="promiseFactory">
    /// Optional factory used to materialize the result promise from the
    /// implementation's task. Promise subclasses (#242) pass one so derived
    /// promises (then/catch/finally results, inherited statics) come out as
    /// subclass instances; null means a plain <see cref="SharpTSPromise"/>.
    /// </param>
    /// <param name="refsEventLoopWhileInFlight">
    /// True for built-ins whose task is real native I/O that always completes (fs, dns, …):
    /// the in-flight task Refs the event loop so the quiescence heuristics cannot abandon a
    /// program whose only pending work is that I/O (the promise-API counterpart of the
    /// callback-API Ref convention from #205). Must stay false for state promises that may
    /// legitimately never settle (reader.closed, pipeline of an unfinished stream, then-
    /// chaining) — those would otherwise hold the loop open forever.
    /// </param>
    /// <param name="speciesResolver">
    /// Optional synchronous pre-step run at call time, BEFORE the rejected-promise
    /// try below, that returns the result-promise <paramref name="promiseFactory"/>
    /// to use for this invocation. Takes precedence over <paramref name="promiseFactory"/>.
    /// Promise.prototype.then/catch/finally pass one so they can read
    /// <c>SpeciesConstructor(promise, %Promise%)</c> (ECMA-262 §27.2.5.4 step 3 —
    /// a ReturnIfAbrupt before PerformPromiseThen): a poisoned <c>constructor</c>/
    /// <c>@@species</c> getter throws SYNCHRONOUSLY out of the call rather than
    /// rejecting the result promise (test262 then/ctor-poisoned, #350).
    /// </param>
    public BuiltInAsyncMethod(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation,
        Func<Interpreter, Task<object?>, object?>? promiseFactory = null,
        bool refsEventLoopWhileInFlight = false,
        Func<Interpreter, object?, Func<Interpreter, Task<object?>, object?>?>? speciesResolver = null)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
        _promiseFactory = promiseFactory;
        _refsEventLoopWhileInFlight = refsEventLoopWhileInFlight;
        _speciesResolver = speciesResolver;
        _metadata = new BuiltInFunctionMetadata();
    }

    // Private constructor for creating bound instances
    private BuiltInAsyncMethod(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation,
        Func<Interpreter, Task<object?>, object?>? promiseFactory,
        bool refsEventLoopWhileInFlight,
        Func<Interpreter, object?, Func<Interpreter, Task<object?>, object?>?>? speciesResolver,
        object? receiver,
        BuiltInFunctionMetadata metadata,
        int specLength)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
        _promiseFactory = promiseFactory;
        _refsEventLoopWhileInFlight = refsEventLoopWhileInFlight;
        _speciesResolver = speciesResolver;
        _receiver = receiver;
        _metadata = metadata;
        _specLength = specLength;
    }

    /// <summary>
    /// ECMA-262 §17 `length`. Falls back to the minimum arity, which matches most methods;
    /// set it explicitly via <see cref="WithSpecLength"/> where the two differ — e.g.
    /// `Promise.prototype.then` accepts 0 arguments but has spec length 2.
    /// </summary>
    public int Arity() => _specLength >= 0 ? _specLength : _minArity;

    /// <summary>
    /// Returns this same instance with the JS-spec length set. Mutates in place — safe
    /// because these are constructed per-lookup and immediately handed to one caller.
    /// </summary>
    public BuiltInAsyncMethod WithSpecLength(int specLength)
    {
        _specLength = specLength;
        return this;
    }

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public BuiltInAsyncMethod Bind(object? receiver)
    {
        // Null receivers don't need caching
        if (receiver == null)
        {
            return new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, _promiseFactory, _refsEventLoopWhileInFlight, _speciesResolver, null, _metadata, _specLength);
        }

        // Value types can't be cached efficiently
        if (receiver.GetType().IsValueType)
        {
            return new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, _promiseFactory, _refsEventLoopWhileInFlight, _speciesResolver, receiver, _metadata, _specLength);
        }

        // Initialize cache lazily
        _boundMethodCache ??= new ConditionalWeakTable<object, BuiltInAsyncMethod>();

        // Try to get cached bound method
        if (_boundMethodCache.TryGetValue(receiver, out var cached))
        {
            return cached;
        }

        // Create new bound method and cache it
        var bound = new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, _promiseFactory, _refsEventLoopWhileInFlight, _speciesResolver, receiver, _metadata, _specLength);
        _boundMethodCache.AddOrUpdate(receiver, bound);
        return bound;
    }

    /// <summary>
    /// Synchronous call - returns a Promise that wraps the async execution.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);

        // Resolve the result-promise factory SYNCHRONOUSLY before entering the
        // try below. For then/catch/finally this performs the SpeciesConstructor
        // read (§27.2.5.4 step 3), so a poisoned constructor/@@species getter
        // throws synchronously out of this call instead of being swallowed into
        // a rejected promise by the catch (#350). Absent a resolver, the static
        // factory (subclass-typed inherited statics, #242) is used.
        var factory = _speciesResolver != null
            ? _speciesResolver(interpreter, _receiver)
            : _promiseFactory;

        try
        {
            var task = _implementation(interpreter, _receiver, arguments);
            KeepEventLoopAliveUntilComplete(interpreter, task);
            return SuppressLegacyReactionWait(WrapResult(interpreter, factory, task));
        }
        catch (Exception ex)
        {
            // If the implementation throws synchronously, return a rejected Promise
            object? errorValue = ex switch
            {
                ThrowException tex => tex.Value,
                SharpTSPromiseRejectedException rex => rex.Reason,
                _ => ex.Message
            };
            if (factory == null)
                return SuppressLegacyReactionWait(SharpTSPromise.Reject(errorValue));
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(errorValue));
            return SuppressLegacyReactionWait(WrapResult(interpreter, factory, tcs.Task));
        }
    }

    private object? SuppressLegacyReactionWait(object? result)
    {
        // A discarded reaction result must never turn the containing expression
        // statement into an implicit top-level await. That legacy interpreter
        // convention would drain the just-queued job before the following
        // synchronous statement, reversing ECMAScript ordering (#1440).
        if (_name is "then" or "catch" or "finally" && result is SharpTSPromise promise)
            promise.SuppressImplicitTopLevelWait = true;
        return result;
    }

    private static object? WrapResult(
        Interpreter interpreter,
        Func<Interpreter, Task<object?>, object?>? factory,
        Task<object?> task)
        => factory != null ? factory(interpreter, task) : new SharpTSPromise(task);

    /// <summary>
    /// Async call - awaits the implementation directly.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);
        var task = _implementation(interpreter, _receiver, arguments);
        KeepEventLoopAliveUntilComplete(interpreter, task);
        return await task;
    }

    /// <summary>
    /// Refs the event loop for the lifetime of an in-flight native task (opt-in via
    /// <c>refsEventLoopWhileInFlight</c>). An I/O built-in's thread-pool task is otherwise
    /// invisible to <c>HasPendingEventLoopWork</c>, so the "never-settling promise" quiescence
    /// heuristic in <c>WaitForPromise</c>/<c>RunEventLoop</c> would abandon a program whose only
    /// pending work is real I/O slower than the quiescence window (the fs-on-slow-CI empty-output
    /// failure class). Mirrors Node: in-flight I/O keeps the process alive; a bare pending
    /// promise does not.
    /// </summary>
    private void KeepEventLoopAliveUntilComplete(Interpreter interpreter, Task task)
    {
        if (!_refsEventLoopWhileInFlight || task.IsCompleted) return;
        interpreter.Ref();
        task.ContinueWith(
            static (_, state) => ((Interpreter)state!).Unref(),
            interpreter,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ValidateArguments(List<object?> arguments)
    {
        if (arguments.Count < _minArity || arguments.Count > _maxArity)
        {
            throw new Exception(
                $"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Count}.");
        }
    }

    public override string ToString() => $"<built-in async {_name}>";
}
