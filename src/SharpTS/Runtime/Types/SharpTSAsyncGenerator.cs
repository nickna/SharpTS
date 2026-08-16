using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime object representing an active async generator instance.
/// </summary>
/// <remarks>
/// Created by <see cref="SharpTSAsyncGeneratorFunction"/> (declarations) and
/// <see cref="SharpTSAsyncArrowGeneratorFunction"/> (function expressions) when called — both drive
/// the same body-statement list, so a single type serves both. Each <c>next()</c>/<c>return()</c>/
/// <c>throw()</c> returns a <see cref="Task{T}"/> that resolves to a <see cref="SharpTSIteratorResult"/>.
///
/// <para><b>Lazy coroutine model.</b> Unlike a fully synchronous generator (which suspends a worker
/// thread at each <c>yield</c>), an async generator can <c>await</c> mid-body, so it cannot use a
/// background thread: the interpreter is single-threaded (a custom <see cref="System.Threading.SynchronizationContext"/>
/// routes every async continuation back to the one event-loop thread), and a worker would race the
/// event loop on the shared interpreter environment. Instead the body runs as an ordinary
/// interpreter async execution (<see cref="Interpreter.ExecuteBlockAsync"/>) on that event-loop
/// thread and suspends at each <c>yield</c> by handing the value to the driving request and awaiting
/// the next one. Reusing the real async execution means an <c>await</c> nested inside a yielded
/// expression preserves the ambient environment like any async function (no closure leak, #752) and a
/// <c>for await…of</c> inside the body drives the async-iterator protocol natively (#717).</para>
///
/// <para><b>Request queue.</b> Pending <c>next()</c>/<c>return()</c>/<c>throw()</c> calls are serviced
/// FIFO (ECMA-262 §27.6.3 AsyncGenerator request queue), so two <c>next()</c> issued before the first
/// settles resolve in call order instead of racing (#690). A re-entrant resume (the body advancing
/// itself synchronously) is rejected with a catchable <c>TypeError</c> rather than deadlocking (#542,
/// mirrors compiled mode).</para>
/// </remarks>
/// <seealso cref="SharpTSAsyncGeneratorFunction"/>
/// <seealso cref="SharpTSAsyncArrowGeneratorFunction"/>
/// <seealso cref="SharpTSGenerator"/>
public class SharpTSAsyncGenerator : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.AsyncGenerator;

    private readonly List<Stmt> _body;
    private readonly RuntimeEnvironment _environment;
    private readonly Interpreter _interpreter;

    private enum State
    {
        /// <summary>Created but body not started; the first next() starts it.</summary>
        SuspendedStart,
        /// <summary>Body suspended at a yield, awaiting the next request.</summary>
        SuspendedYield,
        /// <summary>Body running or suspended at an await (started, not at a yield, not finished).</summary>
        Executing,
        /// <summary>Body finished (ran off the end, returned, or threw).</summary>
        Completed,
    }

    private State _state = State.SuspendedStart;

    /// <summary>
    /// A queued <c>next</c>/<c>return</c>/<c>throw</c>: how it resumes the body, the carried value, and
    /// the promise handed back to the caller (resolved/rejected when the body produces the matching
    /// result). Continuations run asynchronously so a resolved request never re-enters the body inline.
    /// </summary>
    private sealed class Request(GeneratorResumeKind kind, object? value)
    {
        public GeneratorResumeKind Kind { get; } = kind;
        public object? Value { get; } = value;
        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Requests received while the body is Executing (mid-await), drained in arrival order (§27.6.3).
    private readonly Queue<Request> _queue = new();
    // The request the body's next yield/completion will fulfill.
    private Request? _currentRequest;
    // Set while the body is SuspendedYield: completed by the resuming request to wake the body.
    private TaskCompletionSource<Request>? _pendingResume;

    // True whenever the body is synchronously on the call stack — between a resume (start, await
    // completion, or yield resume) and its next suspension/completion. A next/return/throw observing
    // this is the body advancing itself synchronously (re-entrancy), which a real queue can't serve
    // here without deadlocking the body it blocks (#542, #771). Cleared at every suspend point (await
    // via AwaitPreservingEnvironment, yield via SuspendAtYieldAsync) and on completion (CompleteBody),
    // re-set on each resume. Safe to toggle unconditionally: the interpreter is single-threaded, so an
    // external request can only fire while the body is suspended (flag false) — the spurious-true
    // windows are uninterruptible synchronous segments.
    private bool _running;

    /// <summary>
    /// Marks the body as suspended (off the call stack) at an await. Called by
    /// <see cref="Interpreter.AwaitPreservingEnvironment"/> — the chokepoint every guest await routes
    /// through — just before it suspends, so a concurrent request issued during a genuine async gap is
    /// queued rather than misread as re-entrancy (#771).
    /// </summary>
    internal void MarkBodySuspended() => _running = false;

    /// <summary>
    /// Marks the body as resumed (back synchronously on the call stack) after an await. Paired with
    /// <see cref="MarkBodySuspended"/>; runs on both the resolved and rejected resume paths (a body
    /// unwinding a rejected await is still synchronously on the stack).
    /// </summary>
    internal void MarkBodyResumed() => _running = true;

    public SharpTSAsyncGenerator(List<Stmt> body, RuntimeEnvironment environment, Interpreter interpreter)
    {
        _body = body;
        _environment = environment;
        _interpreter = interpreter;
    }

    /// <summary>Advances the generator, resuming a suspended <c>yield</c> with <c>undefined</c>.</summary>
    public Task<object?> Next() => Resume(GeneratorResumeKind.Next, SharpTSUndefined.Instance);

    /// <summary>Advances the generator, resuming a suspended <c>yield</c> with <paramref name="sentValue"/>.</summary>
    public Task<object?> Next(object? sentValue) => Resume(GeneratorResumeKind.Next, sentValue);

    /// <summary>
    /// Resumes the generator with a <c>return</c> completion. A suspended body runs its enclosing
    /// <c>finally</c> blocks before settling as <c>{ value, done: true }</c> (a yielding finally
    /// suspends here instead, ECMA-262 §27.6.1.3 / §14.4.14); a not-yet-started or finished one simply
    /// reports the value.
    /// </summary>
    public Task<object?> Return(object? value = null) => Resume(GeneratorResumeKind.Return, value);

    /// <summary>
    /// Resumes the generator with a <c>throw</c> completion at the suspended <c>yield</c>, running any
    /// active <c>catch</c>/<c>finally</c>. If a catch handles it the body continues; otherwise the
    /// returned promise rejects.
    /// </summary>
    public Task<object?> Throw(object? error = null) => Resume(GeneratorResumeKind.Throw, error);

    /// <summary>
    /// Core of <c>next()</c>/<c>return()</c>/<c>throw()</c>: enqueues the request and, depending on the
    /// generator's state, starts the body, resumes it from a yield, or queues behind an in-flight run.
    /// </summary>
    private Task<object?> Resume(GeneratorResumeKind kind, object? value)
    {
        // Re-entrancy (the body advancing itself during its synchronous segment): reject rather than
        // deadlock — the queued request could only be served by the body that is blocking on it (#542).
        if (_running)
            return Task.FromException<object?>(
                new ThrowException(new SharpTSTypeError("Async generator is already running")));

        switch (_state)
        {
            case State.Completed:
                return SettleCompleted(kind, value);

            case State.SuspendedStart:
                if (kind == GeneratorResumeKind.Return)
                {
                    // return() before the body starts closes it without running the body.
                    _state = State.Completed;
                    return Task.FromResult<object?>(new SharpTSIteratorResult(value, done: true));
                }
                if (kind == GeneratorResumeKind.Throw)
                {
                    // throw() before the body starts completes it abnormally.
                    _state = State.Completed;
                    return Task.FromException<object?>(ThrowException.FromResult(value));
                }
                return StartBody(new Request(kind, value));

            case State.SuspendedYield:
                var resumeReq = new Request(kind, value);
                _currentRequest = resumeReq;
                _state = State.Executing;
                var pending = _pendingResume!;
                _pendingResume = null;
                pending.SetResult(resumeReq); // wakes the body on a later turn (RunContinuationsAsynchronously)
                return resumeReq.Completion.Task;

            case State.Executing:
            default:
                // Body is mid-await (concurrent next): queue and let the body pick it up at its next yield.
                var queued = new Request(kind, value);
                _queue.Enqueue(queued);
                return queued.Completion.Task;
        }
    }

    /// <summary>
    /// Starts the body for the first <c>next()</c>. The body runs synchronously up to its first
    /// suspension (matching ECMA-262, where the first resume runs the body until its first
    /// await/yield); the surrounding save/restore keeps that synchronous prologue from leaking the
    /// generator's environment / active-generator binding into the caller that issued <c>next()</c>.
    /// </summary>
    private Task<object?> StartBody(Request first)
    {
        _currentRequest = first;
        _state = State.Executing;

        RuntimeEnvironment savedEnv = _interpreter.Environment;
        SharpTSAsyncGenerator? savedGen = _interpreter.CurrentAsyncGenerator;
        _running = true;
        try
        {
            _ = RunBodyAsync();
        }
        finally
        {
            _running = false;
            _interpreter.CurrentAsyncGenerator = savedGen;
            _interpreter.SetEnvironment(savedEnv);
        }
        return first.Completion.Task;
    }

    /// <summary>
    /// Runs the generator body as an ordinary interpreter async execution, installing this generator as
    /// the interpreter's active async-generator (so <c>yield</c> expressions suspend through it) and the
    /// generator closure as the ambient environment. Settles the driving request with the completion
    /// value, a <c>return</c> value, or a thrown error.
    /// </summary>
    private async Task RunBodyAsync()
    {
        RuntimeEnvironment prevEnv = _interpreter.Environment;
        SharpTSAsyncGenerator? prevGen = _interpreter.CurrentAsyncGenerator;
        _interpreter.SetEnvironment(_environment);
        _interpreter.CurrentAsyncGenerator = this;

        object? completionValue = SharpTSUndefined.Instance;
        Exception? pendingException = null;
        try
        {
            ExecutionResult result = await _interpreter.ExecuteBlockAsync(_body, _environment);
            if (result.Type == ExecutionResult.ResultType.Return)
                completionValue = result.Value.ToObject();
            else if (result.Type == ExecutionResult.ResultType.Throw)
                pendingException = ThrowException.FromResult(result.Value.ToObject());
        }
        catch (GeneratorReturnException grex)
        {
            // A return() injected at a yield with no enclosing try unwinds to here: settle as a return.
            completionValue = grex.Value;
        }
        catch (Exception ex) when (ex is not YieldException)
        {
            // A rejected await, or a guest throw surfaced as a host exception: reject the driving request.
            pendingException = ex;
        }
        finally
        {
            _interpreter.CurrentAsyncGenerator = prevGen;
            _interpreter.SetEnvironment(prevEnv);
        }

        CompleteBody(completionValue, pendingException);
    }

    /// <summary>
    /// Suspends the body at a plain <c>yield</c>: hands the yielded value to the driving request and
    /// awaits the next one. Invoked by the interpreter when it evaluates a <c>yield</c> inside this
    /// generator's body (see <see cref="Interpreter.EvaluateYieldAsync"/>).
    /// </summary>
    internal Task<object?> OnYieldAsync(object? value, bool isDelegating)
        => isDelegating ? DelegateYieldStarAsync(value) : PlainYieldAsync(value);

    private async Task<object?> PlainYieldAsync(object? value)
    {
        GeneratorResume resume = await SuspendAtYieldAsync(value);
        // The resumed yield evaluates to the sent value; an abrupt resume (return/throw) is realized as
        // the control-flow exception that unwinds the body's own try/finally blocks (§27.5.3.4).
        return resume.Realize();
    }

    /// <summary>
    /// The core suspend: delivers <paramref name="value"/> as <c>{ value, done: false }</c> to the
    /// driving request, awaits the next request, and re-asserts the generator's scope on resume (the
    /// await may have run unrelated event-loop work in between).
    /// </summary>
    private async Task<GeneratorResume> SuspendAtYieldAsync(object? value)
    {
        RuntimeEnvironment genEnv = _interpreter.Environment;
        Request req = _currentRequest!;
        _currentRequest = null;
        _state = State.SuspendedYield;
        req.Completion.SetResult(new SharpTSIteratorResult(value, done: false));

        // The body is now off the stack until a request resumes it, so a request arriving in this
        // window is a normal resume (or a legit concurrent next), not re-entrancy — clear the guard
        // and re-set it once the body is back on the stack (#771).
        _running = false;
        Request next = await TakeNextRequestAsync();
        _running = true;

        _interpreter.SetEnvironment(genEnv);
        _interpreter.CurrentAsyncGenerator = this;
        return new GeneratorResume(next.Kind, next.Value);
    }

    /// <summary>
    /// Returns the next request to resume with: a concurrently-queued one immediately, otherwise a task
    /// completed by the resuming <c>next()</c>/<c>return()</c>/<c>throw()</c>.
    /// </summary>
    private Task<Request> TakeNextRequestAsync()
    {
        if (_queue.Count > 0)
        {
            Request req = _queue.Dequeue();
            _currentRequest = req;
            _state = State.Executing;
            return Task.FromResult(req);
        }
        // _state stays SuspendedYield; Resume() sets _currentRequest + Executing when it wakes us.
        _pendingResume = new TaskCompletionSource<Request>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pendingResume.Task;
    }

    /// <summary>
    /// <c>yield*</c> delegation (ECMA-262 §14.4.14, async path). A delegated async generator is driven
    /// via <c>next/return/throw</c>, forwarding the outer's resume completion into it; a sync iterable
    /// is iterated lazily with the outer suspended for each element.
    /// </summary>
    private async Task<object?> DelegateYieldStarAsync(object? iterable)
    {
        if (iterable is SharpTSAsyncGenerator inner)
        {
            var received = new GeneratorResume(GeneratorResumeKind.Next, SharpTSUndefined.Instance);
            while (true)
            {
                // Drive the delegate with the completion the outer was resumed with, preserving the
                // outer's environment across the inner's own suspensions.
                Task<object?> step = received.Kind switch
                {
                    GeneratorResumeKind.Return => inner.Return(received.Value),
                    GeneratorResumeKind.Throw => inner.Throw(received.Value),
                    _ => inner.Next(received.Value),
                };
                object? innerResult = await _interpreter.AwaitPreservingEnvironment(step);
                (bool done, object? innerValue) = ReadIteratorResult(innerResult);

                if (done)
                {
                    // return → the outer generator itself returns the delegate's value (step c.viii);
                    // next/throw(handled) → yield* evaluates to it and the outer continues (steps a.v/b.5).
                    if (received.Kind == GeneratorResumeKind.Return)
                        throw new GeneratorReturnException(innerValue);
                    return innerValue;
                }

                received = await SuspendAtYieldAsync(innerValue);
            }
        }

        // Custom iterator objects (those with [Symbol.iterator] and a next(v) that
        // consumes its argument) are driven manually so the outer's resume value is
        // forwarded as the argument to next(v) (ECMA-262 §14.4.14, #503).
        if (_interpreter.TryGetCustomIteratorProtocol(iterable, out var iterObj, out var nextFn))
        {
            object? sentValue = SharpTSUndefined.Instance;
            while (true)
            {
                var result = nextFn!.Call(_interpreter, [sentValue]);
                (bool done, object? innerValue) = ReadIteratorResult(result);
                if (done) return innerValue ?? SharpTSUndefined.Instance;
                GeneratorResume resume = await SuspendAtYieldAsync(innerValue);
                switch (resume.Kind)
                {
                    case GeneratorResumeKind.Return:
                    case GeneratorResumeKind.Throw:
                        resume.Realize();
                        return null; // unreachable
                    default:
                        sentValue = resume.Value;
                        break;
                }
            }
        }

        // Built-in iterables (arrays, strings, Maps, Sets): next() ignores resume value.
        foreach (object? element in _interpreter.GetIterableElements(iterable))
        {
            GeneratorResume resume = await SuspendAtYieldAsync(element);
            resume.Realize();
        }
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Settles the body's completion: resolves the driving request once with the completion value
    /// (a <c>return</c> value or undefined when it ran off the end), or rejects it with a thrown error.
    /// Any requests queued behind it report <c>{ undefined, done: true }</c> (the value is delivered
    /// once, §27.6.1.2).
    /// </summary>
    private void CompleteBody(object? completionValue, Exception? pendingException)
    {
        _state = State.Completed;
        // The body's final segment ran synchronously after an await/yield resume that set _running;
        // clear it so a post-completion next()/return()/throw() settles normally rather than being
        // rejected as re-entrancy (#771).
        _running = false;
        Request? req = _currentRequest;
        _currentRequest = null;
        if (req != null)
        {
            if (pendingException != null)
                req.Completion.SetException(pendingException);
            else
                req.Completion.SetResult(new SharpTSIteratorResult(completionValue, done: true));
        }

        while (_queue.Count > 0)
            SettleCompletedInto(_queue.Dequeue());
    }

    /// <summary>Resolves a next/return/throw issued after the generator has finished (§27.6.1.2).</summary>
    private static Task<object?> SettleCompleted(GeneratorResumeKind kind, object? value) => kind switch
    {
        GeneratorResumeKind.Throw => Task.FromException<object?>(ThrowException.FromResult(value)),
        GeneratorResumeKind.Return => Task.FromResult<object?>(new SharpTSIteratorResult(value, done: true)),
        _ => Task.FromResult<object?>(new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true)),
    };

    private static void SettleCompletedInto(Request req)
    {
        switch (req.Kind)
        {
            case GeneratorResumeKind.Throw:
                req.Completion.SetException(ThrowException.FromResult(req.Value));
                break;
            case GeneratorResumeKind.Return:
                req.Completion.SetResult(new SharpTSIteratorResult(req.Value, done: true));
                break;
            default:
                req.Completion.SetResult(new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true));
                break;
        }
    }

    private static (bool Done, object? Value) ReadIteratorResult(object? result) => result switch
    {
        SharpTSIteratorResult ir => (ir.Done, ir.Value),
        SharpTSObject obj => (RuntimeTypes.IsTruthy(obj.GetProperty("done")), obj.GetProperty("value")),
        _ => (true, result),
    };

    public override string ToString() => "[object AsyncGenerator]";
}
