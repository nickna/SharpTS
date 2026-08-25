using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpTS.Runtime;

/// <summary>
/// Best-effort, cross-platform termination for a process and every descendant it owns.
/// The caller remains responsible for disposing the <see cref="Process"/> instance.
/// </summary>
internal static class ProcessTreeTermination
{
    internal static bool TryKill(Process? process)
    {
        if (process == null)
            return false;

        try
        {
            if (process.HasExited)
                return false;

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // The process was never started or exited between HasExited and Kill.
            return false;
        }
        catch (NotSupportedException)
        {
            // A remote Process is not expected here, but cleanup must remain best effort.
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process may have exited or become inaccessible concurrently.
            return false;
        }
        catch (NullReferenceException)
        {
            // Process is not thread-safe: a concurrent Dispose can clear its native
            // handle between the public state checks and the framework's kill core.
            return false;
        }
    }

    internal static bool TryWaitForExit(Process? process, TimeSpan timeout)
    {
        if (process == null)
            return true;

        try
        {
            int milliseconds = timeout <= TimeSpan.Zero
                ? 0
                : (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalMilliseconds));
            return process.WaitForExit(milliseconds);
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
        catch (NullReferenceException)
        {
            // Process.WaitForExitCore can dereference a handle cleared by a concurrent
            // Dispose. Cleanup has already signalled the process, so treat it as reaped.
            return true;
        }
    }

    internal static void Terminate(Process? process, TimeSpan? timeout = null)
    {
        TryKill(process);
        TryWaitForExit(process, timeout ?? TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// Tracks only processes created by one SharpTS runtime. Once shutdown begins, a process
/// racing with shutdown is killed during registration rather than escaping the snapshot.
/// </summary>
internal sealed class OwnedProcessRegistry
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<int, Process> _processes = new();
    private int _stopping;

    internal int Count => _processes.Count;

    internal void Register(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (Volatile.Read(ref _stopping) != 0)
        {
            ProcessTreeTermination.Terminate(process, DefaultShutdownTimeout);
            return;
        }

        int pid = process.Id;
        _processes[pid] = process;

        // Close the race where shutdown took its snapshot between the first stopping
        // check and this insertion. Only remove the exact Process instance we inserted.
        if (Volatile.Read(ref _stopping) != 0 &&
            _processes.TryGetValue(pid, out Process? current) &&
            ReferenceEquals(current, process))
        {
            _processes.TryRemove(pid, out _);
            ProcessTreeTermination.Terminate(process, DefaultShutdownTimeout);
        }
    }

    internal void Unregister(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            int pid = process.Id;
            if (_processes.TryGetValue(pid, out Process? current) && ReferenceEquals(current, process))
                _processes.TryRemove(pid, out _);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
            // The Process was never started.
        }
    }

    internal void TerminateAll(TimeSpan? timeout = null)
    {
        Interlocked.Exchange(ref _stopping, 1);

        KeyValuePair<int, Process>[] snapshot = _processes.ToArray();

        // Signal every tree first so many children terminate concurrently.
        foreach ((_, Process process) in snapshot)
            ProcessTreeTermination.TryKill(process);

        TimeSpan budget = timeout ?? DefaultShutdownTimeout;
        long started = Stopwatch.GetTimestamp();
        foreach ((int pid, Process process) in snapshot)
        {
            TimeSpan remaining = budget - Stopwatch.GetElapsedTime(started);
            ProcessTreeTermination.TryWaitForExit(process, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);

            if (_processes.TryGetValue(pid, out Process? current) && ReferenceEquals(current, process))
                _processes.TryRemove(pid, out _);
        }
    }
}
