using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Runtime support methods for compiled async generators.
/// </summary>
public static partial class RuntimeTypes
{
    /// <summary>
    /// Helper method to await a Task and then continue with MoveNextAsync on an async generator.
    /// This is called when an await inside an async generator is not immediately complete.
    /// </summary>
    /// <param name="task">The task being awaited</param>
    /// <param name="generator">The async enumerator (generator state machine)</param>
    /// <returns>A ValueTask that completes when the generator has processed the await and produced its next result</returns>
    public static ValueTask<bool> AsyncGeneratorAwaitContinue(Task<object> task, IAsyncEnumerator<object> generator)
    {
        // Create a continuation that calls MoveNextAsync after the awaited task completes
        var continuation = task.ContinueWith(
            (_, state) => ((IAsyncEnumerator<object>)state!).MoveNextAsync(),
            generator,
            TaskContinuationOptions.ExecuteSynchronously
        );

        // Unwrap the Task<ValueTask<bool>> to get Task<bool>, then wrap in ValueTask
        return new ValueTask<bool>(UnwrapValueTask(continuation));
    }

    /// <summary>
    /// Unwraps Task&lt;ValueTask&lt;bool&gt;&gt; to Task&lt;bool&gt;.
    /// </summary>
    private static async Task<bool> UnwrapValueTask(Task<ValueTask<bool>> task)
    {
        var valueTask = await task.ConfigureAwait(false);
        return await valueTask.ConfigureAwait(false);
    }
}
