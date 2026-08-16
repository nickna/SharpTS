using System.Collections;
using System.Runtime.ExceptionServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;
using SharpTS.Execution;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// How a suspended generator's <c>yield</c> is resumed (ECMA-262 §27.5.3.4): a plain
/// <c>next(v)</c>, or a <c>return(v)</c>/<c>throw(e)</c> that injects an abrupt completion
/// so active <c>try</c>/<c>finally</c>(/<c>catch</c>) blocks run before the generator settles.
/// </summary>
internal enum GeneratorResumeKind { Next, Return, Throw }

/// <summary>
/// The completion that resumes a suspended generator: its <see cref="Kind"/> plus the carried
/// value (the sent value for <c>next</c>, the return value for <c>return</c>, the error for
/// <c>throw</c>). Returned by the suspend point so a <c>yield*</c> delegation loop can forward
/// the completion into the delegated iterator (ECMA-262 §14.4.14) instead of unwinding the
/// outer generator immediately.
/// </summary>
internal readonly record struct GeneratorResume(GeneratorResumeKind Kind, object? Value)
{
    /// <summary>
    /// Realizes an abrupt resume as the control-flow exception the interpreter unwinds it
    /// with — <see cref="Exceptions.GeneratorReturnException"/> for a return (bypasses
    /// <c>catch</c>, runs <c>finally</c>), <see cref="Exceptions.ThrowException"/> for a
    /// throw. A normal resume just returns its sent value. Used where the suspended <c>yield</c>
    /// is NOT a delegation (a plain <c>yield</c>) and by the <c>yield*</c> fallback for
    /// non-generator iterables.
    /// </summary>
    public object? Realize() => Kind switch
    {
        GeneratorResumeKind.Return => throw new Exceptions.GeneratorReturnException(Value),
        GeneratorResumeKind.Throw => throw Exceptions.ThrowException.FromResult(Value),
        _ => Value,
    };
}

