using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Async iterable iterator returned by timers/promises setInterval().
/// Yields the resolved value on each interval tick until cancelled via break or AbortSignal.
/// Implements the async iterable protocol: Symbol.asyncIterator returns self,
/// next() returns Promise&lt;{value, done}&gt;, return() cleans up.
/// </summary>
public class SharpTSAsyncIntervalIterator : SharpTSObject
{
    public override TypeCategory RuntimeCategory => TypeCategory.AsyncGenerator;

    private readonly int _delayMs;
    private readonly object? _value;
    private readonly CancellationTokenSource _cts;
    private bool _done;

    public SharpTSAsyncIntervalIterator(int delayMs, object? value, CancellationToken? externalToken = null)
        : base(new Dictionary<string, object?>())
    {
        _delayMs = delayMs;
        _value = value;
        _cts = new CancellationTokenSource();
        _done = false;

        // Link external abort signal if provided
        if (externalToken.HasValue)
        {
            externalToken.Value.Register(() =>
            {
                _done = true;
                try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            });

            // If already aborted, mark done immediately
            if (externalToken.Value.IsCancellationRequested)
                _done = true;
        }

        // Set up Symbol.asyncIterator to return self
        SetBySymbol(SharpTSSymbol.AsyncIterator,
            new BuiltIns.BuiltInMethod("[Symbol.asyncIterator]", 0, (Interp _, object? _, List<object?> _) => this));

        // Set up next() method
        SetProperty("next", new BuiltIns.BuiltInAsyncMethod("next", 0, 0, NextAsync));

        // Set up return() method for break cleanup
        SetProperty("return", new BuiltIns.BuiltInAsyncMethod("return", 0, 1, ReturnAsync));
    }

    private async Task<object?> NextAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_done || _cts.IsCancellationRequested)
            return new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true);

        // Ref while the delay is pending: a bare Task.Delay holds no handle,
        // and an unseen pending tick lets the event loop's quiescence check
        // conclude the program is done mid-iteration.
        interpreter.Ref();
        try
        {
            await Task.Delay(_delayMs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _done = true;
            return new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true);
        }
        finally
        {
            interpreter.Unref();
        }

        if (_done || _cts.IsCancellationRequested)
            return new SharpTSIteratorResult(SharpTSUndefined.Instance, done: true);

        return new SharpTSIteratorResult(_value, done: false);
    }

    private Task<object?> ReturnAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        _done = true;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        object? returnValue = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        return Task.FromResult<object?>(new SharpTSIteratorResult(returnValue, done: true));
    }

    public override string ToString() => "[object AsyncIterator]";
}
