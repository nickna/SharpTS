using System.Diagnostics;
using System.Text;

namespace SharpTS.Testing;

/// <summary>Owns one test process, its output pipes, and bounded cleanup.</summary>
internal sealed class TestProcess : IAsyncDisposable
{
    private readonly TimeSpan _cleanupTimeout;
    private readonly Process _process;
    private readonly string _description;
    private readonly TimeSpan _timeout;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _reads = new();
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly StringBuilder _timeline = new();
    private readonly Task _output;
    private readonly Task _error;
    private Task<string>? _cleanup;

    public TestProcess(ProcessStartInfo start, TimeSpan timeout, string? description = null, TimeSpan? cleanupTimeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _timeout = timeout;
        _cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(5);
        ArgumentOutOfRangeException.ThrowIfLessThan(_cleanupTimeout, TimeSpan.Zero);
        _description = description ?? $"{start.FileName} {start.Arguments} {string.Join(" ", start.ArgumentList)}";
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        _process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {_description}.");
        _output = CaptureAsync(_process.StandardOutput, _stdout, "stdout");
        _error = CaptureAsync(_process.StandardError, _stderr, "stderr");
    }

    public string StandardOutput => Snapshot(_stdout);
    public string StandardError => Snapshot(_stderr);
    public string Timeline => Snapshot(_timeline);
    public bool HasExited => _process.HasExited;

    public static TestProcessResult Run(ProcessStartInfo start, TimeSpan timeout, string? description = null)
        => RunAsync(start, timeout, description).GetAwaiter().GetResult();

    public static async Task<TestProcessResult> RunAsync(ProcessStartInfo start, TimeSpan timeout, string? description = null)
    {
        await using var process = new TestProcess(start, timeout, description);
        return await process.WaitForExitAsync().ConfigureAwait(false);
    }

    // Readiness and exit share the same execution deadline. Both pipes are drained
    // throughout the interaction, including while the caller changes fixture files.
    public async Task WaitForOutputAsync(string text)
    {
        while (!StandardOutput.Contains(text, StringComparison.Ordinal))
        {
            if (_clock.Elapsed >= _timeout)
                throw await CreateTimeoutAsync($"waiting for '{text}'").ConfigureAwait(false);
            if (_output.IsCompleted)
            {
                // The owned drain is already complete; no foreign context can block it.
#pragma warning disable VSTHRD003
                await _output.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                if (StandardOutput.Contains(text, StringComparison.Ordinal)) return;
                throw new InvalidOperationException($"{_description} closed stdout before '{text}'. {Diagnostics()}");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
        }
    }

    public async Task<TestProcessResult> WaitForExitAsync()
    {
        TimeSpan remaining = _timeout - _clock.Elapsed;
        try
        {
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            // Output EOF has its own cleanup budget: descendants can inherit pipes
            // even when the process whose handle we own has already exited.
            await _process.WaitForExitAsync().WaitAsync(remaining).ConfigureAwait(false);
            await Task.WhenAll(_output, _error).WaitAsync(_cleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw await CreateTimeoutAsync("waiting for exit/output").ConfigureAwait(false);
        }
        return new(_process.ExitCode, StandardOutput, StandardError, Timeline);
    }

    private async Task CaptureAsync(StreamReader reader, StringBuilder destination, string stream)
    {
        var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), _reads.Token).ConfigureAwait(false);
            if (count == 0) return;
            lock (destination) destination.Append(buffer, 0, count);
            lock (_timeline) _timeline.AppendLine($"[{_clock.Elapsed.TotalSeconds:F3}s {stream}] {new string(buffer, 0, count).TrimEnd()}");
        }
    }

    private async Task<TimeoutException> CreateTimeoutAsync(string stage)
    {
        string cleanup = await CleanupAsync().ConfigureAwait(false);
        return new TimeoutException($"{_description} exceeded {_timeout.TotalSeconds:F0} seconds. {stage}; " +
            $"{Diagnostics()}; {cleanup}; timeline: {Tail(Timeline)}");
    }

    private Task<string> CleanupAsync() => _cleanup ??= CleanupCoreAsync();

    private async Task<string> CleanupCoreAsync()
    {
        var notes = new List<string>();
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { notes.Add($"termination failed: {exception.Message}"); }

        Task exit;
        try { exit = _process.WaitForExitAsync(); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            notes.Add($"exit observation failed: {exception.Message}");
            exit = Task.CompletedTask;
        }
        Task observation = Task.WhenAll(exit, _output, _error);
        try { await observation.WaitAsync(_cleanupTimeout).ConfigureAwait(false); }
        catch (Exception exception)
        { notes.Add($"cleanup observation: {exception.GetType().Name}: {exception.Message}"); }
        finally
        {
            // Cancel pipe reads after their budget, rather than leave background
            // reads waiting forever on a descendant's inherited handles.
            await _reads.CancelAsync().ConfigureAwait(false);
            _ = observation.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return notes.Count == 0 ? "cleanup completed" : string.Join("; ", notes);
    }

    private string Diagnostics() => $"PID {_process.Id}; " +
        (_process.HasExited ? $"exited with code {_process.ExitCode}" : "still running") +
        $"; stdout: {Tail(StandardOutput)}; stderr: {Tail(StandardError)}";

    private static string Snapshot(StringBuilder buffer) { lock (buffer) return buffer.ToString(); }
    private static string Tail(string value) => string.IsNullOrWhiteSpace(value) ? "<empty>"
        : value.Length <= 4000 ? value.Trim() : "<truncated> " + value[^4000..].Trim();

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        _process.Dispose();
        _reads.Dispose();
    }
}

internal sealed record TestProcessResult(int ExitCode, string StandardOutput, string StandardError, string Timeline);
