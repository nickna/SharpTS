using System.ComponentModel;
using System.Text.Json;

namespace SharpTS.Cli;

/// <summary>Optional cache work cannot prevent strict native publication.</summary>
internal sealed class NativePublishCachePreparation(string key, bool canReuse, FileStream? cacheLock) : IDisposable
{
    internal string Key { get; } = key;
    internal bool CanReuse { get; } = canReuse;

    public void Dispose() => cacheLock?.Dispose();

    internal static async Task<NativePublishCachePreparation> PrepareAsync(
        Func<CancellationToken, Task<(string Fingerprint, bool CanReuse)>> fingerprint,
        Func<string, string> createKey, string cacheRoot, TextWriter output, bool quiet,
        CancellationToken callerToken, TimeSpan? budget = null)
    {
        callerToken.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cancellation.CancelAfter(budget ?? TimeSpan.FromSeconds(30));
        var token = cancellation.Token;
        // Enumeration/open can block synchronously. The task owns only read-only
        // identity work and a prospective lock; it never reads or writes cache entries.
        var preparation = Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (!quiet) output.WriteLine("Identifying native toolchain inputs for the optional cache...");
            var identity = await fingerprint(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!identity.CanReuse) return new NativePublishCachePreparation("", false, null);
            string preparedKey = createKey(identity.Fingerprint);
            token.ThrowIfCancellationRequested();
            if (!quiet) output.WriteLine("Acquiring the matching native cache lock...");
            var acquired = await NativePublishCache.LockAsync(cacheRoot, preparedKey, token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                return new NativePublishCachePreparation(preparedKey, true, acquired);
            }
            catch { acquired.Dispose(); throw; }
        }, CancellationToken.None);
        try { return await preparation.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            await FinishCancelledPreparation(preparation).ConfigureAwait(false);
            // Caller cancellation always wins over the optional-cache fallback.
            callerToken.ThrowIfCancellationRequested();
            if (!quiet) output.WriteLine("Native cache preparation timed out; publishing without artifact reuse.");
            return new("", false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or Win32Exception or TimeoutException)
        {
            callerToken.ThrowIfCancellationRequested();
            if (!quiet) output.WriteLine("Native cache inputs are unavailable; publishing without artifact reuse.");
            return new("", false, null);
        }
    }

    private static async Task FinishCancelledPreparation(Task<NativePublishCachePreparation> preparation)
    {
        try { (await preparation.WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false)).Dispose(); }
        catch
        {
            // A blocked filesystem operation may finish after publication resumes.
            // Observe its exception or dispose its late lock; its key is never admitted.
            _ = preparation.ContinueWith(static completed =>
            {
                if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
                else _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
