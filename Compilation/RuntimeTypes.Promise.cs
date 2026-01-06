namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Promise Methods

    /// <summary>
    /// Promise.resolve(value?) - creates a resolved promise or returns existing promise.
    /// </summary>
    public static Task<object?> PromiseResolve(object? value)
    {
        // If already a Task, return as-is (no double wrapping)
        if (value is Task<object?> existingTask)
        {
            return existingTask;
        }
        return Task.FromResult(value);
    }

    /// <summary>
    /// Promise.reject(reason) - creates a rejected promise.
    /// </summary>
    public static Task<object?> PromiseReject(object? reason)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new Exception(Stringify(reason)));
        return tcs.Task;
    }

    /// <summary>
    /// Promise.all(iterable) - waits for all promises to resolve.
    /// </summary>
    public static async Task<object?> PromiseAll(object? iterable)
    {
        if (iterable is not List<object?> list)
        {
            throw new Exception("Runtime Error: Promise.all requires an array argument.");
        }

        // Empty array resolves immediately to empty array
        if (list.Count == 0)
        {
            return new List<object?>();
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in list)
        {
            if (element is Task<object?> task)
            {
                tasks.Add(task);
            }
            else if (element is Task nonGenericTask)
            {
                // Convert non-generic Task to Task<object?>
                tasks.Add(nonGenericTask.ContinueWith(_ => (object?)null));
            }
            else
            {
                // Non-task values are treated as immediately resolved
                tasks.Add(Task.FromResult(element));
            }
        }

        // Wait for all tasks
        var results = await Task.WhenAll(tasks);
        return new List<object?>(results);
    }

    /// <summary>
    /// Promise.race(iterable) - returns the first promise to settle.
    /// </summary>
    public static async Task<object?> PromiseRace(object? iterable)
    {
        if (iterable is not List<object?> list)
        {
            throw new Exception("Runtime Error: Promise.race requires an array argument.");
        }

        // Empty array never settles - we can't really model this in sync .NET
        // So we'll just return null for empty arrays
        if (list.Count == 0)
        {
            return null;
        }

        var tasks = new List<Task<object?>>();

        foreach (var element in list)
        {
            if (element is Task<object?> task)
            {
                tasks.Add(task);
            }
            else if (element is Task nonGenericTask)
            {
                tasks.Add(nonGenericTask.ContinueWith(_ => (object?)null));
            }
            else
            {
                // Non-task values are treated as immediately resolved
                tasks.Add(Task.FromResult(element));
            }
        }

        // Return the result of the first task to complete
        var completedTask = await Task.WhenAny(tasks);
        return await completedTask;
    }

    /// <summary>
    /// Promise.prototype.then(onFulfilled?, onRejected?) - adds callbacks to a promise.
    /// </summary>
    public static async Task<object?> PromiseThen(Task<object?> promise, object? onFulfilled, object? onRejected)
    {
        try
        {
            var value = await promise;

            if (TryInvokeFunction(onFulfilled, value, out var result))
            {
                // Flatten nested tasks/promises
                while (result is Task<object?> innerTask)
                {
                    result = await innerTask;
                }
                return result;
            }

            return value;
        }
        catch (Exception ex)
        {
            if (TryInvokeFunction(onRejected, ex.Message, out var result))
            {
                while (result is Task<object?> innerTask)
                {
                    result = await innerTask;
                }
                return result;
            }
            throw;
        }
    }

    /// <summary>
    /// Invokes a function-like object (TSFunction or emitted $TSFunction).
    /// Uses duck typing to support both the SharpTS TSFunction class and the emitted type.
    /// </summary>
    private static bool TryInvokeFunction(object? func, object? arg, out object? result)
    {
        result = null;

        if (func == null)
            return false;

        // Check for SharpTS.Compilation.TSFunction
        if (func is TSFunction tsFunc)
        {
            result = tsFunc.Invoke(arg);
            return true;
        }

        // Check for emitted $TSFunction or any type with an Invoke method
        var invokeMethod = func.GetType().GetMethod("Invoke");
        if (invokeMethod != null)
        {
            var parameters = invokeMethod.GetParameters();
            // Handle params array signature: Invoke(params object?[] args)
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(object[]))
            {
                result = invokeMethod.Invoke(func, [new object?[] { arg }]);
            }
            else
            {
                result = invokeMethod.Invoke(func, [arg]);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Invokes a function-like object with no arguments (for finally callbacks).
    /// </summary>
    private static bool TryInvokeFunctionNoArgs(object? func, out object? result)
    {
        result = null;

        if (func == null)
            return false;

        // Check for SharpTS.Compilation.TSFunction
        if (func is TSFunction tsFunc)
        {
            result = tsFunc.Invoke();
            return true;
        }

        // Check for emitted $TSFunction or any type with an Invoke method
        var invokeMethod = func.GetType().GetMethod("Invoke");
        if (invokeMethod != null)
        {
            try
            {
                var parameters = invokeMethod.GetParameters();
                // The emitted $TSFunction.Invoke has signature: Invoke(params object?[] args)
                // We need to pass an empty array
                result = invokeMethod.Invoke(func, [Array.Empty<object?>()]);
                return true;
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                // Re-throw the inner exception for cleaner stack traces
                if (ex.InnerException != null)
                    throw ex.InnerException;
                throw;
            }
        }

        return false;
    }

    /// <summary>
    /// Promise.prototype.catch(onRejected) - adds rejection handler to a promise.
    /// </summary>
    public static async Task<object?> PromiseCatch(Task<object?> promise, object? onRejected)
    {
        try
        {
            return await promise;
        }
        catch (Exception ex)
        {
            if (TryInvokeFunction(onRejected, ex.Message, out var result))
            {
                while (result is Task<object?> innerTask)
                {
                    result = await innerTask;
                }
                return result;
            }
            throw;
        }
    }

    /// <summary>
    /// Promise.prototype.finally(onFinally) - adds cleanup handler to a promise.
    /// </summary>
    public static async Task<object?> PromiseFinally(Task<object?> promise, object? onFinally)
    {
        object? value = null;
        Exception? error = null;

        try
        {
            value = await promise;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Call the finally callback (with no arguments)
        if (TryInvokeFunctionNoArgs(onFinally, out var result))
        {
            try
            {
                // If callback returns a Task, wait for it
                if (result is Task<object?> resultTask)
                {
                    await resultTask;
                }
            }
            catch (Exception callbackError)
            {
                throw new Exception(callbackError.Message);
            }
        }

        // Re-throw original error or return original value
        if (error != null)
        {
            throw error;
        }

        return value;
    }

    /// <summary>
    /// Promise.allSettled(iterable) - returns array of outcome objects.
    /// Never rejects - always resolves with all outcomes.
    /// </summary>
    public static async Task<object?> PromiseAllSettled(object? iterable)
    {
        if (iterable is not List<object?> list)
        {
            throw new Exception("Runtime Error: Promise.allSettled requires an array argument.");
        }

        // Empty array resolves immediately to empty array
        if (list.Count == 0)
        {
            return new List<object?>();
        }

        var results = new List<object?>();

        foreach (var element in list)
        {
            try
            {
                object? value;
                if (element is Task<object?> task)
                {
                    value = await task;
                }
                else if (element is Task nonGenericTask)
                {
                    await nonGenericTask;
                    value = null;
                }
                else
                {
                    // Non-task values are treated as immediately resolved
                    value = element;
                }

                // Create fulfilled outcome object (using Dictionary for compiled code)
                var outcome = new Dictionary<string, object?>
                {
                    ["status"] = "fulfilled",
                    ["value"] = value
                };
                results.Add(outcome);
            }
            catch (Exception ex)
            {
                // Create rejected outcome object
                var outcome = new Dictionary<string, object?>
                {
                    ["status"] = "rejected",
                    ["reason"] = ex.Message
                };
                results.Add(outcome);
            }
        }

        return results;
    }

    /// <summary>
    /// State holder for PromiseAny operation (used instead of ref since async methods can't have ref params)
    /// </summary>
    private class AnyStateInternal
    {
        public int PendingCount;
        public readonly List<object?> RejectionReasons = [];
        public readonly TaskCompletionSource<object?> Tcs = new();
        public readonly object Lock = new();
    }

    /// <summary>
    /// Promise.any(iterable) - first fulfilled promise wins.
    /// If all reject, throws AggregateError.
    /// </summary>
    public static async Task<object?> PromiseAny(object? iterable)
    {
        if (iterable is not List<object?> list)
        {
            throw new Exception("Runtime Error: Promise.any requires an array argument.");
        }

        // Empty array rejects immediately with AggregateError
        if (list.Count == 0)
        {
            throw new AggregateException("All promises were rejected",
                new Exception("Runtime Error: Promise.any received an empty array"));
        }

        var state = new AnyStateInternal { PendingCount = list.Count };

        foreach (var element in list)
        {
            if (element is Task<object?> task)
            {
                _ = ProcessPromiseForAnyInternal(task, state);
            }
            else if (element is Task nonGenericTask)
            {
                var wrappedTask = nonGenericTask.ContinueWith(_ => (object?)null);
                _ = ProcessPromiseForAnyInternal(wrappedTask, state);
            }
            else
            {
                // Non-task values are treated as immediately resolved - first one wins
                state.Tcs.TrySetResult(element);
            }
        }

        return await state.Tcs.Task;
    }

    /// <summary>
    /// Helper for PromiseAny - processes a single promise.
    /// </summary>
    private static async Task ProcessPromiseForAnyInternal(Task<object?> task, AnyStateInternal state)
    {
        try
        {
            var result = await task;
            // First fulfillment wins
            state.Tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            lock (state.Lock)
            {
                state.RejectionReasons.Add(ex.Message);
                state.PendingCount--;

                // If all promises rejected, reject with AggregateError
                if (state.PendingCount == 0)
                {
                    state.Tcs.TrySetException(new Exception("AggregateError: All promises were rejected"));
                }
            }
        }
    }

    #endregion
}
