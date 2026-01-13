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
    public static object? GetMember(SharpTSPromise receiver, string name)
    {
        return name switch
        {
            "then" => new BuiltInAsyncMethod("then", 1, 2, (interp, recv, args) =>
                ThenImpl((SharpTSPromise)recv!, args, interp)).Bind(receiver),

            "catch" => new BuiltInAsyncMethod("catch", 1, 1, (interp, recv, args) =>
                CatchImpl((SharpTSPromise)recv!, args, interp)).Bind(receiver),

            "finally" => new BuiltInAsyncMethod("finally", 1, 1, (interp, recv, args) =>
                FinallyImpl((SharpTSPromise)recv!, args, interp)).Bind(receiver),

            _ => null
        };
    }

    /// <summary>
    /// Gets a static method from the Promise namespace.
    /// </summary>
    public static BuiltInAsyncMethod? GetStaticMethod(string name)
    {
        return name switch
        {
            "all" => new BuiltInAsyncMethod("all", 1, 1, (interp, _, args) =>
                AllImpl(args, interp)),

            "race" => new BuiltInAsyncMethod("race", 1, 1, (interp, _, args) =>
                RaceImpl(args, interp)),

            "resolve" => new BuiltInAsyncMethod("resolve", 0, 1, (_, _, args) =>
                Task.FromResult(ResolveImpl(args))),

            "reject" => new BuiltInAsyncMethod("reject", 1, 1, (_, _, args) =>
                Task.FromResult(RejectImpl(args))),

            "allSettled" => new BuiltInAsyncMethod("allSettled", 1, 1, (interp, _, args) =>
                AllSettledImpl(args, interp)),

            "any" => new BuiltInAsyncMethod("any", 1, 1, (interp, _, args) =>
                AnyImpl(args, interp)),

            _ => null
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

        try
        {
            // Wait for the promise to settle
            var value = await promise.GetValueAsync();

            // If fulfilled, call onFulfilled callback
            if (onFulfilled != null)
            {
                var result = CallCallback(onFulfilled, [value], interpreter);
                return await UnwrapResult(result);
            }

            // No onFulfilled callback - pass through value
            return value;
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            // If rejected, call onRejected callback
            if (onRejected != null)
            {
                var result = CallCallback(onRejected, [ex.Reason], interpreter);
                return await UnwrapResult(result);
            }

            // No onRejected callback - re-throw to propagate rejection
            throw;
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            // Handle wrapped rejection exception
            if (onRejected != null)
            {
                var result = CallCallback(onRejected, [rejEx.Reason], interpreter);
                return await UnwrapResult(result);
            }
            throw rejEx;
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

        try
        {
            // Wait for the promise to settle
            return await promise.GetValueAsync();
        }
        catch (SharpTSPromiseRejectedException ex)
        {
            if (onRejected != null)
            {
                var result = CallCallback(onRejected, [ex.Reason], interpreter);
                return await UnwrapResult(result);
            }
            throw;
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            if (onRejected != null)
            {
                var result = CallCallback(onRejected, [rejEx.Reason], interpreter);
                return await UnwrapResult(result);
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
    /// Implementation of Promise.all(iterable)
    /// Resolves when all promises resolve, rejects on first rejection.
    /// </summary>
    private static async Task<object?> AllImpl(List<object?> args, Interpreter interpreter)
    {
        if (args.Count == 0 || args[0] is not SharpTSArray array)
        {
            throw new Exception("Runtime Error: Promise.all requires an array argument.");
        }

        // Empty array resolves immediately to empty array
        if (array.Elements.Count == 0)
        {
            return new SharpTSArray([]);
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in array.Elements)
        {
            if (element is SharpTSPromise promise)
            {
                tasks.Add(promise.GetValueAsync());
            }
            else
            {
                // Non-promise values are treated as immediately resolved
                tasks.Add(Task.FromResult(element));
            }
        }

        // Wait for all promises - will throw on first rejection
        var results = await Task.WhenAll(tasks);
        return new SharpTSArray(new List<object?>(results));
    }

    /// <summary>
    /// Implementation of Promise.race(iterable)
    /// Resolves/rejects with the first promise to settle.
    /// </summary>
    private static async Task<object?> RaceImpl(List<object?> args, Interpreter interpreter)
    {
        if (args.Count == 0 || args[0] is not SharpTSArray array)
        {
            throw new Exception("Runtime Error: Promise.race requires an array argument.");
        }

        // Empty array never settles - return a promise that never resolves
        if (array.Elements.Count == 0)
        {
            // Create a TaskCompletionSource that never completes
            var neverCompletingTcs = new TaskCompletionSource<object?>();
            return new SharpTSPromise(neverCompletingTcs.Task);
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in array.Elements)
        {
            if (element is SharpTSPromise promise)
            {
                tasks.Add(promise.GetValueAsync());
            }
            else
            {
                // Non-promise values are treated as immediately resolved
                tasks.Add(Task.FromResult(element));
            }
        }

        // Return the result of the first task to complete
        var completedTask = await Task.WhenAny(tasks);
        return await completedTask;
    }

    /// <summary>
    /// Implementation of Promise.allSettled(iterable)
    /// Returns array of outcome objects: {status: "fulfilled"|"rejected", value?: T, reason?: E}
    /// Never rejects - always resolves with all outcomes.
    /// </summary>
    private static async Task<object?> AllSettledImpl(List<object?> args, Interpreter interpreter)
    {
        if (args.Count == 0 || args[0] is not SharpTSArray array)
        {
            throw new Exception("Runtime Error: Promise.allSettled requires an array argument.");
        }

        // Empty array resolves immediately to empty array
        if (array.Elements.Count == 0)
        {
            return new SharpTSArray([]);
        }

        List<object?> results = [];

        foreach (var element in array.Elements)
        {
            try
            {
                object? value;
                if (element is SharpTSPromise promise)
                {
                    value = await promise.GetValueAsync();
                }
                else
                {
                    // Non-promise values are treated as immediately resolved
                    value = element;
                }

                // Create fulfilled outcome object
                var outcome = new SharpTSObject(new Dictionary<string, object?>
                {
                    ["status"] = "fulfilled",
                    ["value"] = value
                });
                results.Add(outcome);
            }
            catch (SharpTSPromiseRejectedException ex)
            {
                // Create rejected outcome object
                var outcome = new SharpTSObject(new Dictionary<string, object?>
                {
                    ["status"] = "rejected",
                    ["reason"] = ex.Reason
                });
                results.Add(outcome);
            }
            catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
            {
                // Handle wrapped rejection exception
                var outcome = new SharpTSObject(new Dictionary<string, object?>
                {
                    ["status"] = "rejected",
                    ["reason"] = rejEx.Reason
                });
                results.Add(outcome);
            }
            catch (Exception ex)
            {
                // Handle other exceptions as rejections
                var outcome = new SharpTSObject(new Dictionary<string, object?>
                {
                    ["status"] = "rejected",
                    ["reason"] = ex.Message
                });
                results.Add(outcome);
            }
        }

        return new SharpTSArray(results);
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
    private static async Task<object?> AnyImpl(List<object?> args, Interpreter interpreter)
    {
        if (args.Count == 0 || args[0] is not SharpTSArray array)
        {
            throw new Exception("Runtime Error: Promise.any requires an array argument.");
        }

        // Empty array rejects immediately with AggregateError
        if (array.Elements.Count == 0)
        {
            var aggregateError = new SharpTSObject(new Dictionary<string, object?>
            {
                ["name"] = "AggregateError",
                ["message"] = "All promises were rejected",
                ["errors"] = new SharpTSArray([])
            });
            throw new SharpTSPromiseRejectedException(aggregateError);
        }

        var state = new AnyState { PendingCount = array.Elements.Count };

        foreach (var element in array.Elements)
        {
            if (element is SharpTSPromise promise)
            {
                _ = ProcessPromiseForAny(promise.GetValueAsync(), state);
            }
            else
            {
                // Non-promise values are treated as immediately resolved - first one wins
                state.Tcs.TrySetResult(element);
            }
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
        catch (SharpTSPromiseRejectedException ex)
        {
            HandleRejectionForAny(ex.Reason, state);
        }
        catch (AggregateException aggEx) when (aggEx.InnerException is SharpTSPromiseRejectedException rejEx)
        {
            HandleRejectionForAny(rejEx.Reason, state);
        }
        catch (Exception ex)
        {
            HandleRejectionForAny(ex.Message, state);
        }
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

            // If all promises rejected, reject with AggregateError
            if (state.PendingCount == 0)
            {
                // Create an AggregateError-like object with errors array
                var aggregateError = new SharpTSObject(new Dictionary<string, object?>
                {
                    ["name"] = "AggregateError",
                    ["message"] = "All promises were rejected",
                    ["errors"] = new SharpTSArray(state.RejectionReasons)
                });

                state.Tcs.TrySetException(new SharpTSPromiseRejectedException(aggregateError));
            }
        }
    }

    /// <summary>
    /// Implementation of Promise.resolve(value?)
    /// Returns the value directly - BuiltInAsyncMethod.Call wraps in a Promise.
    /// </summary>
    private static object? ResolveImpl(List<object?> args)
    {
        var value = args.Count > 0 ? args[0] : null;

        // If already a Promise, unwrap it so we don't double-wrap
        // (BuiltInAsyncMethod.Call will wrap the result in a new Promise)
        if (value is SharpTSPromise promise)
        {
            // Return the promise's underlying task result to avoid double-wrapping
            // The caller (BuiltInAsyncMethod.Call) will wrap this in a new Promise
            return promise.Task.GetAwaiter().GetResult();
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
        var reason = args.Count > 0 ? args[0] : null;
        // Throw to let BuiltInAsyncMethod.Call create the rejected Promise
        throw new SharpTSPromiseRejectedException(reason);
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
    private static async Task<object?> UnwrapResult(object? result)
    {
        // Flatten nested Promises
        while (result is SharpTSPromise promise)
        {
            result = await promise.GetValueAsync();
        }
        return result;
    }

    #endregion
}
