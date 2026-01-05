namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript Promise.
/// </summary>
/// <remarks>
/// Wraps a .NET Task&lt;object?&gt; to provide Promise semantics in the interpreter.
/// Supports automatic Promise flattening - Promise&lt;Promise&lt;T&gt;&gt; becomes Promise&lt;T&gt;.
/// </remarks>
public class SharpTSPromise
{
    private readonly Task<object?> _task;

    /// <summary>
    /// Creates a Promise wrapping an existing Task.
    /// </summary>
    public SharpTSPromise(Task<object?> task)
    {
        _task = task ?? throw new ArgumentNullException(nameof(task));
    }

    /// <summary>
    /// Creates a Promise from a synchronous value (immediately resolved).
    /// </summary>
    public static SharpTSPromise Resolve(object? value)
    {
        // Flatten nested Promises
        if (value is SharpTSPromise nestedPromise)
        {
            return nestedPromise;
        }
        return new SharpTSPromise(System.Threading.Tasks.Task.FromResult(value));
    }

    /// <summary>
    /// Creates a rejected Promise with the given reason.
    /// </summary>
    public static SharpTSPromise Reject(object? reason)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new SharpTSPromiseRejectedException(reason));
        return new SharpTSPromise(tcs.Task);
    }

    /// <summary>
    /// Gets the underlying Task.
    /// </summary>
    public Task<object?> Task => _task;

    /// <summary>
    /// Gets the resolved value, flattening any nested Promises.
    /// Used by the await expression to unwrap Promise chains.
    /// </summary>
    public async Task<object?> GetValueAsync()
    {
        object? result = await _task;

        // Flatten nested Promises
        while (result is SharpTSPromise inner)
        {
            result = await inner._task;
        }

        return result;
    }

    /// <summary>
    /// Checks if the Promise is completed (resolved or rejected).
    /// </summary>
    public bool IsCompleted => _task.IsCompleted;

    /// <summary>
    /// Checks if the Promise was rejected.
    /// </summary>
    public bool IsFaulted => _task.IsFaulted;

    /// <summary>
    /// Checks if the Promise was successfully resolved.
    /// </summary>
    public bool IsResolved => _task.IsCompletedSuccessfully;

    public override string ToString() => _task.Status switch
    {
        TaskStatus.RanToCompletion => $"Promise {{ <resolved>: {_task.Result} }}",
        TaskStatus.Faulted => "Promise { <rejected> }",
        _ => "Promise { <pending> }"
    };
}

/// <summary>
/// Exception type for rejected Promises, carrying the rejection reason.
/// </summary>
public class SharpTSPromiseRejectedException : Exception
{
    public object? Reason { get; }

    public SharpTSPromiseRejectedException(object? reason)
        : base(reason?.ToString() ?? "Promise rejected")
    {
        Reason = reason;
    }
}
