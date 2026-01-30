using System.Diagnostics;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript Promise.
/// </summary>
/// <remarks>
/// Wraps a .NET Task&lt;object?&gt; to provide Promise semantics in the interpreter.
/// Supports automatic Promise flattening - Promise&lt;Promise&lt;T&gt;&gt; becomes Promise&lt;T&gt;.
///
/// IMPORTANT: Callers should NOT double-wrap Promises. If you're creating a Promise
/// from a value that might already be a Promise, use SharpTSPromise.Resolve() which
/// handles flattening. Direct constructor calls with Task&lt;SharpTSPromise&gt; will
/// trigger a debug assertion.
/// </remarks>
public class SharpTSPromise : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Promise;

    private readonly Task<object?> _task;

    /// <summary>
    /// Creates a Promise wrapping an existing Task.
    /// </summary>
    /// <remarks>
    /// The task's result should NOT be a SharpTSPromise - this would create
    /// double-wrapping which causes infinite loops in async iterators.
    /// Use SharpTSPromise.Resolve() for values that might already be Promises.
    /// </remarks>
    public SharpTSPromise(Task<object?> task)
    {
        _task = task ?? throw new ArgumentNullException(nameof(task));

        // Debug assertion to catch double-wrapping bugs early.
        // If this fires, the caller is wrapping a Promise in another Promise,
        // which will cause infinite loops when awaited (the inner Promise
        // is returned instead of the actual value).
        Debug.Assert(
            !task.IsCompletedSuccessfully || task.Result is not SharpTSPromise,
            "Double-wrapped Promise detected! The task's result is already a SharpTSPromise. " +
            "Use SharpTSPromise.Resolve() instead of the constructor to handle Promise flattening.");
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
    /// Unwraps a value if it's a Promise, otherwise returns it unchanged.
    /// Used to reduce code duplication in async function return handling.
    /// </summary>
    /// <param name="value">The value that might be a Promise</param>
    /// <returns>The unwrapped value if it was a Promise, or the original value</returns>
    public static async Task<object?> UnwrapIfPromise(object? value)
    {
        if (value is SharpTSPromise promise)
        {
            return await promise.GetValueAsync();
        }
        return value;
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

    /// <summary>
    /// Stack trace captured at the point of rejection.
    /// If the reason is a SharpTSError, uses its stack; otherwise captures .NET stack.
    /// </summary>
    public string? RejectionStack { get; }

    public SharpTSPromiseRejectedException(object? reason)
        : base(FormatMessage(reason))
    {
        Reason = reason;
        RejectionStack = CaptureStack(reason);
    }

    private static string FormatMessage(object? reason)
    {
        if (reason is SharpTSError error)
            return $"{error.Name}: {error.Message}";
        return reason?.ToString() ?? "Promise rejected";
    }

    private static string? CaptureStack(object? reason)
    {
        // If reason is already a SharpTSError, use its stack
        if (reason is SharpTSError error)
            return error.Stack;

        // Otherwise capture .NET stack at rejection point
        var stackTrace = new System.Diagnostics.StackTrace(skipFrames: 2, fNeedFileInfo: true);
        var frames = stackTrace.GetFrames();
        if (frames == null || frames.Length == 0) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method == null) continue;
            var typeName = method.DeclaringType?.Name ?? "";
            var methodName = method.Name;
            var fileName = frame.GetFileName();
            var line = frame.GetFileLineNumber();

            sb.Append($"    at {typeName}.{methodName}");
            if (!string.IsNullOrEmpty(fileName))
                sb.Append($" ({fileName}:{line})");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