/// <summary>
/// Runtime object representing an active generator instance.
/// </summary>
/// <remarks>
/// Created by <see cref="SharpTSGeneratorFunction"/> (generator declarations) and
/// <see cref="SharpTSArrowGeneratorFunction"/> (generator function expressions) when called — both
/// drive the same body-statement list, so a single generator type serves both. Uses thread-based
/// coroutines for lazy evaluation — the generator body runs on a background thread and suspends at
/// each yield point via synchronization primitives. This correctly handles infinite generators and
/// lazy iteration.
/// </remarks>
/// <seealso cref="SharpTSGeneratorFunction"/>
/// <seealso cref="SharpTSArrowGeneratorFunction"/>
/// <seealso cref="SharpTSIteratorResult"/>
public class SharpTSGenerator : IEnumerable<object?>, IDisposable, ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Generator;
    private readonly List<Stmt> _body;
    private readonly RuntimeEnvironment _environment;
    private readonly Interpreter _interpreter;

    // Coroutine synchronization
    private Thread? _workerThread;
    private readonly ManualResetEventSlim _callerReady = new(false);
    private readonly ManualResetEventSlim _workerReady = new(false);

    // State
    private enum State { NotStarted, Suspended, Running, Completed }
    private State _state = State.NotStarted;
    private object? _yieldedValue;
    // The generator's completion value. Defaults to undefined so a body that falls off the
    // end (or a no-arg return()) reports { value: undefined, done: true }, not C# null.
    private object? _returnValue = SharpTSUndefined.Instance;
    // The value passed to the next/return call that resumes the suspended yield.
    // ECMA-262 §27.5.3.3: `yield expr` evaluates to the argument of the resuming
    // `next(v)`. Defaults to undefined so a yield resumed without an explicit value
    // (for...of, yield* delegation, bare next()) evaluates to undefined.
    private object? _sentValue = SharpTSUndefined.Instance;

    // How the suspended yield is resumed (ECMA-262 §27.5.3.4). A plain next(v) resumes
    // normally; return(v)/throw(e) inject an abrupt completion at the yield point so any
    // active try/finally blocks run before the generator settles. The resuming call sets
    // this (and _injectedValue) before signaling the worker, which reads it on wake.
    private GeneratorResumeKind _resumeKind = GeneratorResumeKind.Next;
    // Value carried by an abrupt resume: the return value for Return, the error for Throw.
    private object? _injectedValue;

    private bool _closed;
    private Exception? _workerException;

    // Caller state save/restore across yield points
    private RuntimeEnvironment? _callerEnv;
    private Func<object?, bool, object?>? _callerYieldCallback;

    public SharpTSGenerator(List<Stmt> body, RuntimeEnvironment environment, Interpreter interpreter)
    {
        _body = body;
        _environment = environment;
        _interpreter = interpreter;
    }

    /// <summary>
    /// Rejects a re-entrant resume. ECMA-262 §27.5.3.3 (GeneratorValidate) requires
    /// <c>next</c>/<c>return</c>/<c>throw</c> on a generator whose state is <c>executing</c> to
    /// throw a <c>TypeError</c>. The only way a guest call observes <see cref="State.Running"/>
    /// is re-entrancy — the body advancing itself — because a non-re-entrant caller is blocked in
    /// <see cref="AwaitWorker"/> for the whole running window. Without this guard the re-entrant
    /// call (which runs on the worker thread) would signal the worker to resume itself and then
    /// wait on the same worker-ready event, deadlocking both threads (#515). Checked before the
    /// started/completed branches, matching the spec (executing is rejected before completed is
    /// observed).
    /// </summary>
    private void ThrowIfExecuting()
    {
        if (_state == State.Running)
            throw new ThrowException(new SharpTSTypeError("Generator is already running"));
    }

    /// <summary>
    /// Advances the generator to the next yield point, resuming the suspended
    /// <c>yield</c> with <c>undefined</c> (the implicit value for for...of,
    /// yield* delegation, and a bare <c>next()</c>).
    /// </summary>
    public SharpTSIteratorResult Next() => Next(SharpTSUndefined.Instance);

    /// <summary>
    /// Advances the generator to the next yield point.
    /// </summary>
    /// <param name="sentValue">
    /// The value the resumed <c>yield</c> expression evaluates to (ECMA-262 §27.5.3.3).
    /// Ignored on the first call, which only starts the body — there is no suspended
    /// yield waiting to receive it.
    /// </param>
    public SharpTSIteratorResult Next(object? sentValue)
    {
        ThrowIfExecuting();

        // A finished or disposed generator yields nothing more. The completion value is
        // delivered exactly once (when the body finishes); later next() calls report
        // undefined (ECMA-262 §27.5.3.3 → CreateIterResultObject(undefined, true)).
        if (_closed || _state == State.Completed)
            return new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true);

        // Save caller's environment
        _callerEnv = _interpreter.Environment;

        if (_state == State.NotStarted)
        {
            _state = State.Running;
            _workerThread = new Thread(RunBody) { IsBackground = true, Name = "Generator" };
            _workerThread.Start();
        }
        else
        {
            // Resume: hand the sent value to the suspended yield, restore the
            // generator's environment, and signal the worker.
            _resumeKind = GeneratorResumeKind.Next;
            _sentValue = sentValue;
            _state = State.Running;
            _callerReady.Set();
        }

        return AwaitWorker();
    }

    /// <summary>
    /// Resumes a suspended generator with a <c>return</c> abrupt completion, running any
    /// active <c>try</c>/<c>finally</c> blocks before it settles as done
    /// (ECMA-262 §27.5.3.4). A not-yet-started generator is closed without running its
    /// body; an already-finished one simply reports the supplied value. If a <c>finally</c>
    /// block yields, the generator suspends there and this returns that yielded value.
    /// </summary>
    public SharpTSIteratorResult Return(object? value = null)
    {
        ThrowIfExecuting();

        // Never started: close without running the body — there is no finally to run.
        if (_state == State.NotStarted)
        {
            _state = State.Completed;
            return new SharpTSIteratorResult(value, done: true);
        }

        // Already finished/disposed: report { value, done: true } (no body to resume).
        if (_closed || _state == State.Completed)
            return new SharpTSIteratorResult(value, done: true);

        // Suspended: inject the return at the yield point so finally blocks run. The body's
        // completion (which a finally block may override) becomes the reported value.
        _callerEnv = _interpreter.Environment;
        _resumeKind = GeneratorResumeKind.Return;
        _injectedValue = value;
        _state = State.Running;
        _callerReady.Set();

        return AwaitWorker();
    }

    /// <summary>
    /// Resumes a suspended generator with a <c>throw</c> abrupt completion at the yield
    /// point, running active <c>catch</c>/<c>finally</c> blocks (ECMA-262 §27.5.3.4). If a
    /// <c>catch</c> handles it the generator continues (and may yield again); otherwise the
    /// error propagates to this caller. A not-yet-started or finished generator just throws.
    /// </summary>
    public SharpTSIteratorResult Throw(object? error = null)
    {
        ThrowIfExecuting();

        // Never started or already finished/disposed: nothing to resume — just throw.
        if (_state == State.NotStarted || _closed || _state == State.Completed)
        {
            _state = State.Completed;
            throw ThrowException.FromResult(error);
        }

        // Suspended: inject the throw at the yield point so catch/finally blocks run.
        _callerEnv = _interpreter.Environment;
        _resumeKind = GeneratorResumeKind.Throw;
        _injectedValue = error;
        _state = State.Running;
        _callerReady.Set();

        return AwaitWorker();
    }

    /// <summary>
    /// Blocks until the worker reaches its next yield or completes, then either rethrows a
    /// worker exception (an uncaught throw or a finally that threw) or builds the iterator
    /// result. Shared by <see cref="Next(object?)"/>, <see cref="Return"/> and <see cref="Throw"/>.
    /// </summary>
    private SharpTSIteratorResult AwaitWorker()
    {
        _workerReady.Wait();
        _workerReady.Reset();

        if (_workerException != null)
        {
            var ex = _workerException;
            _workerException = null;
            _state = State.Completed;
            ExceptionDispatchInfo.Capture(ex).Throw();
        }

        if (_state == State.Completed)
            return new SharpTSIteratorResult(_returnValue, done: true);

        return new SharpTSIteratorResult(_yieldedValue, done: false);
    }

    /// <summary>
    /// Runs the generator body on the worker thread.
    /// Uses a yield callback to suspend execution at yield points without
    /// throwing exceptions, preserving the interpreter's call stack and environment.
    /// </summary>
    private void RunBody()
    {
        RuntimeEnvironment previousEnv = _interpreter.Environment;
        _interpreter.SetEnvironment(_environment);

        // Set yield callback so yield expressions suspend the thread
        // instead of throwing YieldException
        _callerYieldCallback = _interpreter.YieldCallback;
        _interpreter.YieldCallback = HandleYieldCallback;

        try
        {
            if (_body.Count == 0)
                return;

            var result = _interpreter.ExecuteBlock(_body, _environment);
            if (result.Type == ExecutionResult.ResultType.Return)
            {
                _returnValue = result.Value.ToObject();
            }
            else if (result.Type == ExecutionResult.ResultType.Throw)
            {
                // An uncaught guest throw — from the body itself or from a throw(e) injection
                // that no catch handled — completes the generator abnormally and propagates
                // to the resuming next()/throw() caller.
                _workerException = ThrowException.FromResult(result.Value.ToObject());
            }
        }
        catch (Exception ex) when (ex is not ThreadInterruptedException)
        {
            _workerException = ex;
        }
        finally
        {
            _interpreter.YieldCallback = _callerYieldCallback;
            _interpreter.SetEnvironment(previousEnv);
            _state = State.Completed;
            _workerReady.Set();
        }
    }

    /// <summary>
    /// Yield callback invoked by the interpreter when a yield expression is evaluated.
    /// Suspends the worker thread until the next Next() call.
    /// For <c>yield*</c>, iterates the delegated iterable lazily and returns its
    /// completion value per ECMA-262 §14.4.14.
    /// </summary>
    private object? HandleYieldCallback(object? value, bool isDelegating)
    {
        if (isDelegating)
        {
            return DelegateYieldStar(_interpreter, value, v => SuspendCore(v), () => _closed);
        }
        // A plain `yield` evaluates to the value passed to the resuming next(v); an abrupt
        // resume (return/throw) is realized as the control-flow exception that unwinds the
        // outer body's own try/finally blocks (ECMA-262 §27.5.3.4).
        return SuspendCore(value).Realize();
    }

    /// <summary>
    /// Shared <c>yield*</c> delegation loop used by both generator types (ECMA-262 §14.4.14).
    /// A delegated SharpTS generator is driven via <c>next/return/throw</c> so the outer
    /// generator's resume completion is forwarded into it; other iterables are iterated lazily.
    /// </summary>
    /// <param name="suspend">
    /// Suspends the outer generator with the delegated value and returns the completion that
    /// resumes it (the spec's <c>GeneratorYield</c>). Each value the delegate yields is handed
    /// to <paramref name="suspend"/>; its returned completion drives the next loop turn —
    /// <c>next(v)</c> forwards the sent value, <c>return(v)</c>/<c>throw(e)</c> forward the
    /// abrupt completion into the delegate (running its <c>finally</c>/<c>catch</c>).
    /// </param>
    internal static object? DelegateYieldStar(Interpreter interpreter, object? iterable, Func<object?, GeneratorResume> suspend, Func<bool> isClosed)
    {
        switch (iterable)
        {
            case SharpTSGenerator gen:
                return DelegateToGenerator(gen.Next, gen.Return, gen.Throw, suspend, isClosed);
            default:
                // Custom iterator objects (those with [Symbol.iterator] and a next(v) that
                // consumes its argument) are driven manually so the outer generator's resume
                // value is forwarded as the argument to next(v) (ECMA-262 §14.4.14, #503).
                if (interpreter.TryGetCustomIteratorProtocol(iterable, out var iterObj, out var nextFn))
                    return DelegateToCustomIterator(interpreter, iterObj, nextFn!, suspend, isClosed);

                // Built-in iterables (arrays, strings, Maps, Sets) and generator-valued
                // [Symbol.iterator]() results: next() ignores the resume value, so driving
                // lazily via GetIterableElements is correct. Abrupt resumes (return/throw)
                // are realized here to unwind the outer body.
                foreach (var element in interpreter.GetIterableElements(iterable))
                {
                    if (isClosed()) return null;
                    suspend(element).Realize();
                }
                return null;
        }
    }

    /// <summary>
    /// Drives a custom (non-generator) iterator object manually, forwarding the outer generator's
    /// resume value into its <c>next(v)</c> on each turn (ECMA-262 §14.4.14, #503).
    /// Abrupt completions (<c>return</c>/<c>throw</c>) are realized as the appropriate
    /// control-flow exception and unwind through the outer body unchanged.
    /// </summary>
    private static object? DelegateToCustomIterator(
        Interpreter interpreter,
        object? iteratorObj,
        ISharpTSCallable nextFn,
        Func<object?, GeneratorResume> suspend,
        Func<bool> isClosed)
    {
        object? sentValue = SharpTSUndefined.Instance;
        while (true)
        {
            if (isClosed()) return null;

            var result = nextFn.Call(interpreter, [sentValue]);

            bool done = false;
            object? value = null;
            if (result is SharpTSObject resultObj)
            {
                done = RuntimeTypes.IsTruthy(resultObj.GetProperty("done"));
                value = resultObj.GetProperty("value");
            }
            else if (result is SharpTSInstance resultInst)
            {
                var doneTok = new Token(TokenType.IDENTIFIER, "done", null, 0);
                var valueTok = new Token(TokenType.IDENTIFIER, "value", null, 0);
                try
                {
                    done = RuntimeTypes.IsTruthy(resultInst.Get(doneTok));
                    value = resultInst.Get(valueTok);
                }
                catch
                {
                    done = RuntimeTypes.IsTruthy(resultInst.GetRawField("done"));
                    value = resultInst.GetRawField("value");
                }
            }

            if (done) return value ?? SharpTSUndefined.Instance;

            var received = suspend(value);
            switch (received.Kind)
            {
                case GeneratorResumeKind.Return:
                case GeneratorResumeKind.Throw:
                    received.Realize();
                    return null; // unreachable
                default:
                    sentValue = received.Value;
                    break;
            }
        }
    }

    /// <summary>
    /// Drives a delegated SharpTS generator, forwarding into it whatever completion the outer
    /// generator receives at each <c>yield*</c> suspension (ECMA-262 §14.4.14):
    /// <list type="bullet">
    /// <item><c>next(v)</c> → <paramref name="next"/>; when the delegate finishes, <c>yield*</c>
    /// evaluates to its return value (step a.v).</item>
    /// <item><c>return(v)</c> → <paramref name="return"/>, running the delegate's <c>finally</c>;
    /// when it finishes, the <c>yield*</c> itself completes as a <c>return</c> carrying the
    /// delegate's value, so the OUTER generator returns (step c.viii).</item>
    /// <item><c>throw(e)</c> → <paramref name="throw"/>, running the delegate's <c>catch</c>/
    /// <c>finally</c>. If the delegate has no handler, <paramref name="throw"/> rethrows and the
    /// error propagates out of <c>yield*</c> (step b.ii). If it catches and finishes,
    /// <c>yield*</c> evaluates to its value as a NORMAL completion — the outer continues past
    /// <c>yield*</c> (step b.5).</item>
    /// </list>
    /// In every case, a delegate that yields again (from its body, a <c>catch</c>, or a
    /// <c>finally</c>) re-suspends the outer; the resulting completion drives the next turn.
    /// </summary>
    private static object? DelegateToGenerator(
        Func<object?, SharpTSIteratorResult> next,
        Func<object?, SharpTSIteratorResult> @return,
        Func<object?, SharpTSIteratorResult> @throw,
        Func<object?, GeneratorResume> suspend,
        Func<bool> isClosed)
    {
        // Seed `received` with normal/undefined: the first inner next() gets undefined (a
        // delegate suspended at its start ignores the argument anyway); every later turn
        // forwards whatever completion the outer received at the previous GeneratorYield.
        var received = new GeneratorResume(GeneratorResumeKind.Next, SharpTSUndefined.Instance);
        while (true)
        {
            if (isClosed()) return null;

            SharpTSIteratorResult inner;
            switch (received.Kind)
            {
                case GeneratorResumeKind.Return:
                    inner = @return(received.Value);
                    // Delegate finished its finally → the outer generator returns this value.
                    if (inner.Done) throw new GeneratorReturnException(inner.Value);
                    break;

                case GeneratorResumeKind.Throw:
                    // @throw rethrows if the delegate has no catch — propagating out of yield*.
                    inner = @throw(received.Value);
                    // Delegate caught + finished → yield* value (normal); outer continues.
                    if (inner.Done) return inner.Value;
                    break;

                default:
                    inner = next(received.Value);
                    // Delegate finished normally → yield* evaluates to its return value.
                    if (inner.Done) return inner.Value;
                    break;
            }

            // Delegate yielded again; suspend the outer and forward the next completion.
            received = suspend(inner.Value);
        }
    }

    /// <summary>
    /// Suspends the worker thread, handing a single yielded value to the caller, and returns the
    /// completion that resumes it (a plain <c>next(v)</c>, or an abrupt <c>return(v)</c>/
    /// <c>throw(e)</c>). Callers decide how to act on the completion: a plain <c>yield</c> realizes
    /// an abrupt resume as a thrown control-flow exception (<see cref="GeneratorResume.Realize"/>),
    /// while a <c>yield*</c> delegation forwards it into the delegated iterator.
    /// </summary>
    private GeneratorResume SuspendCore(object? value)
    {
        _yieldedValue = value;
        _state = State.Suspended;

        // Save generator state, restore caller state
        var generatorEnv = _interpreter.Environment;
        _interpreter.SetEnvironment(_callerEnv!);
        _interpreter.YieldCallback = _callerYieldCallback;

        _workerReady.Set();    // Signal caller that value is ready
        _callerReady.Wait();   // Wait for the resuming next/return/throw call
        _callerReady.Reset();

        // Save caller state (may have changed), restore generator state
        _callerEnv = _interpreter.Environment;
        _callerYieldCallback = _interpreter.YieldCallback;
        _interpreter.SetEnvironment(generatorEnv);
        _interpreter.YieldCallback = HandleYieldCallback;

        // Read the resume completion requested by the resuming next(v)/return(v)/throw(e) call.
        // Consume the resume kind so a later wake that doesn't set it (e.g. Dispose) resumes
        // normally instead of re-injecting a stale abrupt completion.
        var kind = _resumeKind;
        var injected = _injectedValue;
        _resumeKind = GeneratorResumeKind.Next;
        _injectedValue = null;
        return new GeneratorResume(kind, kind == GeneratorResumeKind.Next ? _sentValue : injected);
    }

    // IEnumerable implementation for for...of integration
    public IEnumerator<object?> GetEnumerator()
    {
        while (true)
        {
            var result = Next();
            if (result.Done) yield break;
            yield return result.Value;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[object Generator]";

    public void Dispose()
    {
        _closed = true;
        if (_state == State.Suspended)
            _callerReady.Set();
        _callerReady.Dispose();
        _workerReady.Dispose();
        GC.SuppressFinalize(this);
    }
}
