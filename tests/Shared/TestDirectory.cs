using System.Diagnostics;

namespace SharpTS.Testing;

internal static class TestDirectory
{
    /// <summary>Cleanup must never replace the test's primary exception.</summary>
    public static async Task<bool> TryDeleteAsync(string path, Action<string> report, TimeSpan? timeout = null)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan budget = timeout ?? TimeSpan.FromSeconds(2);
        while (true)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (clock.Elapsed >= budget)
                {
                    report($"Cleanup retained '{path}' after {clock.Elapsed.TotalSeconds:F2}s: {exception.Message}");
                    return false;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }
    }
}
