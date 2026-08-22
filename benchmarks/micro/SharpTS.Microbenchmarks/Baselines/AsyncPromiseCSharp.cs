namespace SharpTS.Microbenchmarks.Baselines;

/// <summary>
/// Native and boxed managed controls for the shared async/Promise workloads.
/// Completed tasks deliberately keep I/O and timer latency out of the probes.
/// </summary>
public static class AsyncPromiseCSharp
{
    public static async Task<double> IdiomaticSequentialAwait(int n)
    {
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += await Task.FromResult((double)i);
        return sum;
    }

    public static async Task<object?> EquivalentSequentialAwait(object? n)
    {
        int count = (int)Convert.ToDouble(n);
        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += Convert.ToDouble(await Task.FromResult<object?>((double)i));
        return sum;
    }

    private static async Task<double> IdiomaticIdentity(double value)
    {
        await Task.CompletedTask;
        return value;
    }

    private static async Task<object?> EquivalentIdentity(object? value)
    {
        await Task.CompletedTask;
        return value;
    }

    public static async Task<double> IdiomaticFunctionCalls(int n)
    {
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += await IdiomaticIdentity(i);
        return sum;
    }

    public static async Task<object?> EquivalentFunctionCalls(object? n)
    {
        int count = (int)Convert.ToDouble(n);
        double sum = 0;
        for (int i = 0; i < count; i++)
            sum += Convert.ToDouble(await EquivalentIdentity((double)i));
        return sum;
    }

    public static async Task<double> IdiomaticThenChain(int n)
    {
        Task<double> chain = Task.FromResult(0d);
        for (int i = 0; i < n; i++)
        {
            int captured = i;
            chain = chain.ContinueWith(
                completed => completed.GetAwaiter().GetResult() + captured,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return await chain;
    }

    public static async Task<object?> EquivalentThenChain(object? n)
    {
        int count = (int)Convert.ToDouble(n);
        Task<object?> chain = Task.FromResult<object?>(0d);
        for (int i = 0; i < count; i++)
        {
            object? captured = (double)i;
            chain = chain.ContinueWith(
                completed => (object?)(Convert.ToDouble(completed.GetAwaiter().GetResult())
                    + Convert.ToDouble(captured)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return await chain;
    }

    public static async Task<double> IdiomaticAll(int n)
    {
        var promises = new Task<double>[n];
        for (int i = 0; i < n; i++)
            promises[i] = Task.FromResult((double)i);

        double[] values = await Task.WhenAll(promises);
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    public static async Task<object?> EquivalentAll(object? n)
    {
        int count = (int)Convert.ToDouble(n);
        var promises = new Task<object?>[count];
        for (int i = 0; i < count; i++)
            promises[i] = Task.FromResult<object?>((double)i);

        object?[] values = await Task.WhenAll(promises);
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
            sum += Convert.ToDouble(values[i]);
        return sum;
    }
}
